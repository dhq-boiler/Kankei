using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using CheckBox = System.Windows.Controls.CheckBox;

namespace Kankei.Desktop;

public partial class SaveLayoutWindow : Window
{
    private readonly WindowDiscovery _discovery;
    private readonly LayoutStore _store;
    private readonly CancellationTokenSource _closed = new();
    private bool _saving;
    public bool IsBusy => _saving;
    public Layout? SavedLayout { get; private set; }

    public SaveLayoutWindow(WindowDiscovery discovery, LayoutStore store, string? name = null)
    {
        InitializeComponent();
        _discovery = discovery;
        _store = store;
        LayoutName.Text = name ?? "";
        RefreshWindows();
        Loaded += (_, _) => LayoutName.Focus();
        Closing += (_, e) => e.Cancel = IsBusy;
        Closed += (_, _) => _closed.Cancel();
    }
    private void RefreshWindows()
    {
        if (_saving) return;
        WindowList.Children.Clear();
        foreach (var window in _discovery.Capture())
        {
            var label = $"{(string.IsNullOrEmpty(window.Title) ? L.T("（タイトルなし）") : window.Title)}  —  {Path.GetFileName(window.ExecutablePath)}";
            var title = new TextBlock { Text = string.IsNullOrEmpty(window.Title) ? L.T("（タイトルなし）") : window.Title, TextTrimming = TextTrimming.CharacterEllipsis };
            var caption = new TextBlock { Text = Path.GetFileNameWithoutExtension(window.ExecutablePath), FontSize = 11,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 3, 0, 0) };
            var content = new StackPanel();
            content.Children.Add(title);
            content.Children.Add(caption);
            var check = new CheckBox { Content = content, Tag = window,
                Background = System.Windows.Media.Brushes.Transparent, HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 10, 0, 10), ToolTip = label };
            content.Background = System.Windows.Media.Brushes.Transparent;
            content.MouseLeftButtonDown += (_, e) => { check.IsChecked = check.IsChecked != true; e.Handled = true; };
            check.SizeChanged += (_, _) => content.Width = Math.Max(0, check.ActualWidth - 36);
            AutomationProperties.SetName(check, label);
            AutomationProperties.SetAutomationId(check, "Window_" + window.WindowHandle);
            check.Checked += SelectionChanged;
            check.Unchecked += SelectionChanged;
            WindowList.Children.Add(check);
        }
        SelectionChanged(this, new RoutedEventArgs());
    }

    private void SelectionChanged(object sender, RoutedEventArgs e)
    {
        var count = WindowList.Children.OfType<CheckBox>().Count(x => x.IsChecked == true);
        Status.Text = L.F("{0} 件選択 / {1} 件", count, WindowList.Children.Count);
        SaveButton.IsEnabled = count > 0 && !_saving;
        Preview?.SetSelection(WindowList.Children.OfType<CheckBox>().Where(x => x.IsChecked == true).Select(x => (SavedWindow)x.Tag).ToArray());
    }

    private void RefreshClick(object sender, RoutedEventArgs e) => RefreshWindows();
    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        _saving = true;
        SaveButton.IsEnabled = false;
        Editor.IsEnabled = false;
        try
        {
            var selected = WindowList.Children.OfType<CheckBox>().Where(x => x.IsChecked == true).Select(x => (SavedWindow)x.Tag).ToArray();
            var name = LayoutName.Text;
            var includeYouTube = IncludeYouTube.IsChecked == true;
            if (await _store.GetAsync(name) is not null && !ConfirmDialog.Show(this,
                    L.F("「{0}」の保存済み配置を上書きしますか？", name), L.T("配置の上書き"))) return;
            var windows = WindowSelection.CaptureSelected(selected, _discovery.Capture());
            windows = await ChromePageState.CaptureAsync(windows, _closed.Token);
            if (includeYouTube) windows = await ChromeYouTubeState.CaptureAsync(windows, _closed.Token);
            var layout = await _store.SaveAsync(name, windows);
            SavedLayout = layout;
            _saving = false;
            DialogResult = true;
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { _saving = false; Editor.IsEnabled = true; SaveButton.IsEnabled = WindowList.Children.OfType<CheckBox>().Any(x => x.IsChecked == true); }
    }
}

