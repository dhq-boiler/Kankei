using Kankei.Desktop;
using System.Text.Json;

namespace Kankei.Tests;

public class ChromePageStateTests
{
    private static SavedWindow Window(long handle, string? url) => new("chrome.exe", "Chrome_WidgetWin_1", 0, 0, 800, 600,
        WindowDisplayState.Normal, WindowHandle: handle, ProcessStartTicks: 10, BrowserUrl: url);

    [Theory] // EQ-1: canonical addresses and Twitter's host redirect
    [InlineData("x.com/home", "https://x.com/home")]
    [InlineData("https://EXAMPLE.com:443", "https://example.com/")]
    [InlineData("https://twitter.com/home", "https://x.com/home")]
    public void EquivalentPageAddressesMatch(string left, string right) => Assert.True(ChromePageState.SamePage(left, right));

    [Theory] // EQ-2: page identity must not collapse to host only
    [InlineData("https://x.com/home", "https://x.com/messages")]
    [InlineData("https://example.com/?a=1", "https://example.com/?a=2")]
    [InlineData("https://example.com/#one", "https://example.com/#two")]
    [InlineData("https://x.com/home", "https://x.com.attacker.test/home")]
    public void DifferentPagesDoNotMatch(string left, string right) => Assert.False(ChromePageState.SamePage(left, right));

    [Theory] // ER-1: non-web input and credentials must never become Chrome launch arguments
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("chrome://settings")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:password@example.com")]
    [InlineData("https://example.com --incognito")]
    public void UnsupportedUrlsAreRejected(string? value) => Assert.Null(ChromePageState.NormalizeUrl(value));

    [Fact] // BUG-1: same HWND after navigation must not overwrite the current page
    public void OriginalWindowOnDifferentPageIsNotReused() =>
        Assert.Null(ChromePageState.Match(Window(1, "https://x.com/home"), [Window(1, "https://example.com/")]));

    [Fact] // ST-1: repeat restore reuses the newly launched page instead of spawning another
    public void MatchingPageInNewWindowIsReused() =>
        Assert.Equal(2, ChromePageState.Match(Window(1, "https://x.com/home"),
            [Window(1, "https://example.com/"), Window(2, "https://x.com/home")])!.WindowHandle);

    [Fact] // DT-1: among matching pages prefer the original instance
    public void OriginalMatchingInstanceWins() =>
        Assert.Equal(1, ChromePageState.Match(Window(1, "https://x.com/home"),
            [Window(2, "https://x.com/home"), Window(1, "https://x.com/home")])!.WindowHandle);

    [Fact] // EQ-3: no candidate means a new window is needed
    public void NoChromeHasNoMatch() => Assert.Null(ChromePageState.Match(Window(1, "https://x.com/home"), []));

    [Fact] // Compatibility: older layout JSON has no BrowserUrl
    public void LegacyLayoutStillDeserializes()
    {
        var old = """{"ExecutablePath":"chrome.exe","ClassName":"Chrome","Left":0,"Top":0,"Width":800,"Height":600,"DisplayState":0}""";
        Assert.Null(JsonSerializer.Deserialize<SavedWindow>(old)!.BrowserUrl);
    }
}
