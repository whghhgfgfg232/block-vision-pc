namespace BlockVision.Core.Configuration;

public class SecurityConfig
{
    public int PasswordMinLength { get; set; } = 8;
    public bool RequireUppercase { get; set; } = true;
    public bool RequireDigit { get; set; } = false;
    public bool RequireSpecial { get; set; } = false;
    public int PasswordExpiryDays { get; set; } = 0; // 0 = никогда
    public int PasswordHistoryCount { get; set; } = 5; // не повторять последние N

    public int AutoWipeAfterFailedAttempts { get; set; } = 0; // удаление данных
    public bool EncryptConfigFile { get; set; } = true;
    public bool UseDpapi { get; set; } = true;
    public bool UseTpmIfAvailable { get; set; } = false;

    public bool EnableSelfProtection { get; set; } = true; // защита от завершения процесса
    public bool EnableAntiDebug { get; set; } = false;
    public bool EnableTamperDetection { get; set; } = true;

    public int SessionTimeoutMinutes { get; set; } = 0;
    public bool RequirePasswordOnWake { get; set; } = true;
    public bool LockOnSmartCardRemoval { get; set; } = false;

    public string EmergencyContactEmail { get; set; } = string.Empty;
    public bool SendAlertsOnFailedAttempts { get; set; } = false;
}
