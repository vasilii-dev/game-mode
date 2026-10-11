using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using FontFamily = System.Windows.Media.FontFamily;

namespace GameMode.Windows.UI;

internal sealed class RiskDialog : Window
{
    public RiskDialog(bool requireConsent = true, bool standalonePreview = false)
    {
        Title = requireConsent ? "Первый запуск Game Mode" : "Game Mode — предупреждение";
        Width = 670; SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 48);
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = MainWindow.Brush("#0B0B0B");
        Foreground = MainWindow.Brush("#F0F0F0");
        FontFamily = new FontFamily("Segoe UI");
        DialogStyles.Apply(this);
        var panel = new StackPanel { Margin = new Thickness(28) };
        var header = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        header.Children.Add(new System.Windows.Controls.Image { Source = BrandAssets.Image("exclamation.png"), Width = 58, Height = 58, Margin = new Thickness(0, 0, 15, 0) });
        var title = MainWindow.Text("Ахтунг, важно!", 29, "#F0F0F0");
        title.FontWeight = FontWeights.SemiBold;
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        panel.Children.Add(header);

        AddParagraph(panel, "Программа Game Mode закрывает все процессы которые превышают использование ОЗУ (по умолчанию) - 200 МиБ.");
        AddParagraph(panel, "Игры, процессы из списка исключений, системные компоненты Windows и службы защищены. Процессы с непроверенными сведениями пропускаются.", true);
        AddParagraph(panel, "Убедитесь, что перед работой с программой Вы настроили все программы для того, чтобы не потерять в них изменения. Программа \"убивает\" процессы, и в случае чего вернутся в программу чтобы все сохранить - не получится.");
        AddParagraph(panel, "В программе предусмотрен \"Список исключений\" - программы, которые не закроются даже при включении режима, это может предотвратить потерю Ваших данных.");
        AddParagraph(panel, "После игры Game Mode попробует запустить подходящие закрытые им приложения заново, свёрнутыми. Прежнее состояние и несохранённые данные не восстанавливаются.", true);
        AddParagraph(panel, "Спасибо, что скачали программу. Не забудьте поставить звезду на GitHub, это очень поможет развитию программы!");

        var actions = new Grid { Margin = new Thickness(0, 23, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var project = new Button { Content = "Открыть GitHub", Padding = new Thickness(13, 11, 13, 11), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        project.Click += (_, _) => ProjectLinks.OpenProject(this);
        actions.Children.Add(project);
        var continueButton = new Button
        {
            Content = requireConsent ? "Я согласен, поехали!" : "Закрыть",
            IsDefault = true, Padding = new Thickness(18, 11, 18, 11),
            Background = MainWindow.Brush("#294B32"), Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = MainWindow.Brush("#52795B"), FontSize = 14
        };
        continueButton.Click += (_, _) => { if (standalonePreview) Close(); else DialogResult = true; };
        Grid.SetColumn(continueButton, 1);
        actions.Children.Add(continueButton);
        panel.Children.Add(actions);
        Content = new ScrollViewer { Content = panel, Background = Background, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private static void AddParagraph(StackPanel panel, string content, bool note = false)
    {
        var text = MainWindow.Text(content, note ? 12 : 14, note ? "#B3C6B7" : "#D5D5D5");
        text.TextWrapping = TextWrapping.Wrap;
        text.LineHeight = note ? 19 : 22;
        text.Margin = new Thickness(0, note ? 10 : 18, 0, 0);
        panel.Children.Add(text);
    }
}
