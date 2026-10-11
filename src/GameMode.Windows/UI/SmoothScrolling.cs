using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GameMode.Windows.UI;

public static class SmoothScrolling
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(SmoothScrolling), new PropertyMetadata(false, EnabledChanged));
    private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached("Offset", typeof(double), typeof(SmoothScrolling), new PropertyMetadata(0d, OffsetChanged));
    private static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached("Target", typeof(double), typeof(SmoothScrolling), new PropertyMetadata(double.NaN));
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    private static void EnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ScrollViewer viewer) return;
        if ((bool)args.NewValue) { viewer.PreviewMouseWheel += Wheel; viewer.PreviewMouseDown += Cancel; viewer.PreviewKeyDown += Cancel; }
        else { viewer.PreviewMouseWheel -= Wheel; viewer.PreviewMouseDown -= Cancel; viewer.PreviewKeyDown -= Cancel; }
    }
    private static void OffsetChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => ((ScrollViewer)target).ScrollToVerticalOffset((double)args.NewValue);
    private static void Wheel(object sender, MouseWheelEventArgs args)
    {
        if (sender is not ScrollViewer viewer || Keyboard.Modifiers != ModifierKeys.None || viewer.ScrollableHeight <= 0) return;
        var node = args.OriginalSource as DependencyObject;
        while (node != null)
        {
            if (node is ScrollViewer inner && inner.ScrollableHeight > 0) { if (inner != viewer) return; break; }
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        var priorTarget = (double)viewer.GetValue(TargetProperty);
        var basis = double.IsNaN(priorTarget) || Math.Abs(viewer.VerticalOffset - priorTarget) > 200 ? viewer.VerticalOffset : priorTarget;
        var lines = SystemParameters.WheelScrollLines;
        if (lines == 0) return;
        var distance = lines < 0 ? viewer.ViewportHeight : Math.Max(1, lines) * 18d;
        var target = Math.Clamp(basis - args.Delta / 120d * distance, 0, viewer.ScrollableHeight);
        if (Math.Abs(target - viewer.VerticalOffset) < 0.1) return;
        args.Handled = true;
        var from = viewer.VerticalOffset;
        viewer.BeginAnimation(OffsetProperty, null);
        viewer.SetValue(OffsetProperty, from);
        viewer.SetValue(TargetProperty, target);
        if (!SystemParameters.ClientAreaAnimation) { viewer.SetValue(OffsetProperty, target); return; }
        var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        animation.Completed += (_, _) => { viewer.SetValue(OffsetProperty, target); viewer.BeginAnimation(OffsetProperty, null); viewer.SetValue(TargetProperty, double.NaN); };
        viewer.BeginAnimation(OffsetProperty, animation);
    }
    private static void Cancel(object sender, RoutedEventArgs _)
    {
        var viewer = (ScrollViewer)sender;
        viewer.SetValue(OffsetProperty, viewer.VerticalOffset);
        viewer.BeginAnimation(OffsetProperty, null);
        viewer.SetValue(TargetProperty, double.NaN);
    }
}
