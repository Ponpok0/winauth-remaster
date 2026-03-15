using System.Windows;
using System.Windows.Threading;

namespace WinAuthRemaster.Services;

public sealed class ClipboardService
{
    private DispatcherTimer? _clearTimer;
    private string? _lastCopied;

    public async void CopyWithAutoClear(string text, int clearAfterSeconds = 30)
    {
        _clearTimer?.Stop();

        try
        {
            Clipboard.SetText(text);
            _lastCopied = text;
        }
        catch (Exception)
        {
            // クリップボードが他プロセスにロックされている場合、100ms後にリトライ
            try
            {
                await Task.Delay(100);
                Clipboard.SetText(text);
                _lastCopied = text;
            }
            catch
            {
                // リトライも失敗 — クリップボードにアクセスできないので諦める
                return;
            }
        }

        _clearTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(clearAfterSeconds)
        };
        _clearTimer.Tick += (_, _) =>
        {
            _clearTimer.Stop();
            try
            {
                // Only clear if clipboard still has our value
                if (Clipboard.ContainsText() && Clipboard.GetText() == _lastCopied)
                    Clipboard.Clear();
            }
            catch { /* クリップボードが他プロセスにロックされている場合は無視 */ }
            _lastCopied = null;
        };
        _clearTimer.Start();
    }
}
