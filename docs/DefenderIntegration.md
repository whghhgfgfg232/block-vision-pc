# Интеграция с Microsoft Defender и любыми приложениями

Block Vision PC создавался как универсальный модуль безопасности. Любое приложение может заблокировать ПК, когда считает, что секретная информация под угрозой.

## Быстрый старт - 1 строка

### C#

```csharp
// Добавь BlockVision.SDK NuGet (или Project Reference)
// using BlockVision.SDK;

BlockVisionClient.QuickLock("Обнаружена угроза!", "MyApp");
```

### PowerShell (Defender Custom Action)

```powershell
$cmd = @{Action='lock'; Source='Microsoft Defender'; Reason='Trojan:Win32/Wacatac'} | ConvertTo-Json -Compress
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'BlockVisionPC_Pipe', 'InOut')
$pipe.Connect(2000)
$w = New-Object System.IO.StreamWriter($pipe)
$w.AutoFlush=$true
$w.WriteLine($cmd)
```

### CLI / Batch / Python

```bash
bvpc lock --reason "Ransomware detected" --source "MyAV"

# Python
import json, win32file, win32pipe
pipe = win32file.CreateFile(r'\\.\pipe\BlockVisionPC_Pipe', win32file.GENERIC_WRITE, 0, None, win32file.OPEN_EXISTING, 0, None)
msg = json.dumps({"Action":"lock","Source":"PythonApp","Reason":"Secret file accessed"})
win32file.WriteFile(pipe, (msg+"\n").encode())
```

### HTTP API

```bash
curl -X POST http://127.0.0.1:17845/lock -H "X-API-Token: yourtoken" -H "Content-Type: application/json" -d '{"source":"MyApp","reason":"Threat"}'
```

---

## Microsoft Defender - автоматическая блокировка при угрозе

### Способ 1: Task Scheduler (рекомендуется)

1. Запустите в BlockVision:

```bash
bvpc install-defender-hook
# Создаст файл Defender_BlockVision_Hook.ps1 на рабочем столе
```

2. Откройте Task Scheduler (`taskschd.msc`)

3. Create Task:
   - Name: `BlockVision Defender Hook`
   - Trigger: On an event
     - Log: `Microsoft-Windows-Windows Defender/Operational`
     - Source: `Microsoft-Windows-Windows Defender`
     - Event ID: `1116` (Threat detected) и `1117` (Action taken) - можно добавить оба
   - Action: Start program
     - Program: `powershell.exe`
     - Arguments: `-ExecutionPolicy Bypass -File "C:\Users\You\Desktop\Defender_BlockVision_Hook.ps1"`
   - Conditions: Uncheck "Start only if on AC"
   - Settings: Allow to run on demand

4. Готово! Теперь при любом обнаружении угрозы Defender заблокирует ПК.

### Способ 2: Вручную через Defender Exclusion? Нет, через API

Если пишете свой антивирус или DLP, который мониторит логи Defender:

```csharp
// Мониторинг EventLog
var query = new EventLogQuery("Microsoft-Windows-Windows Defender/Operational", PathType.LogName, "*[System[EventID=1116]]");
using var watcher = new EventLogWatcher(query);
watcher.EventRecordWritten += (s, e) =>
{
    var threat = e.EventRecord?.Properties[1].Value?.ToString() ?? "Unknown";
    BlockVisionClient.QuickLock($"Defender: {threat}", "DefenderMonitor");
};
watcher.Enabled = true;
```

### Способ 3: Через File Guard (встроено в BlockVision)

BlockVision уже имеет встроенный File Guard, который мониторит защищенные папки:

- В Settings -> Integration -> Protected Folders добавьте `C:\Users\...\Documents\Secrets`
- Включите `LockOnRansomwarePattern` — если за 5 сек >20 изменений файлов, сработает блокировка (защита от шифровальщиков)

---

## Любое приложение как триггер

### Примеры сценариев

| Приложение | Когда блокировать | Пример |
|------------|-------------------|--------|
| **Microsoft Defender** | Threat detected | Trojan, Ransomware |
| **DLP система** | Попытка копирования секретов на USB | FileGuard |
| **Ваше приложение** | Пользователь открыл секретный документ из небезопасной сети | `QuickLock("Confidential accessed from public WiFi")` |
| **Антивирус** | PUP, Keylogger detected | `ReportThreatAsync("Keylogger", "High")` |
| **Корпоративный MDM** | Device не compliant | `client.LockAsync("Device non-compliant")` |

### Уровень доступа

- IPC Server по умолчанию без токена (только localhostNamedPipe, безопасно)
- Если нужен токен: Settings -> Integration -> API Token -> установите случайный GUID
- HTTP API требует токен обязательно, если включен

### Формат команды IPC (JSON over NamedPipe)

```json
{
  "Action": "lock|defender_threat|status|ping|unlock",
  "Source": "MyApp",
  "Reason": "Optional reason",
  "Token": "optional",
  "Parameters": {
    "threat": "Trojan:Win32/...",
    "severity": "High",
    "file": "C:\\path"
  }
}
```

Ответ:

```json
{
  "Success": true,
  "Message": "Lock triggered",
  "Data": null
}
```

---

## Безопасность интеграции

- NamedPipe `BlockVisionPC_Pipe` доступен только локально, ACL = CurrentUser
- Токен хранится в DPAPI шифрованном конфиге
- Логирование всех IPC команд в AuditLogger
- Unlock по IPC требует пароль в Parameters и валидный токен (защита от разблокировки извне)

Если внешнее приложение скомпрометировано и спамит lock — включите токен и BruteForceProtection также распространяется на IPC?

---

## FAQ

**Q: Defender уже блокирует файл, зачем блокировать ПК?**
A: Если угроза в секретной папке или keylogger, лучше заблокировать экран до ввода пароля, чтобы злоумышленник не увидел секреты.

**Q: Может ли Defender разблокировать?**
A: Нет по умолчанию. Unlock через IPC требует пароль. Только пользователь с паролем/ключом может разблокировать.

**Q: Что если BlockVision не запущен?**
A: PowerShell hook имеет fallback: запускает `BlockVisionPC.exe --defender-threat "ThreatName"`

**Q: Работает ли с другими антивирусами?**
A: Да, с любыми! ESET, Kaspersky, Bitdefender — любой может вызвать `bvpc lock` или использовать SDK.

**Q: Требует ли админ прав?**
A: Для KeyboardHook блокировки Win/Alt+Tab не требует админа, но для защиты от завершения процесса и firewall правил — желательно запускать как админ или служба.
