using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BlockVision.App.Services;

/// <summary>
/// Low-level keyboard hook для блокировки Alt+Tab, Win, Ctrl+Esc и т.д.
/// ВНИМАНИЕ: Требует careful handling, чтобы не сломать систему. Отключается при разблокировке.
/// </summary>
public class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    public bool BlockAltTab { get; set; } = true;
    public bool BlockWindowsKeys { get; set; } = true;
    public bool BlockCtrlEsc { get; set; } = true;
    public bool BlockAltF4 { get; set; } = false; // обычно разрешаем внутри приложения обрабатывать

    public bool IsActive => _hookId != IntPtr.Zero;

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Install()
    {
        if (IsActive) return;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName!), 0);
    }

    public void Uninstall()
    {
        if (!IsActive) return;
        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var vkCode = Marshal.ReadInt32(lParam);
            var isSysKey = wParam == (IntPtr)WM_SYSKEYDOWN;

            // Блокируем комбинации
            if (BlockAltTab)
            {
                // Alt + Tab
                if (isSysKey && vkCode == 0x09) // Tab
                    return (IntPtr)1; // блокируем
                // Alt + Esc
                if (isSysKey && vkCode == 0x1B)
                    return (IntPtr)1;
            }

            if (BlockWindowsKeys)
            {
                // LWin 0x5B, RWin 0x5C
                if (vkCode == 0x5B || vkCode == 0x5C)
                    return (IntPtr)1;
            }

            if (BlockCtrlEsc)
            {
                // Ctrl + Esc
                bool ctrlDown = (GetAsyncKeyState(0x11) & 0x8000) != 0; // VK_CONTROL
                if (ctrlDown && vkCode == 0x1B)
                    return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public void Dispose()
    {
        Uninstall();
    }
}

public class DisplayManager
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMetrics(int nIndex);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    // Получить все экраны через WPF System.Windows.Forms
    public static List<System.Drawing.Rectangle> GetAllScreensBounds()
    {
        var screens = new List<System.Drawing.Rectangle>();
        try
        {
            // Используем WinForms Screen
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                screens.Add(screen.Bounds);
            }
        }
        catch
        {
            // Fallback: один экран
            screens.Add(new System.Drawing.Rectangle(0, 0, (int)GetSystemMetrics(SM_CXSCREEN), (int)GetSystemMetrics(SM_CYSCREEN)));
        }
        return screens;
    }

    public static void MakeTopMost(IntPtr handle)
    {
        var screens = GetAllScreensBounds();
        // Объединяем границы для покрытия всех мониторов
        int minX = screens.Min(s => s.X);
        int minY = screens.Min(s => s.Y);
        int maxX = screens.Max(s => s.Right);
        int maxY = screens.Max(s => s.Bottom);

        int width = maxX - minX;
        int height = maxY - minY;

        SetWindowPos(handle, HWND_TOPMOST, minX, minY, width, height, SWP_SHOWWINDOW);
    }
}
