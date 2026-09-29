using Android.Views;
using CarScanner.Controls;
using Microsoft.Maui.Handlers;

namespace CarScanner.Droid.PerformanceVideo;

/// <summary>Android handler: native TextureView + Camera2 controller for <see cref="PerformanceCameraView"/>.</summary>
public sealed class PerformanceCameraViewHandler : ViewHandler<PerformanceCameraView, TextureView>
{
    public static readonly IPropertyMapper<PerformanceCameraView, PerformanceCameraViewHandler> Mapper =
        new PropertyMapper<PerformanceCameraView, PerformanceCameraViewHandler>(ViewMapper);

    private AndroidPerformanceCameraController? _controller;

    public PerformanceCameraViewHandler() : base(Mapper)
    {
    }

    protected override TextureView CreatePlatformView() => new(Context);

    protected override void ConnectHandler(TextureView platformView)
    {
        base.ConnectHandler(platformView);
        _controller = new AndroidPerformanceCameraController(Context, platformView);
        VirtualView.Controller = _controller;
    }

    protected override void DisconnectHandler(TextureView platformView)
    {
        if (VirtualView is not null)
            VirtualView.Controller = null;

        // Released only — not disposed: Android may still call the
        // TextureView listener once while the view is being torn down.
        _controller?.Release();
        _controller = null;
        base.DisconnectHandler(platformView);
    }
}
