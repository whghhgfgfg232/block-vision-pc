using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BlockVision.Core.Security;

/// <summary>
/// Система ключей активации формата BVPC-XXXX-XXXX-XXXX-XXXX
/// С HMAC подписью, привязкой к железу, сроком годности
/// </summary>
public class ActivationKey
{
    public string Key { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public string? BoundHardwareId { get; set; }
    public int MaxActivations { get; set; } = 1;
    public int CurrentActivations { get; set; } = 0;
    public bool IsRevoked { get; set; }
    public string Label { get; set; } = "Standard License";
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public class ActivationKeyManager
{
    private readonly byte[] _hmacSecret;
    private const string Prefix = "BVPC";

    // Регулярка: BVPC-ABCD-1234-EFGH-5678 с опциональным checksum
    private static readonly Regex KeyRegex = new(@"^BVPC-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}(?:-[A-Z0-9]{4})?$", RegexOptions.Compiled);

    public ActivationKeyManager(string masterSecret)
    {
        // masterSecret должен храниться в DPAPI или TPM
        using var sha = SHA256.Create();
        _hmacSecret = sha.ComputeHash(Encoding.UTF8.GetBytes(masterSecret));
    }

    public ActivationKey GenerateKey(TimeSpan? validity = null, string? hardwareId = null, string label = "Standard", int maxActivations = 1)
    {
        // Генерируем 16 байт энтропии -> 4 группы по 4 символа Base32
        var entropy = RandomNumberGenerator.GetBytes(12);
        var encoded = Base32Encode(entropy); // 20 chars -> разбиваем

        // Формат групп: XXXX-XXXX-XXXX-XXXX
        var groups = new List<string>();
        for (int i = 0; i < 16; i += 4)
            groups.Add(encoded.Substring(i, 4));

        var core = string.Join("-", groups);
        var fullKeyWithoutChecksum = $"{Prefix}-{core}";

        // Checksum = первые 4 символа HMAC(core)
        var checksum = ComputeChecksum(fullKeyWithoutChecksum);
        var fullKey = $"{fullKeyWithoutChecksum}-{checksum}";

        return new ActivationKey
        {
            Key = fullKey,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = validity.HasValue ? DateTime.UtcNow + validity.Value : null,
            BoundHardwareId = hardwareId,
            Label = label,
            MaxActivations = maxActivations
        };
    }

    public (bool valid, string reason) ValidateKey(ActivationKey key, bool checkHardware = true)
    {
        if (key.IsRevoked)
            return (false, "Ключ отозван");

        if (!KeyRegex.IsMatch(key.Key))
            return (false, "Неверный формат ключа");

        // Проверка контрольной суммы
        var lastDash = key.Key.LastIndexOf('-');
        var withoutChecksum = key.Key[..lastDash];
        var expectedChecksum = ComputeChecksum(withoutChecksum);
        var actualChecksum = key.Key[(lastDash + 1)..];
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expectedChecksum), Encoding.UTF8.GetBytes(actualChecksum)))
            return (false, "Неверная контрольная сумма");

        if (key.ExpiresAt.HasValue && DateTime.UtcNow > key.ExpiresAt.Value)
            return (false, $"Срок действия истек {key.ExpiresAt.Value:yyyy-MM-dd}");

        if (key.CurrentActivations >= key.MaxActivations)
            return (false, "Исчерпан лимит активаций");

        if (checkHardware && !string.IsNullOrEmpty(key.BoundHardwareId))
        {
            var currentHwid = CryptoHelper.GetHardwareId();
            if (!string.Equals(currentHwid, key.BoundHardwareId, StringComparison.OrdinalIgnoreCase))
                return (false, $"Ключ привязан к другому устройству ({key.BoundHardwareId})");
        }

        return (true, "Ключ действителен");
    }

    public bool TryActivate(ActivationKey key)
    {
        var (valid, _) = ValidateKey(key);
        if (!valid) return false;
        key.CurrentActivations++;
        return true;
    }

    private string ComputeChecksum(string data)
    {
        using var hmac = new HMACSHA256(_hmacSecret);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        // Берем 2 байта -> 4 символа Base32
        return Base32Encode(hash[..2])[..4].ToUpperInvariant();
    }

    private static string Base32Encode(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var result = new StringBuilder();
        int buffer = 0, bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                result.Append(alphabet[(buffer >> bitsLeft) & 31]);
            }
        }
        if (bitsLeft > 0)
            result.Append(alphabet[(buffer << (5 - bitsLeft)) & 31]);

        // Добиваем до кратного 4 и заменяем на цифры/буквы в верхнем регистре
        return result.ToString().Replace('O', 'X').Replace('I', 'Y').Replace('0', 'Z')[..Math.Min(16, result.Length)].ToUpperInvariant().PadRight(16, 'A');
    }

    // Сериализация зашифрованных ключей для хранения
    public static string EncryptKeyStore(List<ActivationKey> keys, string password)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(keys);
        return CryptoHelper.Encrypt(json, password);
    }

    public static List<ActivationKey> DecryptKeyStore(string encrypted, string password)
    {
        var json = CryptoHelper.Decrypt(encrypted, password);
        return System.Text.Json.JsonSerializer.Deserialize<List<ActivationKey>>(json) ?? new();
    }
}

/// <summary>
/// Лицензии: Trial, Personal, Enterprise
/// </summary>
public enum LicenseTier
{
    Trial,
    Personal,
    Professional,
    Enterprise,
    Ultimate
}

public class LicenseInfo
{
    public LicenseTier Tier { get; set; }
    public DateTime ActivatedAt { get; set; }
    public string ActivatedKey { get; set; } = string.Empty;
    public Dictionary<string, bool> Features { get; set; } = new()
    {
        ["CustomWallpaper"] = true,
        ["MultiMonitor"] = true,
        ["DefenderIntegration"] = false,
        ["FileGuard"] = false,
        ["AuditLog"] = false,
        ["ApiAccess"] = false
    };
}
