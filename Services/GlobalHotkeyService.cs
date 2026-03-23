using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WinAuthRemaster.Services;

/// <summary>
/// Win32 RegisterHotKey/UnregisterHotKey を使ったグローバルホットキーサービス。
/// スレッドのメッセージループから直接 WM_HOTKEY を捕捉するため、
/// フォーカス中のコントロールやウィンドウ状態に依存しない。
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0x0001;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private bool _isRegistered;
    private bool _isHooked;

    public Action? Toggled { get; set; }

    /// <summary>スレッドメッセージフックを設定する。</summary>
    public void Initialize()
    {
        if (_isHooked) return;
        ComponentDispatcher.ThreadPreprocessMessage += OnThreadMessage;
        _isHooked = true;
    }

    /// <summary>ホットキーを登録する。既に登録済みなら先に解除する。</summary>
    public bool Register(int modifiers, int vk)
    {
        Unregister();
        uint mods = (uint)modifiers | MOD_NOREPEAT;
        // hWnd=IntPtr.Zero: スレッドのメッセージキューに WM_HOTKEY が投函される
        _isRegistered = RegisterHotKey(IntPtr.Zero, HOTKEY_ID, mods, (uint)vk);
        return _isRegistered;
    }

    /// <summary>登録済みホットキーを解除する。</summary>
    public void Unregister()
    {
        if (!_isRegistered) return;
        UnregisterHotKey(IntPtr.Zero, HOTKEY_ID);
        _isRegistered = false;
    }

    public void Dispose()
    {
        Unregister();
        if (_isHooked)
        {
            ComponentDispatcher.ThreadPreprocessMessage -= OnThreadMessage;
            _isHooked = false;
        }
    }

    private void OnThreadMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message == WM_HOTKEY && (int)msg.wParam == HOTKEY_ID)
        {
            Toggled?.Invoke();
            handled = true;
        }
    }
}
