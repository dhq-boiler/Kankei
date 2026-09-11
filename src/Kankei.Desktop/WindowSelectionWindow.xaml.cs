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
        Closed += (_, _) => _closed.Cancel();
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
                ? "保存済みの配置はありません。下の一覧から保存してください。" : "復元する配置を一覧から選んでください。";
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (revision != _listRevision) return;
            SavedLayouts.ItemsSource = null;
            RestoreStatusText.Text = "一覧を読み込めませんでした: " + ex.Message;
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
            RestoreStatusText.Text = asCurl ? $"「{selected.Name}」のcurlコマンドをコピーしました。PowerShellで実行できます。"
                : $"「{selected.Name}」のAPI URLをコピーしました。HTTP POSTで呼び出してください。";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            RestoreStatusText.Text = "クリップボードにコピーできませんでした。少し待ってから再度お試しください。";
        }
    }

    private async void DeleteLayoutClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || SavedLayouts.SelectedItem is not Layout selected) return;
        _deleting = true;
        UpdateProfileActions();
        try
        {
            if (System.Windows.MessageBox.Show(this,
                $"配置プロファイル「{selected.Name}」を削除しますか？\nこの操作は取り消せません。現在開いているウィンドウは変更されません。",
                "配置プロファイルの削除", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            _store.Delete(selected.Id);
            await RefreshLayoutsAsync();
            RestoreStatusText.Text = $"「{selected.Name}」を削除しました。";
        }
        catch (Exception ex) { RestoreStatusText.Text = "削除できませんでした: " + ex.Message; }
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
                RestoreStatusText.Text = "選択した配置は削除されています。一覧から選び直してください。";
                return;
            }
            RestoreStatusText.Text = $"「{selected.Name}」を復元中…";
            while (job.Status == RestoreStatus.Running) await Task.Delay(200, _closed.Token);
            RestoreStatusText.Text = job.Status == RestoreStatus.Completed ? $"「{selected.Name}」を復元しました。"
                : $"「{selected.Name}」の復元で失敗がありました。" + string.Join(" / ", job.Items.Where(x => x.Status == RestoreItemStatus.Failed).Select(x => x.Detail));
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception ex) { RestoreStatusText.Text = "復元できませんでした: " + ex.Message; }
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
            var label = $"{(string.IsNullOrEmpty(window.Title) ? "（タイトルなし）" : window.Title)}  —  {Path.GetFileName(window.ExecutablePath)}";
            var title = new TextBlock { Text = string.IsNullOrEmpty(window.Title) ? "（タイトルなし）" : window.Title, TextTrimming = TextTrimming.CharacterEllipsis };
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
        Status.Text = $"{count} 件選択 / {WindowList.Children.Count} 件";
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
            if (await _store.GetAsync(name) is not null && System.Windows.MessageBox.Show(this,
                    $"「{name}」の保存済み配置を上書きしますか？", "配置の上書き", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            var windows = WindowSelection.CaptureSelected(selected, _discovery.Capture());
            if (includeYouTube) windows = await ChromeYouTubeState.CaptureAsync(windows, _closed.Token);
            var layout = await _store.SaveAsync(name, windows);
            await RefreshLayoutsAsync(layout.Id);
            Status.Text = $"「{name}」に {windows.Count} 件の配置を保存しました。";
        }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { _saving = false; UpdateProfileActions(); SaveButton.IsEnabled = WindowList.Children.OfType<CheckBox>().Any(x => x.IsChecked == true); }
    }
}
