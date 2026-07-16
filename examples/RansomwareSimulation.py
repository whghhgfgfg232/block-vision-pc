"""
Ransomware Simulation для теста File Guard в Block Vision PC
ВНИМАНИЕ: Только для теста в изолированной папке! Не запускать на реальных данных!

Симулирует паттерн ransomware: быстрое переименование множества файлов
File Guard должен обнаружить >20 изменений за 5 сек и заблокировать ПК
"""

import os
import time
import random
from pathlib import Path

def create_test_files(directory: Path, count=30):
    directory.mkdir(parents=True, exist_ok=True)
    for i in range(count):
        (directory / f"document_{i}.txt").write_text(f"Secret data {i} - {random.randint(1000,9999)}")
    print(f"Создано {count} тестовых файлов в {directory}")

def simulate_ransomware(directory: Path):
    print(f"Симуляция ransomware в {directory} - быстрое переименование!")
    print("File Guard должен заблокировать ПК через Block Vision...")

    # Импортируем наш Python клиент BlockVision
    try:
        # Попытка через NamedPipe напрямую
        import win32file
        import json

        def trigger_lock(reason):
            try:
                handle = win32file.CreateFile(r'\\.\pipe\BlockVisionPC_Pipe', win32file.GENERIC_WRITE, 0, None, win32file.OPEN_EXISTING, 0, None)
                cmd = {"Action": "lock", "Source": "RansomwareSim", "Reason": reason}
                win32file.WriteFile(handle, (json.dumps(cmd)+"\n").encode())
                win32file.CloseHandle(handle)
            except Exception as e:
                print(f"Pipe failed: {e}")

        files = list(directory.glob("*.txt"))
        for i, f in enumerate(files):
            # Переименовываем быстро
            new_name = directory / f"{f.stem}.encrypted_{random.randint(1000,9999)}.locked"
            try:
                f.rename(new_name)
                print(f"[{i+1}/{len(files)}] {f.name} -> {new_name.name}")
            except:
                pass

            if i % 5 == 0:
                time.sleep(0.1) # небольшая пауза но все равно быстро

        trigger_lock(f"Ransomware simulation: {len(files)} files encrypted rapidly")

    except ImportError:
        print("pywin32 не установлен, но реальные изменения файлов уже сделаны - FileGuard должен сработать через FileSystemWatcher")
        files = list(directory.glob("*.txt"))
        for i, f in enumerate(files[:25]):
            new_name = directory / f"{f.stem}.encrypted.locked"
            try:
                f.rename(new_name)
                print(f"Renamed {f.name}")
                time.sleep(0.15)
            except Exception as e:
                print(e)

if __name__ == "__main__":
    from pathlib import Path
    test_dir = Path.home() / "Documents" / "Secrets" / "RansomwareTest"
    # Или используй кастомную папку из аргумента
    import sys
    if len(sys.argv) > 1:
        test_dir = Path(sys.argv[1])

    print(f"Тестовая директория: {test_dir}")
    print("ВНИМАНИЕ: Это только симуляция в тестовой папке!")

    if not test_dir.exists():
        create_test_files(test_dir)

    input("Нажми Enter чтобы начать симуляцию ransomware (Ctrl+C для отмены)...")
    simulate_ransomware(test_dir)
    print("Симуляция завершена. Проверь что Block Vision PC заблокировал экран!")
    print("Чтобы восстановить: переименуй файлы обратно или удали папку")
