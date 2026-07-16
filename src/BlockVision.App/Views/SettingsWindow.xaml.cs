using System.Windows;
using System.Windows.Controls;
using BlockVision.Core.Configuration;
using BlockVision.Core.Security;

namespace BlockVision.App.Views;

public partial class SettingsWindow : Window
{
    private readonly ConfigManager _config;
    private List<ActivationKey> _keys = new();
    private SecureVault? _vault;

    public SettingsWindow()
    {
        InitializeComponent();
        _config = App.ConfigManager!;
        LoadConfigToUi();

        HardwareIdLabel.Text = $"HWID: {CryptoHelper.GetHardwareId()}";
        StatusDot.Fill = App.LockService?.IsLocked == true ? System.Windows.Media.Brushes.Red : System.Windows.Media.Brushes.Green;
        StatusText.Text = App.LockService?.IsLocked == true ? "Заблокировано" : "Защита активна";

        // Загрузка ключей
        LoadKeys();

        // Vault
        try
        {
            var vaultPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlockVisionPC", "vault.bvpc");
            _vault = new SecureVault(vaultPath, "master_pass_placeholder");
            _vault.Load();
            RefreshVault();
        }
        catch { }

        RefreshLogs();
    }

    private void LoadConfigToUi()
    {
        var c = _config.Config;

        // Security
        EncryptConfigCheck.IsChecked = c.Security.EncryptConfigFile;
        AntiTamperCheck.IsChecked = c.Security.EnableTamperDetection;
        SelfProtectionCheck.IsChecked = c.Security.EnableSelfProtection;
        MinLengthBox.Text = c.Security.PasswordMinLength.ToString();

        // Personalization
        WelcomeMsgBox.Text = c.Personalization.WelcomeMessage;
        LockMsgBox.Text = c.Personalization.LockMessage;
        PrimaryColorBox.Text = c.Personalization.PrimaryColor;
        AccentColorBox.Text = c.Personalization.AccentColor;
        BlurCheck.IsChecked = c.Personalization.BlurBackground;
        ParticlesCheck.IsChecked = c.Personalization.EnableParticles;
        ShowClockCheck.IsChecked = c.Locking.ShowClock;

        // Locking
        LockOnStartupCheck.IsChecked = c.Locking.LockOnStartup;
        LockOnIdleCheck.IsChecked = c.Locking.LockOnIdle;
        IdleMinutesBox.Text = c.Locking.IdleMinutes.ToString();
        BlockTaskManagerCheck.IsChecked = c.Locking.BlockTaskManager;
        BlockAltTabCheck.IsChecked = c.Locking.BlockAltTab;
        CoverAllMonitorsCheck.IsChecked = c.Locking.CoverAllMonitors;
        PlaySoundCheck.IsChecked = c.Locking.PlaySoundOnLock;
        FailedAttemptsBox.Text = c.Locking.FailedAttemptsBeforeLockout.ToString();
        LockoutMinutesBox.Text = c.Locking.LockoutMinutes.ToString();

        // Integration
        EnableIpcCheck.IsChecked = c.Integration.EnableIpcServer;
        PipeNameBox.Text = c.Integration.IpcPipeName;
        EnableHttpCheck.IsChecked = c.Integration.EnableHttpApi;
        HttpPortBox.Text = c.Integration.HttpApiPort.ToString();
        ApiTokenBox.Text = c.Integration.HttpApiToken;
        DefenderEnabledCheck.IsChecked = c.Integration.DefenderIntegrationEnabled;
        DefenderLockOnThreatCheck.IsChecked = c.Integration.DefenderLockOnThreat;
        DefenderLockOnPupCheck.IsChecked = c.Integration.DefenderLockOnPup;

        ProtectedFoldersList.Items.Clear();
        foreach (var f in c.Integration.ProtectedFolders)
            ProtectedFoldersList.Items.Add(f.Path);
    }

    private void SaveConfigFromUi()
    {
        var c = _config.Config;

        c.Security.EncryptConfigFile = EncryptConfigCheck.IsChecked == true;
        c.Security.EnableTamperDetection = AntiTamperCheck.IsChecked == true;
        c.Security.EnableSelfProtection = SelfProtectionCheck.IsChecked == true;
        if (int.TryParse(MinLengthBox.Text, out var minLen)) c.Security.PasswordMinLength = minLen;

        c.Personalization.WelcomeMessage = WelcomeMsgBox.Text;
        c.Personalization.LockMessage = LockMsgBox.Text;
        c.Personalization.PrimaryColor = PrimaryColorBox.Text;
        c.Personalization.AccentColor = AccentColorBox.Text;
        c.Personalization.BlurBackground = BlurCheck.IsChecked == true;
        c.Personalization.EnableParticles = ParticlesCheck.IsChecked == true;
        c.Locking.ShowClock = ShowClockCheck.IsChecked == true;

        c.Locking.LockOnStartup = LockOnStartupCheck.IsChecked == true;
        c.Locking.LockOnIdle = LockOnIdleCheck.IsChecked == true;
        if (int.TryParse(IdleMinutesBox.Text, out var idle)) c.Locking.IdleMinutes = idle;
        c.Locking.BlockTaskManager = BlockTaskManagerCheck.IsChecked == true;
        c.Locking.BlockAltTab = BlockAltTabCheck.IsChecked == true;
        c.Locking.CoverAllMonitors = CoverAllMonitorsCheck.IsChecked == true;
        c.Locking.PlaySoundOnLock = PlaySoundCheck.IsChecked == true;
        if (int.TryParse(FailedAttemptsBox.Text, out var fa)) c.Locking.FailedAttemptsBeforeLockout = fa;
        if (int.TryParse(LockoutMinutesBox.Text, out var lo)) c.Locking.LockoutMinutes = lo;

        c.Integration.EnableIpcServer = EnableIpcCheck.IsChecked == true;
        c.Integration.IpcPipeName = PipeNameBox.Text;
        c.Integration.EnableHttpApi = EnableHttpCheck.IsChecked == true;
        if (int.TryParse(HttpPortBox.Text, out var port)) c.Integration.HttpApiPort = port;
        c.Integration.HttpApiToken = ApiTokenBox.Text;
        c.Integration.DefenderIntegrationEnabled = DefenderEnabledCheck.IsChecked == true;
        c.Integration.DefenderLockOnThreat = DefenderLockOnThreatCheck.IsChecked == true;
        c.Integration.DefenderLockOnPup = DefenderLockOnPupCheck.IsChecked == true;

        _config.Save();
    }

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var tag = btn.Tag?.ToString();

        // Скрыть все
        SecurityPanel.Visibility = Visibility.Collapsed;
        PersonalizationPanel.Visibility = Visibility.Collapsed;
        LockingPanel.Visibility = Visibility.Collapsed;
        IntegrationPanel.Visibility = Visibility.Collapsed;
        KeysPanel.Visibility = Visibility.Collapsed;
        LogsPanel.Visibility = Visibility.Collapsed;
        VaultPanel.Visibility = Visibility.Collapsed;

        // Показать выбранную
        switch (tag)
        {
            case "Security": SecurityPanel.Visibility = Visibility.Visible; break;
            case "Personalization": PersonalizationPanel.Visibility = Visibility.Visible; break;
            case "Locking": LockingPanel.Visibility = Visibility.Visible; break;
            case "Integration": IntegrationPanel.Visibility = Visibility.Visible; break;
            case "Keys": KeysPanel.Visibility = Visibility.Visible; break;
            case "Logs": LogsPanel.Visibility = Visibility.Visible; break;
            case "Vault": VaultPanel.Visibility = Visibility.Visible; break;
        }

        // Подсветка активной кнопки - можно реализовать через FindName и триггеры, пока упрощенно
        try
        {
            // Ищем sidebar и обновляем цвета
            // В реальности лучше через MVVM и Binding
        }
        catch { }
    }

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var newPass = NewPasswordBox.Text;
        if (string.IsNullOrWhiteSpace(newPass))
        {
            MessageBox.Show("Введите новый пароль");
            return;
        }

        var strength = PasswordManager.EvaluateStrength(newPass);
        PasswordStrengthText.Text = $"Сложность: {strength}";

        if (strength == PasswordStrength.Weak)
        {
            MessageBox.Show("Пароль слишком слабый! Добавьте заглавные буквы, цифры и символы.");
            return;
        }

        var hash = PasswordManager.HashPassword(newPass);
        _config.Config.AdminPasswordHash = hash;
        _config.Save();

        // Перешифровать хранилище ключей новым паролем
        if (_keys.Count > 0)
        {
            var encrypted = ActivationKeyManager.EncryptKeyStore(_keys, newPass);
            _config.Config.EncryptedKeyStore = encrypted;
            _config.Save();
        }

        MessageBox.Show($"Пароль изменен! Сложность: {strength}", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
        NewPasswordBox.Clear();
    }

    private void LockNow_Click(object sender, RoutedEventArgs e)
    {
        App.LockService?.Lock(Core.Locking.LockReason.Manual, "Ручная блокировка из настроек", "Settings");
        var lockWin = new LockScreenWindow();
        lockWin.Show();
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveConfigFromUi();
            SaveStatusText.Text = "✓ Сохранено";
            Task.Delay(2000).ContinueWith(_ => Dispatcher.Invoke(() => SaveStatusText.Text = ""));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка сохранения: {ex.Message}");
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Сбросить к настройкам по умолчанию?", "Подтверждение", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        {
            _config.Config = DefaultConfigs.CreateDefault();
            _config.Config.HardwareId = CryptoHelper.GetHardwareId();
            LoadConfigToUi();
            _config.Save();
        }
    }

    // Keys
    private void LoadKeys()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_config.Config.EncryptedKeyStore) && !string.IsNullOrWhiteSpace(_config.Config.AdminPasswordHash))
            {
                // Для демо пробуем расшифровать пустым паролем или ищем в памяти
                // В реальности запрашиваем пароль у пользователя
                _keys = new List<ActivationKey>();
            }
            KeysListView.ItemsSource = _keys;
        }
        catch { }
    }

    private void GenerateKey_Click(object sender, RoutedEventArgs e)
    {
        var label = KeyLabelBox.Text;
        int.TryParse(KeyDaysBox.Text, out var days);
        var bind = BindToHardwareCheck.IsChecked == true;
        var hwid = bind ? CryptoHelper.GetHardwareId() : null;

        var manager = App.KeyManager!;
        var key = manager.GenerateKey(TimeSpan.FromDays(days > 0 ? days : 365), hwid, label, 3);

        _keys.Add(key);
        KeysListView.ItemsSource = null;
        KeysListView.ItemsSource = _keys;

        GeneratedKeyBox.Text = key.Key;

        // Сохраняем зашифрованно - нужен пароль администратора в открытом виде, для демо используем фиксированный ключ шифрования
        // В проде - SecureString
        try
        {
            var masterPass = NewPasswordBox.Text;
            if (string.IsNullOrWhiteSpace(masterPass)) masterPass = "TempMasterPassForDemo_ChangeMe!";
            var encrypted = ActivationKeyManager.EncryptKeyStore(_keys, masterPass);
            _config.Config.EncryptedKeyStore = encrypted;
            _config.Save();
        }
        catch { }

        App.Logger?.Log(Core.Logging.AuditEventType.KeyActivated, $"Сгенерирован ключ {key.Key} ({label})", "Settings");
    }

    // Logs
    private void RefreshLogs_Click(object sender, RoutedEventArgs e) => RefreshLogs();

    private void RefreshLogs()
    {
        var logs = App.Logger?.GetRecent(200) ?? new();
        LogsListView.ItemsSource = logs;
    }

    private void ExportLogs_Click(object sender, RoutedEventArgs e)
    {
        var save = new Microsoft.Win32.SaveFileDialog { Filter = "JSON|*.json", FileName = $"audit_{DateTime.Now:yyyyMMdd}.json" };
        if (save.ShowDialog() == true)
        {
            var logs = App.Logger?.GetRecent(1000) ?? new();
            System.IO.File.WriteAllText(save.FileName, System.Text.Json.JsonSerializer.Serialize(logs, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            MessageBox.Show("Логи экспортированы");
        }
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        // Только очистка отображения, файлы остаются для аудита
        LogsListView.ItemsSource = null;
    }

    // Vault
    private void AddVaultItem_Click(object sender, RoutedEventArgs e)
    {
        if (_vault == null) return;
        var name = VaultNameBox.Text;
        var secret = VaultSecretBox.Text;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(secret))
        {
            MessageBox.Show("Заполните имя и секрет");
            return;
        }

        _vault.AddSecret(name, secret, "General", true);
        RefreshVault();
        VaultNameBox.Clear();
        VaultSecretBox.Clear();
    }

    private void RefreshVault()
    {
        if (_vault == null) return;
        VaultListView.ItemsSource = null;
        VaultListView.ItemsSource = _vault.ListItems();
    }

    // Protected folders
    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = NewFolderBox.Text;
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
        {
            MessageBox.Show("Папка не существует");
            return;
        }

        _config.Config.Integration.ProtectedFolders.Add(new ProtectedFolder { Path = path });
        ProtectedFoldersList.Items.Add(path);
        NewFolderBox.Clear();
        _config.Save();
    }
}
