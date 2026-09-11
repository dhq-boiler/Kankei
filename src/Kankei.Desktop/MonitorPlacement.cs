using System.Drawing;
using System.Windows.Forms;

namespace Kankei.Desktop;

public static class MonitorPlacement
{
    public static WindowBounds Bounds(Rectangle rectangle) => new(rectangle.Left, rectangle.Top, rectangle.Width, rectangle.Height);
    public static SavedMonitor Capture(Screen screen) => new(screen.DeviceName, Bounds(screen.Bounds), Bounds(screen.WorkingArea));

    public static WindowBounds Resolve(SavedWindow saved, IReadOnlyList<SavedMonitor> monitors)
    {
        if (monitors.Count == 0) throw new InvalidOperationException("復元先のモニターがありません。");
        var visible = new WindowBounds(saved.Left, saved.Top, saved.Width, saved.Height);
        var target = monitors.FirstOrDefault(x => saved.Monitor is not null && x.DeviceName == saved.Monitor.DeviceName)
            ?? monitors.OrderByDescending(x => Overlap(saved.Monitor?.Bounds ?? visible, x.Bounds)).First();
        var normal = saved.NormalBounds ?? visible;
        if (saved.Monitor is { } old)
        {
            normal = normal with
            {
                Left = normal.Left + target.WorkArea.Left - old.WorkArea.Left,
                Top = normal.Top + target.WorkArea.Top - old.WorkArea.Top
            };
        }
        var area = target.WorkArea;
        var width = Math.Clamp(normal.Width, 1, area.Width);
        var height = Math.Clamp(normal.Height, 1, area.Height);
        return new(Math.Clamp(normal.Left, area.Left, area.Left + area.Width - width),
            Math.Clamp(normal.Top, area.Top, area.Top + area.Height - height), width, height);
    }

    private static long Overlap(WindowBounds a, WindowBounds b) =>
        Math.Max(0L, Math.Min((long)a.Left + a.Width, (long)b.Left + b.Width) - Math.Max(a.Left, b.Left)) *
        Math.Max(0L, Math.Min((long)a.Top + a.Height, (long)b.Top + b.Height) - Math.Max(a.Top, b.Top));
}
