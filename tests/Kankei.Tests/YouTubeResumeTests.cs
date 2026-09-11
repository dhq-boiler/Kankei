using Kankei.Desktop;

namespace Kankei.Tests;

public sealed class YouTubeResumeTests
{
    private static YouTubePlaybackState State(double seconds, bool paused = false) =>
        new("https://youtube.com/watch?v=abc&list=PL123&index=3", seconds, paused);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentPlayerWinsOverOldProfileAndCheckpoint(bool paused)
    {
        var current = State(100, paused);
        Assert.Equal(current, YouTubeResumeStore.Select(State(10), State(50), current));
    }

    [Fact]
    public void ClosedChromeUsesLatestCheckpoint() =>
        Assert.Equal(State(50, true), YouTubeResumeStore.Select(State(10), State(50, true), null));

    [Fact]
    public void WithoutCheckpointUsesProfile() =>
        Assert.Equal(State(10), YouTubeResumeStore.Select(State(10), null, null));

    [Fact]
    public void PositionOnlyProfileDoesNotEnablePlaybackRestore() =>
        Assert.Null(YouTubeResumeStore.Select(null, State(50), State(100)));

    [Fact]
    public void TemporaryLivePlaybackFlagIsNeverPersisted()
    {
        var window = new SavedWindow("chrome.exe", "Chrome", 0, 0, 100, 100, WindowDisplayState.Normal,
            YouTube: State(10)) { PreserveCurrentYouTube = true };
        var json = System.Text.Json.JsonSerializer.Serialize(window);
        Assert.DoesNotContain("PreserveCurrentYouTube", json);
        Assert.False(System.Text.Json.JsonSerializer.Deserialize<SavedWindow>(json)!.PreserveCurrentYouTube);
    }

    [Fact]
    public async Task CheckpointSurvivesNewStoreButDoesNotOverwriteProfileOrLeakIntoResavedProfile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Kankei-resume-" + Guid.NewGuid().ToString("N"));
        var layout = new Layout("work", "work", DateTimeOffset.UtcNow, []);
        try
        {
            await new YouTubeResumeStore(directory).SaveAsync(layout, 0, State(100, true));
            var reopened = new YouTubeResumeStore(directory);
            Assert.Equal(State(100, true), await reopened.GetAsync(layout, 0));
            Assert.Null(await reopened.GetAsync(layout, 1));
            Assert.Null(await reopened.GetAsync(layout with { SavedAt = layout.SavedAt.AddTicks(1) }, 0));
            Assert.Null(await reopened.GetAsync(layout with { Id = "other" }, 0));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task InvalidOrCancelledCaptureLeavesPreviousCheckpointIntact()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Kankei-resume-" + Guid.NewGuid().ToString("N"));
        var layout = new Layout("work", "work", DateTimeOffset.UtcNow, []);
        try
        {
            var store = new YouTubeResumeStore(directory);
            await store.SaveAsync(layout, 0, State(50));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(layout, 0, State(double.NaN)));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(layout, 0, State(100), cancelled.Token));
            Assert.Equal(State(50), await store.GetAsync(layout, 0));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
