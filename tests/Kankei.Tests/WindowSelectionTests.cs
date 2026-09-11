using Kankei.Desktop;

namespace Kankei.Tests;

public class WindowSelectionTests
{
    private static SavedWindow Window(long handle, string title = "A", long started = 100) =>
        new(@"C:\Editor.exe", "Editor", 10, 20, 400, 300, WindowDisplayState.Normal,
            Title: title, WindowHandle: handle, ProcessStartTicks: started);

    [Fact]
    public void OnlySelectedWindowUsesLatestPlacementWithoutApplicationState()
    {
        var selected = Window(1);
        var live = selected with { Left = 500, AdapterId = "app", AdapterStateJson = "{}" };
        var result = Assert.Single(WindowSelection.CaptureSelected([selected], [live, Window(2, "B")]));
        Assert.Equal(500, result.Left);
        Assert.Equal(1, result.WindowHandle);
        Assert.Null(result.AdapterId);
        Assert.Null(result.AdapterStateJson);
    }

    [Fact]
    public void EmptySelectionFails() => Assert.Throws<InvalidOperationException>(() => WindowSelection.CaptureSelected([], [Window(1)]));

    [Fact]
    public void ClosedWindowAbortsEntireSelection() =>
        Assert.Throws<InvalidOperationException>(() => WindowSelection.CaptureSelected([Window(1), Window(2)], [Window(1)]));

    [Fact]
    public void ReusedHandleFromAnotherProcessIsRejected() =>
        Assert.Throws<InvalidOperationException>(() => WindowSelection.CaptureSelected([Window(1)], [Window(1, started: 200)]));

    [Fact]
    public void ExistingInstanceWinsEvenIfTitleChanged() =>
        Assert.Equal(2, WindowDiscovery.Match(Window(2), [Window(1), Window(2, "Changed")])!.WindowHandle);

    [Fact]
    public void RestartedApplicationMatchesTitle() =>
        Assert.Equal(3, WindowDiscovery.Match(Window(1), [Window(2, "B", 200), Window(3, "A", 200)])!.WindowHandle);

    [Fact]
    public void AmbiguousTitlesAreRejected() =>
        Assert.Throws<InvalidOperationException>(() => WindowDiscovery.Match(Window(1), [Window(2), Window(3)]));

    [Fact]
    public void OtherWindowInSameApplicationIsNotMatched() =>
        Assert.Throws<InvalidOperationException>(() => WindowDiscovery.Match(Window(1), [Window(2, "B")]));

    [Fact]
    public void MissingApplicationHasNoMatch() => Assert.Null(WindowDiscovery.Match(Window(1), []));

    [Fact]
    public void LegacyLayoutMatchesUniqueCandidate() =>
        Assert.Equal(2, WindowDiscovery.Match(Window(0) with { Title = null, ProcessStartTicks = 0 }, [Window(2, "B")])!.WindowHandle);

    [Fact]
    public void WpfClassInstanceChangesAcrossRestarts() =>
        Assert.Equal(2, WindowDiscovery.Match(Window(1) with { ClassName = "HwndWrapper[Editor;;old]" },
            [Window(2, started: 200) with { ClassName = "HwndWrapper[Editor;;new]" }])!.WindowHandle);
}
