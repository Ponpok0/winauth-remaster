using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WinAuthRemaster.Crypto;
using WinAuthRemaster.Extensions;
using WinAuthRemaster.Models;
using WinAuthRemaster.Services;
using static WinAuthRemaster.Services.LocalizationService;

namespace WinAuthRemaster.Views;

public partial class AddAuthenticatorDialog : Window
{
    private const int PRESET_CUSTOM = -1;

    private readonly DispatcherTimer _previewTimer;
    private bool _suppressPresetSync;
    private bool _suppressSecretParse;

    public string AuthName { get; private set; } = "";
    public string Secret { get; private set; } = "";
    public HmacAlgorithm Algorithm { get; private set; } = HmacAlgorithm.SHA1;
    public int Period { get; private set; } = 30;
    public int Digits { get; private set; } = 6;

    // プリセット定義: (locKey, algorithmIndex, period, digits)  algorithmIndex=PRESET_CUSTOMはCustom
    private static readonly (string LocKey, int AlgorithmIndex, int Period, int Digits)[] Presets =
    [
        ("Preset_Standard", 0, AuthenticatorEntry.DEFAULT_PERIOD, AuthenticatorEntry.DEFAULT_DIGITS),
        ("Preset_Custom", PRESET_CUSTOM, 0, 0),
    ];

    public AddAuthenticatorDialog()
    {
        InitializeComponent();
        this.MakeLocalBrushesOpaque();

        // プリセットComboBox初期化
        foreach (var (locKey, _, _, _) in Presets)
            PresetBox.Items.Add(new ComboBoxItem { Content = Loc(locKey) });
        _suppressPresetSync = true;
        PresetBox.SelectedIndex = 0; // Standard
        _suppressPresetSync = false;

        UpdateAlgorithmHint();
        UpdateFieldsReadOnly();

        // InitializeComponent完了後にイベント接続（XAML初期化中の発火を防止）
        PresetBox.SelectionChanged += OnPresetChanged;
        AlgorithmBox.SelectionChanged += OnAlgorithmChanged;
        SecretBox.TextChanged += OnSecretTextChanged;
        PeriodBox.TextChanged += OnFieldTextChanged;
        PeriodBox.PreviewTextInput += OnNumericOnly;
        System.Windows.DataObject.AddPastingHandler(PeriodBox, OnNumericPaste);
        DigitsBox.TextChanged += OnFieldTextChanged;
        DigitsBox.PreviewTextInput += OnNumericOnly;
        System.Windows.DataObject.AddPastingHandler(DigitsBox, OnNumericPaste);

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _previewTimer.Tick += (_, _) => UpdatePreview();
        _previewTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _previewTimer.Stop();
        base.OnClosed(e);
    }

    private HmacAlgorithm GetSelectedAlgorithm() => AlgorithmBox.SelectedIndex switch
    {
        1 => HmacAlgorithm.SHA256,
        2 => HmacAlgorithm.SHA512,
        _ => HmacAlgorithm.SHA1
    };

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetSync || PresetBox.SelectedIndex < 0)
            return;

        var (_, algoIdx, period, digits) = Presets[PresetBox.SelectedIndex];
        if (algoIdx != PRESET_CUSTOM)
        {
            _suppressPresetSync = true;
            AlgorithmBox.SelectedIndex = algoIdx;
            PeriodBox.Text = period.ToString();
            DigitsBox.Text = digits.ToString();
            _suppressPresetSync = false;
        }

        UpdateAlgorithmHint();
        UpdateFieldsReadOnly();
        UpdatePreview();
    }

    private bool IsCustomPreset =>
        PresetBox.SelectedIndex >= 0 && Presets[PresetBox.SelectedIndex].AlgorithmIndex == PRESET_CUSTOM;

    private void UpdateFieldsReadOnly()
    {
        bool isCustom = IsCustomPreset;
        AlgorithmBox.IsEnabled = isCustom;
        PeriodBox.IsReadOnly = !isCustom;
        DigitsBox.IsReadOnly = !isCustom;
        // ReadOnly状態を視覚的に伝える
        PeriodBox.Opacity = isCustom ? 1.0 : 0.5;
        DigitsBox.Opacity = isCustom ? 1.0 : 0.5;
    }

    private void OnAlgorithmChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateAlgorithmHint();
        if (!_suppressPresetSync)
            SyncPresetToCustom();
    }

    private void UpdateAlgorithmHint()
    {
        if (!IsCustomPreset)
        {
            // プリセット選択中はCustomへの誘導ヒント
            AlgorithmHint.Text = Loc("Add_HintUseCustom");
            return;
        }

        string key = AlgorithmBox.SelectedIndex switch
        {
            1 => "Add_HintSHA256",
            2 => "Add_HintSHA512",
            _ => "Add_HintSHA1"
        };
        AlgorithmHint.Text = Loc(key);
    }

    // Custom プリセットのインデックス（配列順に依存しない）
    private static readonly int CustomPresetIndex =
        Array.FindIndex(Presets, p => p.AlgorithmIndex == PRESET_CUSTOM);

    // 手動で値を変更したらPresetをCustomに切り替え
    private void SyncPresetToCustom()
    {
        if (!IsCustomPreset)
        {
            _suppressPresetSync = true;
            PresetBox.SelectedIndex = CustomPresetIndex;
            _suppressPresetSync = false;
            UpdateFieldsReadOnly();
        }
    }

    private void OnFieldTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppressPresetSync)
            SyncPresetToCustom();
        UpdatePreview();
    }

    private void OnSecretTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSecretParse) return;

        string text = SecretBox.Text.Trim();

        // Auto-detect otpauth:// URI
        if (text.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            var entry = ImportService.ParseOtpauthUri(text);
            if (entry != null)
            {
                _suppressSecretParse = true;
                _suppressPresetSync = true;

                NameBox.Text = entry.Name;
                SecretBox.Text = Base32.Encode(entry.SecretKey);
                PeriodBox.Text = entry.Period.ToString();
                DigitsBox.Text = entry.Digits.ToString();
                AlgorithmBox.SelectedIndex = entry.Algorithm switch
                {
                    HmacAlgorithm.SHA256 => 1,
                    HmacAlgorithm.SHA512 => 2,
                    _ => 0
                };
                PresetBox.SelectedIndex = CustomPresetIndex; // URI入力時はCustom

                _suppressPresetSync = false;
                _suppressSecretParse = false;

                UpdateAlgorithmHint();
                UpdateFieldsReadOnly();
                return;
            }
        }

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        try
        {
            string secret = SecretBox.Text.Trim();
            if (string.IsNullOrEmpty(secret))
            {
                PreviewCode.Text = "--- ---";
                return;
            }

            byte[] key = Base32.Decode(secret);
            if (key.Length == 0)
            {
                PreviewCode.Text = "--- ---";
                return;
            }

            int period = int.TryParse(PeriodBox.Text, out int p) ? p : AuthenticatorEntry.DEFAULT_PERIOD;
            int digits = int.TryParse(DigitsBox.Text, out int d) ? d : AuthenticatorEntry.DEFAULT_DIGITS;
            var algo = GetSelectedAlgorithm();

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string code = TotpGenerator.GenerateCode(key, nowMs, period, digits, algo);

            PreviewCode.Text = AuthenticatorEntry.FormatCode(code);
        }
        catch (FormatException)
        {
            PreviewCode.Text = "--- ---";
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        string secret = SecretBox.Text.Trim();

        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show(this, Loc("Add_EnterName"), Loc("Add_Validation"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(secret))
        {
            MessageBox.Show(this, Loc("Add_EnterSecret"), Loc("Add_Validation"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Base32.Decode(secret); // Validate
        }
        catch (FormatException)
        {
            MessageBox.Show(this, Loc("Add_InvalidSecret"), Loc("Add_Validation"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(PeriodBox.Text.Trim(), out int period) || period < 10 || period > 300)
        {
            MessageBox.Show(this, Loc("Add_InvalidPeriod"), Loc("Add_Validation"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(DigitsBox.Text.Trim(), out int digits) || digits < 4 || digits > 8)
        {
            MessageBox.Show(this, Loc("Add_InvalidDigits"), Loc("Add_Validation"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AuthName = name;
        Secret = secret;
        Algorithm = GetSelectedAlgorithm();
        Period = period;
        Digits = digits;

        DialogResult = true;
    }

    // 数字以外の入力を拒否
    private static void OnNumericOnly(object sender, TextCompositionEventArgs e)
    {
        foreach (char c in e.Text)
        {
            if (!char.IsDigit(c))
            {
                e.Handled = true;
                return;
            }
        }
    }

    // 貼り付け時も数字以外を拒否
    private static void OnNumericPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)))
        {
            string text = (string)e.DataObject.GetData(typeof(string))!;
            foreach (char c in text)
            {
                if (!char.IsDigit(c))
                {
                    e.CancelCommand();
                    return;
                }
            }
        }
        else
        {
            e.CancelCommand();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

}
