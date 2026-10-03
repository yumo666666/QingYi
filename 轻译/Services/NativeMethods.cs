using System.Runtime.InteropServices;

namespace QingYi.Services;

internal static class NativeMethods
{
    internal const int WH_MOUSE_LL = 14;
    internal const int WH_KEYBOARD_LL = 13;
    internal const int WM_LBUTTONDOWN = 0x0201;
    internal const int WM_LBUTTONUP = 0x0202;
    internal const int WM_MOUSEMOVE = 0x0200;
    internal const int WM_RBUTTONDOWN = 0x0204;
    internal const int WM_RBUTTONUP = 0x0205;
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int VK_SHIFT = 0x10;
    internal const int VK_ESCAPE = 0x1B;
    internal const int VK_CONTROL = 0x11;
    internal const int KEYEVENTF_KEYUP = 0x0002;
    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_SHOWWINDOW = 0x0040;

    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public UIntPtr dwExtraInfo; }

    internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string? className, string windowName);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr hObject);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    internal static void SendCtrlC()
    {
        var inputs = new[]
        {
            new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL } } },
            new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)'C' } } },
            new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)'C', dwFlags = KEYEVENTF_KEYUP } } },
            new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }
}
