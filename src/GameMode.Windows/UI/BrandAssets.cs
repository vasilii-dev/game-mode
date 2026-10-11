using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameMode.Core;

namespace GameMode.Windows.UI;

internal static class BrandAssets
{
    private static readonly Dictionary<string, BitmapImage> Cache = [];

    public static ImageSource Image(string name, int width = 256)
    {
        var key = $"{name}:{width}";
        if (Cache.TryGetValue(key, out var existing)) return existing;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri($"pack://application:,,,/GameMode;component/Assets/Brand/{name}");
        bitmap.DecodePixelWidth = width;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        Cache.Add(key, bitmap);
        return bitmap;
    }

    public static string StateFile(ModeState state) => state switch
    {
        ModeState.Active => "enabled.png",
        ModeState.Waiting => "waiting.png",
        _ => "off.png"
    };

    public static void SavePreview(Window window, string path)
    {
        window.UpdateLayout();
        var content = window.Content as FrameworkElement ?? window;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),
            (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }
}
