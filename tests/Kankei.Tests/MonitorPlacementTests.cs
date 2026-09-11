using Kankei.Desktop;

namespace Kankei.Tests;

public class MonitorPlacementTests
{
    private static readonly SavedMonitor Main = new("main", new(0, 0, 3440, 1440), new(0, 0, 3440, 1392));
    private static readonly SavedMonitor Sub = new("sub", new(-1920, 0, 1920, 1080), new(-1920, 0, 1920, 1032));
    private static SavedWindow Saved() => new("chrome.exe", "Chrome", -1928, -8, 1936, 1048, WindowDisplayState.Maximized);

    [Fact] // Reproduction: actual legacy default.json coordinates
    public void LegacyMaximizedChromeUsesNegativeCoordinateMonitor()
    {
        var bounds = MonitorPlacement.Resolve(Saved(), [Main, Sub]);
        Assert.Equal(Sub.WorkArea, bounds);
    }

    [Fact]
    public void MaximizedWindowUsesSavedNormalSizeOnRecordedMonitor()
    {
        var normal = new WindowBounds(-1800, 100, 1000, 700);
        Assert.Equal(normal, MonitorPlacement.Resolve(Saved() with { Monitor = Sub, NormalBounds = normal }, [Main, Sub]));
    }

    [Fact]
    public void DeviceIdentityFollowsRearrangedMonitor()
    {
        var moved = Sub with { Bounds = Sub.Bounds with { Left = 3440 }, WorkArea = Sub.WorkArea with { Left = 3440 } };
        var result = MonitorPlacement.Resolve(Saved() with { Monitor = Sub, NormalBounds = new(-1800, 100, 1000, 700) }, [Main, moved]);
        Assert.Equal(new WindowBounds(3560, 100, 1000, 700), result);
    }

    [Fact]
    public void DisconnectedMonitorFallsBackOnScreen()
    {
        var result = MonitorPlacement.Resolve(Saved() with { Monitor = Sub, NormalBounds = new(-1800, 100, 1000, 700) }, [Main]);
        Assert.Equal(new WindowBounds(120, 100, 1000, 700), result);
    }

    [Fact]
    public void SmallerWorkAreaClampsOversizedWindow()
    {
        var small = Sub with { WorkArea = new(-1920, 40, 800, 600) };
        Assert.Equal(small.WorkArea, MonitorPlacement.Resolve(Saved() with { Monitor = Sub }, [small]));
    }

    [Fact]
    public void NormalWindowKeepsPosition()
    {
        var saved = Saved() with { DisplayState = WindowDisplayState.Normal, Left = 200, Top = 100, Width = 800, Height = 600 };
        Assert.Equal(new WindowBounds(200, 100, 800, 600), MonitorPlacement.Resolve(saved, [Main, Sub]));
    }
}
