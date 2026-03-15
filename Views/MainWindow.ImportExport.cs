using System.Security;
using System.Windows;
using Microsoft.Win32;
using WinAuthRemaster.Crypto;
using WinAuthRemaster.Models;
using WinAuthRemaster.Services;
using static WinAuthRemaster.Services.LocalizationService;

namespace WinAuthRemaster.Views;

public partial class MainWindow
{
    // Import

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        string winAuthDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinAuth");
        string initialDir = System.IO.Directory.Exists(winAuthDir)
            ? winAuthDir
            : AppContext.BaseDirectory;

        var dlg = new OpenFileDialog
        {
            Title = Loc("FileDialog_ImportTitle"),
            Filter = "All supported|*.xml;*.json;*.txt|WinAuth XML|*.xml|JSON|*.json|otpauth URIs|*.txt",
            Multiselect = false,
            InitialDirectory = initialDir
        };

        if (dlg.ShowDialog(this) != true) return;

        try
        {
            string ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
            var entries = ImportByExtension(ext, dlg.FileName);
            if (entries == null) return; // パスワードダイアログがキャンセルされた

            if (entries.Count > 0)
            {
                _viewModel.ImportEntries(entries);
                ShowStatus(Loc("Status_Imported", entries.Count));
            }
            else
            {
                ShowStatus(Loc("Status_NoAuthFound"));
            }
        }
        catch (InvalidPasswordException)
        {
            MessageBox.Show(this, Loc("Error_WrongPassword"), Loc("Error_ImportError"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{Loc("Error_ImportFailed")}\n{ex.Message}", Loc("Error_ImportError"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>拡張子ごとにインポート処理を分岐。パスワードキャンセル時は null を返す</summary>
    private List<AuthenticatorEntry>? ImportByExtension(string ext, string filePath)
    {
        if (ext == ".xml")
            return ImportFromXml(filePath);
        if (ext == ".txt")
            return ImportFromTxt(filePath);
        return ImportFromJson(filePath);
    }

    private List<AuthenticatorEntry>? ImportFromXml(string filePath)
    {
        var pwDialog = new PasswordDialog(Loc("PwTitle_WinAuth"), isSetMode: false) { Owner = this };
        if (pwDialog.ShowDialog() != true) return null;

        using var access = pwDialog.Password.Reveal();
        return _importService.ImportFromLegacyXml(filePath, access.Value);
    }

    private List<AuthenticatorEntry> ImportFromTxt(string filePath)
    {
        string text = System.IO.File.ReadAllText(filePath);
        return _importService.ImportFromOtpauthUris(text);
    }

    private List<AuthenticatorEntry>? ImportFromJson(string filePath)
    {
        var configService = new ConfigService(filePath);
        var protection = configService.DetectProtection();
        SecureString? password = null;

        if (protection != ProtectionType.None)
        {
            var pwDialog = new PasswordDialog(Loc("PwTitle_FilePassword"), isSetMode: false) { Owner = this };
            if (pwDialog.ShowDialog() != true) return null;
            password = pwDialog.Password;
        }

        using var access = password.Reveal();
        var config = configService.Load(access.Value);
        return config.Entries;
    }

    // Export

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Entries.Count == 0)
        {
            ShowStatus(Loc("Status_NothingToExport"));
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = Loc("FileDialog_ExportTitle"),
            Filter = "otpauth URIs (*.txt)|*.txt|Encrypted JSON (*.json)|*.json",
            DefaultExt = ".txt"
        };

        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var entries = _viewModel.Entries.Select(vm => vm.Entry).ToList();
            string ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();

            if (ext == ".json")
            {
                var pwDialog = new PasswordDialog(Loc("PwTitle_SetExport"), isSetMode: true) { Owner = this };
                if (pwDialog.ShowDialog() != true) return;
                using var access = pwDialog.Password.Reveal();
                _exportService.ExportAsJson(entries, dlg.FileName, access.Value);
            }
            else
            {
                _exportService.ExportAsOtpauthUris(entries, dlg.FileName);
            }

            ShowStatus(Loc("Status_Exported", entries.Count));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{Loc("Error_ExportFailed")}\n{ex.Message}", Loc("Error_ExportError"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Change password

    private void OnChangePasswordClick(object sender, RoutedEventArgs e)
    {
        var dialog = new PasswordDialog(Loc("PwTitle_SetProtection"), isSetMode: true) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var protection = dialog.Password.IsNullOrEmpty()
            ? ProtectionType.None
            : ProtectionType.Password;

        _viewModel.SetProtection(protection, dialog.Password);
        ShowStatus(protection == ProtectionType.None
            ? Loc("Status_PasswordRemoved")
            : Loc("Status_PasswordUpdated"));
    }
}
