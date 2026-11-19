using Kankei.Desktop;

namespace Kankei.Tests;

public class LiveWindowMoveTests
{
    private static readonly SavedMonitor Source = new("source", new(0, 0, 1920, 1080), new(0, 40, 1920, 1040));
    private static readonly SavedMonitor Target = new("target", new(-1280, -200, 1280, 1024), new(-1280, -200, 1280, 984));
    private static SavedWindow Window(WindowDisplayState state = WindowDisplayState.Normal) =>
        new("app.exe", "AppWindow", 100, 140, 800, 600, state, WindowHandle: 42, ProcessStartTicks: 100,
            Monitor: Source, NormalBounds: new(100, 140, 800, 600));

    [Theory]
    [InlineData(WindowDisplayState.Normal)]
    [InlineData(WindowDisplayState.Maximized)]
    [InlineData(WindowDisplayState.Minimized)]
    public void EQ1_MovePreservesDisplayStateAndNormalPositionRelativeToWorkArea(WindowDisplayState state)
    {
        var moved = LiveWindowMove.PlacementOn(Window(state), Target);
        Assert.Equal(state, moved.DisplayState);
        Assert.Equal(Target, moved.Monitor);
        Assert.Equal(new WindowBounds(-1180, -100, 800, 600), moved.NormalBounds);
        Assert.Equal(moved.NormalBounds, MonitorPlacement.Resolve(moved, [Source, Target]));
        Assert.Equal(state == WindowDisplayState.Maximized ? Target.WorkArea : moved.NormalBounds,
            MonitorPlacement.PreviewBounds(moved, [Source, Target]));
    }

    [Theory]
    [InlineData(1279, 983, 1279, 983)]
    [InlineData(1280, 984, 1280, 984)]
    [InlineData(1281, 985, 1280, 984)]
    public void BV1_MoveFitsWindowAtBothSidesOfDestinationSizeBoundary(int width, int height, int expectedWidth, int expectedHeight)
    {
        var moved = LiveWindowMove.PlacementOn(Window() with { NormalBounds = new(100, 140, width, height) }, Target);
        var bounds = moved.NormalBounds!;
        Assert.Equal(expectedWidth, bounds.Width);
        Assert.Equal(expectedHeight, bounds.Height);
        Assert.InRange(bounds.Left, Target.WorkArea.Left, Target.WorkArea.Left + Target.WorkArea.Width - bounds.Width);
        Assert.InRange(bounds.Top, Target.WorkArea.Top, Target.WorkArea.Top + Target.WorkArea.Height - bounds.Height);
    }

    [Fact]
    public void ER1_ClosedOrReplacedWindowCannotMatchByTitleOrReusedHandle()
    {
        var selected = Window();
        Assert.True(LiveWindowMove.IsSameInstance(selected, selected with { Title = "Changed title" }));
        Assert.False(LiveWindowMove.IsSameInstance(selected, selected with { WindowHandle = 43 }));
        Assert.False(LiveWindowMove.IsSameInstance(selected, selected with { ProcessStartTicks = 101 }));
        Assert.False(LiveWindowMove.IsSameInstance(selected, selected with { ExecutablePath = "other.exe" }));
        Assert.False(LiveWindowMove.IsSameInstance(selected, selected with { ClassName = "OtherWindow" }));
        Assert.False(LiveWindowMove.IsSameInstance(selected with { ProcessStartTicks = 0 }, selected with { ProcessStartTicks = 0 }));
        Assert.False(LiveWindowMove.IsSameInstance(selected with { WindowHandle = 0 }, selected with { WindowHandle = 0 }));
        Assert.Throws<InvalidOperationException>(() => new LiveWindowMove(new WindowDiscovery()).Move(selected, Target));
    }
}
