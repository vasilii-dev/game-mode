using System.Windows;
using System.Windows.Media.Animation;

namespace GameMode.Windows.UI;

public partial class AuthorsWindow : Window
{
    public AuthorsWindow()
    {
        InitializeComponent();
        DialogStyles.Apply(this);
        Loaded += (_, _) =>
        {
            if (SystemParameters.ClientAreaAnimation)
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        };
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { Close(); e.Handled = true; } };
    }

    private void OpenProfile_Click(object sender, RoutedEventArgs e) => ProjectLinks.Open(this, "https://github.com/vasilii-dev");
    private void OpenProject_Click(object sender, RoutedEventArgs e) => ProjectLinks.OpenProject(this);
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
