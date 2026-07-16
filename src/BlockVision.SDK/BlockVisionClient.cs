using System.IO.Pipes;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace BlockVision.SDK;

/// <summary>
/// SDK для интеграции любых приложений с Block Vision PC
/// Используется Microsoft Defender, антивирусами, DLP системами, вашими приложениями
/// </summary>
public class BlockVisionClient
{
    private readonly string _pipeName;
    private readonly string _apiToken;
    private readonly string _sourceApp;
    private readonly int _httpPort;
    private readonly bool _useHttp;

    public BlockVisionClient(BlockVisionClientOptions options)
    {
        _pipeName = options.PipeName;
        _apiToken = options.ApiToken;
        _sourceApp = options.SourceAppName;
        _httpPort = options.HttpPort;
        _useHttp = options.UseHttpApi;
    }

    /// <summary>
    /// Заблокировать экран (например, при обнаружении угрозы)
    /// </summary>
    public async Task<bool> LockAsync(string reason, CancellationToken ct = default)
    {
        var cmd = new
        {
            Action = "lock",
            Source = _sourceApp,
            Reason = reason,
            Token = _apiToken
        };

        return _useHttp ? await SendHttpAsync("/lock", cmd, ct) : await SendPipeAsync(cmd, ct);
    }

    /// <summary>
    /// Сообщить об угрозе от Defender
    /// </summary>
    public async Task<bool> ReportThreatAsync(string threatName, string severity = "High", string filePath = "", CancellationToken ct = default)
    {
        var cmd = new
        {
            Action = "defender_threat",
            Source = _sourceApp,
            Reason = $"{threatName} [{severity}] {filePath}",
            Token = _apiToken,
            Parameters = new Dictionary<string, string>
            {
                ["threat"] = threatName,
                ["severity"] = severity,
                ["file"] = filePath
            }
        };

        return _useHttp ? await SendHttpAsync("/lock", cmd, ct) : await SendPipeAsync(cmd, ct);
    }

    /// <summary>
    /// Проверить статус блокировки
    /// </summary>
    public async Task<(bool locked, string message)> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            if (_useHttp)
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                if (!string.IsNullOrEmpty(_apiToken))
                    http.DefaultRequestHeaders.Add("X-API-Token", _apiToken);

                var resp = await http.GetAsync($"http://127.0.0.1:{_httpPort}/status", ct);
                var json = await resp.Content.ReadAsStringAsync(ct);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                var locked = data?["locked"].GetBoolean() ?? false;
                return (locked, locked ? "Locked" : "Unlocked");
            }
            else
            {
                var response = await SendPipeCommandWithResponseAsync(new { Action = "status", Source = _sourceApp, Token = _apiToken }, ct);
                if (response.HasValue && response.Value.TryGetProperty("Data", out var data))
                {
                    var locked = data.TryGetProperty("locked", out var lockedProp) && lockedProp.GetBoolean();
                    return (locked, response.Value.TryGetProperty("Message", out var msg) ? msg.GetString() ?? "" : "");
                }
            }
        }
        catch { }
        return (false, "Unknown - service not running");
    }

    private async Task<bool> SendPipeAsync(object cmd, CancellationToken ct)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(2000, ct);

            var json = JsonSerializer.Serialize(cmd);
            var bytes = Encoding.UTF8.GetBytes(json + "\n");
            await client.WriteAsync(bytes, ct);
            await client.FlushAsync(ct);

            // Читаем ответ
            using var reader = new StreamReader(client, Encoding.UTF8);
            var responseLine = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(responseLine)) return false;

            var resp = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseLine);
            return resp != null && resp.TryGetValue("Success", out var succ) && succ.GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    private async Task<JsonElement?> SendPipeCommandWithResponseAsync(object cmd, CancellationToken ct)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(2000, ct);

            var json = JsonSerializer.Serialize(cmd);
            await client.WriteAsync(Encoding.UTF8.GetBytes(json + "\n"), ct);
            await client.FlushAsync(ct);

            using var reader = new StreamReader(client, Encoding.UTF8);
            var line = await reader.ReadLineAsync();
            if (line == null) return null;
            return JsonSerializer.Deserialize<JsonElement>(line);
        }
        catch { return null; }
    }

    private async Task<bool> SendHttpAsync(string endpoint, object cmd, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            if (!string.IsNullOrEmpty(_apiToken))
                http.DefaultRequestHeaders.Add("X-API-Token", _apiToken);
            http.DefaultRequestHeaders.Add("X-Source", _sourceApp);

            var resp = await http.PostAsJsonAsync($"http://127.0.0.1:{_httpPort}{endpoint}", cmd, ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // --- Статические хелперы для быстрого использования ---

    /// <summary>
    /// Самый простой способ: одна строка чтобы заблокировать ПК из любого приложения
    /// </summary>
    public static bool QuickLock(string reason, string sourceApp = "ExternalApp", string pipeName = "BlockVisionPC_Pipe")
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            client.Connect(1000);
            var cmd = JsonSerializer.Serialize(new { Action = "lock", Source = sourceApp, Reason = reason });
            var bytes = Encoding.UTF8.GetBytes(cmd + "\n");
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch { return false; }
    }
}

public class BlockVisionClientOptions
{
    public string PipeName { get; set; } = "BlockVisionPC_Pipe";
    public string ApiToken { get; set; } = string.Empty; // если включена аутентификация
    public string SourceAppName { get; set; } = "MyApp";
    public bool UseHttpApi { get; set; } = false;
    public int HttpPort { get; set; } = 17845;
}

// Пример использования для Microsoft Defender (PowerShell + C#)
public static class IntegrationExamples
{
    public const string PowerShellExample = @"
# Defender Custom Action - блокировка через BlockVision при угрозе
# Добавить в Defender как custom remediation action

$threat = $args[0] # имя угрозы от Defender
$pipeName = 'BlockVisionPC_Pipe'

$cmd = @{
    Action = 'defender_threat'
    Source = 'Microsoft Defender'
    Reason = $threat
    Parameters = @{ threat = $threat; severity = 'High'; file = $args[1] }
} | ConvertTo-Json -Compress

try {
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $pipeName, 'InOut')
    $pipe.Connect(2000)
    $writer = New-Object System.IO.StreamWriter($pipe)
    $writer.AutoFlush = $true
    $writer.WriteLine($cmd)
    Write-Host 'BlockVisionPC: PC locked due to threat'
} catch {
    Write-Error ""Failed to lock via BlockVision: $_""
}
";

    public const string CSharpExample = @"
using BlockVision.SDK;

// Любое приложение может заблокировать ПК
var client = new BlockVisionClient(new BlockVisionClientOptions
{
    SourceAppName = 'MyAntivirus',
    PipeName = 'BlockVisionPC_Pipe'
});

// При обнаружении секретных данных
await client.LockAsync('Обнаружен доступ к секретным файлам из недоверенного процесса');

// Интеграция с Defender
await client.ReportThreatAsync('Trojan:Win32/Wacatac', 'Severe', @'C:\Users\...\malware.exe');

// Проверить статус
var (locked, msg) = await client.GetStatusAsync();
Console.WriteLine($'Locked: {locked}');

// Быстрый способ без создания клиента
BlockVisionClient.QuickLock('Секретная информация под угрозой', 'MyApp');
";
}
