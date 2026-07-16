using System.Text.Json.Serialization;

namespace BlockVision.Core.Configuration;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ThemeType
{
    Light,
    Dark,
    Neon,
    Matrix,
    Cyberpunk,
    Defender, // в стиле Windows Security
    Custom
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackgroundType
{
    SolidColor,
    Gradient,
    Image,
    Video,
    AnimatedShader
}

public class PersonalizationConfig
{
    public string Language { get; set; } = "ru-RU"; // ru-RU, en-US
    public ThemeType Theme { get; set; } = ThemeType.Dark;
    public string PrimaryColor { get; set; } = "#6C5CE7";
    public string AccentColor { get; set; } = "#00CEC9";
    public string TextColor { get; set; } = "#FFFFFF";
    public BackgroundType BackgroundType { get; set; } = BackgroundType.Gradient;
    public string BackgroundValue { get; set; } = string.Empty; // цвет, путь, url градиента
    public string? CustomWallpaperPath { get; set; }
    public bool BlurBackground { get; set; } = true;
    public double BlurRadius { get; set; } = 20.0;
    public double BackgroundOpacity { get; set; } = 0.9;

    public string WelcomeMessage { get; set; } = "Welcome";
    public string LockMessage { get; set; } = "Locked";
    public bool ShowLogo { get; set; } = true;
    public string? LogoPath { get; set; }
    public string? CustomCss { get; set; } // для продвинутых

    public string DateFormat { get; set; } = "dd MMM yyyy";
    public string TimeFormat { get; set; } = "HH:mm";
    public bool ShowSeconds { get; set; } = false;
    public string FontFamily { get; set; } = "Segoe UI";

    public bool EnableParticles { get; set; } = true;
    public bool EnableScanlineEffect { get; set; } = false;
    public string LockSoundPath { get; set; } = string.Empty;
    public string UnlockSoundPath { get; set; } = string.Empty;

    public Dictionary<string, string> CustomLabels { get; set; } = new()
    {
        ["PasswordPlaceholder"] = "Введите пароль",
        ["KeyPlaceholder"] = "Или введите ключ активации BVPC-XXXX-...",
        ["UnlockButton"] = "Разблокировать",
        ["AttemptsLeft"] = "Попыток осталось",
        ["ThreatDetected"] = "Обнаружена угроза!"
    };

    public AppAnimations Animations { get; set; } = new();
}

public class AppAnimations
{
    public bool EnableFadeIn { get; set; } = true;
    public bool EnableSlideUp { get; set; } = true;
    public bool EnableShakeOnError { get; set; } = true;
    public double AnimationSpeed { get; set; } = 1.0; // 0.5 slow, 1 normal, 2 fast
}
