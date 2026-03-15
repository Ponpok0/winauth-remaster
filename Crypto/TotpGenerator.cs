using System.Security.Cryptography;
using WinAuthRemaster.Models;

namespace WinAuthRemaster.Crypto;

public static class TotpGenerator
{
    // digits 4〜8 に対応する 10^n のルックアップテーブル（浮動小数点を避ける）
    private static readonly uint[] PowersOf10 = [1, 10, 100, 1_000, 10_000, 100_000, 1_000_000, 10_000_000, 100_000_000];

    public static string GenerateCode(byte[] secretKey, long unixTimeMs, int period, int digits, HmacAlgorithm algorithm)
    {
        long counter = unixTimeMs / 1000L / period;
        byte[] counterBytes = new byte[8];
        // Big-endian encoding
        for (int i = 7; i >= 0; i--)
        {
            counterBytes[i] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

        byte[] hash = ComputeHmac(algorithm, secretKey, counterBytes);

        // Dynamic truncation (RFC 4226 Section 5.4)
        int offset = hash[^1] & 0x0F;
        uint fullCode = (uint)(
            (hash[offset] & 0x7F) << 24 |
            hash[offset + 1] << 16 |
            hash[offset + 2] << 8 |
            hash[offset + 3]);

        uint modulo = PowersOf10[digits];
        return (fullCode % modulo).ToString(new string('0', digits));
    }

    public static string GenerateCurrentCode(byte[] secretKey, int period = 30, int digits = 6, HmacAlgorithm algorithm = HmacAlgorithm.SHA1)
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return GenerateCode(secretKey, nowMs, period, digits, algorithm);
    }

    public static int GetRemainingSeconds(int period = 30)
    {
        long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return period - (int)(nowSec % period);
    }

    private static byte[] ComputeHmac(HmacAlgorithm algorithm, byte[] key, byte[] data)
    {
        using HMAC hmac = algorithm switch
        {
            HmacAlgorithm.SHA1 => new HMACSHA1(key),
            HmacAlgorithm.SHA256 => new HMACSHA256(key),
            HmacAlgorithm.SHA512 => new HMACSHA512(key),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };
        return hmac.ComputeHash(data);
    }
}
