using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace GameMode.Windows.UI;

// Render the bundled, unchanged Google SVG paths without a browser or third-party renderer.
public sealed class MaterialIcon : Viewbox
{
    private static readonly Dictionary<string, Geometry> Cache = [];
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(MaterialIcon), new PropertyMetadata("home", Refresh));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(MaterialIcon), new PropertyMetadata(Brushes.White, Refresh));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public MaterialIcon() { Width = 20; Height = 20; IsHitTestVisible = false; Refresh(); }
    private static void Refresh(DependencyObject target, DependencyPropertyChangedEventArgs _) => ((MaterialIcon)target).Refresh();
    private void Refresh()
    {
        if (!Cache.TryGetValue(Kind, out var geometry))
        {
            if (Kind.Any(c => !char.IsAsciiLetter(c) && c != '_')) throw new ArgumentException("Unknown icon name.");
            var resource = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/GameMode;component/Assets/Material/{Kind}.svg"));
            using var stream = resource?.Stream ?? throw new InvalidOperationException($"Missing Material icon: {Kind}");
            var document = XDocument.Load(stream);
            var root = document.Root ?? throw new InvalidOperationException("Invalid SVG.");
            var box = ((string?)root.Attribute("viewBox") ?? "0 0 24 24").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            var group = new GeometryGroup { FillRule = FillRule.Nonzero };
            foreach (var path in root.Descendants().Where(e => e.Name.LocalName == "path" && (string?)e.Attribute("fill") != "none"))
                group.Children.Add(Geometry.Parse("F1 " + ((string?)path.Attribute("d") ?? "")));
            group.Transform = new MatrixTransform(24 / box[2], 0, 0, 24 / box[3], -box[0] * 24 / box[2], -box[1] * 24 / box[3]);
            group.Freeze();
            Cache.Add(Kind, group);
            geometry = group;
        }
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new System.Windows.Shapes.Path { Data = geometry, Fill = Fill });
        Child = canvas;
    }
}
