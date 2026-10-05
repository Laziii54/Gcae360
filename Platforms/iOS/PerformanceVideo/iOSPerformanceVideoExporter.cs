#if IOS
using AVFoundation;
using CarScanner.Services.PerformanceVideo;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using Photos;
using UIKit;

namespace CarScanner.Platforms.iOS.PerformanceVideo;

/// <summary>Exports an iPhone recording and saves the user-approved result to Photos.</summary>
public sealed class iOSPerformanceVideoExporter : IPerformanceVideoExporter
{
    public bool IsSupported => true;

    public async Task ComposeWithOverlayAsync(
        string inputPath,
        string outputPath,
        PerformanceVideoTelemetry telemetry,
        PerformanceOverlayStyle style,
        IProgress<double>? progress,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("The recorded video was not found.", inputPath);

        if (File.Exists(outputPath))
            File.Delete(outputPath);

        using var asset = AVAsset.FromUrl(NSUrl.FromFilename(inputPath));
        using var videoComposition = AVMutableVideoComposition.Create(asset)
            ?? throw new InvalidOperationException("The iPhone cannot prepare this video for telemetry.");

        var durationSeconds = Math.Max(0.1, asset.Duration.Seconds);
        using var overlayLayers = CreateOverlayLayers(
            videoComposition.RenderSize,
            durationSeconds,
            telemetry,
            style);
        videoComposition.AnimationTool = overlayLayers.AnimationTool;

        using var exporter = AVAssetExportSession.FromAsset(
            asset,
            (string?)AVAssetExportSessionPreset.HighestQuality.GetConstant() ?? "AVAssetExportPresetHighestQuality")
            ?? throw new InvalidOperationException("The iPhone cannot export this recording.");

        exporter.OutputUrl = NSUrl.FromFilename(outputPath);
        exporter.OutputFileType = AVFileTypes.Mpeg4.GetConstant();
        exporter.ShouldOptimizeForNetworkUse = true;
        exporter.VideoComposition = videoComposition;

        progress?.Report(0.05);
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() =>
        {
            exporter.CancelExport();
            completion.TrySetCanceled(token);
        });

        exporter.ExportAsynchronously(() => completion.TrySetResult());
        while (!completion.Task.IsCompleted)
        {
            await Task.WhenAny(completion.Task, Task.Delay(150, token)).ConfigureAwait(false);
            progress?.Report(Math.Clamp(exporter.Progress, 0.05f, 0.98f));
        }
        await completion.Task.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();

        if (exporter.Status != AVAssetExportSessionStatus.Completed)
            throw new InvalidOperationException(
                exporter.Error?.LocalizedDescription ?? "The iPhone video export failed.");

        progress?.Report(1.0);

    }

    private static OverlayLayerSet CreateOverlayLayers(
        CGSize renderSize,
        double durationSeconds,
        PerformanceVideoTelemetry telemetry,
        PerformanceOverlayStyle style)
    {
        var width = Math.Max(1, renderSize.Width);
        var height = Math.Max(1, renderSize.Height);
        var unit = Math.Min(width, height);
        var margin = unit * 0.045;

        var parent = new CALayer { Frame = new CGRect(0, 0, width, height) };
        var video = new CALayer { Frame = parent.Bounds };
        var overlay = new CALayer { Frame = parent.Bounds };
        parent.AddSublayer(video);
        parent.AddSublayer(overlay);

        var barHeight = unit * 0.14;
        var bar = new CALayer
        {
            Frame = new CGRect(margin, margin, width - margin * 2, barHeight),
            BackgroundColor = ParseColor(style.PanelColor),
            CornerRadius = (nfloat)(unit * 0.035)
        };
        overlay.AddSublayer(bar);

        var columnWidth = bar.Bounds.Width / PerformanceSplitTracker.Targets.Length;
        var dynamicLayers = new List<CATextLayer>();
        for (var i = 0; i < PerformanceSplitTracker.Targets.Length; i++)
        {
            if (i > 0)
            {
                var divider = new CALayer
                {
                    Frame = new CGRect(columnWidth * i, barHeight * 0.22, Math.Max(1, unit * 0.002), barHeight * 0.56),
                    BackgroundColor = ParseColor(style.BorderColor)
                };
                bar.AddSublayer(divider);
            }

            var title = TextLayer(
                $"0–{PerformanceSplitTracker.Targets[i]}",
                new CGRect(columnWidth * i, barHeight * 0.53, columnWidth, barHeight * 0.32),
                unit * 0.03,
                style.SubTextColor,
                false);
            bar.AddSublayer(title);

            var value = TextLayer(
                "—",
                new CGRect(columnWidth * i, barHeight * 0.12, columnWidth, barHeight * 0.42),
                unit * 0.048,
                style.AccentColor,
                true);
            bar.AddSublayer(value);
            dynamicLayers.Add(value);
        }

        var speedPanelWidth = unit * 0.39;
        var speedPanelHeight = unit * 0.22;
        var speedPanel = new CALayer
        {
            Frame = new CGRect(
                margin + unit * 0.025,
                margin + barHeight + unit * 0.035,
                speedPanelWidth,
                speedPanelHeight),
            BackgroundColor = ParseColor(style.PanelColor),
            CornerRadius = (nfloat)(unit * 0.04)
        };
        overlay.AddSublayer(speedPanel);

        var speed = TextLayer(
            "—",
            new CGRect(unit * 0.025, unit * 0.015, speedPanelWidth * 0.66, speedPanelHeight * 0.86),
            unit * 0.17,
            style.TextColor,
            true,
            CATextLayerAlignmentMode.Left);
        speedPanel.AddSublayer(speed);
        dynamicLayers.Add(speed);

        speedPanel.AddSublayer(TextLayer(
            telemetry.SpeedUnit,
            new CGRect(speedPanelWidth * 0.67, unit * 0.035, speedPanelWidth * 0.30, speedPanelHeight * 0.42),
            unit * 0.045,
            style.SubTextColor,
            false,
            CATextLayerAlignmentMode.Left));

        var frames = telemetry.Snapshot();
        AddTextAnimation(
            speed,
            frames,
            durationSeconds,
            frame => frame.Speed?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—");

        for (var i = 0; i < PerformanceSplitTracker.Targets.Length; i++)
        {
            var splitIndex = i;
            AddTextAnimation(
                dynamicLayers[i],
                frames,
                durationSeconds,
                frame => splitIndex < frame.Splits.Length && frame.Splits[splitIndex] is { } seconds
                    ? $"{seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} {telemetry.SecondsUnit}"
                    : "—");
        }

        var tool = AVVideoCompositionCoreAnimationTool.FromLayer(video, parent);
        return new OverlayLayerSet(parent, video, overlay, tool);
    }

    /// <summary>Value of AVFoundation's AVCoreAnimationBeginTimeAtZero constant.</summary>
    private const double AVCoreAnimationBeginTimeAtZero = 1e-100;

    private static CATextLayer TextLayer(
        string text,
        CGRect frame,
        double fontSize,
        string color,
        bool bold,
        CATextLayerAlignmentMode alignment = CATextLayerAlignmentMode.Center)
    {
        var layer = new CATextLayer
        {
            Frame = frame,
            String = text,
            FontSize = (nfloat)fontSize,
            ForegroundColor = ParseColor(color),
            TextAlignmentMode = alignment,
            ContentsScale = 2,
            Wrapped = false
        };
        layer.SetFont(bold ? "Helvetica-Bold" : "Helvetica");
        return layer;
    }

    private static void AddTextAnimation(
        CATextLayer layer,
        PerformanceOverlayFrame[] sourceFrames,
        double durationSeconds,
        Func<PerformanceOverlayFrame, string> valueSelector)
    {
        var frames = sourceFrames.Length == 0
            ? new[] { new PerformanceOverlayFrame(0, null, new double?[PerformanceSplitTracker.Targets.Length]) }
            : sourceFrames;

        var values = new List<NSString>();
        var times = new List<NSNumber>();
        void Add(double normalizedTime, string value)
        {
            if (values.Count > 0 && string.Equals(values[^1].ToString(), value, StringComparison.Ordinal))
                return;
            values.Add(new NSString(value));
            times.Add(NSNumber.FromDouble(Math.Clamp(normalizedTime, 0, 1)));
        }

        Add(0, valueSelector(frames[0]));
        foreach (var frame in frames)
            Add(frame.TimeMs / (durationSeconds * 1000d), valueSelector(frame));

        // Discrete key-frame animations need one more key time than values
        // (Apple: "the array should have one more entry than the values array"),
        // ending at 1.0. With equal counts Core Animation ignores the key times.
        times.Add(NSNumber.FromDouble(1));

        layer.String = values[0].ToString();
        var animation = CAKeyFrameAnimation.FromKeyPath("string");
        animation.SetValues(values.ToArray());
        animation.KeyTimes = times.ToArray();
        animation.CalculationMode = CAAnimation.AnimationDiscrete;
        // In a video export, BeginTime 0 means "now" (CACurrentMediaTime), so the
        // animation never runs and the layer keeps its first value (0 km/h).
        // AVCoreAnimationBeginTimeAtZero (1e-100) anchors it to the video start.
        animation.BeginTime = AVCoreAnimationBeginTimeAtZero;
        animation.Duration = durationSeconds;
        animation.RemovedOnCompletion = false;
        animation.FillMode = CAFillMode.Forwards;
        layer.AddAnimation(animation, "gcar360-telemetry");

        foreach (var value in values)
            value.Dispose();
        foreach (var time in times)
            time.Dispose();
        animation.Dispose();
    }

    private static CGColor ParseColor(string hex)
    {
        var value = hex.TrimStart('#');
        byte a = 255;
        var offset = 0;
        if (value.Length == 8)
        {
            a = Convert.ToByte(value[..2], 16);
            offset = 2;
        }
        if (value.Length - offset != 6)
            return UIColor.Black.CGColor;
        return UIColor.FromRGBA(
            Convert.ToByte(value.Substring(offset, 2), 16),
            Convert.ToByte(value.Substring(offset + 2, 2), 16),
            Convert.ToByte(value.Substring(offset + 4, 2), 16),
            a).CGColor;
    }

    private sealed class OverlayLayerSet : IDisposable
    {
        public OverlayLayerSet(
            CALayer parent,
            CALayer video,
            CALayer overlay,
            AVVideoCompositionCoreAnimationTool animationTool)
        {
            Parent = parent;
            Video = video;
            Overlay = overlay;
            AnimationTool = animationTool;
        }

        public CALayer Parent { get; }
        public CALayer Video { get; }
        public CALayer Overlay { get; }
        public AVVideoCompositionCoreAnimationTool AnimationTool { get; }

        public void Dispose()
        {
            AnimationTool.Dispose();
            Overlay.Dispose();
            Video.Dispose();
            Parent.Dispose();
        }
    }

    public async Task<bool> EnsureSavePermissionAsync()
    {
        var status = PHPhotoLibrary.GetAuthorizationStatus(PHAccessLevel.AddOnly);
        if (status is PHAuthorizationStatus.Authorized or PHAuthorizationStatus.Limited)
            return true;
        if (status is PHAuthorizationStatus.Denied or PHAuthorizationStatus.Restricted)
            return false;

        var completion = new TaskCompletionSource<PHAuthorizationStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        PHPhotoLibrary.RequestAuthorization(
            PHAccessLevel.AddOnly,
            result => completion.TrySetResult(result));
        status = await completion.Task.ConfigureAwait(false);
        return status is PHAuthorizationStatus.Authorized or PHAuthorizationStatus.Limited;
    }

    public async Task SaveToGalleryAsync(
        string filePath,
        string displayName,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!await EnsureSavePermissionAsync().ConfigureAwait(false))
            throw new UnauthorizedAccessException("Permission to save video to Photos was denied.");

        var completion = new TaskCompletionSource<NSError?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        PHPhotoLibrary.SharedPhotoLibrary.PerformChanges(
            () => PHAssetChangeRequest.FromVideo(NSUrl.FromFilename(filePath)),
            (success, error) => completion.TrySetResult(success ? null : error));

        var saveError = await completion.Task.ConfigureAwait(false);
        if (saveError is not null)
            throw new InvalidOperationException(saveError.LocalizedDescription);
    }
}
#endif
