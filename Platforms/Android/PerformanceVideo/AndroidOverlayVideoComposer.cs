using ABitmap = Android.Graphics.Bitmap;
using ASurfaceTexture = Android.Graphics.SurfaceTexture;
using Android.Media;
using Android.Opengl;
using Android.OS;
using CarScanner.Services.PerformanceVideo;
using Java.Nio;
using AndroidSurface = Android.Views.Surface;

namespace CarScanner.Droid.PerformanceVideo;

/// <summary>
/// Re-encodes a recorded clip with the overlay burned into every frame:
/// MediaExtractor → decoder → SurfaceTexture (OpenGL ES 2) → video + overlay
/// quad → encoder input surface → MediaMuxer. Audio is copied unchanged.
/// Frames stay in the recorder's native orientation; the overlay is drawn
/// rotated so it is upright when the player applies the rotation hint.
/// </summary>
internal static class AndroidOverlayVideoComposer
{
    private const int ColorFormatSurface = 0x7F000789;
    private const int EglRecordableAndroid = 0x3142;
    private const int InfoTryAgainLater = -1;
    private const int InfoOutputFormatChanged = -2;
    private const int InfoOutputBuffersChanged = -3;
    private const long TimeoutUs = 10_000;

    private const string VideoVertexShader =
        "uniform mat4 uSTMatrix;\n" +
        "attribute vec4 aPosition;\n" +
        "attribute vec4 aTexCoord;\n" +
        "varying vec2 vTex;\n" +
        "void main() {\n" +
        "  gl_Position = aPosition;\n" +
        "  vTex = (uSTMatrix * aTexCoord).xy;\n" +
        "}\n";

    private const string VideoFragmentShader =
        "#extension GL_OES_EGL_image_external : require\n" +
        "precision mediump float;\n" +
        "varying vec2 vTex;\n" +
        "uniform samplerExternalOES sTex;\n" +
        "void main() {\n" +
        "  gl_FragColor = texture2D(sTex, vTex);\n" +
        "}\n";

    private const string OverlayVertexShader =
        "attribute vec4 aPosition;\n" +
        "attribute vec2 aTexCoord;\n" +
        "varying vec2 vTex;\n" +
        "void main() {\n" +
        "  gl_Position = aPosition;\n" +
        "  vTex = aTexCoord;\n" +
        "}\n";

    private const string OverlayFragmentShader =
        "precision mediump float;\n" +
        "varying vec2 vTex;\n" +
        "uniform sampler2D sTex;\n" +
        "void main() {\n" +
        "  gl_FragColor = texture2D(sTex, vTex);\n" +
        "}\n";

    private static readonly float[] QuadPositions = { -1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f };
    private static readonly float[] VideoTexCoords = { 0f, 0f, 1f, 0f, 0f, 1f, 1f, 1f };

    public static Task ComposeAsync(
        string inputPath,
        string outputPath,
        PerformanceVideoTelemetry telemetry,
        PerformanceOverlayStyle style,
        IProgress<double>? progress,
        CancellationToken token) =>
        Task.Factory.StartNew(
            () => Compose(inputPath, outputPath, telemetry, style, progress, token),
            token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private static void Compose(
        string inputPath,
        string outputPath,
        PerformanceVideoTelemetry telemetry,
        PerformanceOverlayStyle style,
        IProgress<double>? progress,
        CancellationToken token)
    {
        var rotation = ReadRotation(inputPath);

        MediaExtractor? extractor = null;
        MediaCodec? decoder = null;
        MediaCodec? encoder = null;
        MediaMuxer? muxer = null;
        AndroidSurface? encoderSurface = null;
        AndroidSurface? decoderSurface = null;
        ASurfaceTexture? surfaceTexture = null;
        HandlerThread? frameThread = null;
        ABitmap? overlayBitmap = null;
        PerformanceOverlayPainter? painter = null;
        var egl = new EglState();
        var muxerStarted = false;
        var success = false;

        try
        {
            extractor = new MediaExtractor();
            extractor.SetDataSource(inputPath);

            int videoTrack = -1, audioTrack = -1;
            MediaFormat? videoFormat = null, audioFormat = null;
            for (var i = 0; i < extractor.TrackCount; i++)
            {
                var format = extractor.GetTrackFormat(i);
                var mime = format.GetString(MediaFormat.KeyMime) ?? string.Empty;
                if (videoTrack < 0 && mime.StartsWith("video/", StringComparison.Ordinal))
                {
                    videoTrack = i;
                    videoFormat = format;
                }
                else if (audioTrack < 0 && mime.StartsWith("audio/", StringComparison.Ordinal))
                {
                    audioTrack = i;
                    audioFormat = format;
                }
            }

            if (videoTrack < 0 || videoFormat is null)
                throw new InvalidOperationException("No video track");

            var width = videoFormat.GetInteger(MediaFormat.KeyWidth);
            var height = videoFormat.GetInteger(MediaFormat.KeyHeight);
            var durationUs = videoFormat.ContainsKey(MediaFormat.KeyDuration) ? videoFormat.GetLong(MediaFormat.KeyDuration) : 0L;
            var frameRate = TryGetInt(videoFormat, MediaFormat.KeyFrameRate) ?? 30;
            var bitRate = Math.Max(TryGetInt(videoFormat, MediaFormat.KeyBitRate) ?? 0, Math.Max(4_000_000, width * height * 6));

            // Decode in the recorder's native orientation (no automatic rotation).
            if (videoFormat.ContainsKey("rotation-degrees"))
                videoFormat.SetInteger("rotation-degrees", 0);

            extractor.SelectTrack(videoTrack);

            // ---- Encoder + EGL on its input surface ----
            var outputFormat = MediaFormat.CreateVideoFormat(MediaFormat.MimetypeVideoAvc, width, height);
            outputFormat.SetInteger(MediaFormat.KeyColorFormat, ColorFormatSurface);
            outputFormat.SetInteger(MediaFormat.KeyBitRate, bitRate);
            outputFormat.SetInteger(MediaFormat.KeyFrameRate, frameRate);
            outputFormat.SetInteger(MediaFormat.KeyIFrameInterval, 1);

            encoder = MediaCodec.CreateEncoderByType(MediaFormat.MimetypeVideoAvc);
            encoder.Configure(outputFormat, null, null, MediaCodecConfigFlags.Encode);
            encoderSurface = encoder.CreateInputSurface();
            encoder.Start();

            egl.Setup(encoderSurface);

            var renderer = new Renderer();
            renderer.Setup(rotation);

            // ---- Decoder → SurfaceTexture ----
            var frameAvailable = new SemaphoreSlim(0, int.MaxValue);
            frameThread = new HandlerThread("GCar360OverlayFrames");
            frameThread.Start();
            surfaceTexture = new ASurfaceTexture(renderer.VideoTextureId);
            surfaceTexture.SetOnFrameAvailableListener(new FrameListener(frameAvailable), new Handler(frameThread.Looper!));
            decoderSurface = new AndroidSurface(surfaceTexture);

            var videoMime = videoFormat.GetString(MediaFormat.KeyMime)!;
            decoder = MediaCodec.CreateDecoderByType(videoMime);
            decoder.Configure(videoFormat, decoderSurface, null, 0);
            decoder.Start();

            // ---- Overlay bitmap in display orientation ----
            var displayWidth = rotation is 90 or 270 ? height : width;
            var displayHeight = rotation is 90 or 270 ? width : height;
            overlayBitmap = ABitmap.CreateBitmap(displayWidth, displayHeight, ABitmap.Config.Argb8888!)!;
            painter = new PerformanceOverlayPainter(style, telemetry.SpeedUnit, telemetry.SecondsUnit);

            muxer = new MediaMuxer(outputPath, MuxerOutputType.Mpeg4);
            muxer.SetOrientationHint(rotation);
            var videoTrackOut = -1;
            var audioTrackOut = -1;

            var info = new MediaCodec.BufferInfo();
            var encoderInfo = new MediaCodec.BufferInfo();
            var stMatrix = new float[16];
            bool inputDone = false, decoderDone = false, encoderDone = false;
            long firstPtsUs = -1;
            string? overlayKey = null;
            var lastProgress = -1;

            void DrainEncoder(bool endOfStream)
            {
                while (true)
                {
                    var index = encoder!.DequeueOutputBuffer(encoderInfo, endOfStream ? TimeoutUs : 0);
                    if (index == InfoTryAgainLater)
                    {
                        if (!endOfStream)
                            return;
                        token.ThrowIfCancellationRequested();
                        continue;
                    }

                    if (index == InfoOutputBuffersChanged)
                        continue;

                    if (index == InfoOutputFormatChanged)
                    {
                        if (muxerStarted)
                            throw new InvalidOperationException("Encoder format changed twice");

                        videoTrackOut = muxer!.AddTrack(encoder.OutputFormat);
                        if (audioFormat is not null)
                            audioTrackOut = muxer.AddTrack(audioFormat);
                        muxer.Start();
                        muxerStarted = true;
                        continue;
                    }

                    if (index < 0)
                        continue;

                    var buffer = encoder.GetOutputBuffer(index)!;
                    if ((encoderInfo.Flags & MediaCodecBufferFlags.CodecConfig) != 0)
                        encoderInfo.Size = 0;

                    if (encoderInfo.Size > 0 && muxerStarted)
                    {
                        buffer.Position(encoderInfo.Offset);
                        buffer.Limit(encoderInfo.Offset + encoderInfo.Size);
                        muxer!.WriteSampleData(videoTrackOut, buffer, encoderInfo);
                    }

                    encoder.ReleaseOutputBuffer(index, false);

                    if ((encoderInfo.Flags & MediaCodecBufferFlags.EndOfStream) != 0)
                    {
                        encoderDone = true;
                        return;
                    }
                }
            }

            while (!encoderDone)
            {
                token.ThrowIfCancellationRequested();

                if (!inputDone)
                {
                    var inputIndex = decoder.DequeueInputBuffer(TimeoutUs);
                    if (inputIndex >= 0)
                    {
                        var inputBuffer = decoder.GetInputBuffer(inputIndex)!;
                        var size = extractor.ReadSampleData(inputBuffer, 0);
                        if (size < 0)
                        {
                            decoder.QueueInputBuffer(inputIndex, 0, 0, 0, MediaCodecBufferFlags.EndOfStream);
                            inputDone = true;
                        }
                        else
                        {
                            decoder.QueueInputBuffer(inputIndex, 0, size, extractor.SampleTime, 0);
                            extractor.Advance();
                        }
                    }
                }

                DrainEncoder(false);

                if (decoderDone)
                {
                    DrainEncoder(true);
                    continue;
                }

                var outputIndex = decoder.DequeueOutputBuffer(info, TimeoutUs);
                if (outputIndex < 0)
                    continue;

                var render = info.Size > 0;
                var endOfStream = (info.Flags & MediaCodecBufferFlags.EndOfStream) != 0;
                var ptsUs = info.PresentationTimeUs;
                decoder.ReleaseOutputBuffer(outputIndex, render);

                if (render)
                {
                    if (!frameAvailable.Wait(TimeSpan.FromSeconds(3), token))
                        throw new TimeoutException("Decoded frame did not arrive");

                    surfaceTexture.UpdateTexImage();
                    surfaceTexture.GetTransformMatrix(stMatrix);

                    if (firstPtsUs < 0)
                        firstPtsUs = ptsUs;

                    var state = telemetry.StateAt((ptsUs - firstPtsUs) / 1000d);
                    var key = PerformanceOverlayPainter.KeyOf(state);
                    if (key != overlayKey)
                    {
                        painter.Draw(overlayBitmap, state);
                        renderer.UploadOverlay(overlayBitmap, overlayKey is null);
                        overlayKey = key;
                    }

                    renderer.Draw(width, height, stMatrix);
                    EGLExt.EglPresentationTimeANDROID(egl.Display, egl.Surface, ptsUs * 1000);
                    EGL14.EglSwapBuffers(egl.Display, egl.Surface);

                    if (durationUs > 0)
                    {
                        var percent = (int)Math.Clamp(ptsUs * 100 / durationUs, 0, 100);
                        if (percent != lastProgress)
                        {
                            lastProgress = percent;
                            progress?.Report(percent / 100d);
                        }
                    }
                }

                if (endOfStream)
                {
                    encoder.SignalEndOfInputStream();
                    decoderDone = true;
                }
            }

            // ---- Copy audio unchanged ----
            if (audioTrack >= 0 && audioTrackOut >= 0 && muxerStarted)
                CopyAudio(inputPath, audioTrack, muxer, audioTrackOut, token);

            progress?.Report(1d);
            success = true;
        }
        finally
        {
            try { decoder?.Stop(); } catch { }
            try { decoder?.Release(); } catch { }
            try { encoder?.Stop(); } catch { }
            try { encoder?.Release(); } catch { }
            try { if (muxerStarted) muxer?.Stop(); } catch { success = false; }
            try { muxer?.Release(); } catch { }
            try { extractor?.Release(); } catch { }
            try { decoderSurface?.Release(); } catch { }
            try { surfaceTexture?.Release(); } catch { }
            try { frameThread?.QuitSafely(); } catch { }
            egl.Release();
            try { encoderSurface?.Release(); } catch { }
            painter?.Dispose();
            overlayBitmap?.Recycle();

            if (!success)
            {
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
            }
        }
    }

    private static void CopyAudio(string inputPath, int audioTrack, MediaMuxer muxer, int trackOut, CancellationToken token)
    {
        var extractor = new MediaExtractor();
        try
        {
            extractor.SetDataSource(inputPath);
            extractor.SelectTrack(audioTrack);

            var buffer = ByteBuffer.Allocate(1024 * 1024)!;
            var info = new MediaCodec.BufferInfo();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                buffer.Clear();
                var size = extractor.ReadSampleData(buffer, 0);
                if (size < 0)
                    break;

                var flags = (extractor.SampleFlags & MediaExtractorSampleFlags.Sync) != 0
                    ? MediaCodecBufferFlags.KeyFrame
                    : 0;
                info.Set(0, size, extractor.SampleTime, flags);
                muxer.WriteSampleData(trackOut, buffer, info);
                extractor.Advance();
            }
        }
        finally
        {
            extractor.Release();
        }
    }

    private static int ReadRotation(string path)
    {
        var retriever = new MediaMetadataRetriever();
        try
        {
            retriever.SetDataSource(path);
            var value = retriever.ExtractMetadata(MetadataKey.VideoRotation);
            return int.TryParse(value, out var degrees) ? ((degrees % 360) + 360) % 360 : 0;
        }
        catch
        {
            return 0;
        }
        finally
        {
            try { retriever.Release(); } catch { }
        }
    }

    private static int? TryGetInt(MediaFormat format, string key)
    {
        try
        {
            return format.ContainsKey(key) ? format.GetInteger(key) : null;
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------
    // EGL
    // ------------------------------------------------------------------

    private sealed class EglState
    {
        public EGLDisplay Display { get; private set; } = EGL14.EglNoDisplay!;
        public EGLSurface Surface { get; private set; } = EGL14.EglNoSurface!;
        private EGLContext _context = EGL14.EglNoContext!;

        public void Setup(AndroidSurface target)
        {
            Display = EGL14.EglGetDisplay(EGL14.EglDefaultDisplay)!;
            var version = new int[2];
            if (!EGL14.EglInitialize(Display, version, 0, version, 1))
                throw new InvalidOperationException("EGL init failed");

            int[] attributes =
            {
                EGL14.EglRedSize, 8,
                EGL14.EglGreenSize, 8,
                EGL14.EglBlueSize, 8,
                EGL14.EglAlphaSize, 8,
                EGL14.EglRenderableType, EGL14.EglOpenglEs2Bit,
                EglRecordableAndroid, 1,
                EGL14.EglNone
            };
            var configs = new EGLConfig[1];
            var count = new int[1];
            if (!EGL14.EglChooseConfig(Display, attributes, 0, configs, 0, 1, count, 0) || count[0] == 0)
                throw new InvalidOperationException("No EGL config");

            _context = EGL14.EglCreateContext(Display, configs[0], EGL14.EglNoContext, new[] { EGL14.EglContextClientVersion, 2, EGL14.EglNone }, 0)!;
            Surface = EGL14.EglCreateWindowSurface(Display, configs[0], target, new[] { EGL14.EglNone }, 0)!;
            if (!EGL14.EglMakeCurrent(Display, Surface, Surface, _context))
                throw new InvalidOperationException("EGL make current failed");
        }

        public void Release()
        {
            try
            {
                if (Display != EGL14.EglNoDisplay)
                {
                    EGL14.EglMakeCurrent(Display, EGL14.EglNoSurface, EGL14.EglNoSurface, EGL14.EglNoContext);
                    if (Surface != EGL14.EglNoSurface)
                        EGL14.EglDestroySurface(Display, Surface);
                    if (_context != EGL14.EglNoContext)
                        EGL14.EglDestroyContext(Display, _context);
                    EGL14.EglReleaseThread();
                    EGL14.EglTerminate(Display);
                }
            }
            catch
            {
            }

            Display = EGL14.EglNoDisplay!;
            Surface = EGL14.EglNoSurface!;
            _context = EGL14.EglNoContext!;
        }
    }

    // ------------------------------------------------------------------
    // GL ES 2 drawing: video quad + overlay quad
    // ------------------------------------------------------------------

    private sealed class Renderer
    {
        private int _videoProgram;
        private int _overlayProgram;
        private int _overlayTextureId;
        private FloatBuffer _positions = null!;
        private FloatBuffer _videoTexCoords = null!;
        private FloatBuffer _overlayTexCoords = null!;

        public int VideoTextureId { get; private set; }

        public void Setup(int rotation)
        {
            _positions = ToBuffer(QuadPositions);
            _videoTexCoords = ToBuffer(VideoTexCoords);
            _overlayTexCoords = ToBuffer(OverlayTexCoords(rotation));

            _videoProgram = CreateProgram(VideoVertexShader, VideoFragmentShader);
            _overlayProgram = CreateProgram(OverlayVertexShader, OverlayFragmentShader);

            var textures = new int[2];
            GLES20.GlGenTextures(2, textures, 0);
            VideoTextureId = textures[0];
            _overlayTextureId = textures[1];

            GLES20.GlBindTexture(GLES11Ext.GlTextureExternalOes, VideoTextureId);
            GLES20.GlTexParameteri(GLES11Ext.GlTextureExternalOes, GLES20.GlTextureMinFilter, GLES20.GlLinear);
            GLES20.GlTexParameteri(GLES11Ext.GlTextureExternalOes, GLES20.GlTextureMagFilter, GLES20.GlLinear);
            GLES20.GlTexParameteri(GLES11Ext.GlTextureExternalOes, GLES20.GlTextureWrapS, GLES20.GlClampToEdge);
            GLES20.GlTexParameteri(GLES11Ext.GlTextureExternalOes, GLES20.GlTextureWrapT, GLES20.GlClampToEdge);

            GLES20.GlBindTexture(GLES20.GlTexture2d, _overlayTextureId);
            GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureMinFilter, GLES20.GlLinear);
            GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureMagFilter, GLES20.GlLinear);
            GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureWrapS, GLES20.GlClampToEdge);
            GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureWrapT, GLES20.GlClampToEdge);
        }

        public void UploadOverlay(ABitmap bitmap, bool first)
        {
            GLES20.GlBindTexture(GLES20.GlTexture2d, _overlayTextureId);
            if (first)
                GLUtils.TexImage2D(GLES20.GlTexture2d, 0, bitmap, 0);
            else
                GLUtils.TexSubImage2D(GLES20.GlTexture2d, 0, 0, 0, bitmap);
        }

        public void Draw(int width, int height, float[] stMatrix)
        {
            GLES20.GlViewport(0, 0, width, height);
            GLES20.GlClearColor(0f, 0f, 0f, 1f);
            GLES20.GlClear(GLES20.GlColorBufferBit);

            // Camera frame.
            GLES20.GlDisable(GLES20.GlBlend);
            GLES20.GlUseProgram(_videoProgram);
            GLES20.GlActiveTexture(GLES20.GlTexture0);
            GLES20.GlBindTexture(GLES11Ext.GlTextureExternalOes, VideoTextureId);
            GLES20.GlUniform1i(GLES20.GlGetUniformLocation(_videoProgram, "sTex"), 0);
            GLES20.GlUniformMatrix4fv(GLES20.GlGetUniformLocation(_videoProgram, "uSTMatrix"), 1, false, stMatrix, 0);
            BindAttribute(_videoProgram, "aPosition", _positions);
            BindAttribute(_videoProgram, "aTexCoord", _videoTexCoords);
            GLES20.GlDrawArrays(GLES20.GlTriangleStrip, 0, 4);

            // Overlay (premultiplied alpha).
            GLES20.GlEnable(GLES20.GlBlend);
            GLES20.GlBlendFunc(GLES20.GlOne, GLES20.GlOneMinusSrcAlpha);
            GLES20.GlUseProgram(_overlayProgram);
            GLES20.GlActiveTexture(GLES20.GlTexture0);
            GLES20.GlBindTexture(GLES20.GlTexture2d, _overlayTextureId);
            GLES20.GlUniform1i(GLES20.GlGetUniformLocation(_overlayProgram, "sTex"), 0);
            BindAttribute(_overlayProgram, "aPosition", _positions);
            BindAttribute(_overlayProgram, "aTexCoord", _overlayTexCoords);
            GLES20.GlDrawArrays(GLES20.GlTriangleStrip, 0, 4);
            GLES20.GlDisable(GLES20.GlBlend);
        }

        /// <summary>
        /// Maps each frame corner to the overlay bitmap (display orientation,
        /// row 0 = top) so the overlay is upright after the player rotates
        /// the frame clockwise by <paramref name="rotation"/> degrees.
        /// </summary>
        private static float[] OverlayTexCoords(int rotation)
        {
            var result = new float[8];
            for (var i = 0; i < 4; i++)
            {
                var fx = (QuadPositions[i * 2] + 1f) / 2f;      // frame x, 0 = left
                var fy = (1f - QuadPositions[i * 2 + 1]) / 2f;  // frame y, 0 = top
                (float s, float t) = rotation switch
                {
                    90 => (1f - fy, fx),
                    180 => (1f - fx, 1f - fy),
                    270 => (fy, 1f - fx),
                    _ => (fx, fy)
                };
                result[i * 2] = s;
                result[i * 2 + 1] = t;
            }

            return result;
        }

        private static void BindAttribute(int program, string name, FloatBuffer data)
        {
            var location = GLES20.GlGetAttribLocation(program, name);
            if (location < 0)
                return;
            data.Position(0);
            GLES20.GlEnableVertexAttribArray(location);
            GLES20.GlVertexAttribPointer(location, 2, GLES20.GlFloat, false, 8, data);
        }

        private static FloatBuffer ToBuffer(float[] values)
        {
            var buffer = ByteBuffer.AllocateDirect(values.Length * 4)!
                .Order(ByteOrder.NativeOrder()!)!
                .AsFloatBuffer()!;
            buffer.Put(values);
            buffer.Position(0);
            return buffer;
        }

        private static int CreateProgram(string vertexSource, string fragmentSource)
        {
            var vertex = CompileShader(GLES20.GlVertexShader, vertexSource);
            var fragment = CompileShader(GLES20.GlFragmentShader, fragmentSource);
            var program = GLES20.GlCreateProgram();
            GLES20.GlAttachShader(program, vertex);
            GLES20.GlAttachShader(program, fragment);
            GLES20.GlLinkProgram(program);
            var status = new int[1];
            GLES20.GlGetProgramiv(program, GLES20.GlLinkStatus, status, 0);
            if (status[0] == 0)
                throw new InvalidOperationException("GL link failed: " + GLES20.GlGetProgramInfoLog(program));
            return program;
        }

        private static int CompileShader(int type, string source)
        {
            var shader = GLES20.GlCreateShader(type);
            GLES20.GlShaderSource(shader, source);
            GLES20.GlCompileShader(shader);
            var status = new int[1];
            GLES20.GlGetShaderiv(shader, GLES20.GlCompileStatus, status, 0);
            if (status[0] == 0)
                throw new InvalidOperationException("GL shader failed: " + GLES20.GlGetShaderInfoLog(shader));
            return shader;
        }
    }

    private sealed class FrameListener : Java.Lang.Object, ASurfaceTexture.IOnFrameAvailableListener
    {
        private readonly SemaphoreSlim _signal;

        public FrameListener(SemaphoreSlim signal) => _signal = signal;

        public void OnFrameAvailable(ASurfaceTexture? surfaceTexture) => _signal.Release();
    }
}
