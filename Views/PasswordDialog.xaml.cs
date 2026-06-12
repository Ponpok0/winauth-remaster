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
    private readonly double _designWidth;
    private readonly double _designHeight;
    private double _visibleLeft;
    private double _visibleTop;
    private bool _hasVisiblePosition;
    private Func<SecureString, bool>? _authValidator;
    private UnlockCooldown? _cooldown;

    /// <summary>ホットキーが設定済みならタスクバーからも非表示にできる。</summary>
    public bool CanHideToTray { get; set; }

    /// <summary>画面外退避中かどうか。</summary>
    public bool IsHiddenOffScreen { get; private set; }

    /// <summary>
    /// 初回描画完了時に画面外退避する予約（最小化起動用）。
    /// 描画前に復帰要求が来た場合は RestoreOnScreen が予約を取り消す。
    /// </summary>
    public bool HideOnFirstRender { get; set; }

    /// <summary>画面上に見えていない状態か（画面外退避・起動時 Opacity=0・最小化）。</summary>
    public bool IsInvisible =>
        IsHiddenOffScreen || Opacity < 1 || WindowState == WindowState.Minimized;

    public SecureString Password { get; private set; } = new();

    public PasswordDialog(string title, bool isSetMode)
    {
        InitializeComponent();
        _isSetMode = isSetMode;
        TitleText.Text = title;

        // 透過ウィンドウ（AllowsTransparency）は最小化から復元するとサイズが
        // 壊れることがあるため、設計サイズを保持して Normal 復帰時に再適用する
        _designWidth = Width;
        _designHeight = Height;
        StateChanged += OnStateChangedRestoreSize;
        ContentRendered += OnContentRendered;

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

    private void OnStateChangedRestoreSize(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Normal) return;
        Width = _designWidth;
        Height = _designHeight;
    }

    // 最小化起動: 実位置（CenterScreen / 位置復元）の確定後に画面外退避する。
    // 描画前に復帰要求が先行した場合、予約は RestoreOnScreen が解除済み
    private void OnContentRendered(object? sender, EventArgs e)
    {
        if (HideOnFirstRender)
        {
            HideOnFirstRender = false;
            HideOffScreen();
        }
    }

    /// <summary>
    /// モーダル表示を維持したまま実質不可視にする。
    /// ShowDialog 中のウィンドウは Hide() するとモーダルセッションが終了して
    /// ShowDialog が戻ってしまうため、画面外移動 + Opacity=0 で隠す。
    /// Minimized も使わない（透過ウィンドウは表示前に最小化すると復元サイズが壊れる）。
    /// </summary>
    public void HideOffScreen()
    {
        if (IsHiddenOffScreen) return;

        // 復元先として有効な（画面内の）位置のみ記録する。画面外座標を
        // 取り込むと以後の復帰がすべて画面外になるため
        if (IsOnVirtualScreen(Left, Top))
        {
            _visibleLeft = Left;
            _visibleTop = Top;
            _hasVisiblePosition = true;
        }

        Opacity = 0;
        ShowInTaskbar = false;
        // 仮想スクリーンの左外側へ退避（固定値は負座標側のマルチモニタ構成で実画面に重なり得る）
        Left = SystemParameters.VirtualScreenLeft - Width - 1000;
        IsHiddenOffScreen = true;
    }

    /// <summary>
    /// 不可視状態（画面外退避・起動時 Opacity=0・最小化）から前面に復帰する。冪等。
    /// </summary>
    public void RestoreOnScreen()
    {
        // 「描画完了後に退避する」予約より復帰要求を優先する
        HideOnFirstRender = false;

        if (IsHiddenOffScreen)
        {
            if (_hasVisiblePosition && IsOnVirtualScreen(_visibleLeft, _visibleTop))
            {
                Left = _visibleLeft;
                Top = _visibleTop;
            }
            else
            {
                // 退避中のモニタ構成変更等で復元先が画面外になった → 作業領域中央へ
                Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
                Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - Height) / 2;
            }
            IsHiddenOffScreen = false;
        }

        // 起動時の Opacity=0 状態（退避前のレース窓）や最小化からの復帰も同じ経路で扱う
        Opacity = 1;
        ShowInTaskbar = true;
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    // ウィンドウの一部（50px 以上）が仮想スクリーン内に見える位置か
    // （App.RestoreWindowPosition の可視判定と同条件）
    private bool IsOnVirtualScreen(double left, double top)
    {
        return left + Width > SystemParameters.VirtualScreenLeft + 50 &&
               left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50 &&
               top > SystemParameters.VirtualScreenTop - 10 &&
               top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50;
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        if (CanHideToTray)
        {
            // ホットキーで復帰できるため完全に隠す
            HideOffScreen();
        }
        else
        {
            // ホットキーなし: タスクバーから復帰できるよう通常の最小化
            WindowState = WindowState.Minimized;
        }
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
