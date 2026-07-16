using System.Text.Json;
using BlockVision.Core.Security;

namespace BlockVision.Core.Configuration;

public class ConfigManager
{
    private readonly string _configPath;
    private readonly bool _useEncryption;
    private readonly string _encryptionKey; // в проде берется из DPAPI + пароль

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public AppConfig Config { get; set; }

    public ConfigManager(string configPath, bool useEncryption = true, string encryptionKey = "BlockVision_DefaultKey_ChangeMe!")
    {
        _configPath = configPath;
        _useEncryption = useEncryption;
        _encryptionKey = encryptionKey;
        Config = DefaultConfigs.CreateDefault();
    }

    public void Load()
    {
        if (!File.Exists(_configPath))
        {
            Config = DefaultConfigs.CreateDefault();
            Config.HardwareId = CryptoHelper.GetHardwareId();
            Save();
            return;
        }

        try
        {
            var raw = File.ReadAllText(_configPath);

            string json;
            if (_useEncryption)
            {
                // Пытаемся расшифровать, если не получается - считаем что это открытый JSON (миграция)
                try
                {
                    // Сначала пробуем DPAPI если доступно
                    if (IsDpapiEncrypted(raw))
                        json = CryptoHelper.UnprotectWithDpapi(raw);
                    else
                        json = CryptoHelper.Decrypt(raw, _encryptionKey);
                }
                catch
                {
                    json = raw; // fallback
                }
            }
            else
            {
                json = raw;
            }

            var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (loaded != null)
                Config = loaded;
        }
        catch (Exception ex)
        {
            // Бэкап поврежденного файла
            try
            {
                File.Copy(_configPath, _configPath + $".corrupted.{DateTime.Now:yyyyMMddHHmmss}", true);
            }
            catch { }

            // fallback на дефолт
            Config = DefaultConfigs.CreateDefault();
            throw new InvalidOperationException($"Ошибка загрузки конфигурации, создан дефолт: {ex.Message}", ex);
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Config, JsonOptions);
        string toWrite;

        if (_useEncryption)
        {
            if (Config.Security.UseDpapi)
                toWrite = CryptoHelper.ProtectWithDpapi(json);
            else
                toWrite = CryptoHelper.Encrypt(json, _encryptionKey);
        }
        else
        {
            toWrite = json;
        }

        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Атомарная запись
        var tmp = _configPath + ".tmp";
        File.WriteAllText(tmp, toWrite);
        File.Move(tmp, _configPath, true);

        try { File.SetAttributes(_configPath, FileAttributes.Hidden); } catch { }
    }

    private static bool IsDpapiEncrypted(string data)
    {
        // DPAPI base64 обычно начинается не с { и длиннее
        // Эвристика: если не JSON - считаем DPAPI
        var trimmed = data.TrimStart();
        return !trimmed.StartsWith("{") && !trimmed.StartsWith("[");
    }

    public static string GetDefaultConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "BlockVisionPC", "config.bvpc");
    }
}
