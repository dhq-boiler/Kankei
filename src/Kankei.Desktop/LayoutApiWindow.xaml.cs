using System.Windows;
namespace Kankei.Desktop;

public partial class LayoutApiWindow : Window
{
    private readonly Layout _layout;
    public LayoutApiWindow(Layout layout)
    {
        InitializeComponent();
        _layout = layout;
        ProfileName.Text = layout.Name;
        ApiUrl.Text = LocalApiHost.RestoreUrl(layout.Id);
        CurlCommand.Text = $"curl.exe --request POST \"{ApiUrl.Text}\"";
    }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void CopyApiClick(object sender, RoutedEventArgs e) => CopyRestoreApi(false);
    private void CopyCurlClick(object sender, RoutedEventArgs e) => CopyRestoreApi(true);

    private void CopyRestoreApi(bool asCurl)
    {
        var selected = _layout;
        var url = LocalApiHost.RestoreUrl(selected.Id);
        try
        {
            System.Windows.Clipboard.SetText(asCurl ? $"curl.exe --request POST \"{url}\"" : url);
            Status.Text = asCurl ? L.F("「{0}」のcurlコマンドをコピーしました。PowerShellで実行できます。", selected.Name)
                : L.F("「{0}」のAPI URLをコピーしました。HTTP POSTで呼び出してください。", selected.Name);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            Status.Text = L.T("クリップボードにコピーできませんでした。少し待ってから再度お試しください。");
        }
    }


}
