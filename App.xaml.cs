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
    private const double MAIN_WINDOW_WIDTH = 394;
    private const double MAIN_WINDOW_INITIAL_HEIGHT = 420;
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
        Views.MainWindow.ApplyTheme(settings.IsDarkMode, settings.WindowOpacity);

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
                bool hasHotkey = settings.HotkeyModifiers != null && settings.HotkeyKey != null;
                var dialog = new PasswordDialog(LocalizationService.Loc("PwTitle_WinAuth"), isSetMode: false)
                {
                    CanHideToTray = hasHotkey
                };
                RestoreWindowPosition(dialog, settings);

                // 最小化起動: PasswordDialog を最小化状態で表示
                if (startMinimized)
                {
                    dialog.SourceInitialized += (_, _) =>
                    {
                        dialog.WindowState = WindowState.Minimized;
                        if (hasHotkey)
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
            // SourceInitialized で最小化し、ウィンドウのフラッシュを防ぐ
            window.SourceInitialized += (_, _) =>
            {
                window.WindowState = WindowState.Minimized;
                window.ShowInTaskbar = false;
            };
            window.Show();
            window.Hide();
            // Show → Hide で HWND を確立しつつ不可視にする
        }
        else
        {
            window.Show();
        }
    }

    // 保存済みのウィンドウ位置を復元（PasswordDialog 等、MainWindow 以外にも適用）
    private static void RestoreWindowPosition(Window window, AppSettings settings)
    {
        if (settings.WindowTop is not double top || settings.WindowLeft is not double left)
            return;

        // MainWindow とウィンドウサイズが異なる場合、中央を揃える
        double offsetX = (MAIN_WINDOW_WIDTH - window.Width) / 2;
        double offsetY = (MAIN_WINDOW_INITIAL_HEIGHT - window.Height) / 2;
        if (settings.WindowHeight is > 0 and double h)
            offsetY = (h - window.Height) / 2;

        double x = left + offsetX;
        double y = top + offsetY;

        bool isVisible =
            x + window.Width > SystemParameters.VirtualScreenLeft + 50 &&
            x < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50 &&
            y > SystemParameters.VirtualScreenTop - 10 &&
            y < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50;

        if (isVisible)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Top = y;
            window.Left = x;
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
