using System.Windows;
using System.IO;

namespace Kankei.Desktop;

public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
        Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - 500) / 2;
        Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - 260) / 2;
    }

    public void Update(string layoutName, RestoreJob job)
    {
        TitleText.Text = L.F("還景 ── 「{0}」レイアウトを復元中", layoutName);
        var complete = job.Items.Count(x => x.Status is RestoreItemStatus.Completed or RestoreItemStatus.Failed or RestoreItemStatus.Skipped);
        ProgressText.Text = L.F("{0} / {1} 完了", complete, job.Items.Count);
        ItemsList.ItemsSource = job.Items.Select(x => $"{StatusIcon(x.Status)} {Path.GetFileNameWithoutExtension(x.ExecutablePath)}  {x.Detail ?? StatusText(x.Status)}").ToList();
    }

    private static string StatusIcon(RestoreItemStatus status) => status switch { RestoreItemStatus.Completed => "☑", RestoreItemStatus.Failed => "×", _ => "⏳" };
    private static string StatusText(RestoreItemStatus status) => status switch { RestoreItemStatus.Launching => L.T("起動中"), RestoreItemStatus.Restoring => L.T("復元中"), _ => L.T("待機中") };
}
