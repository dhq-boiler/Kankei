using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Kankei.Desktop;

public sealed class WindowDiscovery
{
    public IReadOnlyList<SavedWindow> Capture()
    {
        var windows = new List<SavedWindow>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || GetWindow(handle, 4) != IntPtr.Zero
                || (GetWindowLongPtr(handle, -20).ToInt64() & 0x80) != 0)
                return true;
            if (DwmGetWindowAttribute(handle, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;

            GetWindowThreadProcessId(handle, out var processId);
            if (processId == Environment.ProcessId) return true;
            var executable = TryGetExecutablePath(processId);
            if (string.IsNullOrWhiteSpace(executable) || !GetWindowRect(handle, out var rectangle))
                return true;

            var className = new StringBuilder(256);
            GetClassName(handle, className, className.Capacity);
            if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            var title = new StringBuilder(2048);
            GetWindowText(handle, title, title.Capacity);
            long started = 0;
            try { using var process = Process.GetProcessById((int)processId); started = process.StartTime.ToUniversalTime().Ticks; }
            catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception or InvalidOperationException) { }
            var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (!GetWindowPlacement(handle, ref placement)) return true;
            var monitor = MonitorPlacement.Capture(System.Windows.Forms.Screen.FromHandle(handle));
            var normal = placement.rcNormalPosition;
            // WINDOWPLACEMENT uses workspace coordinates; SetWindowPos uses screen coordinates.
            var normalBounds = new WindowBounds(normal.Left + monitor.WorkArea.Left - monitor.Bounds.Left,
                normal.Top + monitor.WorkArea.Top - monitor.Bounds.Top, normal.Right - normal.Left, normal.Bottom - normal.Top);
            windows.Add(new SavedWindow(
                executable, className.ToString(), rectangle.Left, rectangle.Top,
                rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top,
                placement.showCmd switch { 2 => WindowDisplayState.Minimized, 3 => WindowDisplayState.Maximized, _ => WindowDisplayState.Normal },
                Title: title.ToString(), WindowHandle: handle.ToInt64(), ProcessStartTicks: started,
                Monitor: monitor, NormalBounds: placement.showCmd is 2 or 3 ? normalBounds :
                    new WindowBounds(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top)));
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    public IntPtr FindWindow(SavedWindow saved)
    {
        return new IntPtr(Match(saved, Capture())?.WindowHandle ?? 0);
    }

    public static SavedWindow? Match(SavedWindow saved, IReadOnlyList<SavedWindow> current)
    {
        var candidates = current.Where(x => string.Equals(x.ExecutablePath, saved.ExecutablePath, StringComparison.OrdinalIgnoreCase)
            && ClassIdentity(x.ClassName) == ClassIdentity(saved.ClassName)).ToArray();
        var sameInstance = candidates.FirstOrDefault(x => saved.WindowHandle != 0 && x.WindowHandle == saved.WindowHandle
            && saved.ProcessStartTicks != 0 && x.ProcessStartTicks == saved.ProcessStartTicks);
        if (sameInstance is not null) return sameInstance;
        var matches = candidates.Where(x => saved.Title is null || x.Title == saved.Title).ToArray();
        if (matches.Length > 1 || (matches.Length == 0 && candidates.Length > 0))
            throw new InvalidOperationException("対象ウィンドウを一意に識別できません。対象を開いて配置を保存し直してください。");
        return matches.SingleOrDefault();
    }

    private static string ClassIdentity(string name) => name.StartsWith("HwndWrapper[", StringComparison.Ordinal)
        ? name.Split(';')[0] : name;

    private static string? TryGetExecutablePath(uint processId)
    {
        try { using var process = Process.GetProcessById((int)processId); return process.MainModule?.FileName; }
        catch (ArgumentException) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct WINDOWPLACEMENT
    {
        public int length, flags, showCmd; public POINT ptMinPosition, ptMaxPosition; public RECT rcNormalPosition;
    }
    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr handle, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr handle, out RECT rectangle);
    [DllImport("user32.dll")] private static extern int GetClassName(IntPtr handle, StringBuilder name, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr handle, ref WINDOWPLACEMENT placement);
}
