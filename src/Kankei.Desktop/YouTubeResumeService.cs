using System.Collections.Concurrent;
using System.Diagnostics;

namespace Kankei.Desktop;

public sealed class YouTubeResumeService(LayoutStore layouts, WindowDiscovery discovery)
{
    private readonly YouTubeResumeStore _store = new();
    private readonly ConcurrentDictionary<string, SavedWindow> _bindings = new();
    private readonly SemaphoreSlim _gate = new(1);

    public async Task<IDisposable> BeginRestoreAsync()
    {
        await _gate.WaitAsync();
        return new GateLease(_gate);
    }

    private sealed class GateLease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

    private async Task<SavedWindow?> FindLiveAsync(Layout layout, int index, IReadOnlyList<SavedWindow> live,
        CancellationToken cancellationToken)
    {
        var saved = _bindings.GetValueOrDefault(YouTubeResumeStore.Key(layout, index)) ?? layout.Windows[index];
        var exact = live.SingleOrDefault(x => x.WindowHandle == saved.WindowHandle && x.ProcessStartTicks != 0
            && x.ProcessStartTicks == saved.ProcessStartTicks && x.ExecutablePath == saved.ExecutablePath);
        if (exact is not null) return exact;
        var expected = await _store.GetAsync(layout, index, cancellationToken) ?? layout.Windows[index].YouTube;
        if (expected is null) return null;
        var matches = new List<SavedWindow>();
        foreach (var candidate in live.Where(x => x.ExecutablePath == saved.ExecutablePath))
        {
            var state = await ChromeYouTubeState.CaptureWindowAsync(candidate, cancellationToken);
            if (state is not null && YouTubePlayback.SamePlaybackContext(expected.Url, state.Url)) matches.Add(candidate);
        }
        if (matches.Count > 1)
            throw new InvalidOperationException("同じ YouTube 動画・再生リストのウィンドウが複数あり、復元先を一意に識別できません。");
        var match = matches.SingleOrDefault();
        if (match is not null) _bindings[YouTubeResumeStore.Key(layout, index)] = match;
        return match;
    }

    // Snapshot every target before restoring any window. This snapshot is temporary for manual restore.
    public async Task<Layout> PrepareAsync(Layout layout, CancellationToken cancellationToken = default)
    {
        var live = discovery.Capture();
        var windows = layout.Windows.ToArray();
        for (var i = 0; i < windows.Length; i++)
        {
            if (windows[i].YouTube is null) continue;
            var target = await FindLiveAsync(layout, i, live, cancellationToken);
            var current = target is null ? null : await ChromeYouTubeState.CaptureWindowAsync(target, cancellationToken);
            var checkpoint = await _store.GetAsync(layout, i, cancellationToken);
            windows[i] = windows[i] with
            {
                YouTube = YouTubeResumeStore.Select(windows[i].YouTube, checkpoint, current),
                PreserveCurrentYouTube = current is not null,
                WindowHandle = target?.WindowHandle ?? windows[i].WindowHandle,
                ProcessStartTicks = target?.ProcessStartTicks ?? windows[i].ProcessStartTicks
            };
        }
        return layout with { Windows = windows };
    }

    public void Remember(Layout layout, int index, IntPtr handle)
    {
        var live = discovery.Capture().SingleOrDefault(x => x.WindowHandle == handle.ToInt64());
        if (live is not null) _bindings[YouTubeResumeStore.Key(layout, index)] = live;
    }

    public async Task CaptureLatestAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var live = discovery.Capture();
            foreach (var layout in await layouts.ListAsync(cancellationToken))
            {
                for (var i = 0; i < layout.Windows.Count; i++)
                {
                    if (layout.Windows[i].YouTube is null) continue;
                    try
                    {
                        var target = await FindLiveAsync(layout, i, live, cancellationToken);
                        if (target is null) continue;
                        var state = await ChromeYouTubeState.CaptureWindowAsync(target, cancellationToken);
                        if (state is not null) await _store.SaveAsync(layout, i, state, cancellationToken);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { Trace.WriteLine($"YouTube checkpoint: {ex.Message}"); }
                }
            }
        }
        finally { _gate.Release(); }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try { await CaptureLatestAsync(cancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Trace.WriteLine($"YouTube checkpoint: {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
