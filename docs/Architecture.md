# Архитектура Block Vision PC

## Обзор

```
External Apps (Defender, Antivirus, DLP, Your App)
        |
        |-- Named Pipe (BlockVisionPC_Pipe)  <-- Recommended
        |-- HTTP API (127.0.0.1:17845)        <-- Cross-lang
        |-- CLI (bvpc lock)                  <-- Scripts
        v
[ IpcServer / HttpApiServer ] --> [ LockService ] --> [ LockScreenWindow ]
                                        |                     |
                                        v                     v
                                  AuditLogger           KeyboardHook (block Alt+Tab etc)
                                        |
                                        v
                              ConfigManager (DPAPI + AES)
                                        |
                              DefenderIntegration & FileGuard
```

## Поток блокировки

1. Внешнее приложение вызывает `BlockVisionClient.QuickLock(reason, source)`
2. IpcServer получает JSON по NamedPipe
3. Проверка токена (опционально)
4. LockService.Lock(reason)
5. AuditLogger пишет событие
6. LockRequested event -> LockScreenWindow.Show() на всех мониторах
7. KeyboardHook.Install() блокирует системные комбинации
8. Пользователь вводит пароль или ключ активации
9. PasswordManager.VerifyPassword() или ActivationKeyManager.ValidateKey()
10. При успехе UnlockRequested -> закрытие LockScreen, открытие Settings
11. При неудаче BruteForceProtection регистрирует попытку, shake анимация, лог

## Поток Defender

```
Windows Defender detects threat -> EventLog ID 1116
    -> Task Scheduler triggers PowerShell hook
        -> PowerShell sends defender_threat via NamedPipe
            -> LockService.TriggerByDefender(threatName)
                -> Lock + DefenderBanner
```

Альтернативный поток:

```
FileSystemWatcher (ProtectedFolders) detects
- access to .key/.pem/.kdbx
- или 20+ изменений за 5 сек (ransomware pattern)
    -> LockService.Lock(FileGuard)
```

## Шифрование

```
Config File:
  JSON -> DPAPI Protect (CurrentUser) -> .bvpc file (hidden)
       или
  JSON -> AES-GCM (PBKDF2 from master key) -> base64 .bvpc

Key Store:
  List<ActivationKey> -> JSON -> AES-GCM (user password) -> EncryptedKeyStore field in config

Vault:
  Secret -> AES-GCM (master password) -> VaultItem.EncryptedData
  List<VaultItem> -> JSON -> DPAPI Protect -> vault.bvpc file (hidden+not indexed)

Password:
  plain -> PBKDF2 SHA512 150k iters + 32 byte salt -> iterations.salt.hash
```

## HWID

```
HWID = SHA256(MachineName + ProcessorCount + OSVersion)[0..16].ToUpper()
Можно расширить WMI: Motherboard Serial + MAC + HDD Serial
```

## Мониторы

- DisplayManager.GetAllScreensBounds() через WinForms Screen.AllScreens
- MakeTopMost через SetWindowPos(HWND_TOPMOST)
- Window размером от minX,minY до maxX,maxY покрывает все экраны

## Темы

PersonalizationConfig хранит:
- Theme enum -> ресурсы в App.xaml
- BackgroundType -> SolidColorBrush / ImageBrush / Gradient
- Primary/Accent Color -> DynamicResource
- CustomLabels -> словарь локализации
- Animations -> bool флаги

В будущем: парсинг CustomCss для продвинутых (в стиле Discord BetterCSS?)
