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
    private readonly GlobalHotkeyService _hotkeyService = new();

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

        // グローバルホットキーを早期登録（PasswordDialog 表示中も有効にする）
        _hotkeyService.Initialize();
        if (settings.HotkeyModifiers != null && settings.HotkeyKey != null)
            _hotkeyService.Register(settings.HotkeyModifiers.Value, settings.HotkeyKey.Value);

        string configPath = settingsService.GetConfigFilePath(settings);
        var configService = new ConfigService(configPath);
        var clipboardService = new ClipboardService();
        var viewModel = new MainViewModel(configService, clipboardService);
        viewModel.SetLockTimeout(settings.LockTimeoutMinutes);

        // 認証ダイアログを経由した場合、ユーザーが引き出して認証を通したなら
        // メインウィンドウは通常表示にする
        bool startMinimized = settings.StartMinimized;

        if (configService.ConfigExists())
        {
            var protection = configService.DetectProtection();

            if (protection != ProtectionType.None &&
                protection != ProtectionType.Dpapi)
            {
                var dialog = new PasswordDialog(LocalizationService.Loc("PwTitle_WinAuth"), isSetMode: false);

                // 最小化起動: PasswordDialog を最小化状態で表示
                if (startMinimized)
                {
                    dialog.SourceInitialized += (_, _) =>
                    {
                        dialog.WindowState = WindowState.Minimized;
                        dialog.ShowInTaskbar = false;
                    };
                }

                // PasswordDialog 表示中のホットキーでダイアログを最小化/復帰
                _hotkeyService.Toggled = () => Dispatcher.Invoke(() =>
                {
                    if (dialog.WindowState == WindowState.Minimized)
                    {
                        dialog.ShowInTaskbar = true;
                        dialog.WindowState = WindowState.Normal;
                        dialog.Activate();
                    }
                    else if (!dialog.IsActive)
                    {
                        dialog.Activate();
                    }
                    else
                    {
                        dialog.WindowState = WindowState.Minimized;
                        dialog.ShowInTaskbar = false;
                    }
                });

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
                    _hotkeyService.Dispose();
                    Shutdown();
                    return;
                }

                // 認証を通した時点でダイアログは表示状態 → メインウィンドウも通常表示
                startMinimized = false;
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

        var window = new MainWindow(viewModel, settingsService, settings, _hotkeyService);
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        if (startMinimized)
        {
            // タスクトレイに格納した状態で起動
            window.WindowState = WindowState.Minimized;
            window.ShowInTaskbar = false;
            window.Show();
            window.Hide();
            // Minimized + Hide で不可視にしつつ HWND を確立する
            // 以後は HideToTray/ShowFromTray で制御される
        }
        else
        {
            window.Show();
        }
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
                        if (MainWindow is MainWindow w)
                        {
                            w.ShowFromTray();
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
