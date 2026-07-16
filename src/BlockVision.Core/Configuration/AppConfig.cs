using System.Text.Json.Serialization;

namespace BlockVision.Core.Configuration;

public class AppConfig
{
    public string Version { get; set; } = "1.0.0";
    public PersonalizationConfig Personalization { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();
    public LockingConfig Locking { get; set; } = new();
    public IntegrationConfig Integration { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();

    // Хешированный пароль администратора (PBKDF2)
    public string AdminPasswordHash { get; set; } = string.Empty;
    // Зашифрованное хранилище ключей активации (AES-GCM, пароль = admin password)
    public string EncryptedKeyStore { get; set; } = string.Empty;
    public string HardwareId { get; set; } = string.Empty;
    public bool IsFirstRun { get; set; } = true;
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
}

public class LockingConfig
{
    public bool LockOnStartup { get; set; } = false;
    public bool LockOnIdle { get; set; } = true;
    public int IdleMinutes { get; set; } = 5;
    public bool LockOnLidClose { get; set; } = true;
    public bool LockOnUsbRemoved { get; set; } = false; // ключ-флешка
    public string? UsbWhitelistSerial { get; set; }

    public bool BlockTaskManager { get; set; } = true;
    public bool BlockAltTab { get; set; } = true;
    public bool BlockWindowsKeys { get; set; } = true;
    public bool HideCursor { get; set; } = false;
    public bool CoverAllMonitors { get; set; } = true;
    public bool DisableInternetOnLock { get; set; } = false; // через firewall rule

    public int FailedAttemptsBeforeLockout { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 10;
    public bool PlaySoundOnLock { get; set; } = true;
    public bool ShowClock { get; set; } = true;
    public bool ShowFailedAttempts { get; set; } = true;
}

public class IntegrationConfig
{
    public bool EnableIpcServer { get; set; } = true;
    public string IpcPipeName { get; set; } = "BlockVisionPC_Pipe";
    public bool EnableHttpApi { get; set; } = false; // локальный REST API на 127.0.0.1
    public int HttpApiPort { get; set; } = 17845;
    public string HttpApiToken { get; set; } = string.Empty;

    public bool DefenderIntegrationEnabled { get; set; } = true;
    public bool DefenderLockOnThreat { get; set; } = true;
    public bool DefenderLockOnPup { get; set; } = false;

    public List<ProtectedFolder> ProtectedFolders { get; set; } = new();
    public List<string> AllowedProcessesToUnlock { get; set; } = new() { "MsMpEng.exe", "Defender" };
}

public class ProtectedFolder
{
    public string Path { get; set; } = string.Empty;
    public bool LockOnAccess { get; set; } = true;
    public bool LockOnModification { get; set; } = true;
    public bool LockOnRansomwarePattern { get; set; } = true; // много переименований за секунду
    public List<string> SensitiveExtensions { get; set; } = new() { ".key", ".pem", ".pfx", ".kdbx", ".docx", ".xlsx" };
}

public class LoggingConfig
{
    public bool EnableAuditLog { get; set; } = true;
    public bool LogFailedAttempts { get; set; } = true;
    public bool LogSuccessfulUnlocks { get; set; } = true;
    public bool LogIpcCommands { get; set; } = true;
    public int MaxLogFiles { get; set; } = 30;
    public bool SendToWindowsEventLog { get; set; } = true;
}

public static class DefaultConfigs
{
    public static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            Personalization = new PersonalizationConfig
            {
                Language = "ru-RU",
                Theme = ThemeType.Dark,
                PrimaryColor = "#6C5CE7",
                AccentColor = "#00CEC9",
                BackgroundType = BackgroundType.Gradient,
                BackgroundValue = "linear-gradient(135deg,#0f0c29,#302b63,#24243e)",
                WelcomeMessage = "Система защищена Block Vision PC",
                LockMessage = "Доступ заблокирован. Введите пароль или ключ активации.",
                ShowLogo = true,
                BlurBackground = true,
                CustomWallpaperPath = null,
                DateFormat = "dd MMMM yyyy",
                TimeFormat = "HH:mm:ss"
            },
            Security = new SecurityConfig
            {
                PasswordMinLength = 8,
                RequireUppercase = true,
                RequireDigit = true,
                RequireSpecial = false,
                AutoWipeAfterFailedAttempts = 0, // 0 = отключено
                EncryptConfigFile = true,
                UseDpapi = true,
                UseTpmIfAvailable = false,
                SessionTimeoutMinutes = 0 // 0 = без таймаута
            },
            Locking = new LockingConfig(),
            Integration = new IntegrationConfig
            {
                ProtectedFolders = new List<ProtectedFolder>
                {
                    new() { Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Secrets"), LockOnAccess = true },
                    new() { Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Confidential"), LockOnRansomwarePattern = true }
                }
            }
        };
    }
}
