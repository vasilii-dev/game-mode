using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;

namespace GameMode.Windows.UI;

internal static class DialogStyles
{
    public static void Apply(Window window)
    {
        WindowAppearance.Apply(window);
        if (System.Windows.Application.Current?.MainWindow is MainWindow main)
            foreach (var type in new[] { typeof(Button), typeof(CheckBox), typeof(TextBox), typeof(ScrollViewer), typeof(System.Windows.Controls.ListBox), typeof(ListBoxItem), typeof(System.Windows.Controls.Primitives.ScrollBar) })
                window.Resources[type] = main.FindResource(type);
    }
}
