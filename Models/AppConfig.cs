namespace WinAuthRemaster.Models;

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public ProtectionType Protection { get; set; } = ProtectionType.None;
    public List<AuthenticatorEntry> Entries { get; set; } = [];
}

public enum ProtectionType
{
    None,
    Password,
    Dpapi,
    DpapiAndPassword
}
