using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GameMode.Windows.UI;

internal static class Motion
{
    public static void Reveal(FrameworkElement element)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var transform = new TranslateTransform();
        element.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(4, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = easing });
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = easing });
    }
}
