/*
 * Block Vision PC - Пример интеграции для любых приложений (C#)
 * Скопируйте BlockVision.SDK.dll или добавьте Project Reference на BlockVision.SDK
 * Или используйте CLI через Process.Start
 */

using System;
using System.Threading.Tasks;
using BlockVision.SDK;

namespace MySecureApp.Examples
{
    class Program
    {
        static async Task Main()
        {
            // Пример 1: Самый простой - одна строка
            Console.WriteLine("Пример 1: QuickLock одной строкой");
            bool ok = BlockVisionClient.QuickLock("Тестовая блокировка из MyApp", "MySecureApp");
            Console.WriteLine($"QuickLock result: {ok}");

            // Пример 2: Через клиент с опциями
            Console.WriteLine("\nПример 2: Клиент с настройками");
            var client = new BlockVisionClient(new BlockVisionClientOptions
            {
                PipeName = "BlockVisionPC_Pipe",
                SourceAppName = "MySecureApp",
                UseHttpApi = false // true если хочешь использовать HTTP API вместо NamedPipe
            });

            // Проверка статуса
            var (locked, msg) = await client.GetStatusAsync();
            Console.WriteLine($"Status: locked={locked}, msg={msg}");

            // Блокировка с причиной
            if (!locked)
            {
                await client.LockAsync("Обнаружена попытка доступа к секретному файлу из недоверенного процесса PID 1234");
                Console.WriteLine("Lock command sent");
            }

            // Пример 3: Симуляция Microsoft Defender
            Console.WriteLine("\nПример 3: Симуляция Defender threat");
            await client.ReportThreatAsync(
                threatName: "Trojan:Win32/Wacatac.B!ml",
                severity: "Severe",
                filePath: @"C:\Users\Alice\Downloads\suspicious.exe"
            );
            Console.WriteLine("Defender threat reported");

            // Пример 4: Использование в DLP сценарии
            Console.WriteLine("\nПример 4: DLP - копирование на USB");
            SimulateDlpScenario(client);

            // Пример 5: Использование через HTTP API (для Python, Node, etc но и из C# можно)
            Console.WriteLine("\nПример 5: HTTP API");
            var httpClient = new BlockVisionClient(new BlockVisionClientOptions
            {
                UseHttpApi = true,
                HttpPort = 17845,
                ApiToken = "your_secret_token_if_enabled",
                SourceAppName = "MySecureApp-HTTP"
            });
            await httpClient.LockAsync("HTTP API lock test");

            Console.WriteLine("\nГотово! Проверь что Block Vision PC заблокировал экран.");
        }

        static void SimulateDlpScenario(BlockVisionClient client)
        {
            // Представь: твое приложение мониторит clipboard или USB
            string secretFile = @"C:\Secrets\customer_database.xlsx";
            string destination = @"E:\USB\";

            // Если кто-то пытается скопировать секрет на USB - блокируем
            bool isSensitive = secretFile.Contains("Secrets");
            bool isExternal = destination.StartsWith("E:") || destination.StartsWith("F:");

            if (isSensitive && isExternal)
            {
                Console.WriteLine($"DLP Alert: попытка скопировать {secretFile} на {destination}");
                // Блокируем ПК чтобы пользователь ввел пароль и мы залогировали инцидент
                BlockVisionClient.QuickLock(
                    $"DLP: попытка копирования секретного файла {System.IO.Path.GetFileName(secretFile)} на внешний носитель",
                    "DLP System"
                );
            }
        }
    }

    // Пример использования в WPF приложении которое хочет защитить свои данные
    public class SecureDocumentViewer
    {
        private BlockVisionClient _bvClient = new(new BlockVisionClientOptions { SourceAppName = "SecureDocViewer" });

        public async Task OpenSecretDocument(string path)
        {
            // Проверяем окружение перед открытием секретного документа
            if (IsUntrustedNetwork())
            {
                // Блокируем ПК, требуем повторную аутентификацию
                await _bvClient.LockAsync($"Попытка открытия секретного документа {path} из небезопасной сети");
                // После разблокировки пользователь должен снова ввести пароль BlockVision
                // Можно дополнительно проверить что разблокировка успешна через GetStatusAsync
                var (locked, _) = await _bvClient.GetStatusAsync();
                if (locked)
                {
                    Console.WriteLine("Пользователь не разблокировал ПК, не открываем документ");
                    return;
                }
            }

            Console.WriteLine($"Открываем секретный документ: {path}");
        }

        bool IsUntrustedNetwork() => true; // Заглушка - проверка WiFi, VPN и т.д.
    }

    // Пример антивируса
    public class MyAntivirusEngine
    {
        private BlockVisionClient _bv = new(new BlockVisionClientOptions { SourceAppName = "MyAntivirus" });

        public void OnVirusDetected(string virusName, string filePath, string severity)
        {
            // Если вирус в системной папке секретов - блокируем ПК немедленно
            if (filePath.Contains("Secrets") || severity == "Critical")
            {
                // Синхронный вызов для критичных случаев
                BlockVisionClient.QuickLock($"Антивирус: обнаружен {virusName} в {filePath}", "MyAntivirus");
            }
            else
            {
                // Асинхронно для обычных угроз
                _ = _bv.ReportThreatAsync(virusName, severity, filePath);
            }
        }
    }
}
