using System.Runtime.InteropServices;
using Kankei.Desktop;
using Forms = System.Windows.Forms;

namespace Kankei.Tests;

[Collection("Foreground desktop")]
public class WindowForegroundTests
{
    [Fact]
    public void RestoreRaisesGroupInSavedOrderAndPreservesBoundsAndMinimizedState()
    {
        RunOnSta(() =>
        {
            using var first = new Forms.Form { Text = "Kankei foreground test first" };
            using var second = new Forms.Form { Text = "Kankei foreground test second" };
            using var minimized = new Forms.Form { Text = "Kankei foreground test minimized" };
            using var unrelated = new Forms.Form { Text = "Kankei foreground test unrelated" };
            first.Show(); second.Show(); minimized.Show();
            minimized.WindowState = Forms.FormWindowState.Minimized;
            unrelated.Show();
            Forms.Application.DoEvents();
            var firstBounds = first.Bounds;
            var secondBounds = second.Bounds;

            var activated = WindowForeground.Restore([first.Handle, minimized.Handle, second.Handle, first.Handle]);
            Forms.Application.DoEvents();

            // Background test runners are subject to the Windows foreground lock.
            if (activated) Assert.Equal(first.Handle, GetForegroundWindow());
            var order = new List<IntPtr>();
            EnumWindows((handle, _) => { order.Add(handle); return true; }, IntPtr.Zero);
            Assert.True(order.IndexOf(first.Handle) < order.IndexOf(second.Handle));
            Assert.True(order.IndexOf(second.Handle) < order.IndexOf(unrelated.Handle));
            Assert.Equal(firstBounds, first.Bounds);
            Assert.Equal(secondBounds, second.Bounds);
            Assert.Equal(Forms.FormWindowState.Minimized, minimized.WindowState);
        });
    }

    [Fact]
    public void RestoreIgnoresEmptyClosedAndHiddenWindowsWithoutChangingForeground()
    {
        RunOnSta(() =>
        {
            using var active = new Forms.Form();
            using var hidden = new Forms.Form();
            using var closed = new Forms.Form();
            var closedHandle = closed.Handle;
            closed.Close();
            active.Show();
            Forms.Application.DoEvents();
            var foreground = GetForegroundWindow();
            Assert.True(WindowForeground.Restore([]));
            Assert.True(WindowForeground.Restore([IntPtr.Zero, closedHandle, hidden.Handle]));
            Assert.Equal(foreground, GetForegroundWindow());
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Foreground test timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}

[CollectionDefinition("Foreground desktop", DisableParallelization = true)]
public class ForegroundDesktopCollection;
