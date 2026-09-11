using Kankei.Desktop;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Kankei.Tests;

public class MonitorRestoreIntegrationTests
{
    [MultiMonitorTheory]
    [InlineData(WindowDisplayState.Normal)]
    [InlineData(WindowDisplayState.Maximized)]
    [InlineData(WindowDisplayState.Minimized)]
    public void RestoreMovesRealWindowFromPrimaryToSecondary(WindowDisplayState state)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                var primary = Forms.Screen.PrimaryScreen!;
                var secondary = Forms.Screen.AllScreens.First(x => !x.Primary);
                using var window = new Forms.Form { Text = "Kankei monitor regression test", StartPosition = Forms.FormStartPosition.Manual,
                    Bounds = new System.Drawing.Rectangle(primary.WorkingArea.Left + 100, primary.WorkingArea.Top + 100, 640, 480) };
                window.Show();
                window.WindowState = Forms.FormWindowState.Maximized;
                Forms.Application.DoEvents();
                Assert.Equal(primary.DeviceName, Forms.Screen.FromHandle(window.Handle).DeviceName);
                var target = MonitorPlacement.Capture(secondary);
                var normal = new WindowBounds(target.WorkArea.Left + 50, target.WorkArea.Top + 50, 640, 480);
                var saved = new SavedWindow("test", "test", target.Bounds.Left, target.Bounds.Top, target.Bounds.Width, target.Bounds.Height,
                    state, Monitor: target, NormalBounds: normal);
                RestoreOrchestrator.RestorePlacement(window.Handle, saved);
                Forms.Application.DoEvents();
                Assert.Equal(secondary.DeviceName, Forms.Screen.FromHandle(window.Handle).DeviceName);
                Assert.Equal(state switch { WindowDisplayState.Maximized => Forms.FormWindowState.Maximized,
                    WindowDisplayState.Minimized => Forms.FormWindowState.Minimized, _ => Forms.FormWindowState.Normal }, window.WindowState);
            }
            catch (Exception ex) { failure = ex; }
            finally { SetThreadDpiAwarenessContext(previous); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Native restore test timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}

public sealed class MultiMonitorTheoryAttribute : TheoryAttribute
{
    public MultiMonitorTheoryAttribute()
    {
        if (Forms.Screen.AllScreens.Length < 2) Skip = "Requires two connected monitors and an interactive Windows desktop.";
    }
}
