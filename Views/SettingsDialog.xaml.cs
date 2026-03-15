using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using WinAuthRemaster.Extensions;
using WinAuthRemaster.Models;
using WinAuthRemaster.Services;
using static WinAuthRemaster.Services.LocalizationService;

namespace WinAuthRemaster.Views;

public partial class SettingsDialog : Window
{
    private readonly string _defaultPath;
    private string _selectedLanguage;
    private int? _hotkeyModifiers;
    private int? _hotkeyKey;

    public AppSettings Result { get; private set; }

    // MainWindowから渡されるコールバック
    public Action? ImportRequested { get; set; }
    public Action? ExportRequested { get; set; }
    public Action? ChangePasswordRequested { get; set; }
    public Action<bool>? ThemePreview { get; set; }
    public Action<double>? OpacityPreview { get; set; }

    public SettingsDialog(AppSettings currentSettings, string resolvedPath)
    {
        InitializeComponent();
        _defaultPath = resolvedPath;
        _selectedLanguage = currentSettings.Language;
        Result = currentSettings;

        PathInput.Text = currentSettings.ConfigFilePath ?? resolvedPath;
        TimeoutInput.Text = currentSettings.LockTimeoutMinutes.ToString();
        DarkModeRadio.IsChecked = currentSettings.IsDarkMode;
        LightModeRadio.IsChecked = !currentSettings.IsDarkMode;

        // 言語コンボボックス初期化
        int selectedIndex = 0;
        for (int i = 0; i < SupportedLanguages.Length; i++)
        {
            var (code, displayName) = SupportedLanguages[i];
            LanguageCombo.Items.Add(new ComboBoxItem { Content = displayName, Tag = code });
            if (code == currentSettings.Language)
                selectedIndex = i;
        }
        LanguageCombo.SelectedIndex = selectedIndex;

        OpacitySlider.Value = currentSettings.WindowOpacity * 100;
        OpacityLabel.Text = $"{(int)OpacitySlider.Value}%";

        _hotkeyModifiers = currentSettings.HotkeyModifiers;
        _hotkeyKey = currentSettings.HotkeyKey;
        HotkeyInput.Text = FormatHotkey(_hotkeyModifiers, _hotkeyKey);

        // Window レベルで PreviewKeyDown を捕捉（TextBox 単体だとイベントが届かない環境対策）
        PreviewKeyDown += OnWindowPreviewKeyDown;

        // 透過ブラシがダイアログに漏れないようローカルで不透明に上書き
        this.MakeLocalBrushesOpaque();

        TimeoutInput.Focus();
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = Loc("FileDialog_ChooseLocation"),
            Filter = "JSON files (*.json)|*.json",
            FileName = "winauth.json",
            OverwritePrompt = false
        };

        if (dlg.ShowDialog(this) == true)
            PathInput.Text = dlg.FileName;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TimeoutInput.Text.Trim(), out int timeout) || timeout < 0 || timeout > 1440)
        {
            MessageBox.Show(this, Loc("Settings_InvalidTimeout"), Loc("Password_Error"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string path = PathInput.Text.Trim();
        bool isDefault = string.IsNullOrEmpty(path) ||
                         string.Equals(path, _defaultPath, System.StringComparison.OrdinalIgnoreCase);

        Result = new AppSettings
        {
            ConfigFilePath = isDefault ? null : path,
            LockTimeoutMinutes = timeout,
            IsDarkMode = DarkModeRadio.IsChecked == true,
            Language = _selectedLanguage,
            WindowOpacity = OpacitySlider.Value / 100.0,
            HotkeyModifiers = _hotkeyModifiers,
            HotkeyKey = _hotkeyKey
        };
        DialogResult = true;
    }

    private void OnThemeRadioChanged(object sender, RoutedEventArgs e)
    {
        bool isDark = DarkModeRadio.IsChecked == true;
        ThemePreview?.Invoke(isDark);
        // テーマ変更でApplicationリソースが透過ブラシに差し替わるので再上書き
        this.MakeLocalBrushesOpaque();
    }

    private void OnOpacitySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityLabel != null)
            OpacityLabel.Text = $"{(int)e.NewValue}%";
        OpacityPreview?.Invoke(e.NewValue / 100.0);
        // 透過度変更でも同様に再上書き
        this.MakeLocalBrushesOpaque();
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageCombo.SelectedItem is ComboBoxItem { Tag: string code })
            _selectedLanguage = code;
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        ImportRequested?.Invoke();
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        ExportRequested?.Invoke();
    }

    private void OnChangePasswordClick(object sender, RoutedEventArgs e)
    {
        ChangePasswordRequested?.Invoke();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    // ホットキー入力（Window レベルで捕捉し、HotkeyInput にフォーカスがある場合のみ処理）

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (HotkeyInput.IsKeyboardFocused)
            OnHotkeyKeyDown(sender, e);
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        var key = (e.Key == Key.System) ? e.SystemKey : e.Key;

        // Esc: 変更キャンセル
        if (key == Key.Escape)
        {
            HotkeyInput.Text = FormatHotkey(_hotkeyModifiers, _hotkeyKey);
            Keyboard.ClearFocus();
            e.Handled = true;
            return;
        }

        // Delete: 割当クリア
        if (key == Key.Delete)
        {
            _hotkeyModifiers = null;
            _hotkeyKey = null;
            HotkeyInput.Text = "";
            e.Handled = true;
            return;
        }

        // 修飾キー単体は無視（Handled にしない → Tab 等の移動キーも通す）
        if (key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;

        // A-Z, 0-9, F1-F12 を受付
        int vk = KeyInterop.VirtualKeyFromKey(key);
        bool isLetter = vk >= 0x41 && vk <= 0x5A;
        bool isDigit = vk >= 0x30 && vk <= 0x39;
        bool isFKey = vk >= 0x70 && vk <= 0x7B; // VK_F1 ~ VK_F12
        if (!isLetter && !isDigit && !isFKey) return;

        // 修飾キー1つ以上必須
        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None) return;

        // WPF ModifierKeys → Win32 MOD_ フラグ
        int win32Mods = 0;
        if (mods.HasFlag(ModifierKeys.Alt)) win32Mods |= 0x0001;
        if (mods.HasFlag(ModifierKeys.Control)) win32Mods |= 0x0002;
        if (mods.HasFlag(ModifierKeys.Shift)) win32Mods |= 0x0004;

        _hotkeyModifiers = win32Mods;
        _hotkeyKey = vk;
        HotkeyInput.Text = FormatHotkey(win32Mods, vk);
        e.Handled = true;
    }

    private void OnHotkeyGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(HotkeyInput.Text))
            HotkeyInput.Text = Loc("Settings_HotkeyHint");
    }

    private void OnHotkeyLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (HotkeyInput.Text == Loc("Settings_HotkeyHint"))
            HotkeyInput.Text = FormatHotkey(_hotkeyModifiers, _hotkeyKey);
    }

    private void OnHotkeyClear(object sender, RoutedEventArgs e)
    {
        _hotkeyModifiers = null;
        _hotkeyKey = null;
        HotkeyInput.Text = "";
    }

    private static string FormatHotkey(int? modifiers, int? key)
    {
        if (modifiers == null || key == null) return "";
        var parts = new List<string>();
        if ((modifiers.Value & 0x0002) != 0) parts.Add("Ctrl");
        if ((modifiers.Value & 0x0001) != 0) parts.Add("Alt");
        if ((modifiers.Value & 0x0004) != 0) parts.Add("Shift");
        parts.Add(KeyInterop.KeyFromVirtualKey(key.Value).ToString());
        return string.Join(" + ", parts);
    }
}
