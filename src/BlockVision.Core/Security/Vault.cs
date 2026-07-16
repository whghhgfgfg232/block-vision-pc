using System.Collections.Concurrent;
using System.Text.Json;

namespace BlockVision.Core.Security;

/// <summary>
/// Защищенное хранилище секретов с шифрованием и очисткой памяти
/// </summary>
public class VaultItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "General"; // Passwords, Keys, Files, Notes
    public string EncryptedData { get; set; } = string.Empty; // AES-GCM
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public bool RequiresLockScreen { get; set; } = true; // при доступе блокировать ПК?
    public int AccessCount { get; set; }
}

public class SecureVault
{
    private readonly ConcurrentDictionary<string, VaultItem> _items = new();
    private readonly string _vaultPath;
    private readonly string _masterPassword; // в реальности храним только хеш, пароль в памяти SecureString

    public SecureVault(string vaultPath, string masterPassword)
    {
        _vaultPath = vaultPath;
        _masterPassword = masterPassword;
    }

    public void AddSecret(string name, string secret, string category = "General", bool requiresLock = true)
    {
        var encrypted = CryptoHelper.Encrypt(secret, _masterPassword);
        var item = new VaultItem
        {
            Name = name,
            Category = category,
            EncryptedData = encrypted,
            RequiresLockScreen = requiresLock
        };
        _items[item.Id] = item;
        Save();
    }

    public string? GetSecret(string id)
    {
        if (_items.TryGetValue(id, out var item))
        {
            item.AccessCount++;
            item.ModifiedAt = DateTime.UtcNow;
            Save();
            return CryptoHelper.Decrypt(item.EncryptedData, _masterPassword);
        }
        return null;
    }

    public bool DeleteSecret(string id)
    {
        var removed = _items.TryRemove(id, out _);
        if (removed) Save();
        return removed;
    }

    public IReadOnlyCollection<VaultItem> ListItems() => _items.Values.ToList().AsReadOnly();

    public void Save()
    {
        var json = JsonSerializer.Serialize(_items.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
        // Дополнительный слой DPAPI для файла на диске
        var protectedJson = CryptoHelper.ProtectWithDpapi(json);
        var dir = Path.GetDirectoryName(_vaultPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_vaultPath, protectedJson);
        // Устанавливаем скрытый + системный атрибут для защиты
        try { File.SetAttributes(_vaultPath, FileAttributes.Hidden | FileAttributes.NotContentIndexed); } catch { }
    }

    public void Load()
    {
        if (!File.Exists(_vaultPath)) return;
        try
        {
            var protectedJson = File.ReadAllText(_vaultPath);
            var json = CryptoHelper.UnprotectWithDpapi(protectedJson);
            var items = JsonSerializer.Deserialize<List<VaultItem>>(json);
            if (items != null)
            {
                _items.Clear();
                foreach (var item in items)
                    _items[item.Id] = item;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Не удалось загрузить хранилище: {ex.Message}", ex);
        }
    }

    public void Wipe()
    {
        // Безопасное удаление: перезапись файла случайными данными
        if (File.Exists(_vaultPath))
        {
            var len = new FileInfo(_vaultPath).Length;
            var random = new byte[len];
            System.Security.Cryptography.RandomNumberGenerator.Fill(random);
            File.WriteAllBytes(_vaultPath, random);
            File.Delete(_vaultPath);
        }
        _items.Clear();
    }
}
