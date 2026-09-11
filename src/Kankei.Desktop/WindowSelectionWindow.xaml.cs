using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace Kankei.Desktop;

public partial class WindowSelectionWindow : Window
{
    private readonly WindowDiscovery _discovery;
    private readonly LayoutStore _store;
    private readonly RestoreOrchestrator _orchestrator;
    private readonly CancellationTokenSource _closed = new();
    private int _listRevision;
    private SaveLayoutWindow? _saveDialog;
    private bool _dialogOpen;
    private bool _restoring;
    private bool _deleting;
    public bool IsBusy => _saveDialog?.IsBusy == true || _restoring || _deleting;

    public WindowSelectionWindow(WindowDiscovery discovery, LayoutStore store, RestoreOrchestrator orchestrator)
    {
        InitializeComponent();
        _discovery = discovery;
        _store = store;
        _orchestrator = orchestrator;
        Activated += async (_, _) =>
        {
            if (!_dialogOpen && !IsBusy) await RefreshLayoutsAsync();
        };
        Closing += (_, e) => e.Cancel = IsBusy;
        Closed += (_, _) => _closed.Cancel();
    }
    private void OpenAboutClick(object sender, RoutedEventArgs e) => ((App)System.Windows.Application.Current).ShowAbout();
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
        selectedId ??= (SavedLayouts.SelectedItem as Layout)?.Id;
        try
        {
            var layouts = await _store.ListAsync(_closed.Token);
            if (revision != _listRevision || _closed.IsCancellationRequested) return;
            // Re-activation after the progress overlay must not erase the operation result.
            if (SavedLayouts.ItemsSource is IReadOnlyList<Layout> current &&
                current.Select(x => (x.Id, x.SavedAt)).SequenceEqual(layouts.Select(x => (x.Id, x.SavedAt))))
            {
                SavedLayouts.SelectedItem = layouts.FirstOrDefault(x => x.Id == selectedId) is Layout match
                    ? current.First(x => x.Id == match.Id) : current.FirstOrDefault();
                return;
            }
            SavedLayouts.ItemsSource = layouts;
            SavedLayouts.SelectedItem = layouts.FirstOrDefault(x => x.Id == selectedId) ?? layouts.FirstOrDefault();
            Status.Text = layouts.Count == 0 ? L.T("新規配置から、最初の配置を保存してください。") : "";
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception ex) { if (revision == _listRevision) Status.Text = L.T("一覧を読み込めませんでした: ") + ex.Message; }
    }
    private void RestoreSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Preview?.SetLayout(SavedLayouts.SelectedItem as Layout);
        if (LayoutActionsButton is not null) LayoutActionsButton.IsEnabled = SavedLayouts.SelectedItem is Layout;
    }
    private async void NewLayoutClick(object sender, RoutedEventArgs e) => await ShowSaveAsync();

    private async Task ShowSaveAsync(string? name = null)
    {
        if (_dialogOpen || IsBusy) return;
        _dialogOpen = true;
        _saveDialog = new SaveLayoutWindow(_discovery, _store, name) { Owner = this };
        string? savedId;
        try { _saveDialog.ShowDialog(); savedId = _saveDialog.SavedLayout?.Id; }
        finally { _saveDialog = null; _dialogOpen = false; }
        await RefreshLayoutsAsync(savedId);
    }

    private void LayoutActionsClick(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen || IsBusy || SavedLayouts.SelectedItem is not Layout selected) return;
        var menu = new System.Windows.Controls.ContextMenu
        {
            PlacementTarget = LayoutActionsButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
            Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush")
        };
        void Add(string label, string id, RoutedEventHandler action)
        {
            var item = new System.Windows.Controls.MenuItem { Header = L.T(label), Padding = new Thickness(12, 8, 18, 8) };
            AutomationProperties.SetAutomationId(item, id);
            item.Click += action;
            menu.Items.Add(item);
        }
        Add("この配置を復元", "RestoreSelected", async (_, _) => await RestoreAsync(selected));
        Add("現在のウィンドウで保存し直す…", "SaveAgain", async (_, _) => await ShowSaveAsync(selected.Name));
        Add("API連携…", "OpenLayoutApi", (_, _) =>
        {
            _dialogOpen = true;
            try { new LayoutApiWindow(selected) { Owner = this }.ShowDialog(); }
            finally { _dialogOpen = false; }
        });
        menu.Items.Add(new System.Windows.Controls.Separator());
        Add("配置を削除…", "DeleteLayout", async (_, _) => await DeleteAsync(selected));
        menu.IsOpen = true;
    }

    private void SetOperationEnabled(bool enabled)
    {
        ProfileBar.IsEnabled = enabled;
    }

    private async Task DeleteAsync(Layout selected)
    {
        if (IsBusy) return;
        _deleting = true;
        SetOperationEnabled(false);
        try
        {
            if (!ConfirmDialog.Show(this, L.F("配置プロファイル「{0}」を削除しますか？\nこの操作は取り消せません。現在開いているウィンドウは変更されません。", selected.Name), L.T("配置プロファイルの削除"))) return;
            _store.Delete(selected.Id);
            await RefreshLayoutsAsync();
            Status.Text = L.F("「{0}」を削除しました。", selected.Name);
        }
        catch (Exception ex) { Status.Text = L.T("削除できませんでした: ") + ex.Message; }
        finally { _deleting = false; SetOperationEnabled(true); }
    }

    private async Task RestoreAsync(Layout selected)
    {
        if (IsBusy) return;
        _restoring = true;
        SetOperationEnabled(false);
        try
        {
            var job = await _orchestrator.StartAsync(selected.Id, true, _closed.Token);
            if (job is null) { await RefreshLayoutsAsync(); Status.Text = L.T("選択した配置は削除されています。一覧から選び直してください。"); return; }
            Status.Text = L.F("「{0}」を復元中…", selected.Name);
            while (job.Status == RestoreStatus.Running) await Task.Delay(200, _closed.Token);
            Status.Text = job.Status == RestoreStatus.Completed ? L.F("「{0}」を復元しました。", selected.Name)
                : L.F("「{0}」の復元で失敗がありました。", selected.Name) + string.Join(" / ", job.Items.Where(x => x.Status == RestoreItemStatus.Failed).Select(x => x.Detail));
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
        catch (Exception ex) { Status.Text = L.T("復元できませんでした: ") + ex.Message; }
        finally { _restoring = false; SetOperationEnabled(true); }
    }
}
