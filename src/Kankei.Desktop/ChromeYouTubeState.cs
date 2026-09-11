using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace Kankei.Desktop;

/// <summary>Captures the active YouTube tab in a specific Chrome window via accessibility.</summary>
public static class ChromeYouTubeState
{
    internal static readonly SemaphoreSlim RestoreGate = new(1);
    private static bool IsChrome(SavedWindow window) => string.Equals(Path.GetFileName(window.ExecutablePath), "chrome.exe", StringComparison.OrdinalIgnoreCase);

    public static Task<YouTubePlaybackState?> CaptureWindowAsync(SavedWindow window, CancellationToken cancellationToken = default) =>
        Task.Run(() => IsChrome(window) && window.AdapterId is null ? Read(new IntPtr(window.WindowHandle)) : null, cancellationToken);

    public static async Task<IReadOnlyList<SavedWindow>> CaptureAsync(IReadOnlyList<SavedWindow> windows, CancellationToken cancellationToken = default)
    {
        var result = new List<SavedWindow>();
        foreach (var window in windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // An explicitly configured application adapter owns its own state.
            var state = IsChrome(window) && window.AdapterId is null
                ? await Task.Run(() => Read(new IntPtr(window.WindowHandle)), cancellationToken) : null;
            result.Add(window with { YouTube = state });
        }
        return result;
    }

    private static AutomationElement Address(AutomationElement root) => root.FindFirstDescendant(cf => cf.ByClassName("OmniboxViewViews"))
        ?? throw new InvalidOperationException("Chrome のアドレスバーを取得できません。YouTube の状態保存を外すか、通常表示で保存してください。");

    private static string Value(AutomationElement element) => element.Patterns.Value.IsSupported
        ? element.Patterns.Value.Pattern.Value.Value : throw new InvalidOperationException("Chrome の状態を読み取れません。");

    private static AutomationElement PlayerButton(AutomationElement root) => root.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
        .FirstOrDefault(x => x.ClassName.Split(' ').Contains("ytp-play-button"))
        ?? throw new InvalidOperationException("YouTube プレーヤーを取得できません。");

    private static YouTubePlaybackState? Read(IntPtr handle)
    {
        using var automation = new UIA3Automation();
        var root = automation.FromHandle(handle);
        var url = YouTubePlayback.NormalizeVideoUrl(Value(Address(root)));
        if (url is null) return null;
        var progress = root.FindFirstDescendant(cf => cf.ByClassName("ytp-progress-bar"))
            ?? throw new InvalidOperationException("YouTube の再生位置を取得できません。動画の読み込み完了後に保存してください。");
        var seconds = YouTubePlayback.ParsePosition(Value(progress));
        var paused = YouTubePlayback.ParsePaused(PlayerButton(root).Name);
        if (!YouTubePlayback.SameVideo(url, Value(Address(root))))
            throw new InvalidOperationException("保存中に動画が切り替わりました。もう一度保存してください。");
        return new(url, seconds, paused);
    }

    public static async Task<IntPtr> RestoreAsync(SavedWindow saved, WindowDiscovery discovery, CancellationToken cancellationToken = default)
    {
        if (!IsChrome(saved) || saved.YouTube is null) throw new InvalidDataException("Chrome の再生状態ではありません。");
        var url = YouTubePlayback.RestoreUrl(saved.YouTube);
        await RestoreGate.WaitAsync(cancellationToken);
        try
        {
            var before = discovery.Capture();
            var live = before.SingleOrDefault(x => x.ExecutablePath == saved.ExecutablePath && x.WindowHandle == saved.WindowHandle
                && saved.ProcessStartTicks != 0 && x.ProcessStartTicks == saved.ProcessStartTicks);
            if (live is not null && saved.PreserveCurrentYouTube)
            {
                // Moving/resizing a live Chrome window does not reset its player. Keep playback
                // uninterrupted; the snapshot is available if Chrome must instead be relaunched.
                return new IntPtr(live.WindowHandle);
            }
            if (live is null)
            {
                var start = new ProcessStartInfo(saved.ExecutablePath) { UseShellExecute = true };
                start.ArgumentList.Add("--new-window");
                start.ArgumentList.Add(url);
                Process.Start(start)?.Dispose();
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < deadline)
                {
                    var added = discovery.Capture().Where(x => x.ExecutablePath == saved.ExecutablePath && !before.Any(old => old.WindowHandle == x.WindowHandle)).ToArray();
                    if (added.Length > 1) throw new InvalidOperationException("新しい Chrome ウィンドウを一意に識別できません。");
                    if (added.Length == 1) { live = added[0]; break; }
                    await Task.Delay(250, cancellationToken);
                }
                if (live is null) throw new TimeoutException("Chrome の起動待機がタイムアウトしました。");
            }
            else
            {
                await Task.Run(() => Navigate(new IntPtr(live.WindowHandle), url), cancellationToken);
            }
            var handle = new IntPtr(live.WindowHandle);
            // Do not mistake the old document, still visible just after Enter, for the restored player.
            await Task.Delay(1500, cancellationToken);
            var until = DateTime.UtcNow.AddSeconds(30);
            Exception? last = null;
            var stable = 0;
            while (DateTime.UtcNow < until)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var current = await ReadForRestoreAsync(handle, cancellationToken);
                    if (current is not null && YouTubePlayback.SameVideo(current.Url, saved.YouTube.Url))
                    {
                        if (!current.IsPaused)
                        {
                            stable = 0;
                            await Task.Run(() => TogglePlayback(handle), cancellationToken);
                        }
                        else if (Math.Abs(current.PositionSeconds - saved.YouTube.PositionSeconds) > 1)
                        {
                            stable = 0;
                            await Task.Run(() => Seek(handle, saved.YouTube.PositionSeconds), cancellationToken);
                        }
                        else if (++stable >= 3)
                        {
                            if (saved.YouTube.IsPaused) return handle;
                            await Task.Run(() => TogglePlayback(handle), cancellationToken);
                            if (await WaitForPlayingAsync(handle, saved.YouTube, cancellationToken)) return handle;
                            stable = 0;
                        }
                    }
                    else stable = 0;
                }
                catch (Exception ex) when (ex is InvalidOperationException or FlaUI.Core.Exceptions.ElementNotAvailableException or COMException or InvalidDataException)
                { last = ex; }
                await Task.Delay(500, cancellationToken);
            }
            throw new TimeoutException("YouTube の再生位置・再生状態を確認できませんでした。広告、ログイン、ライブ配信も確認してください。", last);
        }
        finally { RestoreGate.Release(); }
    }

    private static Task<YouTubePlaybackState?> ReadForRestoreAsync(IntPtr handle, CancellationToken cancellationToken) => Task.Run(() =>
    {
        try { return Read(handle); }
        catch (Exception ex) when (ex is InvalidOperationException or FlaUI.Core.Exceptions.ElementNotAvailableException or COMException or InvalidDataException)
        { return null; }
    }, cancellationToken);

    private static async Task<bool> WaitForPlayingAsync(IntPtr handle, YouTubePlaybackState expected, CancellationToken cancellationToken)
    {
        double? first = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await Task.Delay(500, cancellationToken);
            var state = await ReadForRestoreAsync(handle, cancellationToken);
            if (state is null || state.IsPaused || !YouTubePlayback.SameVideo(state.Url, expected.Url)) return false;
            first ??= state.PositionSeconds;
            if (state.PositionSeconds > first && Math.Abs(first.Value - expected.PositionSeconds) <= 3) return true;
        }
        return false;
    }

    private static void TogglePlayback(IntPtr handle)
    {
        using var automation = new UIA3Automation();
        var button = PlayerButton(automation.FromHandle(handle));
        ClickElement(handle, button);
    }

    private static void Seek(IntPtr handle, double seconds)
    {
        using var automation = new UIA3Automation();
        var progress = automation.FromHandle(handle).FindFirstDescendant(cf => cf.ByClassName("ytp-progress-bar"))
            ?? throw new InvalidOperationException("YouTube の再生位置を取得できません。");
        var current = YouTubePlayback.ParsePosition(Value(progress));
        progress.Focus();
        var delta = seconds - current;
        if (Math.Abs(delta) >= 5)
            SendPlayerKey(handle, delta > 0 ? (byte)0x27 : (byte)0x25);
        else
        {
            // YouTube supports frame stepping while paused. Re-read time after each short batch.
            for (var frame = 0; frame < 15; frame++)
            {
                SendPlayerKey(handle, delta > 0 ? (byte)0xBE : (byte)0xBC);
                Thread.Sleep(20);
            }
        }
    }

    private static void ClickElement(IntPtr handle, AutomationElement element)
    {
        if (GetForegroundWindow() != handle)
            throw new InvalidOperationException("入力先が変わったため Chrome の復元を中止しました。");
        var point = element.GetClickablePoint();
        if (GetAncestor(WindowFromPoint(point), 2) != handle || !SetCursorPos((int)point.X, (int)point.Y))
            throw new InvalidOperationException("Chrome の再生ボタンが別のウィンドウに隠れています。");
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(System.Drawing.Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr handle, uint flags);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    private static void SendPlayerKey(IntPtr handle, byte key)
    {
        if (GetForegroundWindow() != handle)
            throw new InvalidOperationException("入力先が変わったため Chrome の復元を中止しました。");
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 2, UIntPtr.Zero);
    }
    private static void Navigate(IntPtr handle, string url)
    {
        ShowWindow(handle, 9);
        if (!SetForegroundWindow(handle) && GetForegroundWindow() != handle)
            throw new InvalidOperationException("Chrome を前面にできませんでした。");
        using var automation = new UIA3Automation();
        var address = Address(automation.FromHandle(handle));
        address.Focus();
        address.Patterns.Value.Pattern.SetValue(url);
        if (GetForegroundWindow() != handle) throw new InvalidOperationException("入力先が変わったため Chrome の復元を中止しました。");
        keybd_event(0x0D, 0, 0, UIntPtr.Zero);
        keybd_event(0x0D, 0, 2, UIntPtr.Zero);
    }

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);
}
