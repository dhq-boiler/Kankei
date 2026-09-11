using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace Kankei.Desktop;

public static class ChromePageState
{
    public static bool IsChrome(SavedWindow window) =>
        string.Equals(Path.GetFileName(window.ExecutablePath), "chrome.exe", StringComparison.OrdinalIgnoreCase);

    public static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (!value.Contains("://", StringComparison.Ordinal)) value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")
            || uri.Host.Length == 0 || uri.UserInfo.Length != 0 || value.Any(char.IsWhiteSpace)) return null;
        return uri.AbsoluteUri;
    }

    public static bool SamePage(string? left, string? right)
    {
        var a = NormalizeUrl(left);
        var b = NormalizeUrl(right);
        return a is not null && b is not null && Canonical(a) == Canonical(b);
    }

    private static string Canonical(string url)
    {
        var uri = new Uri(url);
        // Twitter redirects its canonical host to X. Paths, query and fragments remain significant.
        return uri.Host is "twitter.com" or "www.twitter.com" or "www.x.com"
            ? new UriBuilder(uri) { Host = "x.com" }.Uri.AbsoluteUri : url;
    }

    public static SavedWindow? Match(SavedWindow saved, IReadOnlyList<SavedWindow> current)
    {
        var matches = current.Where(x => IsChrome(x)
            && string.Equals(x.ExecutablePath, saved.ExecutablePath, StringComparison.OrdinalIgnoreCase)
            && SamePage(saved.BrowserUrl, x.BrowserUrl)).ToArray();
        return matches.FirstOrDefault(x => saved.WindowHandle != 0 && x.WindowHandle == saved.WindowHandle
                && saved.ProcessStartTicks != 0 && x.ProcessStartTicks == saved.ProcessStartTicks)
            ?? matches.FirstOrDefault();
    }

    public static async Task<IReadOnlyList<SavedWindow>> CaptureAsync(IReadOnlyList<SavedWindow> windows, CancellationToken cancellationToken = default)
    {
        var result = new List<SavedWindow>(windows.Count);
        foreach (var window in windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = IsChrome(window) && window.AdapterId is null
                ? await Task.Run(() => ReadUrl(new IntPtr(window.WindowHandle)), cancellationToken) : null;
            result.Add(window with { BrowserUrl = url });
        }
        return result;
    }

    private static string? ReadUrl(IntPtr handle)
    {
        using var automation = new UIA3Automation();
        return ReadUrl(automation.FromHandle(handle));
    }

    private static string? ReadUrl(AutomationElement root)
    {
        var address = root.FindFirstDescendant(cf => cf.ByClassName("OmniboxViewViews"));
        if (address is null || !address.Patterns.Value.IsSupported)
            throw new InvalidOperationException("Chrome のURLを確認できません。アドレスバーが表示される通常のウィンドウで再度お試しください。");
        return NormalizeUrl(address.Patterns.Value.Pattern.Value.Value);
    }

    private static bool TrySelectMatchingTab(SavedWindow saved, SavedWindow candidate)
    {
        if (string.IsNullOrWhiteSpace(saved.Title)) return false;
        using var automation = new UIA3Automation();
        var root = automation.FromHandle(new IntPtr(candidate.WindowHandle));
        var tabs = root.FindAllDescendants(cf => cf.ByControlType(ControlType.TabItem));
        var previous = tabs.FirstOrDefault(t => t.Patterns.SelectionItem.IsSupported && t.Patterns.SelectionItem.Pattern.IsSelected.Value);
        if (previous is null) return false;
        var title = saved.Title.Replace(" - Google Chrome", "", StringComparison.Ordinal);
        foreach (var tab in tabs.Where(t => (t.Name == title || t.Name.StartsWith(title + " - ", StringComparison.Ordinal))
            && t.Patterns.SelectionItem.IsSupported))
        {
            var matched = false;
            try
            {
                tab.Patterns.SelectionItem.Pattern.Select();
                for (var attempt = 0; attempt < 10 && !matched; attempt++)
                {
                    matched = SamePage(saved.BrowserUrl, ReadUrl(root));
                    if (!matched) Thread.Sleep(50);
                }
                if (matched) return true;
            }
            finally { if (!matched) previous.Patterns.SelectionItem.Pattern.Select(); }
        }
        return false;
    }

    public static async Task<IntPtr> RestoreAsync(SavedWindow saved, WindowDiscovery discovery, CancellationToken cancellationToken = default)
    {
        var url = NormalizeUrl(saved.BrowserUrl);
        if (!IsChrome(saved) || url is null) throw new InvalidDataException("復元するChromeのHTTP(S) URLが不正です。");
        await ChromeYouTubeState.RestoreGate.WaitAsync(cancellationToken);
        try
        {
            var before = discovery.Capture();
            var candidates = before.Where(x => string.Equals(x.ExecutablePath, saved.ExecutablePath, StringComparison.OrdinalIgnoreCase)).ToArray();
            var pages = new List<SavedWindow>();
            Exception? unreadable = null;
            foreach (var candidate in candidates)
            {
                try { pages.Add(candidate with { BrowserUrl = await Task.Run(() => ReadUrl(new IntPtr(candidate.WindowHandle)), cancellationToken) }); }
                catch (Exception ex) when (ex is InvalidOperationException or COMException or FlaUI.Core.Exceptions.ElementNotAvailableException) { unreadable = ex; }
            }
            if (Match(saved, pages) is { } existing) return new IntPtr(existing.WindowHandle);
            foreach (var candidate in candidates)
            {
                if (await Task.Run(() => TrySelectMatchingTab(saved, candidate), cancellationToken)) return new IntPtr(candidate.WindowHandle);
            }
            if (unreadable is not null) throw new InvalidOperationException("既存ChromeのURLを確認できないため、新規ウィンドウの作成を中止しました。", unreadable);
            if (!File.Exists(saved.ExecutablePath)) throw new FileNotFoundException("Chrome が見つかりません。", saved.ExecutablePath);
            var start = new ProcessStartInfo(saved.ExecutablePath) { UseShellExecute = true };
            start.ArgumentList.Add("--new-window");
            start.ArgumentList.Add(url);
            Process.Start(start)?.Dispose();
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var added = discovery.Capture().Where(x => string.Equals(x.ExecutablePath, saved.ExecutablePath, StringComparison.OrdinalIgnoreCase)
                    && !before.Any(old => old.WindowHandle == x.WindowHandle && old.ProcessStartTicks == x.ProcessStartTicks)).ToArray();
                foreach (var candidate in added)
                {
                    try
                    {
                        if (SamePage(url, await Task.Run(() => ReadUrl(new IntPtr(candidate.WindowHandle)), cancellationToken))) return new IntPtr(candidate.WindowHandle);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or COMException or FlaUI.Core.Exceptions.ElementNotAvailableException) { }
                }
                await Task.Delay(200, cancellationToken);
            }
            throw new TimeoutException("保存したURLを開いたChromeを確認できませんでした。ログインへのリダイレクトや通信状態を確認してください。");
        }
        finally { ChromeYouTubeState.RestoreGate.Release(); }
    }
}
