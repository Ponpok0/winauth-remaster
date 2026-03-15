using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinAuthRemaster.Crypto;
using WinAuthRemaster.Models;

namespace WinAuthRemaster.Services;

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string ConfigFilePath { get; set; }

    public ConfigService(string? configFilePath = null)
    {
        ConfigFilePath = configFilePath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WinAuth", "winauth.json");
    }

    public bool ConfigExists() => File.Exists(ConfigFilePath);

    public ProtectionType DetectProtection()
    {
        if (!ConfigExists()) return ProtectionType.None;

        string json = File.ReadAllText(ConfigFilePath, Encoding.UTF8);
        var wrapper = JsonSerializer.Deserialize<ConfigWrapper>(json, JsonOptions);
        return wrapper?.Protection ?? ProtectionType.None;
    }

    public AppConfig Load(string? password = null)
    {
        string json = File.ReadAllText(ConfigFilePath, Encoding.UTF8);
        var wrapper = JsonSerializer.Deserialize<ConfigWrapper>(json, JsonOptions)
            ?? throw new InvalidDataException("Failed to parse config file.");

        if (wrapper.Protection == ProtectionType.None)
        {
            return new AppConfig
            {
                Version = wrapper.Version,
                Protection = ProtectionType.None,
                Entries = wrapper.Entries ?? []
            };
        }

        // Encrypted: data field contains Base64-encoded encrypted payload
        if (string.IsNullOrEmpty(wrapper.Data))
            throw new InvalidDataException("Encrypted config has no data.");

        byte[] encrypted = Convert.FromBase64String(wrapper.Data);
        byte[] plainBytes = AesGcmEncryptor.DecryptWithProtection(encrypted, password, wrapper.Protection);
        string plainJson = Encoding.UTF8.GetString(plainBytes);

        var inner = JsonSerializer.Deserialize<InnerData>(plainJson, JsonOptions)
            ?? throw new InvalidDataException("Failed to parse decrypted data.");

        return new AppConfig
        {
            Version = wrapper.Version,
            Protection = wrapper.Protection,
            Entries = inner.Entries ?? []
        };
    }

    public void Save(AppConfig config, string? password = null)
    {
        string dir = Path.GetDirectoryName(ConfigFilePath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        ConfigWrapper wrapper;

        if (config.Protection == ProtectionType.None)
        {
            wrapper = new ConfigWrapper
            {
                Version = config.Version,
                Protection = ProtectionType.None,
                Entries = config.Entries
            };
        }
        else
        {
            var inner = new InnerData { Entries = config.Entries };
            byte[] plainBytes = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(inner, JsonOptions));
            byte[] encrypted = AesGcmEncryptor.EncryptWithProtection(plainBytes, password, config.Protection);

            wrapper = new ConfigWrapper
            {
                Version = config.Version,
                Protection = config.Protection,
                Data = Convert.ToBase64String(encrypted)
            };
        }

        string json = JsonSerializer.Serialize(wrapper, JsonOptions);

        // Atomic write: write to temp file then rename
        string tempPath = ConfigFilePath + ".tmp";
        File.WriteAllText(tempPath, json, Encoding.UTF8);
        File.Move(tempPath, ConfigFilePath, overwrite: true);
    }

    public void CreateBackup(int maxGenerations = 5)
    {
        if (!File.Exists(ConfigFilePath)) return;

        string dir = Path.GetDirectoryName(ConfigFilePath)!;
        string name = Path.GetFileNameWithoutExtension(ConfigFilePath);
        string ext = Path.GetExtension(ConfigFilePath);

        for (int i = maxGenerations; i >= 2; i--)
        {
            string src = Path.Combine(dir, $"{name}.bak{i - 1}{ext}");
            string dst = Path.Combine(dir, $"{name}.bak{i}{ext}");
            if (File.Exists(src))
            {
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(src, dst);
            }
        }

        string bak1 = Path.Combine(dir, $"{name}.bak1{ext}");
        File.Copy(ConfigFilePath, bak1, overwrite: true);
    }

    private sealed class ConfigWrapper
    {
        public int Version { get; set; } = 1;
        public ProtectionType Protection { get; set; }
        public string? Data { get; set; }
        public List<AuthenticatorEntry>? Entries { get; set; }
    }

    private sealed class InnerData
    {
        public List<AuthenticatorEntry> Entries { get; set; } = [];
    }
}
