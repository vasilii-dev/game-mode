using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using CheckBox = System.Windows.Controls.CheckBox;
using Path = System.Windows.Shapes.Path;

namespace GameMode.Windows.UI;

public static class CheckAnimation
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(CheckAnimation), new PropertyMetadata(false, Changed));
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not CheckBox box) return;
        if ((bool)args.NewValue) { box.Loaded += Loaded; box.Checked += Toggled; box.Unchecked += Toggled; }
        else { box.Loaded -= Loaded; box.Checked -= Toggled; box.Unchecked -= Toggled; }
    }
    private static void Loaded(object sender, RoutedEventArgs _) => Update((CheckBox)sender, false);
    private static void Toggled(object sender, RoutedEventArgs _) => Update((CheckBox)sender, ((CheckBox)sender).IsLoaded);
    private static void Update(CheckBox box, bool animate)
    {
        box.ApplyTemplate();
        if (box.Template.FindName("CheckMark", box) is not Path mark) return;
        var target = box.IsChecked == true ? 0d : 20d;
        var from = mark.StrokeDashOffset;
        mark.BeginAnimation(Path.StrokeDashOffsetProperty, null);
        mark.StrokeDashOffset = target;
        if (animate && SystemParameters.ClientAreaAnimation)
            mark.BeginAnimation(Path.StrokeDashOffsetProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(130)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
}
