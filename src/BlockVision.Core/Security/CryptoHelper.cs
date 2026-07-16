using System.Security.Cryptography;
using System.Text;

namespace BlockVision.Core.Security;

/// <summary>
/// Криптографические примитивы: AES-GCM, PBKDF2, DPAPI, Secure Wipe
/// </summary>
public static class CryptoHelper
{
    private const int SaltSize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32; // 256 bit

    public static byte[] GenerateSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    public static byte[] DeriveKey(string password, byte[] salt, int iterations = 100_000)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA512);
        return pbkdf2.GetBytes(KeySize);
    }

    /// <summary>
    /// AES-256-GCM шифрование: возвращает salt+nonce+tag+ciphertext Base64
    /// </summary>
    public static string Encrypt(string plainText, string password)
    {
        var salt = GenerateSalt();
        var key = DeriveKey(password, salt);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);

        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        // Формат: [salt][nonce][tag][cipher]
        var result = new byte[SaltSize + NonceSize + TagSize + cipherBytes.Length];
        Buffer.BlockCopy(salt, 0, result, 0, SaltSize);
        Buffer.BlockCopy(nonce, 0, result, SaltSize, NonceSize);
        Buffer.BlockCopy(tag, 0, result, SaltSize + NonceSize, TagSize);
        Buffer.BlockCopy(cipherBytes, 0, result, SaltSize + NonceSize + TagSize, cipherBytes.Length);

        // Secure wipe
        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(plainBytes);

        return Convert.ToBase64String(result);
    }

    public static string Decrypt(string encryptedBase64, string password)
    {
        var full = Convert.FromBase64String(encryptedBase64);
        if (full.Length < SaltSize + NonceSize + TagSize)
            throw new CryptographicException("Invalid encrypted data format");

        var salt = new byte[SaltSize];
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipher = new byte[full.Length - SaltSize - NonceSize - TagSize];

        Buffer.BlockCopy(full, 0, salt, 0, SaltSize);
        Buffer.BlockCopy(full, SaltSize, nonce, 0, NonceSize);
        Buffer.BlockCopy(full, SaltSize + NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(full, SaltSize + NonceSize + TagSize, cipher, 0, cipher.Length);

        var key = DeriveKey(password, salt);
        var plainBytes = new byte[cipher.Length];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plainBytes);
            return Encoding.UTF8.GetString(plainBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    /// <summary>
    /// DPAPI для привязки к текущему пользователю/машине (Windows)
    /// </summary>
    public static string ProtectWithDpapi(string plainText)
    {
        var data = Encoding.UTF8.GetBytes(plainText);
        var protectedData = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedData);
    }

    public static string UnprotectWithDpapi(string protectedBase64)
    {
        var protectedData = Convert.FromBase64String(protectedBase64);
        var data = ProtectedData.Unprotect(protectedData, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(data);
    }

    public static string GenerateSecureRandomString(int length = 32)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
        var data = RandomNumberGenerator.GetBytes(length);
        var result = new StringBuilder(length);
        foreach (var b in data) result.Append(chars[b % chars.Length]);
        return result.ToString();
    }

    public static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    public static string GetHardwareId()
    {
        // Привязка к железу: CPU + HDD + MachineName (упрощенно без WMI для кроссплатформенности)
        var raw = Environment.MachineName + Environment.ProcessorCount + Environment.OSVersion.VersionString;
        // Можно расширить: Motherboard Serial, MAC и т.д. через WMI
        return ComputeSha256(raw)[..16].ToUpper(); // 16 символов
    }
}
