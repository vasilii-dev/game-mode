using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using GameMode.Core;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using TextBox = System.Windows.Controls.TextBox;
using FontFamily = System.Windows.Media.FontFamily;
using SelectionMode = System.Windows.Controls.SelectionMode;

namespace GameMode.Windows.UI;

internal sealed class ProcessPicker : Window
{
    private readonly IReadOnlyList<ProcessInfo> _processes;
    private readonly ListBox _list;
    private readonly TextBox _search;
    private readonly Button _choose;
    private readonly TextBlock _count;
    private readonly HashSet<string> _selected = [];
    private bool _filtering;
    private readonly bool _standalonePreview;
    public IReadOnlyList<ProcessInfo> SelectedProcesses { get; private set; } = [];

    public ProcessPicker(IReadOnlyList<ProcessInfo> processes, bool standalonePreview = false)
    {
        _standalonePreview = standalonePreview;
        NameScope.SetNameScope(this, new NameScope());
        _processes = processes.OrderByDescending(p => p.WorkingSetBytes).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        Title = "Выбрать запущенные процессы"; Width = 850; Height = 650; MinWidth = 680; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = MainWindow.Brush("#111111"); Foreground = MainWindow.Brush("#F0F0F0"); FontFamily = new FontFamily("Segoe UI");
        DialogStyles.Apply(this);
        var layout = new Grid { Margin = new Thickness(24) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            layout.RowDefinitions.Add(new RowDefinition { Height = height });
        layout.Children.Add(MainWindow.Text("Запущенные процессы", 24, "#F0F0F0"));
        var hint = MainWindow.Text("Ctrl — несколько процессов, Shift — диапазон, Ctrl+A — все видимые. Выбор сохраняется при поиске; правила используют полный путь к .exe.", 12, "#AAAAAA");
        hint.Margin = new Thickness(0, 10, 0, 14); hint.TextWrapping = TextWrapping.Wrap; Grid.SetRow(hint, 1); layout.Children.Add(hint);
        _search = new TextBox { Name = "ProcessSearch", Padding = new Thickness(10), FontSize = 14, Margin = new Thickness(0, 0, 0, 12) };
        RegisterName(_search.Name, _search);
        AutomationProperties.SetName(_search, "Поиск по имени или пути процесса");
        _search.TextChanged += (_, _) => Filter(); Grid.SetRow(_search, 2); layout.Children.Add(_search);
        _list = new ListBox { Name = "ProcessList", Background = Background, Foreground = Foreground, BorderThickness = new Thickness(0), SelectionMode = SelectionMode.Extended, HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch };
        RegisterName(_list.Name, _list);
        VirtualizingPanel.SetScrollUnit(_list, ScrollUnit.Pixel);
        _list.SelectionChanged += (_, e) =>
        {
            if (_filtering) return;
            foreach (ListBoxItem item in e.RemovedItems) if (item.Tag is ProcessInfo p) _selected.Remove(p.Identity);
            foreach (ListBoxItem item in e.AddedItems) if (item.Tag is ProcessInfo p) _selected.Add(p.Identity);
            UpdateCount();
        };
        _list.MouseDoubleClick += (_, _) => Choose();
        _list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { _list.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.Enter) { Choose(); e.Handled = true; }
        };
        Grid.SetRow(_list, 3); layout.Children.Add(_list);
        var actions = new DockPanel { Margin = new Thickness(0, 15, 0, 0) };
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        DockPanel.SetDock(buttons, Dock.Right);
        var clear = new Button { Content = "Снять выбор", Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 8, 0) };
        clear.Click += (_, _) => { _selected.Clear(); _filtering = true; _list.UnselectAll(); _filtering = false; UpdateCount(); };
        var cancel = new Button { Content = "Отмена", IsCancel = true, Padding = new Thickness(16, 9, 16, 9), Margin = new Thickness(0, 0, 8, 0) };
        _choose = new Button { Name = "AddSelectedProcesses", Content = "Добавить", IsDefault = true, IsEnabled = false, Padding = new Thickness(16, 9, 16, 9), Background = MainWindow.Brush("#3B5B44") };
        RegisterName(_choose.Name, _choose);
        _choose.Click += (_, _) => Choose();
        buttons.Children.Add(clear); buttons.Children.Add(cancel); buttons.Children.Add(_choose); actions.Children.Add(buttons);
        _count = MainWindow.Text("Ничего не выбрано", 12, "#AAAAAA"); _count.VerticalAlignment = VerticalAlignment.Center;
        actions.Children.Add(_count); Grid.SetRow(actions, 4); layout.Children.Add(actions);
        Content = new Border { Background = Background, Child = layout };
        Filter(); Loaded += (_, _) => _search.Focus();
    }

    private void Filter()
    {
        if (_list == null) return;
        _filtering = true;
        _list.Items.Clear();
        var filter = _search.Text.Trim();
        foreach (var process in _processes.Where(p => p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || p.FullPath?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true))
        {
            var row = new DockPanel { Margin = new Thickness(10, 8, 10, 8) };
            var icon = ProcessIcons.Create(process.FullPath); DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon);
            var text = new StackPanel();
            text.Children.Add(MainWindow.Text($"{process.Name}  ·  PID {process.Id}  ·  {process.WorkingSetMiB:N0} МиБ", 14, "#F0F0F0"));
            var path = MainWindow.Text(process.FullPath ?? "Путь недоступен: процесс защищён или нужны дополнительные права", 11, "#AAAAAA");
            path.TextWrapping = TextWrapping.Wrap; text.Children.Add(path); row.Children.Add(text);
            var item = new ListBoxItem { Content = row, Tag = process, IsEnabled = process.FullPath != null, ToolTip = process.ProtectedReason ?? process.FullPath, IsSelected = _selected.Contains(process.Identity) };
            _list.Items.Add(item);
        }
        _filtering = false;
        UpdateCount();
    }

    private IReadOnlyList<ProcessInfo> Selection() => _processes.Where(p => p.FullPath != null && _selected.Contains(p.Identity))
        .DistinctBy(p => p.FullPath!, StringComparer.OrdinalIgnoreCase).ToArray();
    private void UpdateCount()
    {
        if (_choose == null || _count == null) return;
        var count = Selection().Count;
        _choose.IsEnabled = count > 0;
        _choose.Content = count > 0 ? $"Добавить ({count})" : "Добавить";
        _count.Text = count > 0 ? $"Выбрано программ: {count}" : "Ничего не выбрано";
    }
    private void Choose()
    {
        var selected = Selection();
        if (selected.Count == 0) return;
        SelectedProcesses = selected;
        if (_standalonePreview) Close(); else DialogResult = true;
    }
}
