using System.ComponentModel;
using System.Runtime.CompilerServices;
using BlockVision.Core.Configuration;
using BlockVision.Core.Locking;
using BlockVision.Core.Security;

namespace BlockVision.App.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private string _password = string.Empty;
    private string _activationKey = string.Empty;
    private string _message = string.Empty;
    private bool _isError;
    private bool _isLoading;
    private int _attemptsLeft = 5;
    private LockReason _lastLockReason;

    private readonly ConfigManager _config;
    private readonly LockService _lockService;

    public MainViewModel(ConfigManager config, LockService lockService)
    {
        _config = config;
        _lockService = lockService;
        WelcomeMessage = config.Config.Personalization.WelcomeMessage;
        LockMessage = config.Config.Personalization.LockMessage;
    }

    public string Password
    {
        get => _password;
        set { _password = value; OnPropertyChanged(); }
    }

    public string ActivationKey
    {
        get => _activationKey;
        set { _activationKey = value; OnPropertyChanged(); }
    }

    public string Message
    {
        get => _message;
        set { _message = value; OnPropertyChanged(); }
    }

    public bool IsError
    {
        get => _isError;
        set { _isError = value; OnPropertyChanged(); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public int AttemptsLeft
    {
        get => _attemptsLeft;
        set { _attemptsLeft = value; OnPropertyChanged(); }
    }

    public string WelcomeMessage { get; set; }
    public string LockMessage { get; set; }
    public LockReason LastLockReason
    {
        get => _lastLockReason;
        set { _lastLockReason = value; OnPropertyChanged(); }
    }

    public string HardwareId => CryptoHelper.GetHardwareId();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
