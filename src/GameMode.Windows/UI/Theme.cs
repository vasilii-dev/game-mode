using System.Windows;
using Application = System.Windows.Application;

namespace GameMode.Windows.UI;

internal static class Theme
{
    private static readonly Uri Source = new("/GameMode;component/UI/Theme.xaml", UriKind.Relative);

    public static void Apply(Application? application)
    {
        if (application == null || application.Resources.MergedDictionaries.Any(d => d.Source == Source)) return;
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = Source });
    }
}
