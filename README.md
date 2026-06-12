# WinAuth Remaster

A modern remaster of [WinAuth](https://github.com/winauth/winauth) — a portable two-factor authenticator for Windows, rebuilt from scratch with WPF and .NET 10.

## Features

- **TOTP code generation** with SHA1, SHA256, SHA512 support
- **Password protection** — encrypt your secrets with a master password (DPAPI-protected files are also supported for reading)
- **Auto-lock** — configurable inactivity timeout
- **Manual lock** — lock instantly from the toolbar button or the L key
- **Global hotkey** — show/hide the app with a system-wide keyboard shortcut
- **System tray** — runs in the tray; restore via tray icon, hotkey, or relaunching the app
- **Start with Windows** — optional auto-start, silently minimized to the tray
- **Number key shortcuts** — press 1–9 to instantly copy a code
- **Search & filter** — quickly find entries by name
- **Drag & drop reordering** — organize your authenticators
- **Card colors** — visually distinguish entries with custom colors
- **Clipboard auto-clear** — automatically clear copied codes after a timeout
- **Dark / light theme** — with adjustable window opacity
- **Always on top** — pin the window above other windows
- **Single instance** — prevents multiple app windows
- **Multi-language** — English, Japanese, Chinese (Simplified), Korean, German, French, Spanish, Portuguese, Russian, Hindi

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
