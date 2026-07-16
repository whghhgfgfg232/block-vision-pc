<#
.SYNOPSIS
    Block Vision PC - Microsoft Defender Integration Hook
    Автоматически блокирует ПК при обнаружении угрозы Defender'ом

.DESCRIPTION
    Установите как задачу в Task Scheduler:
    Триггер: On Event - Log: Microsoft-Windows-Windows Defender/Operational, EventID 1116, 1117, 1015
    Действие: powershell.exe -ExecutionPolicy Bypass -File "C:\Path\To\Defender_BlockVision_Hook.ps1"

    Скрипт получает информацию об угрозе из Event Log и отправляет команду блокировки в Block Vision PC
    через Named Pipe. Если Block Vision не запущен, пытается запустить exe.

.NOTES
    Требует: Block Vision PC установленный и запущенный (или путь к exe)
    Совместимость: Windows 10/11 + Defender
#>

param(
    [string]$ThreatName = "",
    [string]$ThreatFile = "",
    [string]$Severity = "High",
    [string]$PipeName = "BlockVisionPC_Pipe",
    [string]$BvpcExePath = "$env:LOCALAPPDATA\BlockVisionPC\BlockVisionPC.exe"
)

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logLine = "[$timestamp] [$Level] $Message"
    Write-Host $logLine
    try {
        $logFile = Join-Path $env:LOCALAPPDATA "BlockVisionPC\defender_hook.log"
        $dir = Split-Path $logFile -Parent
        if (!(Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        Add-Content -Path $logFile -Value $logLine -ErrorAction SilentlyContinue
    } catch {}
}

Write-Log "=== Defender BlockVision Hook started ==="

# Если имя угрозы не передано как параметр, пробуем получить из последнего события Defender
if ([string]::IsNullOrWhiteSpace($ThreatName)) {
    try {
        $event = Get-WinEvent -LogName "Microsoft-Windows-Windows Defender/Operational" -MaxEvents 1 -FilterXPath "*[System[EventID=1116 or EventID=1015 or EventID=1117]]" -ErrorAction SilentlyContinue
        if ($event) {
            $xml = [xml]$event.ToXml()
            $eventData = $xml.Event.EventData.Data
            # Парсим данные события - структура зависит от версии Defender
            # Обычно: 0=Product, 1=Threat Name, 2=Severity, 3=Category, 4=File Path
            if ($eventData.Count -ge 2) {
                $ThreatName = $eventData[1].'#text'
                if ($eventData.Count -ge 5) {
                    $ThreatFile = $eventData[4].'#text'
                }
                Write-Log "Получена угроза из EventLog: $ThreatName в $ThreatFile"
            } else {
                $ThreatName = $event.Message.Split("`n")[0]
            }
        }
    } catch {
        Write-Log "Не удалось получить событие из EventLog: $_" "WARN"
    }
}

if ([string]::IsNullOrWhiteSpace($ThreatName)) {
    $ThreatName = "Unknown Defender Threat"
}

Write-Log "Угроза: $ThreatName | Файл: $ThreatFile | Серьезность: $Severity"

# Формируем команду для BlockVision
$cmd = @{
    Action = "defender_threat"
    Source = "Microsoft Defender"
    Reason = "Defender detected: $ThreatName in $ThreatFile"
    Parameters = @{
        threat = $ThreatName
        severity = $Severity
        file = $ThreatFile
    }
} | ConvertTo-Json -Compress

Write-Log "Команда: $cmd"

# Попытка 1: Named Pipe
$success = $false
try {
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $PipeName, 'InOut', 'None', 'WriteThrough')
    Write-Log "Подключение к pipe $PipeName..."
    $pipe.Connect(3000) # 3 сек таймаут
    $writer = New-Object System.IO.StreamWriter($pipe)
    $reader = New-Object System.IO.StreamReader($pipe)
    $writer.AutoFlush = $true
    $writer.WriteLine($cmd)
    Write-Log "Команда отправлена, ждем ответ..."
    $pipe.WaitForPipeDrain()
    # Читаем ответ с таймаутом
    $readTask = $reader.ReadLineAsync()
    if ($readTask.Wait(2000)) {
        $response = $readTask.Result
        Write-Log "Ответ от BlockVisionPC: $response"
        if ($response -like "*Success*true*") {
            $success = $true
        }
    } else {
        Write-Log "Таймаут ответа, но команда вероятно отправлена" "WARN"
        $success = $true # считаем успехом если pipe подключился
    }
    $writer.Dispose()
    $reader.Dispose()
    $pipe.Dispose()
} catch {
    Write-Log "Ошибка NamedPipe: $_" "ERROR"
}

# Попытка 2: HTTP API fallback
if (-not $success) {
    try {
        Write-Log "Пробуем HTTP API fallback..."
        $httpBody = @{
            source = "Microsoft Defender"
            reason = "Defender: $ThreatName"
            threat = $ThreatName
            file = $ThreatFile
        } | ConvertTo-Json

        $response = Invoke-RestMethod -Uri "http://127.0.0.1:17845/lock" -Method Post -Body $httpBody -ContentType "application/json" -TimeoutSec 3 -ErrorAction Stop
        Write-Log "HTTP API ответ: $($response | ConvertTo-Json -Compress)"
        $success = $true
    } catch {
        Write-Log "HTTP API тоже не доступен: $_" "WARN"
    }
}

# Попытка 3: Запуск exe напрямую
if (-not $success) {
    Write-Log "Пробуем запустить BlockVisionPC.exe напрямую..." "WARN"
    try {
        # Ищем exe в нескольких местах
        $possiblePaths = @(
            $BvpcExePath,
            "$env:LOCALAPPDATA\BlockVisionPC\BlockVisionPC.exe",
            "C:\Program Files\BlockVisionPC\BlockVisionPC.exe",
            (Join-Path $PSScriptRoot "BlockVisionPC.exe"),
            (Join-Path $PSScriptRoot "..\src\BlockVision.App\bin\Release\net8.0-windows\BlockVisionPC.exe")
        )

        $foundExe = $null
        foreach ($path in $possiblePaths) {
            if (Test-Path $path) {
                $foundExe = $path
                break
            }
        }

        if ($foundExe) {
            Write-Log "Найден exe: $foundExe, запускаем с --defender-threat"
            $arg = "--defender-threat `"$ThreatName in $ThreatFile`""
            Start-Process -FilePath $foundExe -ArgumentList $arg -WindowStyle Normal
            $success = $true
        } else {
            Write-Log "BlockVisionPC.exe не найден в: $($possiblePaths -join ', ')" "ERROR"
        }
    } catch {
        Write-Log "Ошибка запуска exe: $_" "ERROR"
    }
}

if ($success) {
    Write-Log "=== Успех: ПК должен быть заблокирован Block Vision PC ===" "SUCCESS"
    # Дополнительно: показать уведомление Windows
    try {
        Add-Type -AssemblyName System.Windows.Forms
        $notify = New-Object System.Windows.Forms.NotifyIcon
        $notify.Icon = [System.Drawing.SystemIcons]::Shield
        $notify.BalloonTipTitle = "Block Vision PC"
        $notify.BalloonTipText = "ПК заблокирован из-за угрозы: $ThreatName"
        $notify.Visible = $true
        $notify.ShowBalloonTip(5000)
        Start-Sleep -Seconds 6
        $notify.Dispose()
    } catch {}
} else {
    Write-Log "=== Неудача: не удалось связаться с Block Vision PC ===" "ERROR"
    Write-Log "Убедитесь что Block Vision PC установлен и запущен" "ERROR"
    exit 1
}

Write-Log "=== Hook завершен ==="
