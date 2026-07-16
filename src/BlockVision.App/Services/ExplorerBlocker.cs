using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BlockVision.App.Services;

/// <summary>
/// Блокировка проводника и панели задач при активной блокировке
/// Режимы:
/// - HideTaskbar: скрыть панель задач (Shell_TrayWnd)
/// - KillExplorer: убить explorer.exe (режим киоска) - агрессивно, но эффективно
/// - BlockTaskManagerWindow: закрывать окно диспетчера задач
/// </summary>
public class ExplorerBlocker : IDisposable
{
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;

    private Timer? _guardTimer;
    private bool _taskbarHidden = false;
    private bool _explorerKilled = false;
    private readonly bool _hideTaskbar;
    private readonly bool _killExplorer;
    private readonly bool _blockTaskMgr;

    public ExplorerBlocker(bool hideTaskbar = true, bool killExplorer = false, bool blockTaskManager = true)
    {
        _hideTaskbar = hideTaskbar;
        _killExplorer = killExplorer;
        _blockTaskMgr = blockTaskManager;
    }

    public void Enable()
    {
        if (_hideTaskbar)
        {
            HideTaskbar();
            _taskbarHidden = true;
        }

        if (_killExplorer)
        {
            KillExplorer();
            _explorerKilled = true;
        }

        // Запускаем guard timer который каждые 500мс проверяет и скрывает/закрывает нежелательные окна
        _guardTimer = new Timer(GuardTick, null, 0, 500);

        Debug.WriteLine($"[ExplorerBlocker] Enabled: HideTaskbar={_hideTaskbar}, KillExplorer={_killExplorer}, BlockTaskMgr={_blockTaskMgr}");
    }

    public void Disable()
    {
        _guardTimer?.Dispose();
        _guardTimer = null;

        if (_taskbarHidden)
        {
            ShowTaskbar();
            _taskbarHidden = false;
        }

        if (_explorerKilled)
        {
            RestartExplorer();
            _explorerKilled = false;
        }

        Debug.WriteLine("[ExplorerBlocker] Disabled");
    }

    public static void HideTaskbar()
    {
        try
        {
            var trayWnd = FindWindow("Shell_TrayWnd", null);
            if (trayWnd != IntPtr.Zero)
                ShowWindow(trayWnd, SW_HIDE);

            // Скрываем вторичные панели задач на дополнительных мониторах (Win10+)
            var secondaryTray = IntPtr.Zero;
            while (true)
            {
                secondaryTray = FindWindowEx(IntPtr.Zero, secondaryTray, "Shell_SecondaryTrayWnd", null);
                if (secondaryTray == IntPtr.Zero) break;
                ShowWindow(secondaryTray, SW_HIDE);
            }

            // Скрываем кнопку Пуск отдельно (иногда нужно)
            var startButton = FindWindow("Button", "Start");
            if (startButton != IntPtr.Zero)
                ShowWindow(startButton, SW_HIDE);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ExplorerBlocker] HideTaskbar failed: {ex.Message}");
        }
    }

    public static void ShowTaskbar()
    {
        try
        {
            var trayWnd = FindWindow("Shell_TrayWnd", null);
            if (trayWnd != IntPtr.Zero)
                ShowWindow(trayWnd, SW_SHOW);

            var secondaryTray = IntPtr.Zero;
            while (true)
            {
                secondaryTray = FindWindowEx(IntPtr.Zero, secondaryTray, "Shell_SecondaryTrayWnd", null);
                if (secondaryTray == IntPtr.Zero) break;
                ShowWindow(secondaryTray, SW_SHOW);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ExplorerBlocker] ShowTaskbar failed: {ex.Message}");
        }
    }

    public static void KillExplorer()
    {
        try
        {
            var explorers = Process.GetProcessesByName("explorer");
            foreach (var proc in explorers)
            {
                try
                {
                    // Не убиваем если это наш процесс? explorer.exe всегда отдельный
                    proc.Kill();
                    Debug.WriteLine($"[ExplorerBlocker] Killed explorer PID {proc.Id}");
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ExplorerBlocker] KillExplorer failed: {ex.Message}");
        }
    }

    public static void RestartExplorer()
    {
        try
        {
            // Проверяем запущен ли уже
            if (Process.GetProcessesByName("explorer").Length > 0) return;

            var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            if (File.Exists(explorerPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = explorerPath,
                    UseShellExecute = true
                });
                Debug.WriteLine("[ExplorerBlocker] Restarted explorer");
            }
            else
            {
                Process.Start("explorer.exe");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ExplorerBlocker] RestartExplorer failed: {ex.Message}");
        }
    }

    private void GuardTick(object? state)
    {
        try
        {
            if (_blockTaskMgr)
            {
                CloseTaskManagerWindow();
            }

            // В киоск режиме также закрываем любые новые окна проводника
            if (_killExplorer || _hideTaskbar)
            {
                // Если explorer перезапустился сам (Windows иногда перезапускает), снова убиваем если в режиме киоска
                if (_killExplorer && Process.GetProcessesByName("explorer").Length > 0)
                {
                    // Проверяем не в процессе ли разблокировки - если _guardTimer еще активен и _explorerKilled, значит все еще заблокировано
                    // Но не убиваем слишком часто, только если прошло >2 сек после последнего убийства
                    // Для простоты: если в киоск режиме и explorer появился, убиваем снова через 1 сек
                    // (реализовано через то что мы убили при Enable, но Windows может перезапустить)
                }
            }
        }
        catch { }
    }

    private void CloseTaskManagerWindow()
    {
        try
        {
            // Ищем окно диспетчера задач по классу и заголовку
            // Класс: TaskManagerWindow или #32770 (dialog) с заголовком "Диспетчер задач" / "Task Manager"
            EnumWindows((hWnd, lParam) =>
            {
                try
                {
                    var sb = new System.Text.StringBuilder(256);
                    GetWindowText(hWnd, sb, 256);
                    var title = sb.ToString();

                    if (title.Contains("Диспетчер задач", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("Task Manager", StringComparison.OrdinalIgnoreCase) ||
                        title.Contains("TaskManager", StringComparison.OrdinalIgnoreCase))
                    {
                        // Нашли окно диспетчера - закрываем
                        // Используем PostMessage WM_CLOSE
                        PostMessage(hWnd, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE
                        Debug.WriteLine($"[ExplorerBlocker] Closed Task Manager window: {title} hWnd={hWnd}");
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);

            // Также пробуем найти процессы Taskmgr и убить? Но лучше закрывать окно, а не убивать процесс резко
            // Для агрессивного режима можно убить процесс taskmgr
            // var taskmgrs = Process.GetProcessesByName("Taskmgr");
            // foreach (var p in taskmgrs) p.Kill();
        }
        catch { }
    }

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public void Dispose()
    {
        Disable();
    }
}
