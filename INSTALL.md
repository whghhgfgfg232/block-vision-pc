# Установка и развертывание Block Vision PC

## Вариант 1: Сборка из исходников

```bash
# Требования: .NET 8 SDK, Windows 10/11
git clone https://github.com/whghhgfgfg232/block-vision-pc.git
cd block-vision-pc
dotnet restore
dotnet build -c Release

# Запуск
dotnet run --project src/BlockVision.App -c Release

# CLI
dotnet run --project src/BlockVision.CLI -c Release -- lock --reason "Test"
```

## Вариант 2: Публикация single-file

```bash
dotnet publish src/BlockVision.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/app
dotnet publish src/BlockVision.CLI -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/cli

# Скопируй publish/app + publish/cli в %LOCALAPPDATA%\BlockVisionPC\
```

## Вариант 3: Инсталлятор (будет)

Планируется Inno Setup / WiX Toolset инсталлятор.

---

## Настройка автозапуска

### Через реестр (для текущего пользователя)

```powershell
$exePath = "$env:LOCALAPPDATA\BlockVisionPC\BlockVisionPC.exe"
New-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "BlockVisionPC" -Value "`"$exePath`" --minimized" -PropertyType String -Force
```

### Через Task Scheduler (с повышенными правами)

```powershell
$action = New-ScheduledTaskAction -Execute "$env:LOCALAPPDATA\BlockVisionPC\BlockVisionPC.exe" -Argument "--minimized"
$trigger = New-ScheduledTaskTrigger -AtLogon
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType S4U -RunLevel Highest
Register-ScheduledTask -TaskName "BlockVisionPC Autostart" -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Force
```

---

## Интеграция с Defender

### Шаг 1: Сгенерируй hook

```bash
bvpc install-defender-hook
# Создаст Defender_BlockVision_Hook.ps1 на рабочем столе
```

### Шаг 2: Task Scheduler

- Log: Microsoft-Windows-Windows Defender/Operational
- EventID: 1116, 1117
- Action: powershell.exe -ExecutionPolicy Bypass -File "hook.ps1"

Подробнее в `docs/DefenderIntegration.md`

---

## Безопасность

- После установки сразу установи пароль в Settings -> Security
- Включи шифрование конфига (по умолчанию DPAPI)
- Сгенерируй ключи активации и сохрани в безопасном месте
- Добавь секретные папки в File Guard

---

## Удаление

```powershell
# Остановить
taskkill /F /IM BlockVisionPC.exe
taskkill /F /IM bvpc.exe

# Удалить задачу
Unregister-ScheduledTask -TaskName "BlockVisionPC Autostart" -Confirm:$false
Unregister-ScheduledTask -TaskName "BlockVision Defender Hook" -Confirm:$false

# Удалить файлы
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\BlockVisionPC"
Remove-Item -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "BlockVisionPC" -ErrorAction SilentlyContinue
```

Конфиг и vault могут содержать секреты - используй Secure Wipe в настройках или `cipher /w` для очистки диска.
