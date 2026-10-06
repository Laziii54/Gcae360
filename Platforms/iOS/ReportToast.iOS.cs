using CoreGraphics;
using UIKit;

namespace CarScanner.Services.Reports;

public static partial class ReportToast
{
    static partial void PlatformShow(string message)
    {
        var window = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(w => w.IsKeyWindow);
        if (window is null)
            return;

        var label = new UILabel
        {
            Text = message,
            TextColor = UIColor.White,
            BackgroundColor = UIColor.FromRGBA(22, 24, 27, 235),
            TextAlignment = UITextAlignment.Center,
            Font = UIFont.BoldSystemFontOfSize(14),
            Lines = 2,
            Alpha = 0
        };
        label.Layer.CornerRadius = 14;
        label.ClipsToBounds = true;

        var width = Math.Min(window.Bounds.Width - 40, 360);
        var size = label.SizeThatFits(new CGSize(width - 24, 200));
        var height = size.Height + 20;
        var bottom = window.SafeAreaInsets.Bottom;
        label.Frame = new CGRect((window.Bounds.Width - width) / 2, window.Bounds.Height - bottom - height - 70, width, height);
        window.AddSubview(label);

        UIView.Animate(0.25, () => label.Alpha = 1, () =>
            UIView.Animate(0.35, 2.0, UIViewAnimationOptions.CurveEaseOut, () => label.Alpha = 0, label.RemoveFromSuperview));
    }
}
