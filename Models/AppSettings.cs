namespace WinAuthRemaster.Models;

public sealed class AppSettings
{
    public string? ConfigFilePath { get; set; }
    public int LockTimeoutMinutes { get; set; } = 5;
    public double WindowOpacity { get; set; } = 1.0;
    public bool IsDarkMode { get; set; } = true;
    public string Language { get; set; } = "en";

    // ウィンドウ位置・サイズ（null = デフォルト）
    public double? WindowTop { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowHeight { get; set; }
    public bool IsAlwaysOnTop { get; set; }

    // グローバルホットキー（null = 未設定）
    public int? HotkeyModifiers { get; set; }
    public int? HotkeyKey { get; set; }
}
