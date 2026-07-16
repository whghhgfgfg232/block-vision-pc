using BlockVision.Core.Configuration;
using BlockVision.Core.Logging;
using BlockVision.Core.Security;
using BlockVision.SDK;

namespace BlockVision.CLI;

class Program
{
    static async Task Main(string[] args)
    {
        Console.Title = "Block Vision PC - CLI";
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
  ____  _            _    __     ___     _                ____   ____ 
 | __ )| | ___   ___| | __\ \   / (_)___(_) ___  _ __    |  _ \ / ___|
 |  _ \| |/ _ \ / __| |/ / \ \ / /| / __| |/ _ \| '_ \   | |_) | |    
 | |_) | | (_) | (__|   <   \ V / | \__ \ | (_) | | | |  |  __/| |___ 
 |____/|_|\___/ \___|_|\_\   \_/  |_|___/_|\___/|_| |_|  |_|    \____|
                                                                      
  Security Lock Screen for Windows - SDK + Defender Integration
");
        Console.ResetColor();

        if (args.Length == 0)
        {
            PrintHelp();
            return;
        }

        var cmd = args[0].ToLowerInvariant();

        switch (cmd)
        {
            case "lock":
                await HandleLock(args);
                break;
            case "status":
                await HandleStatus();
                break;
            case "gen-key":
                HandleGenKey(args);
                break;
            case "defender":
                await HandleDefender(args);
                break;
            case "config":
                HandleConfig(args);
                break;
            case "vault":
                HandleVault(args);
                break;
            case "install-defender-hook":
                HandleInstallDefenderHook();
                break;
            case "help":
            case "--help":
            case "-h":
                PrintHelp();
                break;
            default:
                Console.WriteLine($"Unknown command: {cmd}");
                PrintHelp();
                break;
        }
    }

    static async Task HandleLock(string[] args)
    {
        var reason = GetArg(args, "--reason") ?? "CLI manual lock";
        var source = GetArg(args, "--source") ?? "BVPC_CLI";
        var pipe = GetArg(args, "--pipe") ?? "BlockVisionPC_Pipe";

        Console.WriteLine($"[CLI] Locking PC via pipe {pipe}... Reason: {reason}");

        // Попытка 1: через SDK (Named Pipe)
        var success = BlockVisionClient.QuickLock(reason, source, pipe);
        if (success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[CLI] ✓ Lock command sent successfully!");
            Console.ResetColor();
        }
        else
        {
            // Попытка 2: запустить процесс BlockVisionApp с аргументом --lock
            Console.WriteLine("[CLI] IPC failed, trying to start BlockVisionPC App...");
            try
            {
                var appPath = Path.Combine(AppContext.BaseDirectory, "BlockVisionPC.exe");
                if (!File.Exists(appPath))
                    appPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlockVisionPC", "BlockVisionPC.exe");

                if (File.Exists(appPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = appPath,
                        Arguments = $"--lock --reason \"{reason}\"",
                        UseShellExecute = true
                    });
                    Console.WriteLine($"[CLI] Started {appPath}");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[CLI] BlockVisionPC.exe not found. Make sure app is installed or running.");
                    Console.WriteLine("[CLI] For testing without app running, this is expected.");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[CLI] Failed: {ex.Message}");
                Console.ResetColor();
            }
        }

        // HTTP fallback
        var useHttp = args.Contains("--http");
        if (useHttp)
        {
            var port = int.TryParse(GetArg(args, "--port"), out var p) ? p : 17845;
            var token = GetArg(args, "--token") ?? "";
            var client = new BlockVisionClient(new BlockVisionClientOptions
            {
                UseHttpApi = true,
                HttpPort = port,
                ApiToken = token,
                SourceAppName = source
            });
            var ok = await client.LockAsync(reason);
            Console.WriteLine($"[HTTP] Lock via http://127.0.0.1:{port}/lock : {(ok ? "OK" : "FAIL")}");
        }
    }

    static async Task HandleStatus()
    {
        var client = new BlockVisionClient(new BlockVisionClientOptions { SourceAppName = "BVPC_CLI" });
        var (locked, msg) = await client.GetStatusAsync();
        Console.WriteLine($"Status: {(locked ? "LOCKED" : "UNLOCKED")} - {msg}");
    }

    static void HandleGenKey(string[] args)
    {
        var label = GetArg(args, "--label") ?? "CLI Generated";
        var daysStr = GetArg(args, "--days") ?? "365";
        var days = int.TryParse(daysStr, out var d) ? d : 365;
        var bind = args.Contains("--bind-hwid");

        var masterSecret = GetArg(args, "--master") ?? "DefaultMasterSecret_ChangeMe_123!";
        var manager = new ActivationKeyManager(masterSecret);
        var hwid = bind ? CryptoHelper.GetHardwareId() : null;
        var key = manager.GenerateKey(TimeSpan.FromDays(days), hwid, label, 5);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\nGenerated Key: {key.Key}");
        Console.ResetColor();
        Console.WriteLine($"Label: {key.Label}");
        Console.WriteLine($"Expires: {key.ExpiresAt}");
        Console.WriteLine($"HWID: {key.BoundHardwareId ?? "Any"}");
        Console.WriteLine($"Max Activations: {key.MaxActivations}");

        // Сохранение в файл
        var outFile = GetArg(args, "--out");
        if (!string.IsNullOrEmpty(outFile))
        {
            File.WriteAllText(outFile, key.Key);
            Console.WriteLine($"Saved to {outFile}");
        }
    }

    static async Task HandleDefender(string[] args)
    {
        var threat = GetArg(args, "--threat") ?? "TestThreat:Win32/Test";
        var file = GetArg(args, "--file") ?? @"C:\Temp\eicar.com";
        var severity = GetArg(args, "--severity") ?? "High";

        Console.WriteLine($"[Defender Simulation] Reporting threat: {threat} in {file} [{severity}]");

        var client = new BlockVisionClient(new BlockVisionClientOptions { SourceAppName = "Microsoft Defender" });
        var ok = await client.ReportThreatAsync(threat, severity, file);
        Console.WriteLine(ok ? "✓ Defender lock triggered" : "✗ Failed to trigger (is BlockVision running?)");

        // Локально так же логируем
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlockVisionPC", "defender_sim.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.AppendAllText(logPath, $"{DateTime.Now:O} - Threat {threat} [{severity}] {file} - Lock={(ok ? "OK" : "FAIL")}\n");
    }

    static void HandleConfig(string[] args)
    {
        var configPath = ConfigManager.GetDefaultConfigPath();
        var mgr = new ConfigManager(configPath, useEncryption: false); // читаем без шифра для просмотра, если шифровано - покажет ошибку
        try
        {
            mgr.Load();
            Console.WriteLine($"Config loaded from {configPath}");
            Console.WriteLine($"HardwareId: {mgr.Config.HardwareId}");
            Console.WriteLine($"Version: {mgr.Config.Version}");
            Console.WriteLine($"LockOnIdle: {mgr.Config.Locking.LockOnIdle} ({mgr.Config.Locking.IdleMinutes} min)");
            Console.WriteLine($"Ipc: {mgr.Config.Integration.EnableIpcServer} Pipe={mgr.Config.Integration.IpcPipeName}");
            Console.WriteLine($"Defender: {mgr.Config.Integration.DefenderIntegrationEnabled}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load decrypted config (maybe encrypted): {ex.Message}");
            Console.WriteLine($"Raw file exists: {File.Exists(configPath)} Size: {(File.Exists(configPath) ? new FileInfo(configPath).Length : 0)}");
        }
    }

    static void HandleVault(string[] args)
    {
        var vaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlockVisionPC", "vault.bvpc");
        var master = GetArg(args, "--master") ?? "default_master";
        var vault = new SecureVault(vaultPath, master);
        try { vault.Load(); } catch { }

        var sub = args.Length > 1 ? args[1] : "list";

        switch (sub)
        {
            case "list":
                var items = vault.ListItems();
                Console.WriteLine($"Vault items ({items.Count}):");
                foreach (var it in items)
                    Console.WriteLine($" - {it.Name} [{it.Category}] accesses={it.AccessCount} created={it.CreatedAt}");
                break;
            case "add":
                var name = GetArg(args, "--name") ?? $"secret_{DateTime.Now:HHmmss}";
                var secret = GetArg(args, "--secret") ?? CryptoHelper.GenerateSecureRandomString(16);
                vault.AddSecret(name, secret, "CLI", true);
                Console.WriteLine($"Added {name}");
                break;
            default:
                Console.WriteLine("vault [list|add] --name X --secret Y --master PASS");
                break;
        }
    }

    static void HandleInstallDefenderHook()
    {
        // Создает PowerShell скрипт для интеграции с Defender как Scheduled Task при угрозе
        var script = @"
# BlockVisionPC - Defender Integration Hook
# Установите как задачу в Task Scheduler: триггер по событию Defender 1116

param(
    [string]$ThreatName = $env:ThreatName,
    [string]$ThreatFile = $env:ThreatFile
)

$pipeName = 'BlockVisionPC_Pipe'
$cmd = @{
    Action = 'defender_threat'
    Source = 'Microsoft Defender'
    Reason = ""Defender detected: $ThreatName in $ThreatFile""
    Parameters = @{
        threat = $ThreatName
        severity = 'High'
        file = $ThreatFile
    }
} | ConvertTo-Json -Compress

try {
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $pipeName, 'InOut')
    $pipe.Connect(2000)
    $sw = New-Object System.IO.StreamWriter($pipe)
    $sw.AutoFlush = $true
    $sw.WriteLine($cmd)
    $sr = New-Object System.IO.StreamReader($pipe)
    $resp = $sr.ReadLine()
    Write-Host ""BlockVision response: $resp""
} catch {
    Write-Error ""Failed to contact BlockVisionPC: $_""
    # Fallback: запустить exe напрямую
    $bvpc = Join-Path $env:LOCALAPPDATA 'BlockVisionPC\BlockVisionPC.exe'
    if (Test-Path $bvpc) {
        Start-Process $bvpc -ArgumentList '--defender-threat', ""$ThreatName in $ThreatFile""
    }
}
";
        var outPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Defender_BlockVision_Hook.ps1");
        File.WriteAllText(outPath, script);
        Console.WriteLine($"PowerShell hook saved to {outPath}");
        Console.WriteLine("To install:");
        Console.WriteLine("1. Open Task Scheduler");
        Console.WriteLine("2. Create Task -> Trigger: On Event, Log: Microsoft-Windows-Windows Defender/Operational, EventID 1116");
        Console.WriteLine($"3. Action: Start powershell.exe -File \"{outPath}\"");
    }

    static string? GetArg(string[] args, string name)
    {
        var idx = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0 && idx + 1 < args.Length) return args[idx + 1];
        return null;
    }

    static void PrintHelp()
    {
        Console.WriteLine(@"
Usage: bvpc <command> [options]

Commands:
  lock [--reason TEXT] [--source APP] [--pipe NAME] [--http] [--port N] [--token TOKEN]
        Заблокировать ПК (используется любыми приложениями, включая Defender)

  status
        Проверить статус блокировки

  gen-key [--label TEXT] [--days N] [--bind-hwid] [--master SECRET] [--out FILE]
        Сгенерировать ключ активации формата BVPC-XXXX-...

  defender [--threat NAME] [--file PATH] [--severity High|Medium|Low]
        Симулировать обнаружение угрозы Defender'ом и заблокировать ПК

  config
        Показать текущую конфигурацию

  vault [list|add] [--name NAME] [--secret SECRET] [--master PASSWORD]
        Управление защищенным хранилищем

  install-defender-hook
        Создать PowerShell скрипт для авто-интеграции с Microsoft Defender

  help
        Показать эту справку

Examples:
  bvpc lock --reason ""Secret file accessed"" --source ""MyApp""
  bvpc defender --threat ""Trojan:Win32/Wacatac"" --file ""C:\\malware.exe""
  bvpc gen-key --label ""Enterprise"" --days 365 --bind-hwid
  bvpc status

Integration (any app can call):
  C#: BlockVisionClient.QuickLock(""Reason"", ""MyApp"");
  PowerShell: see docs/DefenderIntegration.md
  CLI: bvpc lock --reason ""Threat detected"" --source ""Defender""
");
    }
}
