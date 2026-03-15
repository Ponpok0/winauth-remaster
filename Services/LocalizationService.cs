using System.Windows;

namespace WinAuthRemaster.Services;

public static class LocalizationService
{
    public static readonly (string Code, string DisplayName)[] SupportedLanguages =
    [
        ("en", "English"),
        ("ja", "日本語"),
        ("zh-CN", "简体中文"),
        ("ko", "한국어"),
        ("de", "Deutsch"),
        ("fr", "Français"),
        ("es", "Español"),
        ("pt", "Português"),
        ("ru", "Русский"),
        ("hi", "हिन्दी"),
    ];

    /// <summary>リソースキーからローカライズ文字列を取得</summary>
    public static string Loc(string key)
    {
        return Application.Current.TryFindResource(key) as string ?? key;
    }

    /// <summary>string.Format 付きでローカライズ文字列を取得</summary>
    public static string Loc(string key, params object[] args)
    {
        string template = Loc(key);
        return string.Format(template, args);
    }

    /// <summary>英語をベースにロードし、選択言語をオーバーレイ</summary>
    public static void ApplyLanguage(string langCode)
    {
        var app = Application.Current;

        // 既存の文字列辞書を除去
        var toRemove = app.Resources.MergedDictionaries
            .Where(d => d.Source?.OriginalString.Contains("/Strings/") == true)
            .ToList();
        foreach (var d in toRemove)
            app.Resources.MergedDictionaries.Remove(d);

        // 英語をベースとしてロード
        app.Resources.MergedDictionaries.Add(
            new ResourceDictionary { Source = new Uri("pack://application:,,,/Resources/Strings/en.xaml") });

        // 選択言語をオーバーレイ（英語以外）
        if (langCode != "en")
        {
            try
            {
                app.Resources.MergedDictionaries.Add(
                    new ResourceDictionary { Source = new Uri($"pack://application:,,,/Resources/Strings/{langCode}.xaml") });
            }
            catch { /* 言語ファイルが存在しない場合は英語にフォールバック */ }
        }
    }
}
