using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using FontFamily = System.Windows.Media.FontFamily;

namespace GameMode.Windows.UI;

internal sealed class RiskDialog : Window
{
    public RiskDialog()
    {
        Title = "Первый запуск Game Mode";
        Width = 570; SizeToContent = SizeToContent.Height; MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 48); ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = MainWindow.Brush("#151D30");
        Foreground = MainWindow.Brush("#F5F6FF");
        FontFamily = new FontFamily("Segoe UI");
        DialogStyles.Apply(this);
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(MainWindow.Text("Перед первой игрой", 28, "#F5F6FF"));
        var explanation = MainWindow.Text("Game Mode закрывает подходящие процессы, когда их Working Set превышает установленный порог — по умолчанию 200 МиБ.\n\nНесохранённые данные могут быть потеряны. После игры программа попробует повторно запустить подходящие закрытые приложения свёрнутыми. Их прежнее состояние не восстанавливается.\n\nСначала проверьте список исключений: добавьте всё, что нельзя закрывать. Игры, системные компоненты Windows и службы защищены автоматически.", 14, "#CBD5EF");
        explanation.TextWrapping = TextWrapping.Wrap;
        explanation.Margin = new Thickness(0, 18, 0, 18);
        panel.Children.Add(explanation);
        var consent = new CheckBox { Content = "Я понимаю риск потери несохранённых данных", Foreground = Foreground, FontSize = 14, Margin = new Thickness(0, 0, 0, 20) };
        panel.Children.Add(consent);
        var continueButton = new Button { Content = "Проверить настройки", IsDefault = true, IsEnabled = false, Padding = new Thickness(18, 10, 18, 10), Background = MainWindow.Brush("#566EDB"), Foreground = System.Windows.Media.Brushes.White, BorderBrush = MainWindow.Brush("#7D91EA"), FontSize = 15 };
        consent.Checked += (_, _) => continueButton.IsEnabled = true;
        consent.Unchecked += (_, _) => continueButton.IsEnabled = false;
        continueButton.Click += (_, _) => { DialogResult = true; };
        panel.Children.Add(continueButton);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
}
