using System.Diagnostics;
using BlockVision.Core.Configuration;
using BlockVision.Core.Logging;
using BlockVision.Core.Locking;

namespace BlockVision.Core.Integration;

/// <summary>
/// Интеграция с Microsoft Defender и защита папок
/// </summary>
public class DefenderIntegration : IDisposable
{
    private readonly IntegrationConfig _config;
    private readonly LockService _lockService;
    private readonly AuditLogger _logger;
    private readonly List<FileSystemWatcher> _watchers = new();
    private Timer? _defenderCheckTimer;
    private Timer? _ransomwareDetectionTimer;

    // Для детекта ransomware паттерна: много изменений за короткое время
    private readonly Dictionary<string, List<DateTime>> _fileChangeHistory = new();
    private readonly object _historyLock = new();

    public DefenderIntegration(IntegrationConfig config, LockService lockService, AuditLogger logger)
    {
        _config = config;
        _lockService = lockService;
        _logger = logger;
    }

    public void Start()
    {
        if (!_config.DefenderIntegrationEnabled) return;

        SetupFileWatchers();

        // Проверка логов Defender каждые 10 сек
        _defenderCheckTimer = new Timer(_ => CheckDefenderThreats(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));

        // Ransomware детект очистка каждые 30 сек
        _ransomwareDetectionTimer = new Timer(_ => CleanupHistory(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        _logger.Log(AuditEventType.ServiceStarted, "DefenderIntegration запущен", "DefenderIntegration");
    }

    private void SetupFileWatchers()
    {
        foreach (var folder in _config.ProtectedFolders)
        {
            if (!Directory.Exists(folder.Path))
            {
                try { Directory.CreateDirectory(folder.Path); } catch { continue; }
            }

            var watcher = new FileSystemWatcher(folder.Path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            watcher.Created += (s, e) => OnFileEvent(e, folder, "Created");
            watcher.Changed += (s, e) => OnFileEvent(e, folder, "Changed");
            watcher.Deleted += (s, e) => OnFileEvent(e, folder, "Deleted");
            watcher.Renamed += (s, e) => OnFileEvent(new FileSystemEventArgs(WatcherChangeTypes.Renamed, e.OldFullPath, e.Name), folder, "Renamed");

            _watchers.Add(watcher);
        }
    }

    private void OnFileEvent(FileSystemEventArgs e, ProtectedFolder folder, string action)
    {
        try
        {
            var ext = Path.GetExtension(e.FullPath)?.ToLowerInvariant() ?? "";

            // Проверка чувствительных расширений
            if (folder.SensitiveExtensions.Contains(ext))
            {
                if (folder.LockOnAccess && action is "Created" or "Changed")
                {
                    _logger.Log(AuditEventType.FileGuardTriggered, $"Доступ к чувствительному файлу: {e.FullPath}", "FileGuard",
                        new() { ["File"] = e.FullPath, ["Action"] = action, ["Folder"] = folder.Path });
                    _lockService.Lock(LockReason.FileGuard, $"Обнаружен доступ к защищенному файлу: {Path.GetFileName(e.FullPath)}", "FileGuard");
                    return;
                }
            }

            // Ransomware паттерн
            if (folder.LockOnRansomwarePattern)
            {
                lock (_historyLock)
                {
                    var key = folder.Path;
                    if (!_fileChangeHistory.TryGetValue(key, out var list))
                    {
                        list = new List<DateTime>();
                        _fileChangeHistory[key] = list;
                    }
                    list.Add(DateTime.UtcNow);

                    // Если за последние 5 секунд > 20 изменений - похоже на шифровальщик
                    var recent = list.Count(t => (DateTime.UtcNow - t).TotalSeconds < 5);
                    if (recent > 20)
                    {
                        _logger.Log(AuditEventType.FileGuardTriggered, $"Обнаружен ransomware паттерн в {folder.Path}: {recent} изменений за 5 сек", "RansomwareDetector");
                        _lockService.Lock(LockReason.FileGuard, "Обнаружена подозрительная активность шифрования!", "RansomwareDetector");
                        list.Clear();
                    }
                }
            }

            if (folder.LockOnModification && action is "Changed" or "Deleted" or "Renamed")
            {
                // Можно триггерить только для важных файлов
                if (ext is ".kdbx" or ".key" or ".pem")
                {
                    _lockService.Lock(LockReason.FileGuard, $"Изменение критического файла: {e.Name}", "FileGuard");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Log(AuditEventType.FileGuardTriggered, $"Ошибка FileGuard: {ex.Message}", "FileGuard");
        }
    }

    private void CleanupHistory()
    {
        lock (_historyLock)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-1);
            foreach (var key in _fileChangeHistory.Keys.ToList())
            {
                _fileChangeHistory[key] = _fileChangeHistory[key].Where(t => t > cutoff).ToList();
            }
        }
    }

    private void CheckDefenderThreats()
    {
        try
        {
            // Способ 1: проверка Windows Event Log - Microsoft-Windows-Windows Defender/Operational
            // Event ID 1116 = Threat detected, 1117 = Action taken, 1015 = Malware detected
            // Упрощенная реализация через EventLog API

            // В проде используем EventLogQuery с XPath
            if (!EventLog.Exists("Microsoft-Windows-Windows Defender/Operational"))
                return;

            // Способ 2: PowerShell Get-MpThreatDetection (если доступен)
            // Мы делаем быструю проверку через файл лога Defender или через WMI

            // Для демо симулируем: проверяем статус последней угрозы через реестр или файл
            // Если DefenderIntegrationEnabled и LockOnThreat - при активной угрозе лочим

            // Пример чтения из Defender log file (если есть доступ)
            var defenderLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "Support", "MPLog.txt");
            // ... парсинг ...

            // Заглушка: если найдено слово Threat в последних событиях
            // В реальном проекте здесь был бы реальный парсер
        }
        catch (Exception ex)
        {
            // Не критично, Defender может быть недоступен
            Debug.WriteLine($"Defender check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Ручной триггер от внешнего сканера (например, Defender вызывает наш exe с параметрами)
    /// </summary>
    public void ReportThreat(string threatName, string severity, string filePath)
    {
        var details = new Dictionary<string, string>
        {
            ["Threat"] = threatName,
            ["Severity"] = severity,
            ["File"] = filePath
        };

        _logger.Log(AuditEventType.DefenderThreatDetected, $"Defender threat: {threatName} in {filePath}", "Microsoft Defender", details);

        if (_config.DefenderLockOnThreat)
        {
            _lockService.TriggerByDefender($"{threatName} ({severity}) - {Path.GetFileName(filePath)}");
        }
    }

    public void Dispose()
    {
        _defenderCheckTimer?.Dispose();
        _ransomwareDetectionTimer?.Dispose();
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }
}

/// <summary>
/// Парсер логов Defender для автоматического триггера
/// </summary>
public static class DefenderLogParser
{
    public static bool TryParseDefenderEvent(string logLine, out string? threatName, out string? file)
    {
        threatName = null;
        file = null;

        // Пример Defender лога: ... Threat: Trojan:Win32/Wacatac.B!ml, File: C:\...
        if (logLine.Contains("Threat", StringComparison.OrdinalIgnoreCase))
        {
            // Простой парсинг
            var parts = logLine.Split(',');
            foreach (var part in parts)
            {
                if (part.Trim().StartsWith("Threat", StringComparison.OrdinalIgnoreCase))
                    threatName = part.Split(':').LastOrDefault()?.Trim();
                if (part.Trim().StartsWith("File", StringComparison.OrdinalIgnoreCase))
                    file = part.Split(':').LastOrDefault()?.Trim();
            }
            return threatName != null;
        }
        return false;
    }
}
