using System.Text;
using System.Text.RegularExpressions;

namespace WinAuthRemaster.Crypto;

public static partial class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int Shift = 5; // 2^5 = 32
    private const int Mask = 31; // 0x1F

    private static readonly Dictionary<char, int> CharMap = BuildCharMap();

    private static Dictionary<char, int> BuildCharMap()
    {
        var map = new Dictionary<char, int>(Alphabet.Length);
        for (int i = 0; i < Alphabet.Length; i++)
            map[Alphabet[i]] = i;
        return map;
    }

    public static byte[] Decode(string encoded)
    {
        encoded = WhitespaceAndSeparators().Replace(encoded, "");
        encoded = TrailingPadding().Replace(encoded, "");
        encoded = encoded.ToUpperInvariant();

        if (encoded.Length == 0)
            return [];

        int outLength = encoded.Length * Shift / 8;
        byte[] result = new byte[outLength];
        int buffer = 0;
        int next = 0;
        int bitsLeft = 0;

        foreach (char c in encoded)
        {
            if (!CharMap.TryGetValue(c, out int value))
                throw new FormatException($"Invalid Base32 character: {c}");

            buffer <<= Shift;
            buffer |= value & Mask;
            bitsLeft += Shift;
            if (bitsLeft >= 8)
            {
                result[next++] = (byte)(buffer >> (bitsLeft - 8));
                bitsLeft -= 8;
            }
        }

        return result;
    }

    public static string Encode(byte[] data)
    {
        if (data.Length == 0)
            return string.Empty;

        var result = new StringBuilder();
        int buffer = data[0];
        int next = 1;
        int bitsLeft = 8;

        while (bitsLeft > 0 || next < data.Length)
        {
            if (bitsLeft < Shift)
            {
                if (next < data.Length)
                {
                    buffer <<= 8;
                    buffer |= data[next++] & 0xFF;
                    bitsLeft += 8;
                }
                else
                {
                    int pad = Shift - bitsLeft;
                    buffer <<= pad;
                    bitsLeft += pad;
                }
            }
            int index = Mask & (buffer >> (bitsLeft - Shift));
            bitsLeft -= Shift;
            result.Append(Alphabet[index]);
        }

        return result.ToString();
    }

    [GeneratedRegex(@"[\s\-]+")]
    private static partial Regex WhitespaceAndSeparators();

    [GeneratedRegex(@"[=]*$")]
    private static partial Regex TrailingPadding();
}
