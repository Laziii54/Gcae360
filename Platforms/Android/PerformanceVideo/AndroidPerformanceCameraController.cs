using Android.Content;
using Android.Runtime;
using AMatrix = Android.Graphics.Matrix;
using ASurfaceTexture = Android.Graphics.SurfaceTexture;
using Android.Hardware.Camera2;
using Android.Hardware.Camera2.Params;
using Android.Media;
using Android.OS;
using Android.Views;
using CarScanner.Services.PerformanceVideo;
using AndroidSize = Android.Util.Size;

namespace CarScanner.Droid.PerformanceVideo;

/// <summary>
/// Back camera preview (TextureView) + MediaRecorder recording with Camera2.
/// The page is locked to portrait, so the orientation hint is the sensor
/// orientation and the saved file plays upright.
/// </summary>
internal sealed class AndroidPerformanceCameraController : Java.Lang.Object, IPerformanceCameraController, CarScanner.VinCheck.Services.IVinCameraReader, TextureView.ISurfaceTextureListener
{
    private readonly Context _context;
    private readonly TextureView _view;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TaskCompletionSource<bool> _surfaceReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private HandlerThread? _thread;
    private Handler? _handler;
    private CameraDevice? _device;
    private CameraCaptureSession? _session;
    private MediaRecorder? _recorder;
    private Surface? _previewSurface;
    private AndroidSize _videoSize = new(1920, 1080);
    private int _sensorOrientation = 90;
    private string? _outputPath;
    private bool _released;
    private bool _restartWhenSurfaceReturns;
    private bool _torchOn;

    // 6 Mbps at 1080p (4 Mbps below) keeps a one-minute clip around 45 MB,
    // under the 80 MB ranking upload limit. The view model stops recording at
    // 60 s; MediaRecorder's own limit is a backstop a little later.
    private const int BitRate1080p = 6_000_000;
    private const int BitRateLower = 4_000_000;
    private const int MaxRecordingMs = 62_000;
    private const int MediaRecorderInfoMaxDurationReached = 800;
    private volatile bool _recorderAutoStopped;
    private Xamarin.Google.MLKit.Vision.Text.ITextRecognizer? _textRecognizer;

    // The listener the native TextureView holds. It is a separate small object on purpose: Android keeps this Java peer
    // alive on its own, and if the managed object behind it is collected the runtime re-creates it through the activation
    // constructor (Java.Interop TypeManager.CreateInstance). A controller without that constructor made the runtime throw
    // NotSupportedException from a draw call (crash report: TextureView.ISurfaceTextureListenerInvoker ← ShellFlyoutRenderer
    // .DrawChild). The listener below has the constructor, holds the controller weakly, and ignores callbacks after the
    // controller is gone. The controller keeps a strong reference to it while it is attached.
    private SurfaceListener? _listener;

    public AndroidPerformanceCameraController(Context context, TextureView view)
    {
        _context = context;
        _view = view;
        _listener = new SurfaceListener(this);
        _view.SurfaceTextureListener = _listener;
        if (_view.IsAvailable)
            _surfaceReady.TrySetResult(true);
    }

    public bool IsRecording { get; private set; }

    // ------------------------------------------------------------------
    // Preview
    // ------------------------------------------------------------------

    public async Task StartPreviewAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfReleased();
            EnsureThread();
            await _surfaceReady.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            if (_device is null)
                await OpenCameraAsync().ConfigureAwait(false);

            MainThread.BeginInvokeOnMainThread(ConfigureTransform);
            await StartPreviewSessionAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task OpenCameraAsync()
    {
        var manager = (CameraManager)_context.GetSystemService(Context.CameraService)!;
        string? selected = null;

        foreach (var id in manager.GetCameraIdList())
        {
            var characteristics = manager.GetCameraCharacteristics(id);
            if (characteristics.Get(CameraCharacteristics.LensFacing) is Java.Lang.Integer facing &&
                facing.IntValue() == (int)LensFacing.Back)
            {
                selected = id;
                break;
            }
        }

        selected ??= manager.GetCameraIdList().FirstOrDefault()
            ?? throw new InvalidOperationException("No camera");

        var chars = manager.GetCameraCharacteristics(selected);
        if (chars.Get(CameraCharacteristics.SensorOrientation) is Java.Lang.Integer orientation)
            _sensorOrientation = orientation.IntValue();
        HasTorch = chars.Get(CameraCharacteristics.FlashInfoAvailable) is Java.Lang.Boolean flash && flash.BooleanValue();

        if (chars.Get(CameraCharacteristics.ScalerStreamConfigurationMap) is StreamConfigurationMap map)
        {
            var sizes = map.GetOutputSizes(Java.Lang.Class.FromType(typeof(MediaRecorder))) ?? Array.Empty<AndroidSize>();
            _videoSize =
                sizes.FirstOrDefault(size => size.Width == 1920 && size.Height == 1080) ??
                sizes.FirstOrDefault(size => size.Width == 1280 && size.Height == 720) ??
                sizes.Where(size => size.Width <= 1920 && size.Width * 9 == size.Height * 16)
                     .OrderByDescending(size => size.Width)
                     .FirstOrDefault() ??
                sizes.FirstOrDefault() ??
                new AndroidSize(1280, 720);
        }

        var opened = new TaskCompletionSource<CameraDevice>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.OpenCamera(selected, new DeviceCallback(opened), _handler);
        var device = await opened.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

        // The page may have closed while the camera was opening.
        if (_released)
        {
            try { device.Close(); } catch { }
            throw new ObjectDisposedException(nameof(AndroidPerformanceCameraController));
        }

        _device = device;
    }

    private async Task StartPreviewSessionAsync()
    {
        CloseSession();

        var texture = _view.SurfaceTexture ?? throw new InvalidOperationException("Preview surface is not ready");
        texture.SetDefaultBufferSize(_videoSize.Width, _videoSize.Height);
        _previewSurface?.Release();
        _previewSurface = new Surface(texture);

        _session = await CreateSessionAsync(new List<Surface> { _previewSurface }).ConfigureAwait(false);

        var builder = _device!.CreateCaptureRequest(CameraTemplate.Preview);
        builder.AddTarget(_previewSurface);
        builder.Set(CaptureRequest.ControlMode!, Java.Lang.Integer.ValueOf((int)ControlMode.Auto));
        builder.Set(CaptureRequest.ControlAfMode!, Java.Lang.Integer.ValueOf((int)ControlAFMode.ContinuousVideo));
        if (_torchOn && HasTorch)
            builder.Set(CaptureRequest.FlashMode!, Java.Lang.Integer.ValueOf((int)FlashMode.Torch));
        _session.SetRepeatingRequest(builder.Build(), null, _handler);
    }

    // ------------------------------------------------------------------
    // VIN scanning: text in the current picture (ML Kit, on the phone)
    // ------------------------------------------------------------------

    public bool HasTorch { get; private set; }

    public async Task SetTorchAsync(bool on)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _torchOn = on;
            if (_released || _device is null || _session is null || _previewSurface is null || IsRecording || !HasTorch)
                return;

            var builder = _device.CreateCaptureRequest(CameraTemplate.Preview);
            builder.AddTarget(_previewSurface);
            builder.Set(CaptureRequest.ControlMode!, Java.Lang.Integer.ValueOf((int)ControlMode.Auto));
            builder.Set(CaptureRequest.ControlAfMode!, Java.Lang.Integer.ValueOf((int)ControlAFMode.ContinuousPicture));
            builder.Set(CaptureRequest.FlashMode!, Java.Lang.Integer.ValueOf((int)(on ? FlashMode.Torch : FlashMode.Off)));
            _session.SetRepeatingRequest(builder.Build(), null, _handler);
        }
        catch
        {
            // Torch is a convenience only.
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> ReadTextLinesAsync(CancellationToken token)
    {
        if (_released || _device is null)
            return Array.Empty<string>();

        // The picture exactly as shown on the screen (upright).
        var bitmap = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_released || !_view.IsAvailable || _view.Width <= 0 || _view.Height <= 0)
                return null;
            var scale = Math.Min(1.0, 1600.0 / Math.Max(_view.Width, _view.Height));
            return _view.GetBitmap((int)(_view.Width * scale), (int)(_view.Height * scale));
        }).ConfigureAwait(false);

        if (bitmap is null)
            return Array.Empty<string>();

        try
        {
            token.ThrowIfCancellationRequested();
            _textRecognizer ??= Xamarin.Google.MLKit.Vision.Text.TextRecognition.GetClient(
                Xamarin.Google.MLKit.Vision.Text.Latin.TextRecognizerOptions.DefaultOptions);

            var image = Xamarin.Google.MLKit.Vision.Common.InputImage.FromBitmap(bitmap, 0);
            var listener = new GmsTaskListener();
            var task = _textRecognizer.Process(image);
            task.AddOnSuccessListener(listener);
            task.AddOnFailureListener(listener);

            var result = await listener.Task.WaitAsync(TimeSpan.FromSeconds(8), token).ConfigureAwait(false);
            if (result is null)
                return Array.Empty<string>();

            var text = Android.Runtime.Extensions.JavaCast<Xamarin.Google.MLKit.Vision.Text.Text>(result);
            var lines = new List<string>();
            foreach (var block in text.TextBlocks)
            {
                foreach (var line in block.Lines)
                {
                    if (!string.IsNullOrWhiteSpace(line.Text))
                        lines.Add(line.Text);
                }
                // A VIN split over two lines of one block.
                if (block.Lines.Count > 1 && !string.IsNullOrWhiteSpace(block.Text))
                    lines.Add(block.Text.Replace('\n', ' '));
            }

            return lines;
        }
        finally
        {
            try { bitmap.Recycle(); } catch { }
            bitmap.Dispose();
        }
    }

    private sealed class GmsTaskListener : Java.Lang.Object,
        Android.Gms.Tasks.IOnSuccessListener,
        Android.Gms.Tasks.IOnFailureListener
    {
        private readonly TaskCompletionSource<Java.Lang.Object?> _done =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Java.Lang.Object?> Task => _done.Task;

        public void OnSuccess(Java.Lang.Object? result) => _done.TrySetResult(result);

        public void OnFailure(Java.Lang.Exception e) => _done.TrySetException(e);
    }

    private Task<CameraCaptureSession> CreateSessionAsync(IList<Surface> surfaces)
    {
        var configured = new TaskCompletionSource<CameraCaptureSession>(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable CS0618, CA1422 // createCaptureSession(List) works on every API level the app supports.
        _device!.CreateCaptureSession(surfaces, new SessionCallback(configured), _handler);
#pragma warning restore CS0618, CA1422
        return configured.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // ------------------------------------------------------------------
    // Recording
    // ------------------------------------------------------------------

    public async Task StartRecordingAsync(string outputPath, bool withAudio)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfReleased();
            if (IsRecording)
                return;
            if (_device is null)
                throw new InvalidOperationException("Camera is not open");

            CloseSession();

            var recorder = CreateRecorder();
            try
            {
                if (withAudio)
                    recorder.SetAudioSource(AudioSource.Camcorder);
                recorder.SetVideoSource(VideoSource.Surface);
                recorder.SetOutputFormat(OutputFormat.Mpeg4);
                recorder.SetOutputFile(outputPath);
                recorder.SetVideoEncodingBitRate(_videoSize.Width >= 1920 ? BitRate1080p : BitRateLower);
                recorder.SetMaxDuration(MaxRecordingMs);
                recorder.SetVideoFrameRate(30);
                recorder.SetVideoSize(_videoSize.Width, _videoSize.Height);
                recorder.SetVideoEncoder(VideoEncoder.H264);
                if (withAudio)
                {
                    recorder.SetAudioEncoder(AudioEncoder.Aac);
                    recorder.SetAudioEncodingBitRate(128_000);
                    recorder.SetAudioSamplingRate(44_100);
                }
                recorder.SetOrientationHint(_sensorOrientation);
                _recorderAutoStopped = false;
                recorder.Info += (_, args) =>
                {
                    // The recorder finalizes the file itself at the limit;
                    // calling Stop() afterwards would throw and lose it.
                    if ((int)args.What == MediaRecorderInfoMaxDurationReached)
                        _recorderAutoStopped = true;
                };
                recorder.Prepare();

                var recorderSurface = recorder.Surface!;
                _session = await CreateSessionAsync(new List<Surface> { _previewSurface!, recorderSurface }).ConfigureAwait(false);

                var builder = _device.CreateCaptureRequest(CameraTemplate.Record);
                builder.AddTarget(_previewSurface!);
                builder.AddTarget(recorderSurface);
                builder.Set(CaptureRequest.ControlMode!, Java.Lang.Integer.ValueOf((int)ControlMode.Auto));
                builder.Set(CaptureRequest.ControlAfMode!, Java.Lang.Integer.ValueOf((int)ControlAFMode.ContinuousVideo));
                _session.SetRepeatingRequest(builder.Build(), null, _handler);

                recorder.Start();
                _recorder = recorder;
                _outputPath = outputPath;
                IsRecording = true;
            }
            catch
            {
                try { recorder.Reset(); } catch { }
                recorder.Release();
                try { await StartPreviewSessionAsync().ConfigureAwait(false); } catch { }
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> StopRecordingAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsRecording || _recorder is null)
                return null;

            var path = _outputPath;
            try
            {
                _session?.StopRepeating();
                _session?.AbortCaptures();
            }
            catch
            {
                // Session may already be closed.
            }

            try
            {
                if (!_recorderAutoStopped)
                    _recorder.Stop();
            }
            catch (Java.Lang.RuntimeException)
            {
                // Stop right after start → no valid file.
                TryDelete(path);
                path = null;
            }
            finally
            {
                try { _recorder.Reset(); } catch { }
                _recorder.Release();
                _recorder = null;
                IsRecording = false;
            }

            if (!_released)
            {
                try { await StartPreviewSessionAsync().ConfigureAwait(false); } catch { }
            }

            return path;
        }
        finally
        {
            _gate.Release();
        }
    }

    private MediaRecorder CreateRecorder()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            return new MediaRecorder(_context);
#pragma warning disable CS0618, CA1422
        return new MediaRecorder();
#pragma warning restore CS0618, CA1422
    }

    // ------------------------------------------------------------------
    // Layout: center-crop the portrait preview into the view
    // ------------------------------------------------------------------

    private void ConfigureTransform()
    {
        if (_released || _view.Width == 0 || _view.Height == 0)
            return;

        var rotated = _sensorOrientation is 90 or 270;
        float contentWidth = rotated ? _videoSize.Height : _videoSize.Width;
        float contentHeight = rotated ? _videoSize.Width : _videoSize.Height;
        var contentAspect = contentWidth / contentHeight;
        var viewAspect = (float)_view.Width / _view.Height;

        var matrix = new AMatrix();
        if (viewAspect > contentAspect)
            matrix.SetScale(1f, viewAspect / contentAspect, _view.Width / 2f, _view.Height / 2f);
        else
            matrix.SetScale(contentAspect / viewAspect, 1f, _view.Width / 2f, _view.Height / 2f);

        _view.SetTransform(matrix);
    }

    // ------------------------------------------------------------------
    // TextureView.ISurfaceTextureListener
    // ------------------------------------------------------------------

    public void OnSurfaceTextureAvailable(ASurfaceTexture surface, int width, int height)
    {
        if (_released)
            return;
        _surfaceReady.TrySetResult(true);
        ConfigureTransform();

        // Back from background: open the camera again.
        if (_restartWhenSurfaceReturns)
        {
            _restartWhenSurfaceReturns = false;
            _ = RestartPreviewAsync();
        }
    }

    public bool OnSurfaceTextureDestroyed(ASurfaceTexture surface)
    {
        // The preview surface is going away (app in background / page
        // closing): stop the camera first so it never draws into a
        // destroyed surface.
        _surfaceReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_released)
        {
            SuspendCamera();
            _restartWhenSurfaceReturns = true;
        }

        return true;
    }

    private async Task RestartPreviewAsync()
    {
        try
        {
            await StartPreviewAsync().ConfigureAwait(false);
        }
        catch
        {
            // The page shows a black preview; reopening the page fixes it.
        }
    }

    private void SuspendCamera()
    {
        if (_recorder is not null)
        {
            try { _recorder.Stop(); } catch { TryDelete(_outputPath); }
            try { _recorder.Reset(); } catch { }
            try { _recorder.Release(); } catch { }
            _recorder = null;
        }

        IsRecording = false;
        CloseSession();
        try { _device?.Close(); } catch { }
        _device = null;
        try { _previewSurface?.Release(); } catch { }
        _previewSurface = null;
    }

    public void OnSurfaceTextureSizeChanged(ASurfaceTexture surface, int width, int height)
    {
        if (!_released)
            ConfigureTransform();
    }

    public void OnSurfaceTextureUpdated(ASurfaceTexture surface)
    {
    }

    private sealed class SurfaceListener : Java.Lang.Object, TextureView.ISurfaceTextureListener
    {
        private readonly WeakReference<AndroidPerformanceCameraController>? _owner;

        public SurfaceListener(AndroidPerformanceCameraController owner) => _owner = new WeakReference<AndroidPerformanceCameraController>(owner);

        /// <summary>Activation constructor: used by the runtime when it has to re-create this peer. Such an instance has no owner.</summary>
        public SurfaceListener(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer)
        {
        }

        private AndroidPerformanceCameraController? Owner =>
            _owner is not null && _owner.TryGetTarget(out var owner) ? owner : null;

        public void OnSurfaceTextureAvailable(ASurfaceTexture surface, int width, int height) =>
            Owner?.OnSurfaceTextureAvailable(surface, width, height);

        // true = the system may release the surface itself (what the controller always answered).
        public bool OnSurfaceTextureDestroyed(ASurfaceTexture surface) => Owner?.OnSurfaceTextureDestroyed(surface) ?? true;

        public void OnSurfaceTextureSizeChanged(ASurfaceTexture surface, int width, int height) =>
            Owner?.OnSurfaceTextureSizeChanged(surface, width, height);

        public void OnSurfaceTextureUpdated(ASurfaceTexture surface) => Owner?.OnSurfaceTextureUpdated(surface);
    }

    // ------------------------------------------------------------------
    // Cleanup
    // ------------------------------------------------------------------

    public void Release()
    {
        if (_released)
            return;
        _released = true;
        _surfaceReady.TrySetCanceled();

        try
        {
            // The listener object stays referenced until the native view has really let go of it.
            var listener = _listener;
            if (MainThread.IsMainThread)
            {
                _view.SurfaceTextureListener = null;
                _listener = null;
            }
            else
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try { _view.SurfaceTextureListener = null; } catch { }
                    GC.KeepAlive(listener);
                    _listener = null;
                });
        }
        catch
        {
        }

        try
        {
            if (_recorder is not null)
            {
                try { _recorder.Stop(); } catch { TryDelete(_outputPath); }
                try { _recorder.Reset(); } catch { }
                _recorder.Release();
                _recorder = null;
            }
        }
        catch
        {
        }

        IsRecording = false;
        CloseSession();

        try { _device?.Close(); } catch { }
        _device = null;

        try { _previewSurface?.Release(); } catch { }
        _previewSurface = null;

        try { _thread?.QuitSafely(); } catch { }
        _thread = null;
        _handler = null;

        try { _textRecognizer?.Close(); } catch { }
        _textRecognizer = null;
    }

    private void CloseSession()
    {
        try { _session?.Close(); } catch { }
        _session = null;
    }

    private void EnsureThread()
    {
        if (_thread is not null)
            return;

        _thread = new HandlerThread("GCar360PerformanceCamera");
        _thread.Start();
        _handler = new Handler(_thread.Looper!);
    }

    private void ThrowIfReleased()
    {
        if (_released)
            throw new ObjectDisposedException(nameof(AndroidPerformanceCameraController));
    }

    private static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private sealed class DeviceCallback : CameraDevice.StateCallback
    {
        private readonly TaskCompletionSource<CameraDevice> _opened;

        public DeviceCallback(TaskCompletionSource<CameraDevice> opened) => _opened = opened;

        public override void OnOpened(CameraDevice camera) => _opened.TrySetResult(camera);

        public override void OnDisconnected(CameraDevice camera)
        {
            camera.Close();
            _opened.TrySetException(new InvalidOperationException("Camera disconnected"));
        }

        public override void OnError(CameraDevice camera, CameraError error)
        {
            camera.Close();
            _opened.TrySetException(new InvalidOperationException($"Camera error {error}"));
        }
    }

    private sealed class SessionCallback : CameraCaptureSession.StateCallback
    {
        private readonly TaskCompletionSource<CameraCaptureSession> _configured;

        public SessionCallback(TaskCompletionSource<CameraCaptureSession> configured) => _configured = configured;

        public override void OnConfigured(CameraCaptureSession session) => _configured.TrySetResult(session);

        public override void OnConfigureFailed(CameraCaptureSession session) =>
            _configured.TrySetException(new InvalidOperationException("Camera session could not be configured"));
    }
}
