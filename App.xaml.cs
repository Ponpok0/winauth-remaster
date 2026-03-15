using System.IO.Pipes;
using System.Security;
using System.Threading;
using System.Windows;
using WinAuthRemaster.Crypto;
using WinAuthRemaster.Models;
using WinAuthRemaster.Services;
using WinAuthRemaster.ViewModels;
using WinAuthRemaster.Views;

namespace WinAuthRemaster;

public partial class App : Application
{
    private const string MUTEX_NAME = "WinAuthRemaster_SingleInstance";
    private const string PIPE_NAME = "WinAuthRemaster_Activate";
    private static Mutex? _instanceMutex;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // 多重起動防止: Mutex で既存インスタンスを検出
        _instanceMutex = new Mutex(true, MUTEX_NAME, out bool isNewInstance);
        if (!isNewInstance)
        {
            // 既存インスタンスにアクティベート要求を送り、自身は終了
            try
            {
                using var client = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.Out);
                client.Connect(timeout: 2000);
                client.WriteByte(1);
            }
            catch (System.IO.IOException) { /* 既存インスタンスが応答しなくても終了する */ }
            Shutdown();
            return;
        }

        // アクティベート要求を受信するリスナー開始
        StartActivationListener();
        // Prevent shutdown when PasswordDialog (first window) closes before MainWindow exists
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        LocalizationService.ApplyLanguage(settings.Language);

        string configPath = settingsService.GetConfigFilePath(settings);
        var configService = new ConfigService(configPath);
        var clipboardService = new ClipboardService();
        var viewModel = new MainViewModel(configService, clipboardService);
        viewModel.SetLockTimeout(settings.LockTimeoutMinutes);

        if (configService.ConfigExists())
        {
            var protection = configService.DetectProtection();

            if (protection != ProtectionType.None &&
                protection != ProtectionType.Dpapi)
            {
                var dialog = new PasswordDialog(LocalizationService.Loc("PwTitle_WinAuth"), isSetMode: false);
                dialog.SetAuthValidator(pw =>
                {
                    try
                    {
                        viewModel.LoadConfig(pw);
                        return true;
                    }
                    catch (InvalidPasswordException)
                    {
                        return false;
                    }
                });

                if (dialog.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }
            else
            {
                try
                {
                    viewModel.LoadConfig(null);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load config:\n{ex.Message}", "WinAuth",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    viewModel.InitEmpty();
                }
            }
        }
        else
        {
            viewModel.InitEmpty();
        }

        var window = new MainWindow(viewModel, settingsService, settings);
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }

    // 別インスタンスからのアクティベート要求を Named Pipe で受信
    private void StartActivationListener()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PIPE_NAME, PipeDirection.In, 1);
                    server.WaitForConnection();
                    server.ReadByte();
                    Dispatcher.Invoke(() =>
                    {
                        if (MainWindow is Window w)
                        {
                            w.Show();
                            w.WindowState = WindowState.Normal;
                            w.Activate();
                        }
                    });
                }
                catch (System.IO.IOException) { break; }
                catch (ObjectDisposedException) { break; }
            }
        })
        {
            IsBackground = true,
            Name = "ActivationListener"
        };
        thread.Start();
    }
}
