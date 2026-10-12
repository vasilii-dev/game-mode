using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        // Dash values are measured in stroke widths. Match the actual check length so
        // the entire animation draws the check instead of spending most of it in a gap.
        var length = 0d;
        foreach (var figure in mark.Data.GetFlattenedPathGeometry().Figures)
        {
            var previous = figure.StartPoint;
            foreach (var segment in figure.Segments)
            {
                IEnumerable<System.Windows.Point> points = segment switch
                {
                    PolyLineSegment line => line.Points,
                    LineSegment line => [line.Point],
                    _ => []
                };
                foreach (var point in points) { length += (point - previous).Length; previous = point; }
            }
        }
        var hidden = length / mark.StrokeThickness + 1; // Leave room for rounded stroke caps.
        var from = mark.StrokeDashOffset;
        mark.StrokeDashArray = new DoubleCollection([hidden, hidden]);
        var target = box.IsChecked == true ? 0d : hidden;
        mark.BeginAnimation(Path.StrokeDashOffsetProperty, null);
        mark.StrokeDashOffset = target;
        if (animate && SystemParameters.ClientAreaAnimation)
            mark.BeginAnimation(Path.StrokeDashOffsetProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(140)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
}
