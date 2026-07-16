using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using BlockVision.App.Views;
using BlockVision.Core.Locking;

namespace BlockVision.App.Services;

/// <summary>
/// Иконка в трее с меню блокировки
/// </summary>
public class TrayIconService : IDisposable
{
    private TaskbarIcon? _trayIcon;
    private readonly LockService _lockService;

    public TrayIconService(LockService lockService)
    {
        _lockService = lockService;
    }

    public void Initialize()
    {
        try
        {
            _trayIcon = new TaskbarIcon
            {
                Icon = System.Drawing.SystemIcons.Shield,
                ToolTipText = "Block Vision PC - Защита активна"
            };

            var contextMenu = new System.Windows.Controls.ContextMenu();

            var lockItem = new System.Windows.Controls.MenuItem { Header = "🔒 Заблокировать сейчас" };
            lockItem.Click += (s, e) =>
            {
                _lockService.Lock(LockReason.Manual, "Ручная блокировка из трея", "Tray");
                var win = new LockScreenWindow();
                win.Show();
            };
            contextMenu.Items.Add(lockItem);

            var settingsItem = new System.Windows.Controls.MenuItem { Header = "⚙️ Настройки" };
            settingsItem.Click += (s, e) => new SettingsWindow().Show();
            contextMenu.Items.Add(settingsItem);

            contextMenu.Items.Add(new System.Windows.Controls.Separator());

            var statusItem = new System.Windows.Controls.MenuItem { Header = $"Статус: {(_lockService.IsLocked ? "Заблокировано" : "Защита активна")}", IsEnabled = false };
            contextMenu.Items.Add(statusItem);

            contextMenu.Items.Add(new System.Windows.Controls.Separator());

            var exitItem = new System.Windows.Controls.MenuItem { Header = "Выход" };
            exitItem.Click += (s, e) => Application.Current.Shutdown();
            contextMenu.Items.Add(exitItem);

            _trayIcon.ContextMenu = contextMenu;
            _trayIcon.DoubleClickCommand = new RelayCommand(_ =>
            {
                _lockService.Lock(LockReason.Manual, "Блокировка двойным кликом по трею", "Tray");
                new LockScreenWindow().Show();
            });
        }
        catch { }
    }

    public void ShowBalloon(string title, string message)
    {
        try { _trayIcon?.ShowBalloonTip(title, message, BalloonIcon.Warning); } catch { }
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
    }
}

public class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action<object?> _execute;
    public RelayCommand(Action<object?> execute) => _execute = execute;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged;
}
