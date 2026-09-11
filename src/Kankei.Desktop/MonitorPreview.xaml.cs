using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Automation;
using Screen = System.Windows.Forms.Screen;
using UserControl = System.Windows.Controls.UserControl;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Path = System.IO.Path;
using Rectangle = System.Windows.Shapes.Rectangle;
using Panel = System.Windows.Controls.Panel;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Kankei.Desktop;

public partial class MonitorPreview : UserControl
{
    private Layout? _layout;
    private IReadOnlyList<SavedWindow> _selected = [];
    private bool _planned = true;
    private readonly List<FrameworkElement> _shapes = [];
    private static readonly string[] Palette = ["#639BFF", "#51D4C4", "#C297FF", "#F4B86A", "#F28CA9", "#A4CC71"];

    public MonitorPreview() => InitializeComponent();

    public void SetLayout(Layout? layout) { _layout = layout; Render(); }
    public void SetSelection(IReadOnlyList<SavedWindow> windows) { _selected = windows; Render(); }
    private void ShowPlanned(object sender, RoutedEventArgs e) { _planned = true; Render(); }
    private void ShowCurrent(object sender, RoutedEventArgs e) { _planned = false; Render(); }
    private void MapSizeChanged(object sender, SizeChangedEventArgs e) => Render();
    private static SolidColorBrush Ink(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    private void Render()
    {
        if (Map is null || Entries is null) return;
        Map.Children.Clear();
        Entries.Children.Clear();
        _shapes.Clear();
        var screens = Screen.AllScreens;
        var monitors = screens.Select(MonitorPlacement.Capture).ToArray();
        MonitorCount.Text = $"{monitors.Length} 台のモニター";
        PlannedButton.Background = (Brush)FindResource(_planned ? "AccentBrush" : "FieldBrush");
        CurrentButton.Background = (Brush)FindResource(_planned ? "FieldBrush" : "AccentBrush");
        var windows = _planned ? _layout?.Windows ?? [] : _selected;
        Summary.Text = _planned
            ? _layout is null ? "左のプロファイルを選ぶと、復元予定の配置が表示されます。" : $"{_layout.Name} · {windows.Count} 件の復元予定"
            : $"チェックした {windows.Count} 件の現在位置（一覧更新時点）";
        if (monitors.Length == 0 || Map.ActualWidth <= 48 || Map.ActualHeight <= 64) return;
        var left = monitors.Min(m => m.Bounds.Left);
        var top = monitors.Min(m => m.Bounds.Top);
        var right = monitors.Max(m => (double)m.Bounds.Left + m.Bounds.Width);
        var bottom = monitors.Max(m => (double)m.Bounds.Top + m.Bounds.Height);
        var scale = Math.Min((Map.ActualWidth - 48) / (right - left), (Map.ActualHeight - 64) / (bottom - top));
        var ox = (Map.ActualWidth - (right - left) * scale) / 2;
        var oy = (Map.ActualHeight - (bottom - top) * scale) / 2;
        void Place(FrameworkElement item, WindowBounds bounds)
        {
            Canvas.SetLeft(item, ox + (bounds.Left - (double)left) * scale);
            Canvas.SetTop(item, oy + (bounds.Top - (double)top) * scale);
            item.Width = Math.Max(1, bounds.Width * scale);
            item.Height = Math.Max(1, bounds.Height * scale);
            Map.Children.Add(item);
        }
        for (var i = 0; i < monitors.Length; i++)
        {
            var monitor = monitors[i];
            var name = monitor.DeviceName.Replace(@"\\.\", "");
            var label = $"{name}{(screens[i].Primary ? " · メイン" : "")}\n{monitor.Bounds.Width} × {monitor.Bounds.Height}";
            var frame = new Border { Background = Ink("#202E42"), BorderBrush = Ink("#607797"), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(5), ToolTip = label,
                Child = new TextBlock { Text = label, Foreground = Ink("#9AAFCB"), FontSize = 11, TextAlignment = TextAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            AutomationProperties.SetName(frame, label);
            Place(frame, monitor.Bounds);
        }
        for (var i = 0; i < windows.Count; i++)
        {
            var saved = windows[i];
            var number = i + 1;
            var color = Ink(Palette[i % Palette.Length]);
            var bounds = _planned ? MonitorPlacement.PreviewBounds(saved, monitors)
                : saved.DisplayState == WindowDisplayState.Minimized ? saved.NormalBounds ?? new(saved.Left, saved.Top, saved.Width, saved.Height)
                : new WindowBounds(saved.Left, saved.Top, saved.Width, saved.Height);
            var state = saved.DisplayState switch { WindowDisplayState.Maximized => "最大化", WindowDisplayState.Minimized => "最小化・通常位置", _ => "通常" };
            var missing = _planned && saved.Monitor is not null && !monitors.Any(m => m.DeviceName == saved.Monitor.DeviceName);
            var title = string.IsNullOrWhiteSpace(saved.Title) ? Path.GetFileNameWithoutExtension(saved.ExecutablePath) : saved.Title;
            var detail = $"{state} · {bounds.Width} × {bounds.Height} · ({bounds.Left}, {bounds.Top}){(missing ? " · 接続中のモニターへ移動" : "")}";
            var shape = new Grid { ToolTip = $"{number}. {title}\n{detail}", ClipToBounds = true };
            shape.Children.Add(new Rectangle { Stroke = color, StrokeThickness = 1.5, Fill = new SolidColorBrush(Color.FromArgb(42, color.Color.R, color.Color.G, color.Color.B)),
                StrokeDashArray = saved.DisplayState == WindowDisplayState.Minimized ? new DoubleCollection([4, 3]) : null });
            shape.Children.Add(new TextBlock { Text = $"{number}  {Path.GetFileNameWithoutExtension(saved.ExecutablePath)}", FontSize = 10, Foreground = color, Margin = new Thickness(5, 3, 3, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            AutomationProperties.SetName(shape, $"{number}. {title} {detail}");
            Place(shape, bounds);
            _shapes.Add(shape);
            var row = new Border { Padding = new Thickness(8, 7, 8, 7), CornerRadius = new CornerRadius(6), Background = System.Windows.Media.Brushes.Transparent, ToolTip = title };
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = $"{number:00}  {title}", Foreground = color, TextTrimming = TextTrimming.CharacterEllipsis });
            content.Children.Add(new TextBlock { Text = detail, Foreground = (Brush)FindResource("MutedBrush"), FontSize = 10, Margin = new Thickness(23, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
            row.Child = content;
            row.MouseEnter += (_, _) => { foreach (var item in _shapes) item.Opacity = item == shape ? 1 : 0.2; Panel.SetZIndex(shape, 1); row.Background = Ink("#243247"); };
            row.MouseLeave += (_, _) => { foreach (var item in _shapes) item.Opacity = 1; Panel.SetZIndex(shape, 0); row.Background = System.Windows.Media.Brushes.Transparent; };
            Entries.Children.Add(row);
        }
    }
}
