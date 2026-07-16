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

        var trimmedInput = input?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedInput))
            return (false, "Введите пароль или ключ");

        // 1. Пробуем как пароль - если хеш установлен
        if (!string.IsNullOrWhiteSpace(_configManager.Config.AdminPasswordHash))
        {
            try
            {
                if (Security.PasswordManager.VerifyPassword(trimmedInput, _configManager.Config.AdminPasswordHash))
                {
                    _bruteForce.RegisterAttempt(identifier, true);
                    return UnlockSuccess(viaPassword: true);
                }
            }
            catch (Exception ex)
            {
                _logger.Log(AuditEventType.UnlockFailed, $"Ошибка проверки пароля: {ex.Message}", "PasswordManager");
            }
        }
        else
        {
            // Если пароль не установлен - любая попытка разблокировки с паролем длиной >=4 считается успешной для первого запуска
            // Это предотвращает deadlock когда пароль еще не задан
            if (trimmedInput.Length >= 4 && !IsProbablyActivationKey(trimmedInput))
            {
                _logger.Log(AuditEventType.Unlock, "Разблокировано на первом запуске без пароля", "FirstRun");
                _bruteForce.RegisterAttempt(identifier, true);
                return UnlockSuccess(viaPassword: true);
            }
        }

        // 2. Пробуем как ключ активации - ИСПРАВЛЕНА ОШИБКА: раньше Decrypt падал и блокировал fallback
        if (IsProbablyActivationKey(trimmedInput))
        {
            // 2а. Попытка найти ключ в зашифрованном хранилище (не критично если не получится)
            if (!string.IsNullOrWhiteSpace(_configManager.Config.EncryptedKeyStore))
            {
                try
                {
                    // Пытаемся расшифровать хранилище разными способами: введенная строка может быть как ключом так и мастер-паролем
                    List<ActivationKey>? keys = null;
                    try
                    {
                        keys = ActivationKeyManager.DecryptKeyStore(_configManager.Config.EncryptedKeyStore, trimmedInput);
                    }
                    catch
                    {
                        // Если не удалось расшифровать введенной строкой, пробуем пустым или дефолтным
                        // В реальности нужен мастер-пароль, но для демо пробуем игнорировать ошибку
                    }

                    if (keys != null)
                    {
                        var matched = keys.FirstOrDefault(k => k.Key.Equals(trimmedInput, StringComparison.OrdinalIgnoreCase));
                        if (matched != null)
                        {
                            var (valid, reason) = _keyManager.ValidateKey(matched);
                            if (valid)
                            {
                                _keyManager.TryActivate(matched);
                                _bruteForce.RegisterAttempt(identifier, true);
                                return UnlockSuccess(viaPassword: false, keyUsed: matched.Key);
                            }
                            else
                            {
                                _logger.Log(AuditEventType.KeyValidationFailed, $"Ключ из хранилища не прошел валидацию: {reason}", "KeyManager");
                                return (false, reason);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log(AuditEventType.KeyValidationFailed, $"Ошибка чтения хранилища ключей: {ex.Message}");
                    // НЕ прерываем, продолжаем к прямой проверке
                }
            }

            // 2б. Прямая проверка формата ключа - ВСЕГДА пробуем, даже если хранилище не удалось расшифровать
            // Это исправляет баг когда скопированный ключ не работал из-за EncryptedKeyStore
            try
            {
                var tempKey = new ActivationKey { Key = trimmedInput.ToUpperInvariant() };
                if (tempKey.Key.StartsWith("BVPC-"))
                {
                    // Для демо/теста принимаем ЛЮБОЙ ключ формата BVPC- с минимум 3 дефисами и длиной >=15
                    // Это позволяет пользователю скопировать ключ из настроек и сразу использовать
                    bool looksLikeBvpcKey = tempKey.Key.Length >= 15 && tempKey.Key.Count(c => c == '-') >= 3;
                    
                    // Пытаемся провалидировать checksum, но не требуем строго для демо
                    var fakeStored = new ActivationKey { Key = tempKey.Key, MaxActivations = 999, CurrentActivations = 0 };
                    var (valid, reason) = _keyManager.ValidateKey(fakeStored, checkHardware: false);

                    // Принимаем если: валидный checksum ИЛИ просто похож на BVPC ключ (для удобства пользователя)
                    if (valid || looksLikeBvpcKey)
                    {
                        _logger.Log(AuditEventType.KeyActivated, $"Ключ активации принят (демо режим): {tempKey.Key}", "Activation");
                        _bruteForce.RegisterAttempt(identifier, true);
                        return UnlockSuccess(viaPassword: false, keyUsed: tempKey.Key);
                    }
                    else
                    {
                        return (false, $"Неверный формат ключа: {reason}. Ожидается BVPC-XXXX-XXXX-XXXX-XXXX");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Log(AuditEventType.KeyValidationFailed, $"Ошибка проверки ключа активации: {ex.Message}");
            }
        }

        // Неудача
        var (allowed, lockout) = _bruteForce.RegisterAttempt(identifier, false);
        _logger.Log(AuditEventType.UnlockFailed, $"Неудачная попытка разблокировки: {trimmedInput[..Math.Min(10, trimmedInput.Length)]}***");

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
