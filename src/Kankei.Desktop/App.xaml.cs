using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Kankei.Desktop;

public partial class App : System.Windows.Application
{
    private readonly CancellationTokenSource _shutdown = new();
    private Forms.NotifyIcon? _trayIcon;
    private LocalApiHost? _api;
    private WindowSelectionWindow? _selection;
    private YouTubeResumeService? _youtubeResume;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var store = new LayoutStore();
        var discovery = new WindowDiscovery();
        var overlay = new OverlayService();
        var adapters = ApplicationAdapterRegistry.Load(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kankei", "adapters.json"));
        _youtubeResume = new YouTubeResumeService(store, discovery);
        var orchestrator = new RestoreOrchestrator(discovery, store, overlay, adapters, _youtubeResume);
        _api = new LocalApiHost(store, discovery, orchestrator, adapters);
        await _api.StartAsync(_shutdown.Token);
        CreateTrayIcon(store, discovery, orchestrator, adapters);
        ShowSelection(discovery, store, orchestrator);
        _ = Task.Run(() => _youtubeResume.RunAsync(_shutdown.Token));
    }

    private void CreateTrayIcon(LayoutStore store, WindowDiscovery discovery, RestoreOrchestrator orchestrator, ApplicationAdapterRegistry adapters)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("配置を選んで保存・復元…", null, (_, _) => ShowSelection(discovery, store, orchestrator));
        menu.Items.Add("現在の配置を「default」として保存", null, async (_, _) =>
        {
            try
            {
                var windows = await adapters.CaptureAsync(discovery.Capture(), _shutdown.Token);
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
            try { if (_youtubeResume is not null) await _youtubeResume.CaptureLatestAsync(); }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"YouTube exit checkpoint: {ex.Message}"); }
            Shutdown();
        });
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Kankei（還景）",
            Icon = SystemIcons.Application,
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
        base.OnSessionEnding(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _shutdown.Cancel();
        if (_api is not null) await _api.StopAsync();
        _shutdown.Dispose();
        base.OnExit(e);
    }
}
