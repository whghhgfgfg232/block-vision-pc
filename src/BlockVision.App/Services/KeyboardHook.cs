using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BlockVision.App.Services;

/// <summary>
/// Low-level keyboard hook для блокировки Alt+Tab, Win, Ctrl+Esc, Win+E, Win+R и т.д.
/// ВНИМАНИЕ: Требует careful handling, чтобы не сломать систему. Отключается при разблокировке.
/// </summary>
public class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

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
    public bool BlockExplorerHotkeys { get; set; } = true; // Win+E, Win+R, Win+D, Win+L (насколько возможно)
    public bool BlockTaskManagerHotkeys { get; set; } = true; // Ctrl+Shift+Esc, Ctrl+Alt+Del (Del нельзя, но Esc часть)
    public bool BlockAltEsc { get; set; } = true;

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
        Debug.WriteLine($"[KeyboardHook] Installed: {_hookId}");
    }

    public void Uninstall()
    {
        if (!IsActive) return;
        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        Debug.WriteLine("[KeyboardHook] Uninstalled");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var vkCode = Marshal.ReadInt32(lParam);
            var isSysKey = wParam == (IntPtr)WM_SYSKEYDOWN;

            bool winDown = (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0; // LWin/RWin
            bool ctrlDown = (GetAsyncKeyState(0x11) & 0x8000) != 0 || (GetAsyncKeyState(0xA2) & 0x8000) != 0 || (GetAsyncKeyState(0xA3) & 0x8000) != 0;
            bool shiftDown = (GetAsyncKeyState(0x10) & 0x8000) != 0;
            bool altDown = (GetAsyncKeyState(0x12) & 0x8000) != 0 || isSysKey;

            // Блокируем комбинации
            if (BlockAltTab)
            {
                // Alt + Tab
                if (altDown && vkCode == 0x09) // Tab
                    return (IntPtr)1;
                // Alt + Esc - переключение окон
                if (BlockAltEsc && altDown && vkCode == 0x1B)
                    return (IntPtr)1;
                // Alt + F4 - закрыть окно (если включено)
                if (BlockAltF4 && altDown && vkCode == 0x73) // VK_F4 = 0x73
                    return (IntPtr)1;
            }

            if (BlockWindowsKeys)
            {
                // LWin 0x5B, RWin 0x5C - полностью блокируем Win клавиши
                if (vkCode == 0x5B || vkCode == 0x5C)
                    return (IntPtr)1;
            }

            if (BlockExplorerHotkeys && winDown)
            {
                // Win + E (Explorer), Win + R (Run), Win + D (Desktop), Win + M (Minimize), Win + L (Lock - частично), Win + Tab (Task View)
                // VK codes: E=0x45, R=0x52, D=0x44, M=0x4D, L=0x4C, Tab=0x09
                if (vkCode == 0x45 || vkCode == 0x52 || vkCode == 0x44 || vkCode == 0x4D || vkCode == 0x4C || vkCode == 0x09)
                    return (IntPtr)1;
                // Win + anything - для киоск режима блокируем все Win комбинации
                if (BlockExplorerHotkeys)
                {
                    // В киоск режиме блокируем любой Win + <key>
                    return (IntPtr)1;
                }
            }

            if (BlockCtrlEsc)
            {
                // Ctrl + Esc - открыть Start
                if (ctrlDown && vkCode == 0x1B)
                    return (IntPtr)1;
            }

            if (BlockTaskManagerHotkeys)
            {
                // Ctrl + Shift + Esc - диспетчер задач
                if (ctrlDown && shiftDown && vkCode == 0x1B)
                    return (IntPtr)1;
                // Ctrl + Alt + Del нельзя перехватить в user-mode (Secure Attention Sequence), но можем блокировать Del часть если Ctrl+Alt нажаты
                // Alt + Ctrl + Del - в некоторых конфигурациях
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

    public static void MakeTopMostAlways(IntPtr handle, int width, int height, int x = 0, int y = 0)
    {
        SetWindowPos(handle, HWND_TOPMOST, x, y, width, height, SWP_SHOWWINDOW);
    }
}
