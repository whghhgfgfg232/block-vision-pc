using System.Collections.Concurrent;
using System.Text.Json;

namespace BlockVision.Core.Logging;

public enum AuditEventType
{
    Lock,
    Unlock,
    UnlockFailed,
    LockoutStarted,
    ConfigChanged,
    PasswordChanged,
    KeyActivated,
    KeyValidationFailed,
    IpcCommandReceived,
    DefenderThreatDetected,
    FileGuardTriggered,
    TamperDetected,
    WipeInitiated,
    ServiceStarted,
    ServiceStopped
}

public class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public AuditEventType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? User { get; set; }
    public string? Source { get; set; } // Defender, App, User, System
    public string? IpAddress { get; set; }
    public Dictionary<string, string> Details { get; set; } = new();
    public string HardwareId { get; set; } = string.Empty;
}

public class AuditLogger : IDisposable
{
    private readonly string _logDir;
    private readonly ConcurrentQueue<AuditEvent> _queue = new();
    private readonly Timer _flushTimer;
    private readonly object _fileLock = new();
    private bool _disposed;

    public event Action<AuditEvent>? OnEventLogged;

    public AuditLogger(string? logDir = null)
    {
        _logDir = logDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlockVisionPC", "logs");
        Directory.CreateDirectory(_logDir);
        _flushTimer = new Timer(_ => Flush(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public void Log(AuditEventType type, string message, string? source = null, Dictionary<string, string>? details = null)
    {
        var ev = new AuditEvent
        {
            Type = type,
            Message = message,
            Source = source ?? Environment.MachineName,
            User = Environment.UserName,
            Details = details ?? new(),
            HardwareId = BlockVision.Core.Security.CryptoHelper.GetHardwareId()
        };
        _queue.Enqueue(ev);
        OnEventLogged?.Invoke(ev);

        // Немедленная запись для критичных
        if (type is AuditEventType.UnlockFailed or AuditEventType.TamperDetected or AuditEventType.DefenderThreatDetected)
            Flush();

#if DEBUG
        Console.WriteLine($"[AUDIT] {ev.Timestamp:HH:mm:ss} {type}: {message}");
#endif
    }

    public void Flush()
    {
        if (_queue.IsEmpty) return;

        var batch = new List<AuditEvent>();
        while (_queue.TryDequeue(out var ev)) batch.Add(ev);
        if (batch.Count == 0) return;

        lock (_fileLock)
        {
            var fileName = Path.Combine(_logDir, $"audit_{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
            try
            {
                using var writer = new StreamWriter(fileName, true);
                foreach (var ev in batch)
                {
                    var line = JsonSerializer.Serialize(ev);
                    writer.WriteLine(line);
                }
            }
            catch { /* не падаем если лог недоступен */ }

            // Windows Event Log интеграция
            try
            {
                if (!System.Diagnostics.EventLog.SourceExists("BlockVisionPC"))
                    System.Diagnostics.EventLog.CreateEventSource("BlockVisionPC", "Application");

                foreach (var ev in batch.Where(e => e.Type is AuditEventType.DefenderThreatDetected or AuditEventType.TamperDetected))
                {
                    System.Diagnostics.EventLog.WriteEntry("BlockVisionPC", $"{ev.Type}: {ev.Message}", System.Diagnostics.EventLogEntryType.Warning, 1001);
                }
            }
            catch { }
        }
    }

    public List<AuditEvent> GetRecent(int count = 100)
    {
        var all = new List<AuditEvent>();
        var files = Directory.GetFiles(_logDir, "audit_*.jsonl").OrderByDescending(f => f).Take(5);

        foreach (var file in files)
        {
            try
            {
                var lines = File.ReadAllLines(file);
                foreach (var line in lines.Reverse())
                {
                    if (all.Count >= count) break;
                    var ev = JsonSerializer.Deserialize<AuditEvent>(line);
                    if (ev != null) all.Add(ev);
                }
            }
            catch { }
        }

        return all.OrderByDescending(e => e.Timestamp).Take(count).ToList();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Flush();
        _flushTimer.Dispose();
    }
}
