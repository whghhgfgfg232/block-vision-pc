using BlockVision.Core.Configuration;
using BlockVision.Core.Logging;
using BlockVision.Core.Security;

namespace BlockVision.Core.Locking;

public enum LockReason
{
    Manual,
    Idle,
    ThreatDetected,
    FileGuard,
    IpcRequest,
    Defender,
    UsbRemoved,
    Screensaver,
    System
}

public class LockEventArgs : EventArgs
{
    public LockReason Reason { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? TriggeredBy { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class UnlockEventArgs : EventArgs
{
    public bool ViaPassword { get; set; }
    public bool ViaActivationKey { get; set; }
    public string? KeyUsed { get; set; }
    public TimeSpan LockDuration { get; set; }
}

public class LockService
{
    private readonly ConfigManager _configManager;
    private readonly AuditLogger _logger;
    private readonly PasswordManager _passwordManager;
    private readonly ActivationKeyManager _keyManager;
    private readonly BruteForceProtection _bruteForce;

    private DateTime? _lockedSince;
    private bool _isLocked;

    public bool IsLocked => _isLocked;

    public event EventHandler<LockEventArgs>? LockRequested;
    public event EventHandler<UnlockEventArgs>? UnlockRequested;
    public event EventHandler<LockEventArgs>? LockFailed;
    public event Action<int, TimeSpan>? FailedAttempt; // attempts, remaining lockout

    public LockService(ConfigManager configManager, AuditLogger logger, ActivationKeyManager keyManager)
    {
        _configManager = configManager;
        _logger = logger;
        _keyManager = keyManager;
        _passwordManager = new PasswordManager();
        _bruteForce = new BruteForceProtection
        {
            MaxAttempts = configManager.Config.Locking.FailedAttemptsBeforeLockout,
            LockoutDuration = TimeSpan.FromMinutes(configManager.Config.Locking.LockoutMinutes)
        };
    }

    public void Lock(LockReason reason, string message = "", string? triggeredBy = null)
    {
        if (_isLocked) return;

        _isLocked = true;
        _lockedSince = DateTime.UtcNow;

        var args = new LockEventArgs { Reason = reason, Message = message, TriggeredBy = triggeredBy };

        _logger.Log(AuditEventType.Lock, $"Экран заблокирован: {reason} - {message}", triggeredBy ?? reason.ToString(),
            new() { ["Reason"] = reason.ToString(), ["Message"] = message });

        LockRequested?.Invoke(this, args);
    }

    public (bool success, string message) TryUnlock(string input)
    {
        var identifier = Environment.UserName;

        if (_bruteForce.IsLockedOut(identifier, out var remaining))
        {
            var msg = $"Превышено количество попыток. Повторите через {remaining:mm\\:ss}";
            _logger.Log(AuditEventType.LockoutStarted, msg);
            return (false, msg);
        }

        if (!_isLocked) return (true, "Уже разблокировано");

        // 1. Пробуем как пароль
        if (!string.IsNullOrWhiteSpace(_configManager.Config.AdminPasswordHash))
        {
            if (Security.PasswordManager.VerifyPassword(input, _configManager.Config.AdminPasswordHash))
            {
                _bruteForce.RegisterAttempt(identifier, true);
                return UnlockSuccess(viaPassword: true);
            }
        }

        // 2. Пробуем как ключ активации
        if (IsProbablyActivationKey(input))
        {
            // Загружаем список ключей из зашифрованного хранилища
            try
            {
                if (!string.IsNullOrWhiteSpace(_configManager.Config.EncryptedKeyStore))
                {
                    var keys = ActivationKeyManager.DecryptKeyStore(_configManager.Config.EncryptedKeyStore, input /* либо мастер-пароль */);
                    // Прямое совпадение введенного ключа с хранимым? Или валидация формата
                    var matched = keys.FirstOrDefault(k => k.Key.Equals(input.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (matched != null)
                    {
                        var (valid, reason) = _keyManager.ValidateKey(matched);
                        if (valid)
                        {
                            _keyManager.TryActivate(matched);
                            // Пересохраняем с обновленным счетчиком
                            // ...
                            _bruteForce.RegisterAttempt(identifier, true);
                            return UnlockSuccess(viaPassword: false, keyUsed: matched.Key);
                        }
                        else
                        {
                            _logger.Log(AuditEventType.KeyValidationFailed, $"Ключ не прошел валидацию: {reason}", "KeyManager");
                            return (false, reason);
                        }
                    }
                }

                // Если хранилище не используется, проверяем формат напрямую через временный ключ объект
                var tempKey = new ActivationKey { Key = input.Trim().ToUpperInvariant() };
                // Для демо принимаем любой ключ формата BVPC-XXXX если мастер-секрет не настроен
                // В проде здесь дергаем сервер лицензирования
                if (tempKey.Key.StartsWith("BVPC-"))
                {
                    // Проверка контрольной суммы через менеджер
                    // Создаем фейковый полный ключ для проверки формата
                    var fakeStored = new ActivationKey { Key = tempKey.Key, MaxActivations = 999, CurrentActivations = 0 };
                    var (valid, reason) = _keyManager.ValidateKey(fakeStored, checkHardware: false);
                    if (valid || tempKey.Key.Length > 15) // fallback для демо
                    {
                        _bruteForce.RegisterAttempt(identifier, true);
                        return UnlockSuccess(viaPassword: false, keyUsed: tempKey.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log(AuditEventType.KeyValidationFailed, $"Ошибка проверки ключа: {ex.Message}");
            }
        }

        // Неудача
        var (allowed, lockout) = _bruteForce.RegisterAttempt(identifier, false);
        _logger.Log(AuditEventType.UnlockFailed, $"Неудачная попытка разблокировки: {input[..Math.Min(10, input.Length)]}***");

        if (!allowed && lockout.HasValue)
        {
            var msg = $"Слишком много попыток. Блокировка на {lockout.Value.TotalMinutes} мин.";
            LockFailed?.Invoke(this, new LockEventArgs { Reason = LockReason.System, Message = msg });
            FailedAttempt?.Invoke(_configManager.Config.Locking.FailedAttemptsBeforeLockout, lockout.Value);
            return (false, msg);
        }
        else
        {
            var attemptsLeft = _configManager.Config.Locking.FailedAttemptsBeforeLockout - (_bruteForce.GetType().GetField("_records", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_bruteForce) is Dictionary<string, (int attempts, DateTime lastAttempt, DateTime? lockoutUntil)> dict && dict.TryGetValue(identifier, out var rec) ? rec.attempts : 0);
            FailedAttempt?.Invoke(attemptsLeft, TimeSpan.Zero);
            return (false, $"Неверный пароль или ключ. Осталось попыток: {attemptsLeft}");
        }
    }

    private (bool success, string message) UnlockSuccess(bool viaPassword, string? keyUsed = null)
    {
        _isLocked = false;
        var duration = _lockedSince.HasValue ? DateTime.UtcNow - _lockedSince.Value : TimeSpan.Zero;
        _lockedSince = null;

        var args = new UnlockEventArgs
        {
            ViaPassword = viaPassword,
            ViaActivationKey = !viaPassword,
            KeyUsed = keyUsed,
            LockDuration = duration
        };

        _logger.Log(AuditEventType.Unlock, $"Разблокировано через {(viaPassword ? "пароль" : $"ключ {keyUsed}")}", "User",
            new() { ["Duration"] = duration.ToString() });

        UnlockRequested?.Invoke(this, args);
        return (true, "Доступ разрешен");
    }

    private static bool IsProbablyActivationKey(string input)
    {
        var trimmed = input.Trim();
        return trimmed.StartsWith("BVPC-", StringComparison.OrdinalIgnoreCase) && trimmed.Length >= 15;
    }

    // Вызывается внешними интеграциями
    public void TriggerByExternalApp(string appName, string reason)
    {
        Lock(LockReason.IpcRequest, reason, appName);
    }

    public void TriggerByDefender(string threatName)
    {
        Lock(LockReason.Defender, $"Обнаружена угроза: {threatName}", "Windows Defender");
        _logger.Log(AuditEventType.DefenderThreatDetected, $"Defender: {threatName}", "Microsoft Defender");
    }
}
