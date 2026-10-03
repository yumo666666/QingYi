using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;

namespace QingYi.Services;

using DrawingRectangle = System.Drawing.Rectangle;
using WpfPoint = System.Windows.Point;

public sealed class GlobalInputMonitor : IDisposable
{
    private readonly NativeMethods.HookProc _mouseCallback;
    private readonly NativeMethods.HookProc _keyboardCallback;
    private readonly DispatcherTimer _selectionTimer;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private WpfPoint _mouseDown;
    private WpfPoint _lastClick;
    private uint _lastClickTime;
    private bool _leftDown;
    private bool _suppressRightUp;
    private bool _captureActive;
    private bool _captureDrag;
    private WpfPoint _captureStart;
    private bool _disposed;
    private bool _paused;

    public event Action? CaptureRequested;
    public event Action<WpfPoint>? CaptureSelectionStarted;
    public event Action<DrawingRectangle>? CaptureSelectionChanged;
    public event Action<DrawingRectangle>? CaptureSelectionCompleted;
    public event Action? CaptureCancelled;
    public event Action<WpfPoint>? TextSelectionRequested;
    public event Action? NonChromeClick;

    public Func<WpfPoint, bool>? IsAppChromeAt { get; set; }
    public bool IsCaptureActive => _captureActive;
    public bool IsPaused { get => _paused; set => _paused = value; }

    public GlobalInputMonitor()
    {
        _mouseCallback = MouseHook;
        _keyboardCallback = KeyboardHook;
        _selectionTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(170) };
        _selectionTimer.Tick += SelectionTimerTick;
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseCallback, IntPtr.Zero, 0);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardCallback, IntPtr.Zero, 0);
        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
        {
            if (_mouseHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_mouseHook);
            if (_keyboardHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windows 全局输入监听初始化失败。");
        }
    }

    private IntPtr MouseHook(int code, IntPtr message, IntPtr data)
    {
        if (code < 0) return NativeMethods.CallNextHookEx(_mouseHook, code, message, data);
        var msg = message.ToInt32();
        var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(data);
        var point = new WpfPoint(info.pt.X, info.pt.Y);

        if (msg == NativeMethods.WM_RBUTTONDOWN)
        {
            if (_captureActive)
            {
                _suppressRightUp = true;
                CancelCapture();
                return new IntPtr(1);
            }
            if (!_paused && (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0)
            {
                _captureActive = true;
                _suppressRightUp = true;
                CaptureRequested?.Invoke();
                return new IntPtr(1);
            }
        }
        if (msg == NativeMethods.WM_RBUTTONUP && _suppressRightUp)
        {
            _suppressRightUp = false;
            return new IntPtr(1);
        }

        if (_captureActive)
        {
            if (msg == NativeMethods.WM_LBUTTONDOWN)
            {
                if (IsAppChromeAt?.Invoke(point) == true) return NativeMethods.CallNextHookEx(_mouseHook, code, message, data);
                _captureDrag = true;
                _captureStart = point;
                CaptureSelectionStarted?.Invoke(point);
                return new IntPtr(1);
            }
            if (msg == NativeMethods.WM_MOUSEMOVE && _captureDrag)
            {
                CaptureSelectionChanged?.Invoke(MakeRectangle(_captureStart, point));
            }
            if (msg == NativeMethods.WM_LBUTTONUP && _captureDrag)
            {
                _captureDrag = false;
                var rect = MakeRectangle(_captureStart, point);
                if (rect.Width >= 4 && rect.Height >= 4) CaptureSelectionCompleted?.Invoke(rect);
                return new IntPtr(1);
            }
            return NativeMethods.CallNextHookEx(_mouseHook, code, message, data);
        }

        if (_paused) return NativeMethods.CallNextHookEx(_mouseHook, code, message, data);
        if (msg == NativeMethods.WM_LBUTTONDOWN)
        {
            var appChrome = IsAppChromeAt?.Invoke(point) == true;
            if (!appChrome) NonChromeClick?.Invoke();
            _mouseDown = point;
            _leftDown = !appChrome;
        }
        else if (msg == NativeMethods.WM_LBUTTONUP && _leftDown)
        {
            _leftDown = false;
            var distance = Math.Abs(point.X - _mouseDown.X) + Math.Abs(point.Y - _mouseDown.Y);
            var doubleClick = Environment.TickCount - (long)_lastClickTime <= NativeMethods.GetDoubleClickTime() &&
                              Math.Abs(point.X - _lastClick.X) <= 5 && Math.Abs(point.Y - _lastClick.Y) <= 5;
            _lastClick = point;
            _lastClickTime = unchecked((uint)Environment.TickCount);
            if (distance >= 5 || doubleClick)
            {
                _selectionTimer.Stop();
                _selectionTimer.Start();
            }
        }
        return NativeMethods.CallNextHookEx(_mouseHook, code, message, data);
    }

    private IntPtr KeyboardHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && _captureActive && message.ToInt32() is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
        {
            var key = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(data);
            if (key.vkCode == NativeMethods.VK_ESCAPE)
            {
                CancelCapture();
                return new IntPtr(1);
            }
        }
        return NativeMethods.CallNextHookEx(_keyboardHook, code, message, data);
    }

    private void SelectionTimerTick(object? sender, EventArgs e)
    {
        _selectionTimer.Stop();
        TextSelectionRequested?.Invoke(_lastClick);
    }

    public void CompleteCapture() => _captureActive = false;

    public void CancelCapture()
    {
        if (!_captureActive) return;
        _captureActive = false;
        _captureDrag = false;
        CaptureCancelled?.Invoke();
    }

    private static DrawingRectangle MakeRectangle(WpfPoint a, WpfPoint b)
    {
        var x = (int)Math.Min(a.X, b.X);
        var y = (int)Math.Min(a.Y, b.Y);
        return new DrawingRectangle(x, y, (int)Math.Abs(b.X - a.X), (int)Math.Abs(b.Y - a.Y));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _selectionTimer.Stop();
        if (_mouseHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_mouseHook);
        if (_keyboardHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_keyboardHook);
    }
}
