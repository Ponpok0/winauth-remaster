using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WinAuthRemaster.Services;

/// <summary>
/// Win32 RegisterHotKey/UnregisterHotKey を使ったグローバルホットキーサービス。
/// WM_HOTKEY を受信したら Toggled コールバックを発火する。
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

    private IntPtr _hwnd;
    private HwndSource? _source;
    private bool _isRegistered;

    public Action? Toggled { get; set; }

    /// <summary>ウィンドウハンドルを渡してメッセージフックを設定する。</summary>
    public void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
    }

    /// <summary>ホットキーを登録する。既に登録済みなら先に解除する。</summary>
    public bool Register(int modifiers, int vk)
    {
        if (_hwnd == IntPtr.Zero) return false;
        Unregister();
        uint mods = (uint)modifiers | MOD_NOREPEAT;
        _isRegistered = RegisterHotKey(_hwnd, HOTKEY_ID, mods, (uint)vk);
        return _isRegistered;
    }

    /// <summary>登録済みホットキーを解除する。</summary>
    public void Unregister()
    {
        if (!_isRegistered || _hwnd == IntPtr.Zero) return;
        UnregisterHotKey(_hwnd, HOTKEY_ID);
        _isRegistered = false;
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            Toggled?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }
}
