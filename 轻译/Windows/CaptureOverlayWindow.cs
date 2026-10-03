using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using QingYi.Services;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace QingYi.Windows;

public sealed class CaptureOverlayWindow : Window
{
    private readonly ScreenFrame _frame;
    private readonly OverlayVisual _visual;
    private readonly bool _isPrimary;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    public CaptureOverlayWindow(ScreenFrame frame, bool isPrimary)
    {
        _frame = frame;
        _isPrimary = isPrimary;
        _visual = new OverlayVisual(frame, isPrimary);
        Content = _visual;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Left = frame.Bounds.Left / frame.ScaleX;
        Top = frame.Bounds.Top / frame.ScaleY;
        Width = frame.Bounds.Width / frame.ScaleX;
        Height = frame.Bounds.Height / frame.ScaleY;
        SourceInitialized += OnSourceInitialized;
        Show();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook((IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) => IntPtr.Zero);
        SetWindowLongPtr(hwnd, -20, new IntPtr(GetWindowLongPtr(hwnd, -20).ToInt64() | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        _ = NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, _frame.Bounds.Left, _frame.Bounds.Top,
            _frame.Bounds.Width, _frame.Bounds.Height, NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    public void SetSelection(Rectangle? rectangle)
    {
        _visual.Selection = rectangle;
        _visual.InvalidateVisual();
    }

    public bool ContainsPhysicalPoint(System.Drawing.Point point) => _frame.Bounds.Contains(point);
    public bool IsToolbarPoint(System.Drawing.Point point) => false;

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
}

internal sealed class OverlayVisual : FrameworkElement
{
    private readonly ScreenFrame _frame;
    private readonly bool _isPrimary;
    public Rectangle? Selection { get; set; }

    public OverlayVisual(ScreenFrame frame, bool primary)
    {
        _frame = frame;
        _isPrimary = primary;
        Focusable = false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var area = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawImage(_frame.Source, area);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(142, 92, 96, 104)), null, area);

        if (Selection is { Width: > 0, Height: > 0 } selection)
        {
            var local = new Rect((selection.Left - _frame.Bounds.Left) / _frame.ScaleX,
                (selection.Top - _frame.Bounds.Top) / _frame.ScaleY,
                selection.Width / _frame.ScaleX, selection.Height / _frame.ScaleY);
            dc.PushClip(new RectangleGeometry(local));
            dc.DrawImage(_frame.Source, area);
            dc.Pop();
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(116, 100, 232)), 1.5), local);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(50, 116, 100, 232)), null, local);
        }

        if (_isPrimary)
        {
            var pill = new Rect(22, 20, 310, 42);
            dc.DrawRoundedRectangle(Brushes.White, null, pill, 12, 12);
            var text = new FormattedText(Selection is null ? "左键拖动选择区域  ·  Esc 或右键取消" : "松开鼠标完成选择  ·  Esc 取消",
                System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI, Microsoft YaHei UI"), 13, new SolidColorBrush(Color.FromRgb(45, 47, 53)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(38, 32));
        }
    }
}
