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
        TitleText.Text = $"還景 ── 「{layoutName}」レイアウトを復元中";
        var complete = job.Items.Count(x => x.Status is RestoreItemStatus.Completed or RestoreItemStatus.Failed or RestoreItemStatus.Skipped);
        ProgressText.Text = $"{complete} / {job.Items.Count} 完了";
        ItemsList.ItemsSource = job.Items.Select(x => $"{StatusIcon(x.Status)} {Path.GetFileNameWithoutExtension(x.ExecutablePath)}  {x.Detail ?? StatusText(x.Status)}").ToList();
    }

    private static string StatusIcon(RestoreItemStatus status) => status switch { RestoreItemStatus.Completed => "☑", RestoreItemStatus.Failed => "×", _ => "⏳" };
    private static string StatusText(RestoreItemStatus status) => status switch { RestoreItemStatus.Launching => "起動中", RestoreItemStatus.Restoring => "復元中", _ => "待機中" };
}
