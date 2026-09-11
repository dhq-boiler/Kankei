using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.IO;
using CheckBox = System.Windows.Controls.CheckBox;

namespace Kankei.Desktop;

public partial class WindowSelectionWindow : Window
{
    private readonly WindowDiscovery _discovery;
    private readonly LayoutStore _store;
    private readonly RestoreOrchestrator _orchestrator;
    private readonly CancellationTokenSource _closed = new();
    private int _listRevision;
    private bool _restoring;
    private bool _saving;
    private bool _deleting;
    public bool IsBusy => _saving || _restoring || _deleting;
    private void OpenAboutClick(object sender, RoutedEventArgs e) => ((App)System.Windows.Application.Current).ShowAbout();

    public WindowSelectionWindow(WindowDiscovery discovery, LayoutStore store, RestoreOrchestrator orchestrator)
    {
        InitializeComponent();
        _discovery = discovery;
        _store = store;
        _orchestrator = orchestrator;
        RefreshWindows();
        Loaded += (_, _) => LayoutName.Focus();
        Activated += async (_, _) => await RefreshLayoutsAsync();
        Localization.Current.Changed += LanguageChanged;
        Closed += (_, _) => { _closed.Cancel(); Localization.Current.Changed -= LanguageChanged; };
    }

    private void LanguageChanged()
    {
        SelectionChanged(this, new RoutedEventArgs());
        if (!IsBusy) RestoreStatusText.Text = L.T("復元する配置を一覧から選んでください。");
    }

    private void LanguageClick(object sender, RoutedEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = (System.Windows.Controls.Button)sender,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush")
        };
        foreach (var (code, label) in new[] { ("ja", "日本語"), ("en", "English") })
        {
            var item = new System.Windows.Controls.MenuItem { Header = label, IsCheckable = true, IsChecked = Localization.Current.Language == code };
            AutomationProperties.SetAutomationId(item, "Language_" + code);
            item.Click += (_, _) =>
            {
                try { Localization.Current.SetLanguage(code); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { System.Windows.MessageBox.Show(this, ex.Message, L.T("言語設定を保存できませんでした")); }
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private async Task RefreshLayoutsAsync(string? selectedId = null)
    {
        var revision = ++_listRevision;
        try
        {
            var layouts = await _store.ListAsync(_closed.Token);
            if (revision != _listRevision || _closed.IsCancellationRequested) return;
            selectedId ??= (SavedLayouts.SelectedItem as Layout)?.Id;
            SavedLayouts.ItemsSource = layouts;
            SavedLayouts.SelectedItem = layouts.FirstOrDefault(x => x.Id == selectedId);
            if (!_restoring) RestoreStatusText.Text = layouts.Count == 0
                ? L.T("保存済みの配置はありません。下の一覧から保存してください。") : L.T("復元する配置を一覧から選んでください。");
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (revision != _listRevision) return;
            SavedLayouts.ItemsSource = null;
            RestoreStatusText.Text = L.T("一覧を読み込めませんでした: ") + ex.Message;
        }
    }

    private void RestoreSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateProfileActions();
        Preview?.SetLayout(SavedLayouts.SelectedItem as Layout);
    }

    private void UpdateProfileActions()
    {
        var selected = SavedLayouts.SelectedItem is Layout;
        if (RestoreButton is not null) RestoreButton.IsEnabled = selected && !IsBusy;
        if (DeleteLayoutButton is not null) DeleteLayoutButton.IsEnabled = selected && !IsBusy;
        if (CopyApiButton is not null) CopyApiButton.IsEnabled = selected;
        if (CopyCurlButton is not null) CopyCurlButton.IsEnabled = selected;
    }

    private void CopyApiClick(object sender, RoutedEventArgs e) => CopyRestoreApi(false);
    private void CopyCurlClick(object sender, RoutedEventArgs e) => CopyRestoreApi(true);

    private void CopyRestoreApi(bool asCurl)
    {
        if (SavedLayouts.SelectedItem is not Layout selected) return;
        var url = LocalApiHost.RestoreUrl(selected.Id);
        try
        {
            System.Windows.Clipboard.SetText(asCurl ? $"curl.exe --request POST \"{url}\"" : url);
            RestoreStatusText.Text = asCurl ? L.F("「{0}」のcurlコマンドをコピーしました。PowerShellで実行できます。", selected.Name)
                : L.F("「{0}」のAPI URLをコピーしました。HTTP POSTで呼び出してください。", selected.Name);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            RestoreStatusText.Text = L.T("クリップボードにコピーできませんでした。少し待ってから再度お試しください。");
        }
    }

    private async void DeleteLayoutClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || SavedLayouts.SelectedItem is not Layout selected) return;
        _deleting = true;
        UpdateProfileActions();
        try
        {
            if (!ConfirmDialog.Show(this,
                L.F("配置プロファイル「{0}」を削除しますか？\nこの操作は取り消せません。現在開いているウィンドウは変更されません。", selected.Name),
                L.T("配置プロファイルの削除"))) return;
            _store.Delete(selected.Id);
            await RefreshLayoutsAsync();
            RestoreStatusText.Text = L.F("「{0}」を削除しました。", selected.Name);
        }
        catch (Exception ex) { RestoreStatusText.Text = L.T("削除できませんでした: ") + ex.Message; }
        finally { _deleting = false; UpdateProfileActions(); }
    }

    private async void RefreshLayoutsClick(object sender, RoutedEventArgs e) => await RefreshLayoutsAsync();

    private async void RestoreClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || SavedLayouts.SelectedItem is not Layout selected) return;
        _restoring = true;
        UpdateProfileActions();
        RestorePanel.IsEnabled = false;
        try
        {
            var job = await _orchestrator.StartAsync(selected.Id, true, _closed.Token);
            if (job is null)
            {
                await RefreshLayoutsAsync();
                RestoreStatusText.Text = L.T("選択した配置は削除されています。一覧から選び直してください。");
                return;
            }
            RestoreStatusText.Text = L.F("「{0}」を復元中…", selected.Name);
            while (job.Status == RestoreStatus.Running) await Task.Delay(200, _closed.Token);
            RestoreStatusText.Text = job.Status == RestoreStatus.Completed ? L.F("「{0}」を復元しました。", selected.Name)
                : L.F("「{0}」の復元で失敗がありました。", selected.Name) + string.Join(" / ", job.Items.Where(x => x.Status == RestoreItemStatus.Failed).Select(x => x.Detail));
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception ex) { RestoreStatusText.Text = L.T("復元できませんでした: ") + ex.Message; }
        finally
        {
            _restoring = false;
            RestorePanel.IsEnabled = true;
            UpdateProfileActions();
        }
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
        UpdateProfileActions();
        SaveButton.IsEnabled = false;
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
            await RefreshLayoutsAsync(layout.Id);
            Status.Text = L.F("「{0}」に {1} 件の配置を保存しました。", name, windows.Count);
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { _saving = false; UpdateProfileActions(); SaveButton.IsEnabled = WindowList.Children.OfType<CheckBox>().Any(x => x.IsChecked == true); }
    }
}
