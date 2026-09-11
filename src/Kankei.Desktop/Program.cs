using Velopack;

namespace Kankei.Desktop;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        using var instance = new Mutex(true, @"Local\Kankei.Desktop", out var created);
        if (!created) return;
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
