using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace GameMode.Windows.UI;

internal static class WindowAppearance
{
    public static void Apply(Window window)
    {
        window.Icon = BrandAssets.Image("Icon.png", 64);
        void DarkCaption()
        {
            var enabled = 1;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == nint.Zero) return;
            // Unsupported DWM attributes simply leave the normal Windows caption in place.
            _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        }
        if (new WindowInteropHelper(window).Handle != nint.Zero) DarkCaption();
        else window.SourceInitialized += (_, _) => DarkCaption();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
