using System.Runtime.InteropServices;

namespace Kankei.Desktop;

public static class WindowForeground
{
    // Capture enumerates windows from front to back. Raise in reverse order,
    // without changing their position, size, minimized state or topmost setting.
    public static bool Restore(IEnumerable<IntPtr> restoredWindows)
    {
        var handles = restoredWindows.Distinct()
            .Where(handle => IsWindowVisible(handle) && !IsIconic(handle)).ToArray();
        var succeeded = true;
        for (var index = handles.Length - 1; index >= 0; index--)
            succeeded &= SetWindowPos(handles[index], IntPtr.Zero, 0, 0, 0, 0,
                0x0001 | 0x0002 | 0x0010); // NOSIZE | NOMOVE | NOACTIVATE

        // Windows can reject activation when another process owns foreground input.
        // Do not attach input queues: an unresponsive target could hang our UI.
        return handles.Length == 0 || (SetForegroundWindow(handles[0]) && succeeded);
    }

    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
