namespace Kankei.Desktop;

public sealed class OverlayService
{
    private OverlayWindow? _window;
    private string _layoutName = string.Empty;

    public void Show(string layoutName, RestoreJob job)
    {
        _layoutName = layoutName;
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _window ??= new OverlayWindow();
            _window.Update(_layoutName, job);
            if (!_window.IsVisible) _window.Show();
        });
    }

    public void Refresh(RestoreJob job)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => _window?.Update(_layoutName, job));
    }

    public Task CloseAfterDelayAsync(TimeSpan delay) => Task.Run(async () =>
    {
        await Task.Delay(delay);
        System.Windows.Application.Current.Dispatcher.Invoke(() => _window?.Hide());
    });
}
