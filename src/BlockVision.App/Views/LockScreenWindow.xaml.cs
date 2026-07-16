using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BlockVision.App.Services;
using BlockVision.Core.Configuration;
using BlockVision.Core.Locking;
using BlockVision.Core.Logging;
using BlockVision.Core.Security;

// Fix WPF vs WinForms ambiguity (both UseWPF and UseWindowsForms enabled)
using System.Windows.Input;
using Timer = System.Threading.Timer;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Cursors = System.Windows.Input.Cursors;
using WpfMessageBox = System.Windows.MessageBox;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace BlockVision.App.Views;

public partial class LockScreenWindow : Window
{
    private readonly KeyboardHook _keyboardHook;
    private readonly Timer _clockTimer;
    private readonly ConfigManager _config;
    private readonly LockService _lockService;
    private readonly AuditLogger _logger;
    private bool _isUnlocking = false;

    public LockScreenWindow()
    {
        InitializeComponent();

        _config = App.ConfigManager!;
        _lockService = App.LockService!;
        _logger = App.Logger!;
        _keyboardHook = new KeyboardHook
        {
            BlockAltTab = _config.Config.Locking.BlockAltTab,
            BlockWindowsKeys = _config.Config.Locking.BlockWindowsKeys,
            BlockCtrlEsc = true
        };

        // Подписка на события
        _lockService.LockRequested += OnLockRequested;
        _lockService.UnlockRequested += OnUnlockRequested;
        _lockService.FailedAttempt += OnFailedAttempt;

        // Таймер часов
        _clockTimer = new Timer(_ => Dispatcher.Invoke(UpdateClock), null, 0, 1000);

        Loaded += (_, _) => ApplyPersonalization();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Сделать во весь экран на всех мониторах
        if (_config.Config.Locking.CoverAllMonitors)
        {
            var handle = new WindowInteropHelper(this).Handle;
            DisplayManager.MakeTopMost(handle);

            // Растягиваем окно на все мониторы
            var screens = DisplayManager.GetAllScreensBounds();
            if (screens.Count > 1)
            {
                var minX = screens.Min(s => s.X);
                var minY = screens.Min(s => s.Y);
                var maxX = screens.Max(s => s.Right);
                var maxY = screens.Max(s => s.Bottom);
                Left = minX;
                Top = minY;
                Width = maxX - minX;
                Height = maxY - minY;
            }
        }

        // Блокировка клавиатуры - только если не первый запуск без пароля
        if (!string.IsNullOrWhiteSpace(_config.Config.AdminPasswordHash))
        {
            try { _keyboardHook.Install(); } catch { }
        }

        // Фокус - КРИТИЧНО для ввода с клавиатуры
        // Используем Dispatcher с приоритетом Input чтобы гарантировать фокус после всех Loaded событий
        Dispatcher.BeginInvoke(new Action(() =>
        {
            FocusPasswordBox();
        }), System.Windows.Threading.DispatcherPriority.Input);

        UpdateClock();
        UpdatePlaceholders();
        HardwareIdText.Text = $"ID: {CryptoHelper.GetHardwareId()}";

        // Анимация появления
        if (FindResource("FadeIn") is Storyboard sb) sb.Begin(this);

        // Если есть причина блокировки из LockService
        if (_lockService.IsLocked)
        {
            LockReasonText.Text = "Экран заблокирован системой безопасности";
        }
        else if (string.IsNullOrWhiteSpace(_config.Config.AdminPasswordHash))
        {
            // Первый запуск без пароля - подсказка
            LockReasonText.Text = "Первый запуск: установите пароль в настройках";
            ShowMessage("Пароль не установлен. Нажмите 'Настройки' внизу и задайте пароль администратора.", false);
        }

        // Курсор
        if (_config.Config.Locking.HideCursor)
            Cursor = Cursors.None;
    }

    private void FocusPasswordBox()
    {
        try
        {
            PasswordBox.Focusable = true;
            PasswordBox.IsEnabled = true;
            PasswordBox.Focus();
            Keyboard.Focus(PasswordBox);
            PasswordBox.CaretIndex = PasswordBox.Text.Length;
            PasswordBox.SelectAll();
        }
        catch { }
    }

    private void UpdatePlaceholders()
    {
        PasswordPlaceholder.Visibility = string.IsNullOrEmpty(PasswordBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        KeyPlaceholder.Visibility = string.IsNullOrEmpty(ActivationKeyBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RootGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Клик по фону - фокусируем пароль
        if (e.OriginalSource is Grid || e.OriginalSource is Border || e.OriginalSource is StackPanel)
        {
            FocusPasswordBox();
        }
    }

    private void PasswordBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Разрешаем фокус по клику - ИСПРАВЛЕНО: не блокируем если уже в фокусе, позволяем выделить текст
        if (!PasswordBox.IsKeyboardFocused)
        {
            e.Handled = true;
            FocusPasswordBox();
        }
    }

    private void ActivationKeyBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Аналогично для поля ключа активации - позволяем фокус
        if (!ActivationKeyBox.IsKeyboardFocused)
        {
            e.Handled = true;
            FocusActivationKeyBox();
        }
    }

    private void PasswordBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholders();
    }

    private void ActivationKeyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholders();
    }

    private void ActivationKeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        UpdatePlaceholders();
    }

    private void ActivationKeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        UpdatePlaceholders();
    }

    private void PasswordBox_LostFocus(object sender, RoutedEventArgs e)
    {
        UpdatePlaceholders();
        // ИСПРАВЛЕНО: не форсируем фокус обратно, если пользователь пытается перейти в поле ключа активации или на кнопку
        // Старая логика вызывала дикое раздражение - невозможно было ввести ключ активации
        var focused = Keyboard.FocusedElement as DependencyObject;
        if (focused == ActivationKeyBox || focused == UnlockButton || IsAncestorOf(ActivationKeyBox, focused) || IsAncestorOf(UnlockButton, focused))
        {
            // Пользователь перешел в поле ключа или кнопку - разрешаем
            return;
        }

        // Если фокус ушел в никуда (например клик по пустому месту) и экран заблокирован - возвращаем в пароль через небольшую задержку
        // Но только если оба поля пустые или фокус не в ключе
        if (_lockService.IsLocked && !_isUnlocking)
        {
            if (string.IsNullOrWhiteSpace(PasswordBox.Text) && string.IsNullOrWhiteSpace(ActivationKeyBox.Text))
            {
                // Если оба пустые и фокус потерян полностью - вернем в пароль
                if (focused == null || (focused != PasswordBox && focused != ActivationKeyBox))
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // Проверяем еще раз что пользователь не перешел в ключ
                        if (!ActivationKeyBox.IsKeyboardFocused && !UnlockButton.IsKeyboardFocused)
                            FocusPasswordBox();
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }
    }

    private static bool IsAncestorOf(DependencyObject? parent, DependencyObject? child)
    {
        if (parent == null || child == null) return false;
        var current = child;
        while (current != null)
        {
            if (current == parent) return true;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void FocusActivationKeyBox()
    {
        try
        {
            ActivationKeyBox.Focusable = true;
            ActivationKeyBox.IsEnabled = true;
            ActivationKeyBox.Focus();
            Keyboard.Focus(ActivationKeyBox);
            ActivationKeyBox.CaretIndex = ActivationKeyBox.Text.Length;
        }
        catch { }
    }

    private void ApplyPersonalization()
    {
        var p = _config.Config.Personalization;
        WelcomeText.Text = p.WelcomeMessage;
        LockReasonText.Text = p.LockMessage;

        PasswordPlaceholder.Text = p.CustomLabels.GetValueOrDefault("PasswordPlaceholder", "Введите пароль");
        KeyPlaceholder.Text = p.CustomLabels.GetValueOrDefault("KeyPlaceholder", "BVPC-XXXX-XXXX-XXXX-XXXX");
        UnlockButton.Content = p.CustomLabels.GetValueOrDefault("UnlockButton", "Разблокировать");

        // Цвета - используем MediaColor чтобы избежать конфликта с System.Drawing.Color
        try
        {
            var primary = (MediaColor)MediaColorConverter.ConvertFromString(p.PrimaryColor);
            var accent = (MediaColor)MediaColorConverter.ConvertFromString(p.AccentColor);
            Resources["PrimaryBrush"] = new MediaSolidColorBrush(primary);
            Resources["AccentBrush"] = new MediaSolidColorBrush(accent);
        }
        catch { }

        // Фон
        if (p.BackgroundType == BackgroundType.SolidColor)
        {
            try
            {
                BackgroundGrid.Background = new MediaSolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(p.BackgroundValue));
            }
            catch { }
        }
        else if (p.BackgroundType == BackgroundType.Image && !string.IsNullOrEmpty(p.CustomWallpaperPath) && System.IO.File.Exists(p.CustomWallpaperPath))
        {
            try
            {
                var img = new ImageBrush(new System.Windows.Media.Imaging.BitmapImage(new Uri(p.CustomWallpaperPath)));
                img.Stretch = Stretch.UniformToFill;
                img.Opacity = p.BackgroundOpacity;
                BackgroundGrid.Background = img;
            }
            catch { }
        }

        BlurOverlay.Opacity = p.BlurBackground ? 0.7 : 0.4;
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        var fmtTime = _config.Config.Personalization.TimeFormat;
        var fmtDate = _config.Config.Personalization.DateFormat;

        try
        {
            TimeText.Text = now.ToString(fmtTime);
            DateText.Text = now.ToString(fmtDate);
        }
        catch
        {
            TimeText.Text = now.ToString("HH:mm:ss");
            DateText.Text = now.ToString("dd MMMM yyyy");
        }

        TimeText.Visibility = _config.Config.Locking.ShowClock ? Visibility.Visible : Visibility.Collapsed;
        DateText.Visibility = _config.Config.Locking.ShowClock ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnLockRequested(object? sender, LockEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            LockReasonText.Text = e.Message;
            
            if (e.Reason == LockReason.Defender || e.Reason == LockReason.FileGuard)
            {
                ThreatInfoBorder.Visibility = Visibility.Visible;
                ThreatDetailsText.Text = $"{e.TriggeredBy}: {e.Message}";
                
                DefenderBanner.Visibility = Visibility.Visible;
                DefenderBannerText.Text = e.Message;
            }

            Show();
            Activate();
            Topmost = true;
            PasswordBox.Focus();

            // Звук блокировки
            if (_config.Config.Locking.PlaySoundOnLock)
            {
                try { System.Media.SystemSounds.Hand.Play(); } catch { }
            }
        });
    }

    private void OnUnlockRequested(object? sender, UnlockEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            SuccessBorder.Visibility = Visibility.Visible;
            SuccessText.Text = $"✓ Доступ разрешен через {(e.ViaPassword ? "пароль" : "ключ активации")}. Длительность блокировки: {e.LockDuration:mm\\:ss}";
            MessageBorder.Visibility = Visibility.Collapsed;

            _keyboardHook.Uninstall();
            _clockTimer.Dispose();

            // Задержка для красоты
            Task.Delay(800).ContinueWith(_ => Dispatcher.Invoke(() =>
            {
                Hide();
                // Не закрываем полностью - оставляем в трее
                var mainWin = new SettingsWindow();
                mainWin.Show();
                Close();
            }));
        });
    }

    private void OnFailedAttempt(int attemptsLeft, TimeSpan lockout)
    {
        Dispatcher.Invoke(() =>
        {
            if (lockout > TimeSpan.Zero)
            {
                ShowMessage($"Слишком много неудачных попыток. Повторите через {lockout:mm\\:ss}", true);
            }
            else
            {
                ShowMessage($"Неверный пароль или ключ", true);
                AttemptsPanel.Visibility = _config.Config.Locking.ShowFailedAttempts ? Visibility.Visible : Visibility.Collapsed;
                AttemptsText.Text = $"{_config.Config.Personalization.CustomLabels.GetValueOrDefault("AttemptsLeft", "Попыток осталось")}: {attemptsLeft}";

                // Shake анимация
                if (_config.Config.Personalization.Animations.EnableShakeOnError && FindResource("Shake") is Storyboard shake)
                {
                    shake.Begin(MainPanel);
                }
            }
        });
    }

    private async void UnlockButton_Click(object sender, RoutedEventArgs e) => await TryUnlockAsync();

    private async void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        PasswordPlaceholder.Visibility = string.IsNullOrEmpty(PasswordBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (e.Key == Key.Enter) await TryUnlockAsync();
    }

    private async void ActivationKeyBox_KeyDown(object sender, KeyEventArgs e)
    {
        KeyPlaceholder.Visibility = string.IsNullOrEmpty(ActivationKeyBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (e.Key == Key.Enter) await TryUnlockAsync();
    }

    private void PasswordBox_GotFocus(object sender, RoutedEventArgs e)
    {
        PasswordPlaceholder.Visibility = string.IsNullOrEmpty(PasswordBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task TryUnlockAsync()
    {
        if (_isUnlocking) return;
        _isUnlocking = true;

        LoadingGrid.Visibility = Visibility.Visible;
        UnlockButton.IsEnabled = false;

        try
        {
            string passwordInput = PasswordBox.Text.Trim();
            string keyInput = ActivationKeyBox.Text.Trim();

            // Определяем какое поле в фокусе чтобы понять что пользователь хочет использовать
            bool isKeyFocused = ActivationKeyBox.IsKeyboardFocused;
            bool isPasswordFocused = PasswordBox.IsKeyboardFocused;

            // Собираем список попыток в порядке приоритета: сначала то поле где фокус, потом остальное
            var attempts = new List<string>();
            if (isKeyFocused && !string.IsNullOrWhiteSpace(keyInput))
                attempts.Add(keyInput);
            if (isPasswordFocused && !string.IsNullOrWhiteSpace(passwordInput))
                attempts.Add(passwordInput);

            // Добавляем остальные непустые если еще не добавлены
            if (!attempts.Contains(passwordInput) && !string.IsNullOrWhiteSpace(passwordInput))
                attempts.Add(passwordInput);
            if (!attempts.Contains(keyInput) && !string.IsNullOrWhiteSpace(keyInput))
                attempts.Add(keyInput);

            if (attempts.Count == 0)
            {
                ShowMessage("Введите пароль или ключ активации", true);
                return;
            }

            // Имитация задержки для защиты от брутфорса
            await Task.Delay(300);

            bool anySuccess = false;
            string lastMessage = "";

            foreach (var input in attempts)
            {
                var (success, message) = _lockService.TryUnlock(input);
                lastMessage = message;
                if (success)
                {
                    anySuccess = true;
                    break;
                }
            }

            if (!anySuccess)
            {
                ShowMessage(lastMessage, true);
                // Очищаем только неверное поле, оставляем другое
                // Если была попытка пароля и она не удалась, но ключ тоже был введен и не удался - очищаем оба
                // Если вводили только ключ - очищаем только ключ, чтобы можно было исправить опечатку? Нет, лучше не очищать ключ полностью, а показать ошибку
                // Для удобства: если вводили пароль - очищаем пароль, если только ключ - не очищаем чтобы исправить, а подсвечиваем
                if (!string.IsNullOrWhiteSpace(passwordInput) && attempts.Contains(passwordInput))
                    PasswordBox.Clear();

                // Фокусируем обратно в поле где была ошибка
                if (isKeyFocused)
                    FocusActivationKeyBox();
                else
                    FocusPasswordBox();
            }
            else
            {
                // Успех обрабатывается через событие OnUnlockRequested
            }
        }
        finally
        {
            LoadingGrid.Visibility = Visibility.Collapsed;
            UnlockButton.IsEnabled = true;
            _isUnlocking = false;
        }
    }

    private void ShowMessage(string msg, bool isError)
    {
        if (isError)
        {
            MessageBorder.Visibility = Visibility.Visible;
            MessageText.Text = msg;
            SuccessBorder.Visibility = Visibility.Collapsed;
        }
        else
        {
            SuccessBorder.Visibility = Visibility.Visible;
            SuccessText.Text = msg;
            MessageBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Блокируем Alt+F4 если настроено, иначе разрешаем только с паролем админа
        if (e.Key == Key.F4 && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            if (_config.Config.Locking.BlockAltTab) e.Handled = true;
        }

        if (e.Key == Key.Escape)
        {
            // Esc не разблокирует, только очищает поля
            PasswordBox.Clear();
            ActivationKeyBox.Clear();
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Нельзя закрыть пока заблокировано (защита)
        if (_lockService.IsLocked && !_isUnlocking)
        {
            e.Cancel = true;
            ShowMessage("Экран заблокирован. Введите пароль для выхода.", true);
        }
        else
        {
            _keyboardHook.Dispose();
            _clockTimer.Dispose();
        }
    }

    private void Settings_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Открыть настройки только после ввода пароля? Для демо открываем, но требуем повторный пароль внутри
        var settings = new SettingsWindow();
        settings.ShowDialog();
    }
}
