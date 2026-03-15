using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using WinAuthRemaster.Models;

namespace WinAuthRemaster.Crypto;

public static class AesGcmEncryptor
{
    private const byte FormatVersion = 0x01;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32; // 256 bits
    private const int Pbkdf2Iterations = 600_000;

    public static byte[] Encrypt(byte[] plaintext, string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] key = DeriveKey(password, salt);

        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        CryptographicOperations.ZeroMemory(key);

        // Format: [version:1][salt:16][nonce:12][tag:16][ciphertext:N]
        byte[] result = new byte[1 + SaltSize + NonceSize + TagSize + ciphertext.Length];
        result[0] = FormatVersion;
        salt.CopyTo(result.AsSpan(1));
        nonce.CopyTo(result.AsSpan(1 + SaltSize));
        tag.CopyTo(result.AsSpan(1 + SaltSize + NonceSize));
        ciphertext.CopyTo(result.AsSpan(1 + SaltSize + NonceSize + TagSize));

        return result;
    }

    public static byte[] Decrypt(byte[] encrypted, string password)
    {
        if (encrypted.Length < 1 + SaltSize + NonceSize + TagSize)
            throw new CryptographicException("Invalid encrypted data: too short.");

        if (encrypted[0] != FormatVersion)
            throw new CryptographicException($"Unsupported format version: {encrypted[0]}");

        var span = encrypted.AsSpan();
        byte[] salt = span.Slice(1, SaltSize).ToArray();
        byte[] nonce = span.Slice(1 + SaltSize, NonceSize).ToArray();
        byte[] tag = span.Slice(1 + SaltSize + NonceSize, TagSize).ToArray();
        byte[] ciphertext = span.Slice(1 + SaltSize + NonceSize + TagSize).ToArray();

        byte[] key = DeriveKey(password, salt);
        byte[] plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (CryptographicException)
        {
            throw new InvalidPasswordException();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        return plaintext;
    }

    public static byte[] EncryptWithProtection(byte[] plaintext, string? password, ProtectionType protection)
    {
        byte[] data;
        if (protection is ProtectionType.Password or ProtectionType.DpapiAndPassword)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password required for this protection type.");
            data = Encrypt(plaintext, password);
        }
        else
        {
            data = plaintext;
        }

        if (protection is ProtectionType.Dpapi or ProtectionType.DpapiAndPassword)
        {
            data = System.Security.Cryptography.ProtectedData.Protect(
                data, null, DataProtectionScope.CurrentUser);
        }

        return data;
    }

    public static byte[] DecryptWithProtection(byte[] encrypted, string? password, ProtectionType protection)
    {
        byte[] data = encrypted;

        if (protection is ProtectionType.Dpapi or ProtectionType.DpapiAndPassword)
        {
            data = System.Security.Cryptography.ProtectedData.Unprotect(
                data, null, DataProtectionScope.CurrentUser);
        }

        if (protection is ProtectionType.Password or ProtectionType.DpapiAndPassword)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password required for this protection type.");
            data = Decrypt(data, password);
        }

        return data;
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);
    }
}

public class InvalidPasswordException : Exception
{
    public InvalidPasswordException() : base("Invalid password.") { }
    public InvalidPasswordException(string message) : base(message) { }
    public InvalidPasswordException(string message, Exception inner) : base(message, inner) { }
}
