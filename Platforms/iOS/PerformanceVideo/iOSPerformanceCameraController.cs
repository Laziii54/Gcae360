#if IOS
using AVFoundation;
using CarScanner.Services.PerformanceVideo;
using CarScanner.VinCheck.Services;
using Foundation;
using Vision;

namespace CarScanner.Platforms.iOS.PerformanceVideo;

/// <summary>
/// One AVFoundation camera session shared by Performance Video and VIN OCR.
/// Keeping both features on the same controller preserves the existing pages.
/// </summary>
internal sealed class iOSPerformanceCameraController : IPerformanceCameraController, IVinCameraReader
{
    private readonly CameraPreviewView _view;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AVCaptureSession? _session;
    private AVCaptureDevice? _videoDevice;
    private AVCaptureMovieFileOutput? _movieOutput;
    private AVCapturePhotoOutput? _photoOutput;
    private MovieRecordingDelegate? _recordingDelegate;
    private bool _released;
    private string? _outputPath;

    public iOSPerformanceCameraController(CameraPreviewView view) => _view = view;

    public bool IsRecording { get; private set; }
    public bool HasTorch => _videoDevice?.HasTorch == true;

    public async Task StartPreviewAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfReleased();
            if (_session is null)
                await MainThread.InvokeOnMainThreadAsync(ConfigureSession);

            if (_session?.Running == false)
                _session.StartRunning();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ConfigureSession()
    {
        ThrowIfReleased();

        var session = new AVCaptureSession
        {
            SessionPreset = AVCaptureSession.PresetHigh
        };

        var device = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video)
            ?? throw new InvalidOperationException("No iPhone camera is available.");
        var videoInput = AVCaptureDeviceInput.FromDevice(device, out var videoError)
            ?? throw new InvalidOperationException(videoError?.LocalizedDescription ?? "Cannot open the camera.");
        if (!session.CanAddInput(videoInput))
            throw new InvalidOperationException("The camera input is not supported.");
        session.AddInput(videoInput);

        // Audio is optional. Recording still works when microphone access was denied.
        var audioDevice = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Audio);
        if (audioDevice is not null)
        {
            var audioInput = AVCaptureDeviceInput.FromDevice(audioDevice, out _);
            if (audioInput is not null && session.CanAddInput(audioInput))
                session.AddInput(audioInput);
        }

        var movieOutput = new AVCaptureMovieFileOutput();
        if (!session.CanAddOutput(movieOutput))
            throw new InvalidOperationException("Video recording is not supported on this device.");
        session.AddOutput(movieOutput);

        var photoOutput = new AVCapturePhotoOutput();
        if (!session.CanAddOutput(photoOutput))
            throw new InvalidOperationException("VIN image capture is not supported on this device.");
        session.AddOutput(photoOutput);

        var preview = new AVCaptureVideoPreviewLayer(session)
        {
            VideoGravity = AVLayerVideoGravity.ResizeAspectFill,
            Frame = _view.Bounds
        };
        _view.Layer.InsertSublayer(preview, 0);
        _view.PreviewLayer = preview;

        _session = session;
        _videoDevice = device;
        _movieOutput = movieOutput;
        _photoOutput = photoOutput;
    }

    public async Task StartRecordingAsync(string outputPath, bool withAudio)
    {
        await StartPreviewAsync().ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfReleased();
            if (IsRecording)
                return;

            if (File.Exists(outputPath))
                File.Delete(outputPath);

            _outputPath = outputPath;
            _recordingDelegate = new MovieRecordingDelegate();
            _movieOutput!.StartRecordingToOutputFile(
                NSUrl.FromFilename(outputPath),
                _recordingDelegate);
            IsRecording = true;
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
            if (!IsRecording || _movieOutput is null || _recordingDelegate is null)
                return null;

            _movieOutput.StopRecording();
            var error = await _recordingDelegate.Completion.ConfigureAwait(false);
            IsRecording = false;
            var path = error is null && _outputPath is not null && File.Exists(_outputPath)
                ? _outputPath
                : null;
            _recordingDelegate = null;
            return path;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetTorchAsync(bool on)
    {
        var device = _videoDevice;
        if (device is null || !device.HasTorch)
            return;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (!device.LockForConfiguration(out _))
                return;
            try
            {
                device.TorchMode = on ? AVCaptureTorchMode.On : AVCaptureTorchMode.Off;
            }
            finally
            {
                device.UnlockForConfiguration();
            }
        });
    }

    public async Task<IReadOnlyList<string>> ReadTextLinesAsync(CancellationToken token)
    {
        var output = _photoOutput;
        if (_released || output is null)
            return Array.Empty<string>();

        var photoDelegate = new PhotoCaptureDelegate();
        output.CapturePhoto(AVCapturePhotoSettings.Create(), photoDelegate);
        using var data = await photoDelegate.Completion.WaitAsync(TimeSpan.FromSeconds(8), token).ConfigureAwait(false);

        var completion = new TaskCompletionSource<IReadOnlyList<string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var request = new VNRecognizeTextRequest((recognizedRequest, error) =>
        {
            if (error is not null)
            {
                completion.TrySetException(new InvalidOperationException(error.LocalizedDescription));
                return;
            }

            var lines = (recognizedRequest.GetResults<VNRecognizedTextObservation>() ??
                         Array.Empty<VNRecognizedTextObservation>())
                .Select(observation => observation.TopCandidates(1).FirstOrDefault()?.String)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Cast<string>()
                .ToArray();
            completion.TrySetResult(lines);
        })
        {
            RecognitionLevel = VNRequestTextRecognitionLevel.Accurate,
            UsesLanguageCorrection = false
        };

        using var handler = new VNImageRequestHandler(data, new NSDictionary());
        if (!handler.Perform(new VNRequest[] { request }, out var visionError))
            throw new InvalidOperationException(visionError?.LocalizedDescription ?? "VIN recognition failed.");

        return await completion.Task.WaitAsync(TimeSpan.FromSeconds(8), token).ConfigureAwait(false);
    }

    public void Release()
    {
        if (_released)
            return;
        _released = true;

        try
        {
            if (_movieOutput?.Recording == true)
                _movieOutput.StopRecording();
        }
        catch { }

        try { _session?.StopRunning(); } catch { }
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _view.PreviewLayer?.RemoveFromSuperLayer();
            _view.PreviewLayer?.Dispose();
            _view.PreviewLayer = null;
        });
        IsRecording = false;
    }

    private void ThrowIfReleased()
    {
        if (_released)
            throw new ObjectDisposedException(nameof(iOSPerformanceCameraController));
    }

    private sealed class MovieRecordingDelegate : AVCaptureFileOutputRecordingDelegate
    {
        private readonly TaskCompletionSource<NSError?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<NSError?> Completion => _completion.Task;

        public override void FinishedRecording(
            AVCaptureFileOutput captureOutput,
            NSUrl outputFileUrl,
            NSObject[] connections,
            NSError? error) => _completion.TrySetResult(error);
    }

    private sealed class PhotoCaptureDelegate : AVCapturePhotoCaptureDelegate
    {
        private readonly TaskCompletionSource<NSData> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<NSData> Completion => _completion.Task;

        public override void DidFinishProcessingPhoto(
            AVCapturePhotoOutput output,
            AVCapturePhoto photo,
            NSError? error)
        {
            if (error is not null)
            {
                _completion.TrySetException(new InvalidOperationException(error.LocalizedDescription));
                return;
            }

            var data = photo.FileDataRepresentation;
            if (data is null)
                _completion.TrySetException(new InvalidOperationException("The camera returned an empty image."));
            else
                _completion.TrySetResult(data);
        }
    }
}
#endif
