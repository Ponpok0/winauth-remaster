# WinAuth Remaster

A modern remaster of [WinAuth](https://github.com/winauth/winauth) — a portable two-factor authenticator for Windows, rebuilt from scratch with WPF and .NET 10.

## Features

- **TOTP code generation** with SHA1, SHA256, SHA512 support
- **Password protection** — encrypt your secrets with password, Windows DPAPI, or both
- **Auto-lock** — configurable inactivity timeout
- **Global hotkey** — show/hide the app with a system-wide keyboard shortcut
- **Number key shortcuts** — press 1–9 to instantly copy a code
- **Search & filter** — quickly find entries by name
- **Drag & drop reordering** — organize your authenticators
- **Card colors** — visually distinguish entries with custom colors
- **Clipboard auto-clear** — automatically clear copied codes after a timeout
- **Single instance** — prevents multiple app windows
- **Multi-language** — English, Japanese, Chinese (Simplified), Korean, German, French, Spanish, Portuguese

## Import / Export

**Import from:**
- WinAuth 3 encrypted XML (`.xml`) — full backward compatibility
- Encrypted JSON (`.json`)
- `otpauth://` URI text files

**Export to:**
- `otpauth://` URI text files (`.txt`)
- Encrypted JSON (`.json`)

## Building

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build
dotnet run
```

To publish a self-contained executable:

```bash
dotnet publish -c Release --self-contained true -r win-x64
```

## License

[MIT](LICENSE)

## Acknowledgments

Based on the original [WinAuth](https://github.com/winauth/winauth) by Colin Mackie.
