using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace QuickStartLauncher.Services;
internal static class WindowMotion
{
    public static void Enter(Window window)
    {
        if (window.Content is not FrameworkElement content || !SystemParameters.ClientAreaAnimation) return;
        content.RenderTransformOrigin = new Point(.5, .5);
        var scale = new ScaleTransform(1, 1); content.RenderTransform = scale;
        var animation = new DoubleAnimation(.98, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation); scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        content.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }
}
