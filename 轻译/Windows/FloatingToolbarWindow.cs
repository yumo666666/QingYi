using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using QingYi.Services;
using Brushes = System.Windows.Media.Brushes;

namespace QingYi.Windows;

public sealed class FloatingToolbarWindow : Window
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private readonly ToolbarVisual _visual;
    private readonly DispatcherTimer _copyFeedbackTimer;
    private bool _dragging;
    private System.Drawing.Point _dragStartCursor;
    private double _dragStartLeft;
    private double _dragStartTop;
    private double _dragScaleX = 1;
    private double _dragScaleY = 1;

    public event Action? TranslateClicked;
    public event Action? CopyClicked;

    public FloatingToolbarWindow(bool imageMode, double scale = 1)
    {
        Title = imageMode ? "图片翻译工具栏" : "划词翻译工具栏";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        _visual = new ToolbarVisual(imageMode)
        {
            LayoutTransform = new ScaleTransform(Math.Clamp(scale, 0.75, 1.4), Math.Clamp(scale, 0.75, 1.4))
        };
        Content = _visual;
        _copyFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(950) };
        _copyFeedbackTimer.Tick += (_, _) => _visual.ResetCopyFeedback();
        _visual.TranslateButton.Click += (_, _) => TranslateClicked?.Invoke();
        _visual.CopyButton.Click += (_, _) => CopyClicked?.Invoke();
        _visual.Grip.PreviewMouseLeftButtonDown += (_, e) => BeginDrag(_visual.Grip, e);
        _visual.Grip.PreviewMouseMove += (_, e) => ContinueDrag(e);
        _visual.Grip.PreviewMouseLeftButtonUp += (_, e) => EndDrag(_visual.Grip, e);
        _visual.Grip.LostMouseCapture += (_, _) => _dragging = false;
        Closed += (_, _) => _copyFeedbackTimer.Stop();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = GetWindowLongPtr(hwnd, -20).ToInt64();
            SetWindowLongPtr(hwnd, -20, new IntPtr(ex | WsExNoActivate | WsExToolWindow));
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        };
    }

    public static UIElement CreatePreview(double scale) => ToolbarVisual.CreatePreview(scale);

    public void ShowCopyFeedback(bool success, string? error = null)
    {
        _copyFeedbackTimer.Stop();
        _visual.ShowCopyFeedback(success, error);
        _copyFeedbackTimer.Start();
    }

    private void BeginDrag(Border grip, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
        if (!GetCursorPos(out var cursor)) return;

        var dpi = GetDpiForWindow(new WindowInteropHelper(this).Handle);
        _dragScaleX = _dragScaleY = (dpi == 0 ? 96 : dpi) / 96d;
        _dragStartCursor = new System.Drawing.Point(cursor.X, cursor.Y);
        _dragStartLeft = Left;
        _dragStartTop = Top;
        _dragging = grip.CaptureMouse();
        e.Handled = true;
        if (!_dragging)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    private void ContinueDrag(System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || !GetCursorPos(out var cursor)) return;
        UpdateDragPosition(new System.Drawing.Point(cursor.X, cursor.Y));
        e.Handled = true;
    }

    private void UpdateDragPosition(System.Drawing.Point cursor)
    {
        var monitorPoint = new NativeMethods.POINT { X = cursor.X, Y = cursor.Y };
        var monitor = Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y));
        var dpiResult = GetDpiForMonitor(NativeMethods.MonitorFromPoint(monitorPoint, 2), 0, out var dpiX, out var dpiY);
        if (dpiResult != 0) { dpiX = 96; dpiY = 96; }
        var scaleX = (dpiX == 0 ? 96 : dpiX) / 96d;
        var scaleY = (dpiY == 0 ? 96 : dpiY) / 96d;
        var left = _dragStartLeft + (cursor.X - _dragStartCursor.X) / _dragScaleX;
        var top = _dragStartTop + (cursor.Y - _dragStartCursor.Y) / _dragScaleY;
        Left = Math.Clamp(left, monitor.Bounds.Left / scaleX, monitor.Bounds.Right / scaleX - ActualWidth);
        Top = Math.Clamp(top, monitor.Bounds.Top / scaleY, monitor.Bounds.Bottom / scaleY - ActualHeight);
    }

    private void EndDrag(Border grip, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        if (System.Windows.Input.Mouse.Captured == grip) System.Windows.Input.Mouse.Capture(null);
        e.Handled = true;
    }

    public void ShowNear(System.Drawing.Point point)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        Opacity = 0;
        Show();
        Dispatcher.BeginInvoke(() =>
        {
            var screen = Forms.Screen.FromPoint(point);
            uint dpiX = 96, dpiY = 96;
            var monitorPoint = new NativeMethods.POINT { X = point.X, Y = point.Y };
            _ = NativeMethods.GetDpiForMonitor(NativeMethods.MonitorFromPoint(monitorPoint, 2), 0, out dpiX, out dpiY);
            var sx = (dpiX == 0 ? 96 : dpiX) / 96d;
            var sy = (dpiY == 0 ? 96 : dpiY) / 96d;
            var left = Math.Clamp(point.X / sx - ActualWidth / 2, screen.Bounds.Left / sx, screen.Bounds.Right / sx - ActualWidth);
            var top = point.Y / sy - ActualHeight - 12;
            if (top < screen.Bounds.Top / sy) top = point.Y / sy + 14;
            top = Math.Clamp(top, screen.Bounds.Top / sy, screen.Bounds.Bottom / sy - ActualHeight);
            Left = left;
            Top = top;
            Opacity = 1;
        });
    }

    public System.Drawing.Rectangle GetPhysicalBounds()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (GetWindowRect(hwnd, out var rect)) return new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        return Rectangle.Empty;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
