namespace Kankei.Desktop;

public sealed class LiveWindowMove(WindowDiscovery discovery)
{
    public static SavedWindow PlacementOn(SavedWindow current, SavedMonitor target) => current with
    {
        Monitor = target,
        NormalBounds = MonitorPlacement.Resolve(current, [target])
    };

    public void Move(SavedWindow selected, SavedMonitor target)
    {
        // Title matching could move a replacement window after the dragged window has closed.
        var current = discovery.Capture().FirstOrDefault(x => IsSameInstance(selected, x))
            ?? throw new InvalidOperationException(L.T("対象ウィンドウは閉じられたか、移動できなくなりました。一覧を更新してください。"));
        var connected = System.Windows.Forms.Screen.AllScreens.Select(MonitorPlacement.Capture)
            .FirstOrDefault(x => x.DeviceName == target.DeviceName)
            ?? throw new InvalidOperationException(L.T("移動先のモニターが切断されました。一覧を更新してください。"));
        if (current.Monitor?.DeviceName == connected.DeviceName) return;
        RestoreOrchestrator.RestorePlacement(new IntPtr(current.WindowHandle), PlacementOn(current, connected));
    }

    public static bool IsSameInstance(SavedWindow selected, SavedWindow current) =>
        selected.WindowHandle != 0 && selected.ProcessStartTicks != 0 &&
        selected.WindowHandle == current.WindowHandle && selected.ProcessStartTicks == current.ProcessStartTicks &&
        string.Equals(selected.ExecutablePath, current.ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
        selected.ClassName == current.ClassName;
}
