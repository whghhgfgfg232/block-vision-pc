# Политика безопасности Block Vision PC

## Поддерживаемые версии

| Версия | Поддерживается |
|--------|---------------|
| 1.0.x  | ✅            |
| <1.0   | ❌            |

## Модель угроз

Block Vision PC защищает от:

- Несанкционированный доступ к ПК когда пользователь отошел (idle lock)
- Просмотр секретов посторонними (fullscreen lock)
- Кража данных через USB, clipboard (DLP интеграция)
- Ransomware шифрование секретов (File Guard)
- Вредоносное ПО (Defender интеграция)
- Brute-force (cooldown, audit log)

Не защищает от (out of scope для 1.0):

- Физический доступ с Live USB (нужен BitLocker)
- DMA атаки (требуется Kernel DMA Protection)
- Cold boot атаки (требуется TPM + PIN)

## Криптография

- **Пароли**: PBKDF2-SHA512, 150k итераций, 32 byte salt, 64 byte hash, Constant-time compare
- **Конфиг**: DPAPI CurrentUser или AES-256-GCM с PBKDF2 ключом
- **Ключи активации**: HMAC-SHA256 checksum, Base32 entropy 12 bytes
- **Vault**: AES-256-GCM + DPAPI двойной слой, hidden file attribute
- **Secure Wipe**: Random overwrite перед удалением

## Сообщить об уязвимости

Если нашли уязвимость безопасности:

1. **НЕ** создавайте публичный Issue
2. Отправьте email: security@blockvision.local (заглушка, в проде замените на реальный)
3. Опишите: версия, шаги воспроизведения, impact
4. Мы ответим в течение 48 часов

### Что считается уязвимостью

- Obход экрана блокировки без пароля/ключа
- Чтение конфига без DPAPI/ключа
- Повышение привилегий через IPC без токена
- Bypass brute-force protection
- RCE через IPC/HTTP API

### Что НЕ считается

- Возможность выключить ПК кнопкой питания (физический доступ)
- Alt+Tab блокировка не работает в некоторых играх (fullscreen exclusive - это особенность Windows, не баг)
- Отсутствие защиты от live USB (требуется BitLocker)

## Hardening рекомендации

Для максимальной защиты:

1. Включи BitLocker + TPM + PIN
2. Включи Secure Boot + HVCI (Core Isolation)
3. Запускай BlockVision как службу с Protected Process (требует драйвера)
4. Включи токен для IPC и HTTP API
5. Используй HWID-bound ключи
6. Настрой File Guard на все секретные папки
7. Включи аудит логов в Windows EventLog + SIEM

## Зависимости

- Newtonsoft.Json 13.0.3 (проверено на CVE)
- System.Security.Cryptography.ProtectedData 8.0.0
- Hardcodet.NotifyIcon.Wpf 1.1.0

Регулярно обновляй `dotnet list package --vulnerable`

## Лицензия ключей

Не делись master_secret из ActivationKeyManager! Он должен генерироваться случайно при установке и храниться в DPAPI/TMP.

Если master_secret скомпрометирован, все ключи можно подделать. Ротация: сгенерируй новый master_secret и перевыпусти ключи.
