using System.Diagnostics;
using System.Windows;

namespace GameMode.Windows.UI;

internal static class ProjectLinks
{
    public const string ProjectUrl = "https://github.com/vasilii-dev/game-mode";
    public static void OpenProject(Window owner) => Open(owner, ProjectUrl);
    public static void Open(Window owner, string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(owner, "Не удалось открыть браузер: " + exception.Message + "\n" + url,
                "Game Mode", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
