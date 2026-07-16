using System.Security.Cryptography;

namespace BlockVision.Core.Security;

public enum PasswordStrength
{
    Weak,
    Medium,
    Strong,
    VeryStrong
}

public class PasswordManager
{
    private const int SaltSize = 32;
    private const int HashSize = 64;
    private const int Iterations = 150_000;

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA512);
        var hash = pbkdf2.GetBytes(HashSize);

        // Формат: iterations.salt.hash
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string storedHash)
    {
        try
        {
            var parts = storedHash.Split('.');
            if (parts.Length != 3) return false;

            var iterations = int.Parse(parts[0]);
            var salt = Convert.FromBase64String(parts[1]);
            var expectedHash = Convert.FromBase64String(parts[2]);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA512);
            var computedHash = pbkdf2.GetBytes(expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
        }
        catch
        {
            return false;
        }
    }

    public static PasswordStrength EvaluateStrength(string password)
    {
        int score = 0;
        if (password.Length >= 8) score++;
        if (password.Length >= 12) score++;
        if (password.Any(char.IsUpper)) score++;
        if (password.Any(char.IsLower)) score++;
        if (password.Any(char.IsDigit)) score++;
        if (password.Any(ch => !char.IsLetterOrDigit(ch))) score++;

        return score switch
        {
            <= 2 => PasswordStrength.Weak,
            3 => PasswordStrength.Medium,
            4 or 5 => PasswordStrength.Strong,
            _ => PasswordStrength.VeryStrong
        };
    }

    public static bool IsPasswordPwned(string password)
    {
        // Заглушка для проверки по HaveIBeenPwned API - в проде реализовать k-anonymity
        var common = new HashSet<string> { "123456", "password", "qwerty", "admin", "letmein", "12345678" };
        return common.Contains(password.ToLowerInvariant());
    }
}

public class BruteForceProtection
{
    private readonly Dictionary<string, (int attempts, DateTime lastAttempt, DateTime? lockoutUntil)> _records = new();
    private readonly object _lock = new();

    public int MaxAttempts { get; set; } = 5;
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan AttemptWindow { get; set; } = TimeSpan.FromMinutes(15);

    public bool IsLockedOut(string identifier, out TimeSpan remaining)
    {
        lock (_lock)
        {
            if (_records.TryGetValue(identifier, out var rec) && rec.lockoutUntil.HasValue)
            {
                if (DateTime.UtcNow < rec.lockoutUntil.Value)
                {
                    remaining = rec.lockoutUntil.Value - DateTime.UtcNow;
                    return true;
                }
                // сброс
                _records[identifier] = (0, DateTime.UtcNow, null);
            }
            remaining = TimeSpan.Zero;
            return false;
        }
    }

    public (bool allowed, TimeSpan? lockout) RegisterAttempt(string identifier, bool success)
    {
        lock (_lock)
        {
            if (!_records.TryGetValue(identifier, out var rec))
                rec = (0, DateTime.UtcNow, null);

            if (success)
            {
                _records[identifier] = (0, DateTime.UtcNow, null);
                return (true, null);
            }

            // сброс если окно прошло
            if (DateTime.UtcNow - rec.lastAttempt > AttemptWindow)
                rec = (0, DateTime.UtcNow, null);

            rec.attempts++;
            rec.lastAttempt = DateTime.UtcNow;

            if (rec.attempts >= MaxAttempts)
            {
                rec.lockoutUntil = DateTime.UtcNow + LockoutDuration;
                _records[identifier] = rec;
                return (false, LockoutDuration);
            }

            _records[identifier] = rec;
            return (true, null);
        }
    }
}
