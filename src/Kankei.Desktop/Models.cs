namespace Kankei.Desktop;

public sealed record SavedWindow(
    string ExecutablePath,
    string ClassName,
    int Left,
    int Top,
    int Width,
    int Height,
    WindowDisplayState DisplayState,
    string? AdapterId = null,
    string? AdapterStateJson = null,
    string? Title = null,
    long WindowHandle = 0,
    long ProcessStartTicks = 0,
    SavedMonitor? Monitor = null,
    WindowBounds? NormalBounds = null,
    YouTubePlaybackState? YouTube = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool PreserveCurrentYouTube { get; init; }
}

public sealed record YouTubePlaybackState(string Url, double PositionSeconds, bool IsPaused, int Version = 1);

public sealed record WindowBounds(int Left, int Top, int Width, int Height);
public sealed record SavedMonitor(string DeviceName, WindowBounds Bounds, WindowBounds WorkArea);

public enum WindowDisplayState { Normal, Minimized, Maximized }

public sealed record Layout(string Id, string Name, DateTimeOffset SavedAt, IReadOnlyList<SavedWindow> Windows);

public enum RestoreStatus { Running, Completed, CompletedWithFailures, Failed }
public enum RestoreItemStatus { Pending, Launching, Restoring, Completed, Failed, Skipped }

public sealed record RestoreItem(string ExecutablePath, RestoreItemStatus Status, string? Detail = null);
public sealed record RestoreEvent(DateTimeOffset OccurredAt, string Message, string? ExecutablePath = null);

public sealed class RestoreJob
{
    public RestoreJob(string id, string layoutId)
    {
        Id = id;
        LayoutId = layoutId;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public string Id { get; }
    public string LayoutId { get; }
    public DateTimeOffset StartedAt { get; }
    public RestoreStatus Status { get; set; } = RestoreStatus.Running;
    public List<RestoreItem> Items { get; } = [];
    public List<RestoreEvent> Events { get; } = [];
}

public sealed record CreateLayoutRequest(string Name, bool IncludeYouTube = true);
public sealed record RestoreRequest(bool ShowOverlay = true);
