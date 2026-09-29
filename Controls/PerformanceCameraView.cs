using CarScanner.Services.PerformanceVideo;

namespace CarScanner.Controls;

/// <summary>
/// Full-screen camera preview for Performance Video. The platform handler
/// creates the native preview and assigns <see cref="Controller"/>.
/// </summary>
public sealed class PerformanceCameraView : View
{
    private IPerformanceCameraController? _controller;

    public IPerformanceCameraController? Controller
    {
        get => _controller;
        set
        {
            _controller = value;
            ControllerChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ControllerChanged;
}
