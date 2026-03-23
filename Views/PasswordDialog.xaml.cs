using System.Security;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WinAuthRemaster.Crypto;
using static WinAuthRemaster.Services.LocalizationService;

namespace WinAuthRemaster.Views;

public partial class PasswordDialog : Window
{
    private readonly bool _isSetMode;
    private Func<SecureString, bool>? _authValidator;
    private UnlockCooldown? _cooldown;

    /// <summary>ホットキーが設定済みならタスクバーからも非表示にできる。</summary>
    public bool CanHideToTray { get; set; }

    public SecureString Password { get; private set; } = new();

    public PasswordDialog(string title, bool isSetMode)
    {
        InitializeComponent();
        _isSetMode = isSetMode;
        TitleText.Text = title;

        if (isSetMode)
            ConfirmPanel.Visibility = Visibility.Visible;
        else
            CancelButton.Visibility = Visibility.Collapsed;

        // ボタン等以外の領域でドラッグ移動
        PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;

        PasswordInput.Focus();
    }

    /// <summary>
    /// 認証バリデータを設定すると、OK押下時にダイアログ内で検証＋クールダウンを行う。
    /// </summary>
    public void SetAuthValidator(Func<SecureString, bool> validator)
    {
        _authValidator = validator;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_cooldown?.IsCooldown == true) return;

        if (PasswordInput.SecurePassword.Length == 0)
        {
            ShowError(Loc("Password_EnterPassword"));
            return;
        }

        if (_isSetMode)
        {
            using var pw = PasswordInput.SecurePassword.Reveal();
            using var confirm = ConfirmInput.SecurePassword.Reveal();
            if (pw.Value != confirm.Value)
            {
                ShowError(Loc("Password_DontMatch"));
                return;
            }
        }

        // 認証バリデータが設定されている場合、ダイアログ内で検証
        if (_authValidator != null)
        {
            var testPassword = PasswordInput.SecurePassword.Copy();
            testPassword.MakeReadOnly();

            if (!_authValidator(testPassword))
            {
                _cooldown ??= new UnlockCooldown(PasswordInput, ErrorText, OkButton);
                _cooldown.OnFailed();
                return;
            }
        }

        Password = PasswordInput.SecurePassword.Copy();
        Password.MakeReadOnly();
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            OnOk(sender, e);
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
        if (CanHideToTray)
            ShowInTaskbar = false;
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;

        // ボタン・テキストボックス等のインタラクティブ要素はスキップ
        var current = source;
        while (current != null)
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.TextBox
                or System.Windows.Controls.PasswordBox)
                return;
            current = VisualTreeHelper.GetParent(current);
        }

        try { DragMove(); }
        catch (InvalidOperationException) { }
    }
}
