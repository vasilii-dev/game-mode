using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;

namespace GameMode.Windows.UI;

public static class ProcessIcons
{
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Workers = new(3);

    public static Image Create(string? path, double size = 24, string? executableName = null)
    {
        var image = new Image { Width = size, Height = size, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        if (path == null && executableName == null) return image;
        image.Loaded += async (_, _) =>
        {
            var file = path ?? await InstalledGamePaths.FindAsync(executableName!);
            if (file == null) return;
            var icon = await LoadAsync(file);
            if (icon != null) { image.Source = icon; image.Visibility = Visibility.Visible; }
        };
        return image;
    }

    public static Task<ImageSource?> LoadAsync(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || !Path.IsPathFullyQualified(path)) return Task.FromResult<ImageSource?>(null);
        if (Cache.Count > 512) Cache.Clear();
        return Cache.GetOrAdd(path, static file => Task.Run(async () =>
        {
            await Workers.WaitAsync();
            nint large = 0, small = 0;
            try
            {
                if (!File.Exists(file) || ExtractIconEx(file, -1, out large, out small, 0) == 0) return null;
                if (ExtractIconEx(file, 0, out large, out small, 1) == 0 || large == 0) return null;
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(large, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze();
                return (ImageSource)bitmap;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception or NotSupportedException) { return null; }
            finally { if (large != 0) DestroyIcon(large); if (small != 0 && small != large) DestroyIcon(small); Workers.Release(); }
        }));
    }

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out nint large, out nint small, uint count);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);
}
