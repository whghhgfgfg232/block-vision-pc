"""
Block Vision PC - Python интеграция пример
Любое приложение (даже на Python) может заблокировать экран Windows

Требует: pywin32 (pip install pywin32) для NamedPipe, или можно через HTTP API без зависимостей
"""

import json
import socket
import time
import sys

# Вариант 1: Через Named Pipe (Windows, требует pywin32)
try:
    import win32file
    import win32pipe

    def lock_via_pipe(reason: str, source: str = "PythonApp", pipe_name: str = "BlockVisionPC_Pipe") -> bool:
        try:
            pipe_path = rf'\\.\pipe\{pipe_name}'
            handle = win32file.CreateFile(
                pipe_path,
                win32file.GENERIC_WRITE | win32file.GENERIC_READ,
                0, None,
                win32file.OPEN_EXISTING,
                0, None
            )
            cmd = {
                "Action": "lock",
                "Source": source,
                "Reason": reason
            }
            msg = json.dumps(cmd) + "\n"
            win32file.WriteFile(handle, msg.encode('utf-8'))
            win32file.CloseHandle(handle)
            print(f"[Pipe] Lock sent: {reason}")
            return True
        except Exception as e:
            print(f"[Pipe] Failed: {e}")
            return False

    def report_threat_via_pipe(threat: str, file: str, severity: str = "High") -> bool:
        try:
            pipe_path = r'\\.\pipe\BlockVisionPC_Pipe'
            handle = win32file.CreateFile(pipe_path, win32file.GENERIC_WRITE, 0, None, win32file.OPEN_EXISTING, 0, None)
            cmd = {
                "Action": "defender_threat",
                "Source": "Python Defender Integration",
                "Reason": f"{threat} in {file}",
                "Parameters": {"threat": threat, "file": file, "severity": severity}
            }
            win32file.WriteFile(handle, (json.dumps(cmd)+"\n").encode())
            win32file.CloseHandle(handle)
            print(f"[Pipe] Threat reported: {threat}")
            return True
        except Exception as e:
            print(f"[Pipe] Threat report failed: {e}")
            return False

except ImportError:
    print("pywin32 not installed, pipe method unavailable. Use HTTP method.")
    def lock_via_pipe(*args, **kwargs): return False
    def report_threat_via_pipe(*args, **kwargs): return False


# Вариант 2: Через HTTP API (кроссплатформенно, без зависимостей)
import http.client
import urllib.parse

def lock_via_http(reason: str, source: str = "PythonApp", port: int = 17845, token: str = "") -> bool:
    try:
        conn = http.client.HTTPConnection("127.0.0.1", port, timeout=3)
        body = json.dumps({"source": source, "reason": reason})
        headers = {"Content-Type": "application/json"}
        if token:
            headers["X-API-Token"] = token
        headers["X-Source"] = source
        conn.request("POST", "/lock", body=body, headers=headers)
        resp = conn.getresponse()
        data = resp.read().decode()
        print(f"[HTTP] Status {resp.status}: {data}")
        return 200 <= resp.status < 300
    except Exception as e:
        print(f"[HTTP] Failed: {e}")
        return False


# Вариант 3: Через CLI bvpc.exe
import subprocess
import shutil

def lock_via_cli(reason: str, source: str = "PythonApp") -> bool:
    exe = shutil.which("bvpc") or shutil.which("bvpc.exe")
    if not exe:
        # пробуем рядом с этим скриптом
        possible = ["./bvpc.exe", "../src/BlockVision.CLI/bin/Release/net8.0-windows/bvpc.exe"]
        for p in possible:
            import os
            if os.path.exists(p):
                exe = p
                break
    if not exe:
        print("[CLI] bvpc not found")
        return False
    try:
        result = subprocess.run([exe, "lock", "--reason", reason, "--source", source], capture_output=True, text=True, timeout=5)
        print(f"[CLI] {result.stdout} {result.stderr}")
        return result.returncode == 0
    except Exception as e:
        print(f"[CLI] Failed: {e}")
        return False


# Примеры использования

def example_secret_file_monitor():
    """Мониторит папку с секретами и блокирует ПК при доступе"""
    import os
    from pathlib import Path

    secret_dir = Path.home() / "Documents" / "Secrets"
    secret_dir.mkdir(exist_ok=True)

    print(f"Мониторим {secret_dir}... (создайте файл для теста)")

    # Простой мониторинг (в проде используйте watchdog)
    seen = set()
    while True:
        try:
            current = set(secret_dir.iterdir())
            new_files = current - seen
            for f in new_files:
                print(f"Обнаружен новый файл: {f.name} - блокируем ПК!")
                lock_via_pipe(f"Доступ к секретному файлу {f.name}", "Python FileGuard")
                # также пробуем HTTP как fallback
                lock_via_http(f"Доступ к {f.name}", "Python FileGuard")
            seen = current
            time.sleep(1)
        except KeyboardInterrupt:
            break


def example_ransomware_detection():
    """Симуляция обнаружения ransomware паттерна"""
    print("Симулируем ransomware: 25 быстрых переименований в защищенной папке")
    success = lock_via_pipe("Обнаружена подозрительная активность шифрования (Python simulation)", "Python Ransomware Detector")
    if not success:
        lock_via_http("Ransomware pattern detected", "Python Detector")


def example_defender_simulation():
    """Симуляция угрозы от Defender"""
    print("Симулируем угрозу от Python-антивируса")
    report_threat_via_pipe("PythonTest:Win32/EICAR", r"C:\Temp\eicar.com", "High")


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "monitor":
        example_secret_file_monitor()
    elif len(sys.argv) > 1 and sys.argv[1] == "ransomware":
        example_ransomware_detection()
    elif len(sys.argv) > 1 and sys.argv[1] == "defender":
        example_defender_simulation()
    else:
        print("""
Block Vision PC - Python Integration Example
Usage:
  python PythonIntegration.py monitor      # Мониторинг папки секретов
  python PythonIntegration.py ransomware   # Симуляция ransomware
  python PythonIntegration.py defender     # Симуляция угрозы Defender

Быстрый тест блокировки:
        """)
        lock_via_pipe("Тестовая блокировка из Python", "Python Test")
        time.sleep(0.5)
        lock_via_http("Тестовая блокировка из Python (HTTP)", "Python Test HTTP")
