using System.Diagnostics;
using System.Windows;

namespace Kankei.Desktop;

public partial class AboutWindow : Window
{
    private readonly UpdateService _updates;
    private readonly Func<Task> _apply;
    private bool _closed;
    public AboutWindow(UpdateService updates, Func<Task> apply)
    {
        InitializeComponent();
        _updates = updates;
        _apply = apply;
        VersionText.Text = L.F("バージョン {0}", AppInfo.Version);
        _updates.Changed += UpdateChanged;
        Localization.Current.Changed += UpdateChanged;
        Closed += (_, _) => { _closed = true; _updates.Changed -= UpdateChanged; Localization.Current.Changed -= UpdateChanged; };
        Refresh();
    }
    private void UpdateChanged() { if (!_closed) Dispatcher.BeginInvoke(Refresh); }
    private void Refresh()
    {
        VersionText.Text = L.F("バージョン {0}", AppInfo.Version);
        UpdateStatus.Text = _updates.Status;
        AutomaticUpdates.IsChecked = _updates.AutomaticUpdates;
        var busy = _updates.State is AppUpdateState.Checking or AppUpdateState.Downloading or AppUpdateState.Applying;
        CheckUpdates.IsEnabled = !busy && _updates.State != AppUpdateState.NotInstalled;
        ApplyUpdate.IsEnabled = !busy && _updates.AvailableVersion is not null;
        AutomaticUpdates.IsEnabled = !busy;
    }
    private async void CheckUpdatesClick(object sender, RoutedEventArgs e) => await _updates.CheckAsync();
    private async void ApplyUpdateClick(object sender, RoutedEventArgs e) => await _apply();
    private void AutomaticUpdatesClick(object sender, RoutedEventArgs e)
    {
        try { _updates.SetAutomaticUpdates(AutomaticUpdates.IsChecked == true); }
        catch (Exception ex) { System.Windows.MessageBox.Show(this, ex.Message, L.T("更新設定を保存できませんでした")); Refresh(); }
    }
    private void OpenReleasesClick(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo(AppInfo.Repository + "/releases") { UseShellExecute = true });
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
