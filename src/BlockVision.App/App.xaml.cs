using System.Windows;
using BlockVision.App.Services;
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
