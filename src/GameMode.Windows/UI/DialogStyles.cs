using System.Windows;

namespace GameMode.Windows.UI;

internal static class DialogStyles
{
    public static void Apply(Window window)
    {
        WindowAppearance.Apply(window);
        Theme.Apply(System.Windows.Application.Current);
    }
}
