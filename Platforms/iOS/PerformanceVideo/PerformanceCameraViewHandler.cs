#if IOS
using CarScanner.Controls;
using Microsoft.Maui.Handlers;
using UIKit;

namespace CarScanner.Platforms.iOS.PerformanceVideo;

public sealed class PerformanceCameraViewHandler : ViewHandler<PerformanceCameraView, CameraPreviewView>
{
    public static readonly IPropertyMapper<PerformanceCameraView, PerformanceCameraViewHandler> Mapper =
        new PropertyMapper<PerformanceCameraView, PerformanceCameraViewHandler>(ViewMapper);

    private iOSPerformanceCameraController? _controller;

    public PerformanceCameraViewHandler() : base(Mapper)
    {
    }

    protected override CameraPreviewView CreatePlatformView() => new();

    protected override void ConnectHandler(CameraPreviewView platformView)
    {
        base.ConnectHandler(platformView);
        _controller = new iOSPerformanceCameraController(platformView);
        VirtualView.Controller = _controller;
    }

    protected override void DisconnectHandler(CameraPreviewView platformView)
    {
        if (VirtualView is not null)
            VirtualView.Controller = null;

        _controller?.Release();
        _controller = null;
        base.DisconnectHandler(platformView);
    }
}

public sealed class CameraPreviewView : UIView
{
    public AVFoundation.AVCaptureVideoPreviewLayer? PreviewLayer { get; set; }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        if (PreviewLayer is not null)
            PreviewLayer.Frame = Bounds;
    }
}
#endif
