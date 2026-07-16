using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BlockVision.App.Services;

/// <summary>
/// Охрана окна блокировки - не дает ему потерять фокус, быть свернутым, закрытым
/// </summary>
public class WindowGuard : IDisposable
{
    private readonly Window _window;
    private Timer? _topmostTimer;
    private HwndSource? _hwndSource;
    private bool _enabled = false;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd); // свернуто ли

    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    public WindowGuard(Window window)
    {
        _window = window;
    }

    public void Enable()
    {
        if (_enabled) return;
        _enabled = true;

        // Подписываемся на события окна
        _window.Deactivated += OnDeactivated;
        _window.StateChanged += OnStateChanged;
        _window.LocationChanged += OnLocationChanged;

        // HwndSource hook для перехвата WM_CLOSE, WM_SYSCOMMAND и т.д.
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle != IntPtr.Zero)
        {
            _hwndSource = HwndSource.FromHwnd(handle);
            _hwndSource?.AddHook(WndProc);
        }
        else
        {
            // Если handle еще не создан, ждем Loaded
            _window.Loaded += (s, e) =>
            {
                var h = new WindowInteropHelper(_window).Handle;
                if (h != IntPtr.Zero)
                {
                    _hwndSource = HwndSource.FromHwnd(h);
                    _hwndSource?.AddHook(WndProc);
                }
            };
        }

        // Таймер который каждые 200мс проверяет что окно TopMost и не свернуто
        _topmostTimer = new Timer(KeepTopmost, null, 0, 200);

        DebugGuard("Enabled");
    }

    public void Disable()
    {
        if (!_enabled) return;
        _enabled = false;

        _topmostTimer?.Dispose();
        _topmostTimer = null;

        _window.Deactivated -= OnDeactivated;
        _window.StateChanged -= OnStateChanged;
        _window.LocationChanged -= OnLocationChanged;

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        DebugGuard("Disabled");
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!_enabled) return;
        // Когда окно теряет фокус - возвращаем его на передний план
        // Но не делаем это если пользователь вводит ключ активации в другом окне? Нет, мы должны держать фокус
        // Для удобства: если Deactivated произошло из-за клика по нашему же окну (например по другому контролу) - не возвращаем
        // Но если фокус ушел на другое приложение (explorer, taskmgr) - возвращаем

        _window.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_enabled) return;
            try
            {
                _window.Topmost = true;
                _window.Activate();
                var handle = new WindowInteropHelper(_window).Handle;
                SetForegroundWindow(handle);
            }
            catch { }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (!_enabled) return;
        // Запрещаем сворачивание
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Maximized;
            var handle = new WindowInteropHelper(_window).Handle;
            ShowWindow(handle, SW_RESTORE);
        }
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!_enabled) return;
        // Удерживаем окно на 0,0 и на весь экран (если CoverAllMonitors включено)
        // Для простоты не двигаем, но можно вернуть на место если пользователь попытался сдвинуть
    }

    private void KeepTopmost(object? state)
    {
        if (!_enabled) return;
        try
        {
            _window.Dispatcher.Invoke(() =>
            {
                if (!_enabled) return;
                if (!_window.Topmost)
                    _window.Topmost = true;

                var handle = new WindowInteropHelper(_window).Handle;
                if (handle != IntPtr.Zero)
                {
                    if (IsIconic(handle))
                    {
                        ShowWindow(handle, SW_RESTORE);
                        _window.WindowState = WindowState.Maximized;
                    }
                }

                // Проверяем что окно все еще видимо и активно
                if (!_window.IsVisible)
                    _window.Show();
            });
        }
        catch { }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_CLOSE = 0x0010;
        const int WM_QUERYENDSESSION = 0x0011;
        const int WM_SYSCOMMAND = 0x0112;
        const int SC_CLOSE = 0xF060;
        const int SC_MINIMIZE = 0xF020;
        const int SC_MAXIMIZE = 0xF030;

        if (!_enabled) return IntPtr.Zero;

        // Блокируем закрытие окна через системное меню или Alt+F4
        if (msg == WM_CLOSE)
        {
            handled = true; // блокируем закрытие
            DebugGuard("Blocked WM_CLOSE");
            return (IntPtr)1;
        }

        if (msg == WM_SYSCOMMAND)
        {
            int command = wParam.ToInt32() & 0xFFF0;
            if (command == SC_CLOSE)
            {
                handled = true;
                DebugGuard("Blocked SC_CLOSE");
                return (IntPtr)1;
            }
            if (command == SC_MINIMIZE)
            {
                handled = true;
                DebugGuard("Blocked SC_MINIMIZE");
                return (IntPtr)1;
            }
        }

        // Блокируем завершение сессии (logoff/shutdown) если нужно? Лучше не блокировать shutdown полностью, а только logoff?
        // Для киоска можно блокировать и shutdown, но это агрессивно
        // if (msg == WM_QUERYENDSESSION) { handled = true; return (IntPtr)0; }

        return IntPtr.Zero;
    }

    private void DebugGuard(string msg)
    {
        System.Diagnostics.Debug.WriteLine($"[WindowGuard] {msg} for {_window.GetType().Name}");
    }

    public void Dispose() => Disable();
}

public static class DebugGuard
{
    [System.Diagnostics.Conditional("DEBUG")]
    public static void Write(string msg) => System.Diagnostics.Debug.WriteLine(msg);
}
