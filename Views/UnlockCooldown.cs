using System.Windows;
using System.Windows.Controls;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using System.Windows.Threading;
using static WinAuthRemaster.Services.LocalizationService;

namespace WinAuthRemaster.Views;

/// <summary>
/// パスワード認証失敗時のクールダウン処理を共通化するヘルパー。
/// 失敗回数 × 0.5秒 (最大2.5秒) のクールダウン中、ドットアニメーションで待機演出する。
/// </summary>
public sealed class UnlockCooldown
{
    private readonly PasswordBox _passwordBox;
    private readonly TextBlock _errorText;
    private readonly ButtonBase? _submitButton;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private int _failedAttempts;
    private int _ticksLeft;
    private int _dots;

    public bool IsCooldown { get; private set; }

    public UnlockCooldown(PasswordBox passwordBox, TextBlock errorText, ButtonBase? submitButton = null)
    {
        _passwordBox = passwordBox;
        _errorText = errorText;
        _submitButton = submitButton;
        _timer.Tick += OnTick;
    }

    /// <summary>認証失敗を記録し、クールダウンを開始する。</summary>
    public void OnFailed()
    {
        if (_failedAttempts >= 5) _failedAttempts = 5; // 上限で固定（オーバーフロー防止）
        else _failedAttempts++;
        IsCooldown = true;

        double cooldownMs = Math.Min(_failedAttempts, 5) * 500.0;
        _ticksLeft = Math.Max(1, (int)(cooldownMs / _timer.Interval.TotalMilliseconds));
        _dots = 0;

        _passwordBox.IsEnabled = false;
        if (_submitButton != null) _submitButton.IsEnabled = false;
        _errorText.Text = "";
        _errorText.Visibility = Visibility.Visible;
        _timer.Start();
    }

    /// <summary>状態をリセットする（ロック解除成功時やロック状態変更時に呼ぶ）。</summary>
    public void Reset()
    {
        _timer.Stop();
        _failedAttempts = 0;
        IsCooldown = false;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _ticksLeft--;
        _dots = (_dots % 3) + 1;
        _errorText.Text = new string('.', _dots);

        if (_ticksLeft > 0) return;

        _timer.Stop();
        IsCooldown = false;
        _passwordBox.IsEnabled = true;
        if (_submitButton != null) _submitButton.IsEnabled = true;
        _passwordBox.Clear();
        _passwordBox.Focus();
        _errorText.Text = Loc("Lock_WrongPassword");
    }
}
