namespace WinAuthRemaster.Models;

public sealed class AuthenticatorEntry
{
    public const int DEFAULT_DIGITS = 6;
    public const int DEFAULT_PERIOD = 30;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Issuer { get; set; } = "";
    public byte[] SecretKey { get; set; } = [];
    public int Digits { get; set; } = DEFAULT_DIGITS;
    public int Period { get; set; } = DEFAULT_PERIOD;
    public HmacAlgorithm Algorithm { get; set; } = HmacAlgorithm.SHA1;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public int SortOrder { get; set; }
    public string? CardColor { get; set; }

    /// <summary>コードを桁数に応じて空白区切りで整形（例: "123 456", "1234 5678"）</summary>
    public static string FormatCode(string code) => code.Length switch
    {
        6 => $"{code[..3]} {code[3..]}",
        8 => $"{code[..4]} {code[4..]}",
        _ => code
    };
}

public enum HmacAlgorithm
{
    SHA1,
    SHA256,
    SHA512
}
