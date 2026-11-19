using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Threading;
using Point = System.Windows.Point;
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
    private bool _live;
    private bool _dragging;
    private WindowDiscovery? _discovery;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly List<(SavedMonitor Monitor, Border Frame, Rect Area)> _targets = [];
    private sealed record WindowDrag(MonitorPreview Owner, SavedWindow Window);
    private readonly List<FrameworkElement> _shapes = [];
    private static readonly string[] Palette = ["#639BFF", "#51D4C4", "#C297FF", "#F4B86A", "#F28CA9", "#A4CC71"];

    public MonitorPreview()
    {
        InitializeComponent();
        _refresh.Tick += (_, _) =>
        {
            if (_live && !_dragging && IsVisible && IsEnabled && Mouse.LeftButton != MouseButtonState.Pressed &&
                !Entries.Children.OfType<FrameworkElement>().Any(x => x.ContextMenu?.IsOpen == true) &&
                !_shapes.Any(x => x.ContextMenu?.IsOpen == true)) RefreshLive();
        };
        Loaded += (_, _) => { Localization.Current.Changed += Render; _refresh.Start(); Render(); };
        Unloaded += (_, _) => { Localization.Current.Changed -= Render; _refresh.Stop(); };
    }

    public void EnableLive(WindowDiscovery discovery)
    {
        _discovery = discovery;
        _live = true;
        LiveControls.Visibility = Visibility.Visible;
        RefreshLive(true);
    }

    public void SetLayout(Layout? layout) { _planned = true; _layout = layout; Render(); }
    public void SetSelection(IReadOnlyList<SavedWindow> windows) { _planned = false; _selected = windows; Render(); }
    private void LiveClick(object sender, RoutedEventArgs e) { _live = true; MoveStatus.Text = ""; RefreshLive(true); }
    private void PlannedClick(object sender, RoutedEventArgs e) { _live = false; MoveStatus.Text = ""; Render(); }
    private void RefreshClick(object sender, RoutedEventArgs e) { if (_live) RefreshLive(true); else Render(); }
    private void RefreshLive(bool force = false)
    {
        if (_discovery is null) return;
        var current = _discovery.Capture().OrderBy(x => Path.GetFileNameWithoutExtension(x.ExecutablePath), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.WindowHandle).ToArray();
        if (!force && _selected.SequenceEqual(current) && _targets.Select(x => x.Monitor)
                .SequenceEqual(Screen.AllScreens.Select(MonitorPlacement.Capture))) return;
        _selected = current;
        Render();
    }

    private void AttachMove(FrameworkElement element, SavedWindow window, IReadOnlyList<SavedMonitor> monitors)
    {
        element.Cursor = System.Windows.Input.Cursors.Hand;
        element.Focusable = true;
        Point? start = null;
        element.PreviewMouseLeftButtonDown += (_, e) =>
        {
            start = e.GetPosition(element);
            element.Focus();
            element.CaptureMouse();
        };
        element.PreviewMouseLeftButtonUp += (_, _) => { start = null; element.ReleaseMouseCapture(); };
        element.LostMouseCapture += (_, _) => start = null;
        element.MouseLeave += (_, _) => { if (Mouse.LeftButton != MouseButtonState.Pressed) start = null; };
        element.MouseMove += (_, e) =>
        {
            if (start is not Point origin || e.LeftButton != MouseButtonState.Pressed) { start = null; return; }
            var delta = e.GetPosition(element) - origin;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            start = null;
            _dragging = true;
            element.ReleaseMouseCapture();
            try { System.Windows.DragDrop.DoDragDrop(element, new System.Windows.DataObject(typeof(WindowDrag), new WindowDrag(this, window)), System.Windows.DragDropEffects.Move); }
            finally { _dragging = false; ClearTarget(); RefreshLive(true); }
            e.Handled = true;
        };
        var menu = new System.Windows.Controls.ContextMenu { Style = (Style)FindResource("PopupMenu") };
        foreach (var monitor in monitors)
        {
            var item = new System.Windows.Controls.MenuItem { Header = L.F("{0} へ移動", monitor.DeviceName.Replace(@"\\.\", "")),
                IsEnabled = monitor.DeviceName != window.Monitor?.DeviceName };
            item.Click += (_, _) => MoveWindow(window, monitor);
            menu.Items.Add(item);
        }
        element.ContextMenu = menu;
    }

    private void ClearTarget()
    {
        foreach (var target in _targets) { target.Frame.BorderBrush = Ink("#607797"); target.Frame.BorderThickness = new Thickness(1.5); }
    }

    private SavedMonitor? DropTarget(System.Windows.DragEventArgs e)
    {
        if (!_live || !IsEnabled || e.Data.GetData(typeof(WindowDrag)) is not WindowDrag drag || drag.Owner != this) return null;
        var point = e.GetPosition(Map);
        return _targets.FirstOrDefault(x => x.Area.Contains(point)).Monitor;
    }

    private void MapDragOver(object sender, System.Windows.DragEventArgs e)
    {
        ClearTarget();
        var monitor = DropTarget(e);
        e.Effects = monitor is null ? System.Windows.DragDropEffects.None : System.Windows.DragDropEffects.Move;
        foreach (var target in _targets.Where(x => x.Monitor == monitor))
        { target.Frame.BorderBrush = Ink("#51D4C4"); target.Frame.BorderThickness = new Thickness(3); }
        e.Handled = true;
    }
    private void MapDragLeave(object sender, System.Windows.DragEventArgs e) => ClearTarget();
    private void MapDrop(object sender, System.Windows.DragEventArgs e)
    {
        var target = DropTarget(e);
        e.Effects = System.Windows.DragDropEffects.None;
        if (target is not null && e.Data.GetData(typeof(WindowDrag)) is WindowDrag drag)
        { if (MoveWindow(drag.Window, target)) e.Effects = System.Windows.DragDropEffects.Move; }
        ClearTarget();
        e.Handled = true;
    }
    private bool MoveWindow(SavedWindow window, SavedMonitor target)
    {
        if (_discovery is null) return false;
        var moved = false;
        try
        {
            new LiveWindowMove(_discovery).Move(window, target);
            MoveStatus.Text = L.F("「{0}」を {1} へ移動しました。", window.Title ?? Path.GetFileNameWithoutExtension(window.ExecutablePath), target.DeviceName.Replace(@"\\.\", ""));
            moved = true;
        }
        catch (Exception ex) { MoveStatus.Text = L.T("ウィンドウを移動できませんでした: ") + ex.Message; }
        if (!_dragging) RefreshLive(true);
        return moved;
    }
    private void MapSizeChanged(object sender, SizeChangedEventArgs e) => Render();
    private static SolidColorBrush Ink(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    private void Render()
    {
        if (Map is null || Entries is null) return;
        Map.Children.Clear();
        Entries.Children.Clear();
        _shapes.Clear();
        _targets.Clear();
        LiveButton.IsEnabled = !_live;
        PlannedButton.IsEnabled = _live;
        PreviewHeading.Text = L.T(_live ? "ウィンドウの移動" : "配置プレビュー");
        PreviewHint.Text = L.T(_live
            ? "最大化・最小化を保ってモニター間を移動します。Escでドラッグを取り消せます。保存済みの配置は変更しません。"
            : "現在のモニター構成に合わせた位置の目安です。最小化は通常位置を破線で表示します。");
        var screens = Screen.AllScreens;
        var monitors = screens.Select(MonitorPlacement.Capture).ToArray();
        MonitorCount.Text = L.F("{0} 台のモニター", monitors.Length);
        var windows = _live ? _selected : _planned ? _layout?.Windows ?? [] : _selected;
        Summary.Text = _live ? L.F("起動中 {0} 件 · 図または一覧から移動先のモニターへドラッグ。右クリックでも移動できます。", windows.Count) : _planned
            ? _layout is null ? L.T("配置を選ぶと、復元予定の位置が表示されます。") : L.F("{0} · {1} 件の復元予定", _layout.Name, windows.Count)
            : L.F("チェックした {0} 件の現在位置（一覧更新時点）", windows.Count);
        if (monitors.Length == 0 || Map.ActualWidth <= 48 || Map.ActualHeight <= 64) return;
        var left = monitors.Min(m => m.Bounds.Left);
        var top = monitors.Min(m => m.Bounds.Top);
        var right = monitors.Max(m => (double)m.Bounds.Left + m.Bounds.Width);
        var bottom = monitors.Max(m => (double)m.Bounds.Top + m.Bounds.Height);
        var scale = Math.Min((Map.ActualWidth - 24) / (right - left), (Map.ActualHeight - 24) / (bottom - top));
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
            var label = $"{name}{(screens[i].Primary ? L.T(" · メイン") : "")}\n{monitor.Bounds.Width} × {monitor.Bounds.Height}";
            var frame = new Border { Background = Ink("#202E42"), BorderBrush = Ink("#607797"), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(5), ToolTip = label,
                Child = new TextBlock { Text = label, Foreground = Ink("#9AAFCB"), FontSize = 11, TextAlignment = TextAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            AutomationProperties.SetName(frame, label);
            Place(frame, monitor.Bounds);
            AutomationProperties.SetAutomationId(frame, "Monitor_" + i);
            _targets.Add((monitor, frame, new Rect(Canvas.GetLeft(frame), Canvas.GetTop(frame), frame.Width, frame.Height)));
        }
        for (var i = 0; i < windows.Count; i++)
        {
            var saved = windows[i];
            var number = i + 1;
            var color = Ink(Palette[i % Palette.Length]);
            var bounds = !_live && _planned ? MonitorPlacement.PreviewBounds(saved, monitors)
                : saved.DisplayState == WindowDisplayState.Minimized ? saved.NormalBounds ?? new(saved.Left, saved.Top, saved.Width, saved.Height)
                : new WindowBounds(saved.Left, saved.Top, saved.Width, saved.Height);
            var state = saved.DisplayState switch { WindowDisplayState.Maximized => L.T("最大化"), WindowDisplayState.Minimized => L.T("最小化・通常位置"), _ => L.T("通常") };
            var missing = !_live && _planned && saved.Monitor is not null && !monitors.Any(m => m.DeviceName == saved.Monitor.DeviceName);
            var title = string.IsNullOrWhiteSpace(saved.Title) ? Path.GetFileNameWithoutExtension(saved.ExecutablePath) : saved.Title;
            var detail = $"{state} · {bounds.Width} × {bounds.Height} · ({bounds.Left}, {bounds.Top}){(missing ? L.T(" · 接続中のモニターへ移動") : "")}";
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
            if (_live)
            {
                AutomationProperties.SetAutomationId(shape, "LiveShape_" + saved.WindowHandle);
                AutomationProperties.SetAutomationId(row, "LiveWindow_" + saved.WindowHandle);
                AutomationProperties.SetName(row, title);
                AttachMove(shape, saved, monitors);
                AttachMove(row, saved, monitors);
            }
            row.MouseEnter += (_, _) => { foreach (var item in _shapes) item.Opacity = item == shape ? 1 : 0.2; Panel.SetZIndex(shape, 1); row.Background = Ink("#243247"); };
            row.MouseLeave += (_, _) => { foreach (var item in _shapes) item.Opacity = 1; Panel.SetZIndex(shape, 0); row.Background = System.Windows.Media.Brushes.Transparent; };
            Entries.Children.Add(row);
        }
    }
}
