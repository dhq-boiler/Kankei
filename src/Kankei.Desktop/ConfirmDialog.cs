using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Button = System.Windows.Controls.Button;

namespace Kankei.Desktop;

internal static class ConfirmDialog
{
    public static bool Show(Window owner, string message, string title)
    {
        var dialog = new Window
        {
            Owner = owner, Title = title, Width = 520, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/Icons/kankei.ico"))
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        var yes = new Button { Content = L.T("はい"), MinWidth = 90, Margin = new Thickness(0, 0, 10, 0) };
        var no = new Button { Content = L.T("いいえ"), MinWidth = 90, IsCancel = true, IsDefault = true };
        System.Windows.Automation.AutomationProperties.SetAutomationId(yes, "ConfirmYes");
        System.Windows.Automation.AutomationProperties.SetAutomationId(no, "ConfirmNo");
        yes.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        return dialog.ShowDialog() == true;
    }
}

