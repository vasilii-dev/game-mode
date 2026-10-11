using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameMode.Core;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using TextBox = System.Windows.Controls.TextBox;
using FontFamily = System.Windows.Media.FontFamily;
using Orientation = System.Windows.Controls.Orientation;

namespace GameMode.Windows.UI;

internal sealed class ProcessPicker : Window
{
    private readonly IReadOnlyList<ProcessInfo> _processes;
    private readonly ListBox _list;
    private readonly TextBox _search;
    private readonly Button _choose;
    public ProcessInfo? SelectedProcess { get; private set; }

    public ProcessPicker(IReadOnlyList<ProcessInfo> processes)
    {
        _processes = processes.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Id).ToArray();
        Title = "Выбрать запущенный процесс"; Width = 800; Height = 620; MinWidth = 640; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = MainWindow.Brush("#151D30"); Foreground = MainWindow.Brush("#F5F6FF"); FontFamily = new FontFamily("Segoe UI");
        DialogStyles.Apply(this);
        var layout = new Grid { Margin = new Thickness(24) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(MainWindow.Text("Запущенные процессы", 24, "#F5F6FF"));
        var hint = MainWindow.Text("Выбирается полный путь к .exe. Процессы без доступного пути нельзя добавить этим способом.", 13, "#AAB4CC");
        hint.Margin = new Thickness(0, 10, 0, 14); hint.TextWrapping = TextWrapping.Wrap; Grid.SetRow(hint, 1); layout.Children.Add(hint);
        _search = new TextBox { Padding = new Thickness(10), FontSize = 14, Background = MainWindow.Brush("#0F1526"), Foreground = Foreground, CaretBrush = Foreground, BorderBrush = MainWindow.Brush("#596786"), Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetName(_search, "Поиск по имени или пути процесса");
        _search.ToolTip = "Поиск по имени или пути";
        _search.TextChanged += (_, _) => Filter(); Grid.SetRow(_search, 2); layout.Children.Add(_search);
        _list = new ListBox { Background = MainWindow.Brush("#0F1526"), Foreground = Foreground, BorderBrush = MainWindow.Brush("#34435F"), HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch };
        _list.SelectionChanged += (_, _) => _choose!.IsEnabled = _list.SelectedItem is ListBoxItem { Tag: ProcessInfo { FullPath: not null } };
        _list.MouseDoubleClick += (_, _) => Choose();
        _list.KeyDown += (_, e) => { if (e.Key == Key.Enter) Choose(); };
        Grid.SetRow(_list, 3); layout.Children.Add(_list);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
        var cancel = new Button { Content = "Отмена", IsCancel = true, Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(0, 0, 10, 0) };
        _choose = new Button { Content = "Добавить процесс", IsDefault = true, IsEnabled = false, Padding = new Thickness(18, 9, 18, 9), Background = MainWindow.Brush("#566EDB"), Foreground = System.Windows.Media.Brushes.White };
        _choose.Click += (_, _) => Choose(); actions.Children.Add(cancel); actions.Children.Add(_choose);
        Grid.SetRow(actions, 4); layout.Children.Add(actions); Content = layout;
        Filter(); Loaded += (_, _) => _search.Focus();
    }

    private void Filter()
    {
        _list.Items.Clear();
        var filter = _search.Text.Trim();
        foreach (var process in _processes.Where(p => p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || p.FullPath?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true))
        {
            var panel = new StackPanel { Margin = new Thickness(9, 7, 9, 7) };
            panel.Children.Add(MainWindow.Text($"{process.Name}   ·   PID {process.Id}   ·   {process.WorkingSetMiB:N0} МиБ", 14, "#F5F6FF"));
            var path = MainWindow.Text(process.FullPath ?? "Путь недоступен: процесс защищён или нужны дополнительные права", 12, "#AAB4CC");
            path.TextWrapping = TextWrapping.Wrap; panel.Children.Add(path);
            var item = new ListBoxItem { Content = panel, Tag = process, IsEnabled = process.FullPath != null, ToolTip = process.ProtectedReason ?? process.FullPath };
            _list.Items.Add(item);
        }
    }

    private void Choose()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: ProcessInfo { FullPath: not null } process }) return;
        SelectedProcess = process;
        DialogResult = true;
    }
}
