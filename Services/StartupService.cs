using Microsoft.Win32;

namespace WinAuthRemaster.Services;

/// <summary>
/// Windows 起動時の自動起動をレジストリで制御する。
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run を使用。
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "WinAuthRemaster";

    /// <summary>
    /// スタートアップ起動を示すコマンドライン引数。
    /// 手動起動と区別するため、レジストリ登録コマンドにのみ付与される。
    /// </summary>
    public const string MinimizedArg = "--minimized";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(AppName) is string;
    }

    public static void SetEnabled(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null) return;

        if (enable)
        {
            string command = BuildRunCommand();
            if (!string.IsNullOrEmpty(command))
                key.SetValue(AppName, command);
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// 登録済みの起動コマンドが現行形式と異なる場合に再登録する
    /// （--minimized なしの旧形式や、exe パス変更からの移行）。
    /// </summary>
    public static void MigrateIfOutdated()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(AppName) is not string current) return;

        string expected = BuildRunCommand();
        if (!string.IsNullOrEmpty(expected) && current != expected)
            key.SetValue(AppName, expected);
    }

    private static string BuildRunCommand()
    {
        string exePath = Environment.ProcessPath ?? "";
        return string.IsNullOrEmpty(exePath) ? "" : $"\"{exePath}\" {MinimizedArg}";
    }
}
