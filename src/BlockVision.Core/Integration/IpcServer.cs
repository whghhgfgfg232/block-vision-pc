using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using BlockVision.Core.Logging;
using BlockVision.Core.Locking;

namespace BlockVision.Core.Integration;

public class IpcCommand
{
    public string Action { get; set; } = string.Empty; // lock, unlock, status, ping
    public string? Source { get; set; } // App name: Defender, MyApp, etc
    public string? Reason { get; set; }
    public string? Token { get; set; } // API token для аутентификации
    public Dictionary<string, string>? Parameters { get; set; }
}

public class IpcResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
}

public class IpcServer : IDisposable
{
    private readonly string _pipeName;
    private readonly string _expectedToken;
    private readonly LockService _lockService;
    private readonly AuditLogger _logger;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public bool IsRunning { get; private set; }

    public IpcServer(string pipeName, string token, LockService lockService, AuditLogger logger)
    {
        _pipeName = pipeName;
        _expectedToken = token;
        _lockService = lockService;
        _logger = logger;
    }

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        IsRunning = true;
        _serverTask = Task.Run(() => RunServerAsync(_cts.Token));
        _logger.Log(AuditEventType.ServiceStarted, $"IPC Server запущен на pipe {_pipeName}", "IpcServer");
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _cts?.Cancel();
        IsRunning = false;
        _logger.Log(AuditEventType.ServiceStopped, "IPC Server остановлен", "IpcServer");
    }

    private async Task RunServerAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 10, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(token);

                // Читаем команду
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                IpcCommand? cmd = null;
                try
                {
                    cmd = JsonSerializer.Deserialize<IpcCommand>(line);
                }
                catch
                {
                    // Попробуем plain text: "LOCK|Defender|Threat found"
                    var parts = line.Split('|');
                    if (parts.Length >= 1)
                        cmd = new IpcCommand { Action = parts[0], Source = parts.Length > 1 ? parts[1] : "Unknown", Reason = parts.Length > 2 ? parts[2] : "" };
                }

                if (cmd == null)
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new IpcResponse { Success = false, Message = "Invalid command format" }));
                    continue;
                }

                // Проверка токена если задан
                if (!string.IsNullOrEmpty(_expectedToken) && cmd.Token != _expectedToken)
                {
                    _logger.Log(AuditEventType.IpcCommandReceived, $"Отклонен IPC запрос с неверным токеном от {cmd.Source}", cmd.Source);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new IpcResponse { Success = false, Message = "Unauthorized" }));
                    continue;
                }

                var response = HandleCommand(cmd);
                _logger.Log(AuditEventType.IpcCommandReceived, $"IPC {cmd.Action} от {cmd.Source}: {response.Message}", cmd.Source);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.Log(AuditEventType.IpcCommandReceived, $"Ошибка IPC: {ex.Message}", "IpcServer");
                await Task.Delay(500, token).ContinueWith(_ => { });
            }
        }
    }

    private IpcResponse HandleCommand(IpcCommand cmd)
    {
        return cmd.Action.ToLowerInvariant() switch
        {
            "lock" => HandleLock(cmd),
            "unlock" => HandleUnlock(cmd),
            "status" => new IpcResponse { Success = true, Message = _lockService.IsLocked ? "Locked" : "Unlocked", Data = new { locked = _lockService.IsLocked } },
            "ping" => new IpcResponse { Success = true, Message = "Pong" },
            "defender_threat" => HandleDefenderThreat(cmd),
            _ => new IpcResponse { Success = false, Message = $"Unknown action: {cmd.Action}" }
        };
    }

    private IpcResponse HandleLock(IpcCommand cmd)
    {
        _lockService.TriggerByExternalApp(cmd.Source ?? "UnknownApp", cmd.Reason ?? "External lock request");
        return new IpcResponse { Success = true, Message = "Lock triggered" };
    }

    private IpcResponse HandleUnlock(IpcCommand cmd)
    {
        // Разблокировка через IPC только если есть параметр force и токен
        if (cmd.Parameters != null && cmd.Parameters.TryGetValue("password", out var pwd))
        {
            var (ok, msg) = _lockService.TryUnlock(pwd);
            return new IpcResponse { Success = ok, Message = msg };
        }
        return new IpcResponse { Success = false, Message = "Unlock via IPC requires password param and valid token" };
    }

    private IpcResponse HandleDefenderThreat(IpcCommand cmd)
    {
        var threat = cmd.Reason ?? cmd.Parameters?.GetValueOrDefault("threat") ?? "Unknown threat";
        _lockService.TriggerByDefender(threat);
        return new IpcResponse { Success = true, Message = $"Defender lock triggered for {threat}" };
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}

// HTTP API альтернатива для не-Windows или cross-lang интеграций
public class HttpApiServer : IDisposable
{
    private readonly System.Net.HttpListener _listener;
    private readonly LockService _lockService;
    private readonly AuditLogger _logger;
    private readonly string _token;
    private CancellationTokenSource? _cts;

    public HttpApiServer(int port, string token, LockService lockService, AuditLogger logger)
    {
        _listener = new System.Net.HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _lockService = lockService;
        _logger = logger;
        _token = token;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();
        Task.Run(() => RunAsync(_cts.Token));
        _logger.Log(AuditEventType.ServiceStarted, $"HTTP API запущен на {_listener.Prefixes.First()}", "HttpApi");
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                var req = ctx.Request;
                var resp = ctx.Response;

                // Auth
                var auth = req.Headers["X-API-Token"] ?? req.QueryString["token"];
                if (!string.IsNullOrEmpty(_token) && auth != _token)
                {
                    resp.StatusCode = 401;
                    await WriteJson(resp, new { error = "Unauthorized" });
                    continue;
                }

                if (req.Url?.AbsolutePath == "/lock" && req.HttpMethod == "POST")
                {
                    using var sr = new StreamReader(req.InputStream);
                    var body = await sr.ReadToEndAsync();
                    var data = JsonSerializer.Deserialize<Dictionary<string, string>>(body);
                    var source = data?.GetValueOrDefault("source") ?? req.Headers["X-Source"] ?? "HttpApp";
                    var reason = data?.GetValueOrDefault("reason") ?? "HTTP lock";

                    _lockService.TriggerByExternalApp(source, reason);
                    await WriteJson(resp, new { success = true, message = "Locked" });
                }
                else if (req.Url?.AbsolutePath == "/status")
                {
                    await WriteJson(resp, new { locked = _lockService.IsLocked, timestamp = DateTime.UtcNow });
                }
                else
                {
                    resp.StatusCode = 404;
                    await WriteJson(resp, new { error = "Not found" });
                }
            }
            catch { await Task.Delay(100); }
        }
    }

    private static async Task WriteJson(System.Net.HttpListenerResponse resp, object obj)
    {
        resp.ContentType = "application/json";
        var json = JsonSerializer.Serialize(obj);
        var bytes = Encoding.UTF8.GetBytes(json);
        await resp.OutputStream.WriteAsync(bytes);
        resp.Close();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener.Stop();
    }

    public void Dispose()
    {
        Stop();
        _listener.Close();
        _cts?.Dispose();
    }
}
