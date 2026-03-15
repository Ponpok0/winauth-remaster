using System.IO;
using System.Text;
using System.Web;
using System.Xml;
using WinAuthRemaster.Crypto;
using WinAuthRemaster.Models;

namespace WinAuthRemaster.Services;

public sealed class ImportService
{
    /// <summary>
    /// Import from legacy WinAuth winauth.xml file.
    /// </summary>
    public List<AuthenticatorEntry> ImportFromLegacyXml(string filePath, string? password)
    {
        var entries = new List<AuthenticatorEntry>();
        string xml = File.ReadAllText(filePath, Encoding.UTF8);

        using var reader = XmlReader.Create(new StringReader(xml));
        reader.MoveToContent(); // <WinAuth>

        string? encrypted = reader.GetAttribute("encrypted");
        var passwordType = LegacyDecryptor.DecodePasswordTypes(encrypted);

        // If the root element itself is encrypted
        if (passwordType != LegacyDecryptor.LegacyPasswordType.None)
        {
            string data = reader.ReadElementContentAsString();
            string decrypted = LegacyDecryptor.DecryptSequence(data, passwordType, password);
            byte[] plain = LegacyDecryptor.HexToByteArray(decrypted);

            using var ms = new MemoryStream(plain);
            using var innerReader = XmlReader.Create(ms);
            ReadXmlInternal(innerReader, password, entries);
            return entries;
        }

        ParseWinAuthXml(reader, password, entries);
        return entries;
    }

    /// <summary>
    /// Import from otpauth:// URI text (one per line).
    /// </summary>
    public List<AuthenticatorEntry> ImportFromOtpauthUris(string text)
    {
        var entries = new List<AuthenticatorEntry>();
        foreach (string line in text.Split('\n', '\r'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
            {
                var entry = ParseOtpauthUri(trimmed);
                if (entry != null)
                    entries.Add(entry);
            }
        }
        return entries;
    }

    public static AuthenticatorEntry? ParseOtpauthUri(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return null;

        // otpauth://totp/Label?secret=...&issuer=...&algorithm=...&digits=...&period=...
        if (!string.Equals(parsed.Host, "totp", StringComparison.OrdinalIgnoreCase))
            return null;

        var query = HttpUtility.ParseQueryString(parsed.Query);
        string? secret = query["secret"];
        if (string.IsNullOrEmpty(secret))
            return null;

        // Label is the path, e.g., /Issuer:account or /account
        string label = Uri.UnescapeDataString(parsed.AbsolutePath.TrimStart('/'));
        string issuer = query["issuer"] ?? "";
        string name = label;

        // If label is "Issuer:Account", extract issuer from it
        int colonIdx = label.IndexOf(':');
        if (colonIdx > 0)
        {
            if (string.IsNullOrEmpty(issuer))
                issuer = label[..colonIdx].Trim();
            name = label[(colonIdx + 1)..].Trim();
        }

        // If name is empty, use full label
        if (string.IsNullOrEmpty(name))
            name = label;

        // Compose display name
        string displayName = !string.IsNullOrEmpty(issuer) ? $"{issuer} ({name})" : name;

        var entry = new AuthenticatorEntry
        {
            Name = displayName,
            Issuer = issuer,
            SecretKey = Base32.Decode(secret),
            Digits = int.TryParse(query["digits"], out int d) ? d : AuthenticatorEntry.DEFAULT_DIGITS,
            Period = int.TryParse(query["period"], out int p) ? p : AuthenticatorEntry.DEFAULT_PERIOD,
            Algorithm = ParseAlgorithm(query["algorithm"]),
        };

        return entry;
    }

    private void ParseWinAuthXml(XmlReader reader, string? password, List<AuthenticatorEntry> entries)
    {
        reader.MoveToContent();
        if (reader.IsEmptyElement) return;

        reader.Read();
        while (!reader.EOF)
        {
            if (reader.IsStartElement())
            {
                switch (reader.Name)
                {
                    case "data":
                        ParseEncryptedDataElement(reader, password, entries);
                        break;

                    case "WinAuthAuthenticator":
                        var entry = ParseWinAuthAuthenticator(reader, password);
                        if (entry != null)
                            entries.Add(entry);
                        break;

                    default:
                        reader.Skip();
                        break;
                }
            }
            else
            {
                reader.Read();
            }
        }
    }

    private void ParseEncryptedDataElement(XmlReader reader, string? password, List<AuthenticatorEntry> entries)
    {
        string? encrypted = reader.GetAttribute("encrypted");
        var passwordType = LegacyDecryptor.DecodePasswordTypes(encrypted);

        if (passwordType != LegacyDecryptor.LegacyPasswordType.None)
        {
            string data = reader.ReadElementContentAsString();
            string decrypted = LegacyDecryptor.DecryptSequence(data, passwordType, password);
            byte[] plain = LegacyDecryptor.HexToByteArray(decrypted);

            // Use MemoryStream like the original code for proper encoding detection
            using var ms = new MemoryStream(plain);
            using var innerReader = XmlReader.Create(ms);
            ReadXmlInternal(innerReader, password, entries);
        }
        else
        {
            reader.Skip();
        }
    }

    /// <summary>
    /// Reads decrypted inner XML matching the original WinAuth's ReadXmlInternal behavior.
    /// The decrypted content may contain a root element wrapping config/authenticator entries.
    /// </summary>
    private void ReadXmlInternal(XmlReader reader, string? password, List<AuthenticatorEntry> entries)
    {
        reader.MoveToContent();
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.Read();
        while (!reader.EOF)
        {
            if (reader.IsStartElement())
            {
                switch (reader.Name)
                {
                    case "config":
                        ReadXmlInternal(reader, password, entries);
                        break;

                    case "data":
                        ParseEncryptedDataElement(reader, password, entries);
                        break;

                    case "WinAuthAuthenticator":
                        var entry = ParseWinAuthAuthenticator(reader, password);
                        if (entry != null)
                            entries.Add(entry);
                        break;

                    default:
                        reader.Skip();
                        break;
                }
            }
            else
            {
                reader.Read();
            }
        }
    }

    private AuthenticatorEntry? ParseWinAuthAuthenticator(XmlReader reader, string? password)
    {
        string? typeAttr = reader.GetAttribute("type");

        // We only support TOTP-compatible types (GoogleAuthenticator, MicrosoftAuthenticator, etc.)
        // All of these use the same TOTP algorithm; we extract secretdata the same way.

        var entry = new AuthenticatorEntry();

        reader.Read();
        while (!reader.EOF)
        {
            if (reader.IsStartElement())
            {
                switch (reader.Name)
                {
                    case "name":
                        entry.Name = reader.ReadElementContentAsString();
                        break;

                    case "created":
                        if (long.TryParse(reader.ReadElementContentAsString(), out long created))
                        {
                            entry.CreatedUtc = DateTimeOffset.FromUnixTimeMilliseconds(created).UtcDateTime;
                        }
                        break;

                    case "authenticatordata":
                        ParseAuthenticatorData(reader, entry, password);
                        break;

                    default:
                        reader.Skip();
                        break;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                reader.Read(); // Move past </WinAuthAuthenticator>
                break;
            }
            else
            {
                reader.Read(); // Skip whitespace, etc.
            }
        }

        // Only return if we got a valid secret key
        return entry.SecretKey.Length > 0 ? entry : null;
    }

    private void ParseAuthenticatorData(XmlReader reader, AuthenticatorEntry entry, string? password)
    {
        // authenticatordata may itself be encrypted
        string? encrypted = reader.GetAttribute("encrypted");
        var passwordType = LegacyDecryptor.DecodePasswordTypes(encrypted);

        if (passwordType != LegacyDecryptor.LegacyPasswordType.None)
        {
            string data = reader.ReadElementContentAsString();
            string decrypted = LegacyDecryptor.DecryptSequence(data, passwordType, password);
            byte[] plain = LegacyDecryptor.HexToByteArray(decrypted);

            // Use MemoryStream like original code for proper encoding detection
            using var ms = new MemoryStream(plain);
            using var innerReader = XmlReader.Create(ms);
            innerReader.MoveToContent();
            ParseAuthenticatorDataInner(innerReader, entry);
            return;
        }

        ParseAuthenticatorDataInner(reader, entry);
    }

    private void ParseAuthenticatorDataInner(XmlReader reader, AuthenticatorEntry entry)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        reader.Read();
        while (!reader.EOF)
        {
            if (reader.IsStartElement())
            {
                switch (reader.Name)
                {
                    case "secretdata":
                        ParseSecretData(reader.ReadElementContentAsString(), entry);
                        break;

                    case "servertimediff":
                    case "lastservertime":
                        reader.Skip(); // We don't use time sync
                        break;

                    default:
                        reader.Skip();
                        break;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                reader.Read(); // Move past end element
                break;
            }
            else
            {
                reader.Read(); // Skip whitespace, etc.
            }
        }
    }

    /// <summary>
    /// Parse secretdata format: "hexkey\tdigits\thmactype\tperiod"
    /// May have "|" separator for game-specific suffixes.
    /// </summary>
    private void ParseSecretData(string secretData, AuthenticatorEntry entry)
    {
        if (string.IsNullOrEmpty(secretData)) return;

        string[] mainParts = secretData.Split('|')[0].Split('\t');

        if (mainParts.Length > 0 && mainParts[0].Length > 0)
            entry.SecretKey = LegacyDecryptor.HexToByteArray(mainParts[0]);

        if (mainParts.Length > 1 && int.TryParse(mainParts[1], out int digits))
            entry.Digits = digits;

        if (mainParts.Length > 2)
            entry.Algorithm = ParseAlgorithm(mainParts[2]);

        if (mainParts.Length > 3 && int.TryParse(mainParts[3], out int period))
            entry.Period = period;
    }

    private static HmacAlgorithm ParseAlgorithm(string? value)
    {
        if (string.IsNullOrEmpty(value)) return HmacAlgorithm.SHA1;

        return value.ToUpperInvariant() switch
        {
            "SHA1" or "HMACSHA1" => HmacAlgorithm.SHA1,
            "SHA256" or "HMACSHA256" => HmacAlgorithm.SHA256,
            "SHA512" or "HMACSHA512" => HmacAlgorithm.SHA512,
            _ => HmacAlgorithm.SHA1
        };
    }
}
