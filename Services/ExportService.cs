using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinAuthRemaster.Crypto;
using WinAuthRemaster.Models;

namespace WinAuthRemaster.Services;

public sealed class ExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public void ExportAsOtpauthUris(IEnumerable<AuthenticatorEntry> entries, string filePath)
    {
        var sb = new StringBuilder();
        foreach (var entry in entries)
            sb.AppendLine(ToOtpauthUri(entry));

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    public void ExportAsJson(IEnumerable<AuthenticatorEntry> entries, string filePath, string? password = null)
    {
        var config = new AppConfig
        {
            Entries = entries.ToList(),
            Protection = string.IsNullOrEmpty(password) ? ProtectionType.None : ProtectionType.Password
        };

        var service = new ConfigService(filePath);
        service.Save(config, password);
    }

    public static string ToOtpauthUri(AuthenticatorEntry entry)
    {
        string secret = Base32.Encode(entry.SecretKey);
        string label = Uri.EscapeDataString(entry.Name);
        string issuer = Uri.EscapeDataString(entry.Issuer);

        var sb = new StringBuilder();
        sb.Append($"otpauth://totp/{label}?secret={secret}");

        if (!string.IsNullOrEmpty(entry.Issuer))
            sb.Append($"&issuer={issuer}");

        if (entry.Algorithm != HmacAlgorithm.SHA1)
            sb.Append($"&algorithm={entry.Algorithm}");

        if (entry.Digits != AuthenticatorEntry.DEFAULT_DIGITS)
            sb.Append($"&digits={entry.Digits}");

        if (entry.Period != AuthenticatorEntry.DEFAULT_PERIOD)
            sb.Append($"&period={entry.Period}");

        return sb.ToString();
    }
}
