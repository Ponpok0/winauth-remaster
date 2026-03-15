using System.Security.Cryptography;
using System.Text;

namespace WinAuthRemaster.Crypto;

/// <summary>
/// Decrypts legacy WinAuth3 encrypted data (Blowfish/ECB/ISO10126d2 + PBKDF2).
/// Used exclusively for importing from existing winauth.xml files.
/// </summary>
public static class LegacyDecryptor
{
    private const int SaltLength = 8;
    private const int Pbkdf2Iterations = 2000;
    private const int Pbkdf2KeySize = 256; // bytes (not bits!) - matches original WinAuth behavior
    private static readonly string EncryptionHeader = ByteArrayToHex(Encoding.UTF8.GetBytes("WINAUTH3"));

    [Flags]
    public enum LegacyPasswordType
    {
        None = 0,
        Explicit = 1,
        User = 2,
        Machine = 4,
    }

    public static LegacyPasswordType DecodePasswordTypes(string? passwordTypes)
    {
        if (string.IsNullOrEmpty(passwordTypes))
            return LegacyPasswordType.None;

        var result = LegacyPasswordType.None;
        foreach (char c in passwordTypes)
        {
            result |= c switch
            {
                'y' => LegacyPasswordType.Explicit,
                'u' => LegacyPasswordType.User,
                'm' => LegacyPasswordType.Machine,
                _ => LegacyPasswordType.None
            };
        }
        return result;
    }

    public static string DecryptSequence(string data, LegacyPasswordType passwordType, string? password)
    {
        // Check for encryption header "WINAUTH3" (hex-encoded)
        // Use OrdinalIgnoreCase because the stored hex may be uppercase while our header is lowercase
        if (data.Length >= EncryptionHeader.Length && data.StartsWith(EncryptionHeader, StringComparison.OrdinalIgnoreCase))
        {
            int dataStart = EncryptionHeader.Length;
            string salt = data.Substring(dataStart, Math.Min(SaltLength * 2, data.Length - dataStart));
            dataStart += salt.Length;

            using var sha = SHA256.Create();
            int hashLen = sha.HashSize / 8 * 2; // 64 hex chars
            string hash = data.Substring(dataStart, Math.Min(hashLen, data.Length - dataStart));
            dataStart += hash.Length;

            string encryptedData = data[dataStart..];
            string decrypted = DecryptSequenceNoHash(encryptedData, passwordType, password);

            // Verify hash: SHA256(salt + decrypted_data)
            byte[] compareBytes = HexToByteArray(salt + decrypted);
            string compareHash = ByteArrayToHex(sha.ComputeHash(compareBytes));
            if (!string.Equals(compareHash, hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidPasswordException("Password verification failed.");

            return decrypted;
        }

        return DecryptSequenceNoHash(data, passwordType, password);
    }

    private static string DecryptSequenceNoHash(string data, LegacyPasswordType passwordType, string? password)
    {
        // Decrypt in reverse order of encryption: Machine -> User -> Explicit
        if (passwordType.HasFlag(LegacyPasswordType.Machine))
        {
            byte[] cipher = HexToByteArray(data);
            byte[] plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.LocalMachine);
            data = ByteArrayToHex(plain);
        }

        if (passwordType.HasFlag(LegacyPasswordType.User))
        {
            byte[] cipher = HexToByteArray(data);
            byte[] plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
            data = ByteArrayToHex(plain);
        }

        if (passwordType.HasFlag(LegacyPasswordType.Explicit))
        {
            if (string.IsNullOrEmpty(password))
                throw new InvalidPasswordException("Password required.");
            data = BlowfishDecrypt(data, password);
        }

        return data;
    }

    private static string BlowfishDecrypt(string hexData, string password)
    {
        // Extract salt (first 16 hex chars = 8 bytes)
        byte[] saltBytes = HexToByteArray(hexData[..(SaltLength * 2)]);
        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);

        // PBKDF2 key derivation - 256 bytes (not bits!) matches original code exactly
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            passwordBytes, saltBytes, Pbkdf2Iterations, HashAlgorithmName.SHA1, Pbkdf2KeySize);

        // Extract encrypted data (after salt)
        byte[] inBytes = HexToByteArray(hexData[(SaltLength * 2)..]);

        // Blowfish/ECB with ISO10126d2 padding - using custom implementation
        // that accepts 256-byte keys (BouncyCastle 2.6.2+ rejects keys > 56 bytes)
        byte[] outBytes;
        try
        {
            outBytes = LegacyBlowfish.DecryptEcb(inBytes, key);
        }
        catch (Exception ex)
        {
            throw new InvalidPasswordException("Decryption failed - wrong password?", ex);
        }

        return ByteArrayToHex(outBytes);
    }

    // Hex encoding utilities matching original WinAuth ByteArrayToString / StringToByteArray

    public static string ByteArrayToHex(byte[] bytes)
    {
        return Convert.ToHexStringLower(bytes);
    }

    public static byte[] HexToByteArray(string hex)
    {
        return Convert.FromHexString(hex);
    }
}
