using Kankei.Desktop;

namespace Kankei.Tests;

public class YouTubePlaybackTests
{
    [Fact]
    public void PlaylistUrlRetainsVideoListAndIndexWithSavedTime()
    {
        var state = new YouTubePlaybackState("https://www.youtube.com/watch?v=abc&list=PL123&index=3&t=5s", 10516, true);
        var restored = YouTubePlayback.RestoreUrl(state);
        Assert.Contains("list=PL123", restored);
        Assert.Contains("index=3", restored);
        Assert.Contains("t=10516s", restored);
        Assert.DoesNotContain("t=5s", restored);
        Assert.True(YouTubePlayback.SameVideo(state.Url, restored));
    }

    [Theory]
    [InlineData("2 時間 55 分 16 秒 / 3 時間 42 分 56 秒", 10516)]
    [InlineData("1 minute 2 seconds / 10 minutes", 62)]
    [InlineData("0 秒 / 1 分", 0)]
    [InlineData("2:55:16 / 3:42:56", 10516)]
    [InlineData("0:01 / 10:00", 1)]
    public void ParsesPlayerTime(string text, double seconds) => Assert.Equal(seconds, YouTubePlayback.ParsePosition(text));

    [Theory]
    [InlineData("再生（k）", true)]
    [InlineData("一時停止（k）", false)]
    [InlineData("Play (k)", true)]
    [InlineData("Pause (k)", false)]
    public void ReadsPausedState(string text, bool paused) => Assert.Equal(paused, YouTubePlayback.ParsePaused(text));

    [Theory]
    [InlineData("https://youtube.com.evil.test/watch?v=abc")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://example.com/watch?v=abc")]
    [InlineData("https://youtube.com/playlist?list=PL123")]
    [InlineData("https://youtube.com/watch")]
    public void RejectsNonVideoUrls(string url) => Assert.Null(YouTubePlayback.NormalizeVideoUrl(url));

    [Fact]
    public void HandlesOmniboxWithoutSchemeAndStandaloneVideo()
    {
        var url = YouTubePlayback.NormalizeVideoUrl("youtube.com/watch?v=abc");
        Assert.Equal("https://youtube.com/watch?v=abc", url);
        Assert.True(YouTubePlayback.SameVideo(url!, "https://www.youtube.com/watch?v=abc&t=1s"));
        Assert.False(YouTubePlayback.SameVideo(url!, "https://www.youtube.com/watch?v=abc&list=PL123"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidSavedTime(double seconds) =>
        Assert.Throws<InvalidDataException>(() => YouTubePlayback.RestoreUrl(new("https://youtube.com/watch?v=abc", seconds, true)));

    [Fact]
    public void RejectsUnsupportedStateVersion() =>
        Assert.Throws<InvalidDataException>(() => YouTubePlayback.RestoreUrl(new("https://youtube.com/watch?v=abc", 0, true, 2)));

    [Fact]
    public void AllowsYouTubeToCanonicalizePlaylistIndex()
    {
        Assert.True(YouTubePlayback.SameVideo("https://youtube.com/watch?v=abc&list=PL123&index=3",
            "https://youtube.com/watch?v=abc&list=PL123&index=4"));
        Assert.False(YouTubePlayback.SameVideo("https://youtube.com/watch?v=abc&list=PL123&index=3",
            "https://youtube.com/watch?v=def&list=PL123&index=3"));
    }

    [Theory]
    [InlineData("https://youtube.com/watch?v=next&list=PL123&index=4", true)]
    [InlineData("https://youtube.com/watch?v=abc&list=OTHER", false)]
    [InlineData("https://youtube.com/watch?v=abc", false)]
    public void ReusesReopenedPlaylistEvenAfterAdvancingToNextVideo(string current, bool matches) =>
        Assert.Equal(matches, YouTubePlayback.SamePlaybackContext("https://youtube.com/watch?v=abc&list=PL123", current));

    [Fact]
    public void RejectsLiveOrUnavailableTime() => Assert.Throws<InvalidDataException>(() => YouTubePlayback.ParsePosition("ライブ"));
}
