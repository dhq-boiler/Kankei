using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Kankei.Desktop;

public partial class App : System.Windows.Application
{
    private readonly CancellationTokenSource _shutdown = new();
    private Forms.NotifyIcon? _trayIcon;
    private Icon? _applicationIcon;
    private LocalApiHost? _api;
    private WindowSelectionWindow? _selection;
    private YouTubeResumeService? _youtubeResume;
    private UpdateService? _updates;
    private RestoreOrchestrator? _orchestrator;
    private AboutWindow? _about;
    private bool _exiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            var enabled = 1;
            DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper((Window)sender).Handle, 20, ref enabled, sizeof(int));
        }));
        var store = new LayoutStore();
        var discovery = new WindowDiscovery();
        var overlay = new OverlayService();
        var adapters = ApplicationAdapterRegistry.Load(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kankei", "adapters.json"));
        _youtubeResume = new YouTubeResumeService(store, discovery);
        var orchestrator = new RestoreOrchestrator(discovery, store, overlay, adapters, _youtubeResume);
        _orchestrator = orchestrator;
        _updates = new UpdateService(new VelopackUpdateBackend());
        _api = new LocalApiHost(store, discovery, orchestrator, adapters);
        await _api.StartAsync(_shutdown.Token);
        CreateTrayIcon(store, discovery, orchestrator, adapters);
        ShowSelection(discovery, store, orchestrator);
        _ = Task.Run(() => _youtubeResume.RunAsync(_shutdown.Token));
        _updates.UpdateFound += version => Dispatcher.BeginInvoke(() =>
            _trayIcon?.ShowBalloonTip(8000, "Kankei の更新", $"バージョン {version} が公開されました。クリックして更新を確認できます。", Forms.ToolTipIcon.Info));
        _trayIcon!.BalloonTipClicked += (_, _) => ShowAbout();
        _ = Task.Run(() => _updates.RunAsync(_shutdown.Token));
    }

    private void CreateTrayIcon(LayoutStore store, WindowDiscovery discovery, RestoreOrchestrator orchestrator, ApplicationAdapterRegistry adapters)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("配置を選んで保存・復元…", null, (_, _) => ShowSelection(discovery, store, orchestrator));
        menu.Items.Add("バージョン情報・アップデート…", null, (_, _) => ShowAbout());
        menu.Items.Add("現在の配置を「default」として保存", null, async (_, _) =>
        {
            try
            {
                var windows = await adapters.CaptureAsync(discovery.Capture(), _shutdown.Token);
                windows = await ChromePageState.CaptureAsync(windows, _shutdown.Token);
                windows = await ChromeYouTubeState.CaptureAsync(windows, _shutdown.Token);
                await store.SaveAsync("default", windows, _shutdown.Token);
            }
            catch (Exception ex) { Forms.MessageBox.Show(ex.Message, "Kankei"); }
        });
        var restoreMenu = new Forms.ToolStripMenuItem("保存済みの配置を復元");
        menu.Items.Add(restoreMenu);
        var menuRevision = 0;
        menu.Opening += async (_, _) =>
        {
            var revision = ++menuRevision;
            restoreMenu.DropDownItems.Clear();
            restoreMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("読み込み中…") { Enabled = false });
            try
            {
                var layouts = await store.ListAsync(_shutdown.Token);
                if (revision != menuRevision || menu.IsDisposed) return;
                restoreMenu.DropDownItems.Clear();
                foreach (var layout in layouts)
                {
                    var entry = new Forms.ToolStripMenuItem(layout.Name.Replace("&", "&&"));
                    entry.Click += async (_, _) =>
                    {
                        try
                        {
                            var job = await orchestrator.StartAsync(layout.Id, true, _shutdown.Token);
                            if (job is null) Forms.MessageBox.Show("配置が削除されています。メニューを開き直してください。", "Kankei");
                        }
                        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
                        catch (Exception ex) { Forms.MessageBox.Show(ex.Message, "Kankei"); }
                    };
                    restoreMenu.DropDownItems.Add(entry);
                }
                if (layouts.Count == 0) restoreMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("保存済みの配置はありません") { Enabled = false });
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
            catch (Exception)
            {
                if (revision != menuRevision || menu.IsDisposed) return;
                restoreMenu.DropDownItems.Clear();
                restoreMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("一覧を読み込めませんでした") { Enabled = false });
            }
        };
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("終了", null, async (_, _) =>
        {
            await ExitAsync();
        });
        using (var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/Icons/kankei.ico")).Stream)
        using (var icon = new Icon(stream))
            _applicationIcon = (Icon)icon.Clone();
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Kankei（還景）",
            Icon = _applicationIcon,
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => ShowSelection(discovery, store, orchestrator);
    }

    private void ShowSelection(WindowDiscovery discovery, LayoutStore store, RestoreOrchestrator orchestrator)
    {
        if (_selection is null)
        {
            _selection = new WindowSelectionWindow(discovery, store, orchestrator);
            _selection.Closed += (_, _) => _selection = null;
            _selection.Show();
        }
        if (_selection.WindowState == WindowState.Minimized) _selection.WindowState = WindowState.Normal;
        _selection.Activate();
    }

    public void ShowAbout()
    {
        if (_updates is null) return;
        if (_about is null)
        {
            _about = new AboutWindow(_updates, UpdateNowAsync);
            if (_selection?.IsVisible == true) _about.Owner = _selection;
            _about.Closed += (_, _) => _about = null;
            _about.Show();
        }
        _about.Activate();
    }

    private async Task PrepareForExitAsync()
    {
        if (_selection?.IsBusy == true) throw new InvalidOperationException("配置の保存・復元が終わってから更新・終了してください。");
        _orchestrator?.BeginShutdown();
        if (_youtubeResume is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _youtubeResume.CaptureLatestAsync(timeout.Token).WaitAsync(timeout.Token);
        }
    }

    private async Task CompleteExitAsync()
    {
        _shutdown.Cancel();
        if (_api is not null) await _api.StopAsync();
        Shutdown();
    }

    private async Task UpdateNowAsync()
    {
        if (_exiting || _updates is null) return;
        _exiting = true;
        try
        {
            if (await _updates.ApplyNowAsync(PrepareForExitAsync)) await CompleteExitAsync();
            else _orchestrator?.CancelShutdown();
        }
        finally { _exiting = false; }
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        try
        {
            await PrepareForExitAsync();
            _updates?.ApplyOnExit();
            await CompleteExitAsync();
        }
        catch (Exception ex)
        {
            _orchestrator?.CancelShutdown();
            Forms.MessageBox.Show(ex.Message, "Kankei");
        }
        finally { _exiting = false; }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Windows gives applications a bounded shutdown window. The periodic checkpoint survives
        // even if Chrome has already exited or Windows stops us before the final read completes.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try
        {
            if (_youtubeResume is not null)
                Task.Run(() => _youtubeResume.CaptureLatestAsync(timeout.Token)).Wait(TimeSpan.FromSeconds(4));
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"YouTube shutdown checkpoint: {ex.Message}"); }
        try { _updates?.ApplyOnExit(); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Update on shutdown: {ex.Message}"); }
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _applicationIcon?.Dispose();
        _shutdown.Cancel();
        _shutdown.Dispose();
        base.OnExit(e);
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
