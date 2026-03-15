using System.Runtime.InteropServices;
using System.Security;

namespace WinAuthRemaster.Crypto;

/// <summary>
/// SecureString から平文を取り出し、使用後にメモリをゼロクリアするためのヘルパー。
/// using ブロックで使い、スコープ外で自動クリアする。
/// </summary>
public readonly ref struct SecureStringAccess
{
    private readonly IntPtr _bstr;

    /// <summary>平文。SecureString が null の場合は null。</summary>
    public string? Value { get; }

    public SecureStringAccess(SecureString? secureString)
    {
        if (secureString == null || secureString.Length == 0)
        {
            _bstr = IntPtr.Zero;
            Value = null;
        }
        else
        {
            _bstr = Marshal.SecureStringToBSTR(secureString);
            Value = Marshal.PtrToStringBSTR(_bstr);
        }
    }

    public void Dispose()
    {
        if (_bstr != IntPtr.Zero)
            Marshal.ZeroFreeBSTR(_bstr);
    }
}

public static class SecureStringExtensions
{
    /// <summary>
    /// SecureString を using var access = ss.Reveal() で展開し、access.Value で平文を取得する。
    /// SecureString が null でも安全に呼べる。スコープ離脱時に BSTR をゼロクリアする。
    /// </summary>
    public static SecureStringAccess Reveal(this SecureString? secureString) => new(secureString);

    public static bool IsNullOrEmpty(this SecureString? secureString)
        => secureString == null || secureString.Length == 0;
}
