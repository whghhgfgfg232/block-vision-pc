using System.Windows;
using BlockVision.App.Services;
using BlockVision.App.Views;
using BlockVision.Core.Configuration;
using BlockVision.Core.Integration;
using BlockVision.Core.Logging;
using BlockVision.Core.Locking;
using BlockVision.Core.Security;

// Resolve WPF vs WinForms ambiguity
using WpfApplication = System.Windows.Application;
using WpfStartupEventArgs = System.Windows.StartupEventArgs;
using WpfExitEventArgs = System.Windows.ExitEventArgs;

namespace BlockVision.App;

public partial class App : WpfApplication
{
    public static ConfigManager? ConfigManager { get; private set; }
    public static LockService? LockService { get; private set; }
    public static AuditLogger? Logger { get; private set; }
    public static IpcServer? IpcServer { get; private set; }
    public static HttpApiServer? HttpApi { get; private set; }
    public static DefenderIntegration? DefenderIntegration { get; private set; }
    public static ActivationKeyManager? KeyManager { get; private set; }
    public static TrayIconService? TrayIcon { get; private set; }

    private LockScreenWindow? _lockWindow;

    protected override void OnStartup(WpfStartupEventArgs e)
    {
        base.OnStartup(e);

        // Инициализация ядра
        var configPath = ConfigManager.GetDefaultConfigPath();
        ConfigManager = new ConfigManager(configPath, useEncryption: true, encryptionKey: "BVPC_MasterKey_2024_Secure!");
        try { ConfigManager.Load(); } catch { /* создаст дефолт */ }

        Logger = new AuditLogger();
        KeyManager = new ActivationKeyManager(ConfigManager.Config.HardwareId + "_SecretSalt_BVPC");
        LockService = new LockService(ConfigManager, Logger, KeyManager);

        // Подписка на событие блокировки ДО обработки аргументов, чтобы показать окно если нужно
        LockService.LockRequested += (s, args) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_lockWindow == null || !_lockWindow.IsVisible)
                {
                    _lockWindow = new LockScreenWindow();
                    _lockWindow.Show();
                }
            });
        };

        LockService.UnlockRequested += (s, args) =>
        {
            // Закрытие lock окна обрабатывается внутри самого LockScreenWindow
        };

        // Авто-блокировка при старте если настроено
        if (ConfigManager.Config.Locking.LockOnStartup)
        {
            LockService.Lock(LockReason.System, "Автоблокировка при запуске");
        }

        // IPC для интеграции с любыми приложениями (Defender и т.д.)
        if (ConfigManager.Config.Integration.EnableIpcServer)
        {
            IpcServer = new IpcServer(ConfigManager.Config.Integration.IpcPipeName, ConfigManager.Config.Integration.HttpApiToken, LockService, Logger);
            IpcServer.Start();
        }

        if (ConfigManager.Config.Integration.EnableHttpApi)
        {
            HttpApi = new HttpApiServer(ConfigManager.Config.Integration.HttpApiPort, ConfigManager.Config.Integration.HttpApiToken, LockService, Logger);
            HttpApi.Start();
        }

        if (ConfigManager.Config.Integration.DefenderIntegrationEnabled)
        {
            DefenderIntegration = new DefenderIntegration(ConfigManager.Config.Integration, LockService, Logger);
            DefenderIntegration.Start();
        }

        // Обработка аргументов командной строки (для Defender и других)
        HandleCommandLineArgs(e.Args);

        Logger.Log(AuditEventType.ServiceStarted, "BlockVisionPC App запущен", "App");

        // Tray icon
        try
        {
            TrayIcon = new TrayIconService(LockService!);
            TrayIcon.Initialize();
        }
        catch { }

        // Глобальный обработчик непойманных исключений
        DispatcherUnhandledException += (s, args) =>
        {
            Logger?.Log(AuditEventType.TamperDetected, $"Unhandled exception: {args.Exception.Message}", "App");
            args.Handled = true;
        };

        // === ЛОГИКА ПОКАЗА ОКОН ===
        // Если первый запуск или пароль не установлен - сразу показываем настройки для установки пароля
        // Это исправляет deadlock когда LockScreen требует пароль которого еще нет
        if (ConfigManager.Config.IsFirstRun || string.IsNullOrWhiteSpace(ConfigManager.Config.AdminPasswordHash))
        {
            Logger.Log(AuditEventType.ServiceStarted, "Первый запуск - показываем окно настройки пароля", "FirstRun");
            var settings = new SettingsWindow();
            settings.Show();
            
            // Для первого запуска не показываем блокировку
            if (ConfigManager.Config.IsFirstRun)
            {
                ConfigManager.Config.IsFirstRun = false;
                ConfigManager.Save();
            }
        }
        else if (LockService.IsLocked)
        {
            // Если заблокировано (по LockOnStartup или по внешнему триггеру) - показываем LockScreen
            _lockWindow = new LockScreenWindow();
            _lockWindow.Show();
        }
        else
        {
            // Обычный запуск без блокировки - показываем настройки (или можно ничего не показывать и оставить только трей)
            // Для удобства первый раз показываем настройки, потом можно свернуть в трей
            bool minimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
            if (!minimized)
            {
                var settings = new SettingsWindow();
                settings.Show();
            }
        }
    }

    private void HandleCommandLineArgs(string[] args)
    {
        if (args.Length == 0) return;

        var argStr = string.Join(" ", args).ToLowerInvariant();
        if (argStr.Contains("--lock") || argStr.Contains("/lock"))
        {
            var reason = "Manual CLI lock";
            var reasonIdx = Array.FindIndex(args, a => a.Equals("--reason", StringComparison.OrdinalIgnoreCase));
            if (reasonIdx >= 0 && reasonIdx + 1 < args.Length) reason = args[reasonIdx + 1];
            LockService?.Lock(LockReason.IpcRequest, reason, "CLI");
        }
        else if (argStr.Contains("--defender-threat"))
        {
            var threatIdx = Array.FindIndex(args, a => a.Equals("--defender-threat", StringComparison.OrdinalIgnoreCase));
            var threat = threatIdx >= 0 && threatIdx + 1 < args.Length ? args[threatIdx + 1] : "Unknown Defender threat";
            LockService?.TriggerByDefender(threat);
        }
    }

    protected override void OnExit(WpfExitEventArgs e)
    {
        Logger?.Log(AuditEventType.ServiceStopped, "BlockVisionPC App завершен", "App");
        TrayIcon?.Dispose();
        IpcServer?.Dispose();
        HttpApi?.Dispose();
        DefenderIntegration?.Dispose();
        Logger?.Dispose();
        ConfigManager?.Save();
        base.OnExit(e);
    }
}
