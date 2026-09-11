using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Kankei.Desktop;

public sealed class RestoreOrchestrator(WindowDiscovery discovery, LayoutStore store, OverlayService overlay, ApplicationAdapterRegistry adapters,
    YouTubeResumeService? youtubeResume = null)
{
    private readonly ConcurrentDictionary<string, RestoreJob> _jobs = new();
    private readonly object _lifecycle = new();
    private bool _stopping;

    public void BeginShutdown()
    {
        lock (_lifecycle)
        {
            if (_jobs.Values.Any(x => x.Status == RestoreStatus.Running)) throw new InvalidOperationException("配置の復元が終わってから更新・終了してください。");
            _stopping = true;
        }
    }
    public void CancelShutdown() { lock (_lifecycle) _stopping = false; }

    public RestoreJob? GetJob(string id) => _jobs.GetValueOrDefault(id);

    public async Task<RestoreJob?> StartAsync(string layoutId, bool showOverlay, CancellationToken cancellationToken = default)
    {
        var layout = await store.GetAsync(layoutId, cancellationToken);
        if (layout is null) return null;

        var job = new RestoreJob("rst_" + Guid.NewGuid().ToString("N"), layout.Id);
        lock (_lifecycle)
        {
            if (_stopping) throw new InvalidOperationException("更新・終了の準備中です。");
            _jobs[job.Id] = job;
        }
        _ = Task.Run(() => RestoreAsync(layout, job, showOverlay), CancellationToken.None);
        return job;
    }

    private async Task RestoreAsync(Layout layout, RestoreJob job, bool showOverlay)
    {
        if (showOverlay) overlay.Show(layout.Name, job);
        try
        {
            using var resumeLease = youtubeResume is null ? null : await youtubeResume.BeginRestoreAsync();
            if (youtubeResume is not null) layout = await youtubeResume.PrepareAsync(layout);
            var restoredAdapters = new HashSet<string>();
            for (var windowIndex = 0; windowIndex < layout.Windows.Count; windowIndex++)
            {
                var saved = layout.Windows[windowIndex];
                var item = new RestoreItem(saved.ExecutablePath, RestoreItemStatus.Launching);
                job.Items.Add(item);
                AddEvent(job, "アプリを確認中", saved.ExecutablePath);
                try
                {
                    if (saved.YouTube is not null && saved.AdapterId is not null)
                        throw new InvalidDataException("YouTube と MCP アダプターの状態は同時に復元できません。");
                    var adapter = adapters.ForSavedWindow(saved);
                    if (adapter is not null && !restoredAdapters.Contains(adapter.Id))
                    {
                        try
                        {
                            await adapter.LaunchOrFocusAsync(CancellationToken.None);
                            await adapter.ImportStateAsync(saved.AdapterStateJson!, CancellationToken.None);
                            restoredAdapters.Add(adapter.Id);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException(adapter.DescribeFailure(ex), ex);
                        }
                    }
                    var handle = saved.YouTube is not null
                        ? await ChromeYouTubeState.RestoreAsync(saved, discovery)
                        : saved.BrowserUrl is not null && adapter is null
                            ? await ChromePageState.RestoreAsync(saved, discovery)
                            : discovery.FindWindow(saved);
                    if (handle == IntPtr.Zero)
                    {
                        if (adapter is null)
                        {
                            if (!File.Exists(saved.ExecutablePath)) throw new FileNotFoundException("実行ファイルが見つかりません。", saved.ExecutablePath);
                            Process.Start(new ProcessStartInfo(saved.ExecutablePath) { UseShellExecute = true });
                        }
                        handle = await WaitForWindowAsync(saved, TimeSpan.FromSeconds(10));
                    }
                    if (handle == IntPtr.Zero) throw new TimeoutException("ウィンドウの起動待機がタイムアウトしました。");

                    item = item with { Status = RestoreItemStatus.Restoring };
                    ReplaceItem(job, item);
                    RestorePlacement(handle, saved);
                    if (saved.YouTube is not null) youtubeResume?.Remember(layout, windowIndex, handle);
                    item = item with { Status = RestoreItemStatus.Completed, Detail = "配置を復元しました。" };
                    ReplaceItem(job, item);
                    AddEvent(job, "復元完了", saved.ExecutablePath);
                }
                catch (Exception ex)
                {
                    item = item with { Status = RestoreItemStatus.Failed, Detail = ex.Message };
                    ReplaceItem(job, item);
                    AddEvent(job, "復元失敗: " + ex.Message, saved.ExecutablePath);
                }
                overlay.Refresh(job);
            }
            job.Status = job.Items.Any(x => x.Status == RestoreItemStatus.Failed)
                ? RestoreStatus.CompletedWithFailures : RestoreStatus.Completed;
        }
        catch (Exception ex)
        {
            job.Status = RestoreStatus.Failed;
            AddEvent(job, "ジョブの予期しない失敗: " + ex.Message);
        }
        finally
        {
            overlay.Refresh(job);
            if (showOverlay) await overlay.CloseAfterDelayAsync(TimeSpan.FromSeconds(5));
        }
    }

    private async Task<IntPtr> WaitForWindowAsync(SavedWindow saved, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var handle = discovery.FindWindow(saved);
            if (handle != IntPtr.Zero) return handle;
            await Task.Delay(250);
        }
        return IntPtr.Zero;
    }

    public static void RestorePlacement(IntPtr handle, SavedWindow saved)
    {
        var bounds = MonitorPlacement.Resolve(saved, System.Windows.Forms.Screen.AllScreens.Select(MonitorPlacement.Capture).ToArray());
        // Move while restored: maximizing first keeps the window on its current monitor.
        ShowWindow(handle, 9);
        if (!SetWindowPos(handle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0040 | 0x0004))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (saved.DisplayState != WindowDisplayState.Normal)
            ShowWindow(handle, saved.DisplayState == WindowDisplayState.Maximized ? 3 : 2);
        SetForegroundWindow(handle);
    }

    private static void ReplaceItem(RestoreJob job, RestoreItem replacement)
    {
        var index = job.Items.FindIndex(x => x.ExecutablePath == replacement.ExecutablePath && x.Status != RestoreItemStatus.Completed && x.Status != RestoreItemStatus.Failed);
        if (index >= 0) job.Items[index] = replacement;
    }

    private static void AddEvent(RestoreJob job, string message, string? executablePath = null) =>
        job.Events.Add(new RestoreEvent(DateTimeOffset.UtcNow, message, executablePath));

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
