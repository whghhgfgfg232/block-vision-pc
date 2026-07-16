# 🛡️ Block Vision PC

**Система безопасности блокировки экрана с поддержкой пароля, ключей активации и интеграцией с любыми приложениями (включая Microsoft Defender).**

> Если у вас есть секретная информация — Block Vision PC блокирует экран компьютера до ввода пароля или ключа активации. Может использоваться любыми приложениями как модуль безопасности.

---

## ✨ Возможности

### 🔒 Блокировка
- **Fullscreen на всех мониторах** — TopMost, borderless, покрывает все экраны
- **Anti-bypass** — блокировка Alt+Tab, Win клавиш, Ctrl+Esc, TaskManager через low-level hook
- **Причины блокировки**: ручная, по простою, по USB-ключу, по триггеру от приложений, по угрозе от Defender, по File Guard
- **Brute-force защита** — лимит попыток, cooldown, логи

### 🔑 Аутентификация
- **Пароль администратора** — PBKDF2-SHA512, 150k итераций, salt
- **Ключи активации** — формат `BVPC-XXXX-XXXX-XXXX-XXXX-CHECKSUM`, HMAC-SHA256, привязка к HWID, срок годности, лимит активаций
- **DPAPI + AES-256-GCM** — двойное шифрование конфига и хранилища
- **Vault** — защищенное хранилище секретов с авто-блокировкой при доступе

### 🎨 Персонализация (куча настроек)
- Темы: Dark, Light, Neon, Matrix, Cyberpunk, Defender-style, Custom
- Фон: Solid, Gradient, Image, Video, Animated Shader
- Цвета, шрифты, blur, частицы, scanline эффект
- Кастомные сообщения, логотип, звуки блокировки/разблокировки
- Часы, формат даты, анимации (fade, slide, shake)
- Язык: ru-RU / en-US

### 🔌 Интеграция — используется любыми приложениями
Любое приложение одной строкой может заблокировать ПК:

**C# SDK:**
```csharp
using BlockVision.SDK;
BlockVisionClient.QuickLock("Секретный файл под угрозой", "MyApp");
await new BlockVisionClient(new() { SourceAppName = "Defender" })
    .ReportThreatAsync("Trojan:Win32/Wacatac", "High", @"C:\malware.exe");
```

**CLI:**
```bash
bvpc lock --reason "Threat detected" --source "Defender"
bvpc defender --threat "Trojan:Win32/Wacatac" --file "C:\malware.exe"
```

**PowerShell (для Microsoft Defender):**
```powershell
$cmd = @{Action='defender_threat'; Source='Microsoft Defender'; Reason='Trojan found'} | ConvertTo-Json
$pipe = New-Object IO.Pipes.NamedPipeClientStream('.','BlockVisionPC_Pipe','InOut')
$pipe.Connect(2000); $w=New-Object IO.StreamWriter($pipe); $w.AutoFlush=$true; $w.WriteLine($cmd)
```

**HTTP API (127.0.0.1:17845):**
```http
POST /lock
X-API-Token: your_token
{"source":"MyApp","reason":"Access to secrets"}
```

**Интеграция с Microsoft Defender:**
- Task Scheduler триггер на событие Defender 1116 (Threat detected)
- PowerShell hook в `examples/Defender_BlockVision_Hook.ps1`
- File Guard — мониторинг защищенных папок, ransomware паттерн детект (множество переименований за 5 сек)

### 📊 Безопасность и Аудит
- Audit log в JSONL + Windows Event Log
- Windows EventLog Source `BlockVisionPC`
- Шифрование конфига, скрытый атрибут файлов
- Anti-tamper, self-protection

---

## 🏗️ Архитектура

```
BlockVisionPC.sln
├── src/BlockVision.Core      # Ядро: Crypto, Config, LockService, Logging
│   ├── Security/             # AES-GCM, PBKDF2, ActivationKey, Vault
│   ├── Configuration/        # AppConfig, Personalization, ConfigManager
│   ├── Locking/              # LockService, BruteForceProtection
│   ├── Integration/          # IpcServer, HttpApi, DefenderIntegration
│   └── Logging/              # AuditLogger
├── src/BlockVision.App       # WPF приложение: LockScreen + Settings
│   ├── Views/                # LockScreenWindow (fullscreen), SettingsWindow
│   ├── Services/             # KeyboardHook, DisplayManager
│   └── Assets/
├── src/BlockVision.SDK       # SDK для любых приложений
│   └── BlockVisionClient.cs  # QuickLock, ReportThreat, Status
└── src/BlockVision.CLI       # CLI bvpc.exe
    └── Program.cs
```

---

## 🚀 Быстрый старт

### Требования
- .NET 8.0 SDK
- Windows 10/11 (WPF + DPAPI + NamedPipe)

### Сборка
```bash
dotnet build BlockVisionPC.sln -c Release
```

### Запуск
```bash
# GUI с настройками и блокировкой
dotnet run --project src/BlockVision.App

# CLI
dotnet run --project src/BlockVision.CLI -- lock --reason "Manual"
dotnet run --project src/BlockVision.CLI -- gen-key --label "Enterprise" --days 365 --bind-hwid
dotnet run --project src/BlockVision.CLI -- defender --threat "EICAR-Test" --file "C:\test\eicar.com"
dotnet run --project src/BlockVision.CLI -- install-defender-hook
```

### Первый запуск
1. При первом запуске создается `config.bvpc` в `%LOCALAPPDATA%\BlockVisionPC\` (скрытый, DPAPI шифрованный)
2. Установите пароль администратора в Настройки → Безопасность
3. Сгенерируйте ключ активации: Настройки → Ключи → Генерировать
4. Настройте персонализацию, интеграции, защищенные папки
5. Тест блокировки: кнопка "Заблокировать сейчас" или `bvpc lock`

---

## 🔧 Настройки

Конфиг `%LOCALAPPDATA%\BlockVisionPC\config.bvpc` (JSON, шифрованный):

```json
{
  "personalization": {
    "theme": "Dark",
    "primaryColor": "#6C5CE7",
    "welcomeMessage": "Система защищена Block Vision PC",
    "backgroundType": "Gradient"
  },
  "locking": {
    "lockOnIdle": true,
    "idleMinutes": 5,
    "blockAltTab": true,
    "coverAllMonitors": true,
    "failedAttemptsBeforeLockout": 5
  },
  "integration": {
    "enableIpcServer": true,
    "ipcPipeName": "BlockVisionPC_Pipe",
    "defenderIntegrationEnabled": true,
    "defenderLockOnThreat": true,
    "protectedFolders": [{"Path": "%USERPROFILE%\\Documents\\Secrets"}]
  }
}
```

---

## 🛡️ Интеграция с Microsoft Defender

### Автоматическая (рекомендуется)
1. Запустите `bvpc install-defender-hook` — создаст `Defender_BlockVision_Hook.ps1` на рабочем столе
2. Откройте Task Scheduler → Create Task
3. Trigger: On an event → Log: `Microsoft-Windows-Windows Defender/Operational`, Source: `Windows Defender`, Event ID: `1116`
4. Action: Start program `powershell.exe` with args `-ExecutionPolicy Bypass -File "C:\...\Defender_BlockVision_Hook.ps1"`

Теперь при любом обнаружении угрозы Defender'ом — ПК автоматически блокируется!

### Ручная через Defender Custom Action
Добавьте в Defender exclusion ? Нет — используйте наш SDK в своем антивирусе.

### Через наше File Guard
Добавьте папки с секретами в Настройки → Интеграции → Защищенные папки. При доступе/изменении `.key, .pem, .kdbx` — авто-блок.

---

## 🔑 Формат ключа активации

```
BVPC-ABCD-1234-EFGH-5678-WXYZ
|    |    |    |    |    └─ HMAC checksum (4 символа)
|    └────┴────┴────┴────── 16 символов entropy Base32
└─ Префикс Block Vision PC
```

Валидация:
- Regex + HMAC-SHA256 checksum
- Срок годности
- Привязка к HWID = SHA256(MachineName+CPU+OS)[0..16]
- Лимит активаций
- Revoke список

Генерация:
```csharp
var manager = new ActivationKeyManager(masterSecret);
var key = manager.GenerateKey(TimeSpan.FromDays(365), hwid, "Enterprise", 10);
```

---

## 📁 Хранилище Vault

- Шифрование AES-GCM с мастер-паролем
- DPAPI слой для файла
- Скрытый + NotContentIndexed
- Secure wipe (перезапись случайными данными перед удалением)
- Требует блокировку экрана при доступе (опционально)

---

## 📜 Логи

- `%LOCALAPPDATA%\BlockVisionPC\logs\audit_YYYY-MM-DD.jsonl`
- Windows Event Log: `Application` → Source `BlockVisionPC`
- Экспорт в JSON через UI

Типы событий: Lock, Unlock, UnlockFailed, LockoutStarted, DefenderThreatDetected, FileGuardTriggered, TamperDetected и т.д.

---

## 🧩 Примеры

В папке `examples/`:

- `DefenderIntegration.ps1` — PowerShell пример
- `CSharpIntegrationExample.cs` — C# пример для любых приложений
- `RansomwareSimulation.py` — симуляция ransomware для теста File Guard

---

## 🔐 Безопасность

- Никогда не храним пароли в открытом виде — только PBKDF2 хеш
- Конфиг шифруется DPAPI (привязка к пользователю) или AES-GCM
- Vault — двойное шифрование
- Защита от отладки, anti-tamper (опционально)
- Brute-force protection с exponential backoff
- Код открыт для аудита

---

## 📄 Лицензия

MIT — используйте свободно, но с атрибуцией.

---

## 🤝 Контрибьютинг

PR welcome! Особенно для:
- Поддержка TPM
- Linux/Mac (Avalonia версия)
- Biometric unlock (Windows Hello)
- Smart-card / USB key
- Облачная синхронизация ключей

---

Сделано с ❤️ для защиты секретной информации. Если Defender находит угрозу — Block Vision PC уже заблокировал экран.
