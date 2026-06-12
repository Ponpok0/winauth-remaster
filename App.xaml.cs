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
    // パイプ名前空間はマシングローバルのため、ユーザー SID を付与してセッション間の
    // 衝突（別ユーザーセッションでのリスナー生成失敗）を防ぐ。Mutex も対応を揃える
    private static readonly string USER_SID = GetCurrentUserSid();
    private static readonly string MUTEX_NAME = "WinAuthRemaster_SingleInstance_" + USER_SID;
    private static readonly string PIPE_NAME = "WinAuthRemaster_Activate_" + USER_SID;
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
            // スタートアップ起動（--minimized）の2重起動はサイレントに終了する
            // （二重登録等の場合に、サイレント起動が既存インスタンスの前面化に化けるのを防ぐ）
            if (!e.Args.Contains(StartupService.MinimizedArg))
            {
                // 既存インスタンスにアクティベート要求を送り、自身は終了
                try
                {
                    using var client = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.Out);
                    client.Connect(timeout: 2000);
                    client.WriteByte(1);
                }
                catch (System.IO.IOException) { /* 既存インスタンスが応答しなくても終了する */ }
                catch (TimeoutException) { /* Connect のタイムアウト。同上 */ }
            }
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
        bool hotkeyRegistered = false;
        if (settings.HotkeyModifiers != null && settings.HotkeyKey != null)
            hotkeyRegistered = _hotkeyService.Register(settings.HotkeyModifiers.Value, settings.HotkeyKey.Value);

        string configPath = settingsService.GetConfigFilePath(settings);
        var configService = new ConfigService(configPath);
        var clipboardService = new ClipboardService();
        var viewModel = new MainViewModel(configService, clipboardService);

        // 旧形式（--minimized なし）のスタートアップ登録を現行形式へ移行
        StartupService.MigrateIfOutdated();

        // 最小化起動はスタートアップ起動（--minimized 付き）のみに適用する。
        // 手動起動では設定に関わらず表示する（不可視起動だと起動の成否が判別できない）
        bool startMinimized = settings.StartMinimized && e.Args.Contains(StartupService.MinimizedArg);

        if (configService.ConfigExists())
        {
            var protection = configService.DetectProtection();

            if (protection != ProtectionType.None &&
                protection != ProtectionType.Dpapi)
            {
                // 登録の「成功」を条件にする: 他アプリとのキー衝突で登録に失敗した場合に
                // ホットキー前提の不可視化（画面外退避）を行うと、復帰手段が細るため
                bool hasHotkey = hotkeyRegistered;
                var dialog = new PasswordDialog(LocalizationService.Loc("PwTitle_WinAuth"), isSetMode: false)
                {
                    CanHideToTray = hasHotkey
                };
                RestoreWindowPosition(dialog, settings);

                // 最小化起動: PasswordDialog を不可視で開始
                if (startMinimized)
                {
                    if (hasHotkey)
                    {
                        // Minimized を経由しない: 透過ウィンドウ（AllowsTransparency）は
                        // 表示前に最小化すると復元サイズが壊れ、画面左下にミニウィンドウ
                        // として残る。Opacity=0 で初回描画を隠し、描画完了後（実位置の
                        // 確定後）に画面外へ退避する。Hide() は ShowDialog を終了させて
                        // しまうため使えない
                        dialog.ShowActivated = false;
                        dialog.ShowInTaskbar = false;
                        dialog.Opacity = 0;
                        dialog.HideOnFirstRender = true;
                    }
                    else
                    {
                        // ホットキーなし: タスクバーから復帰できるよう最小化で開始
                        dialog.SourceInitialized += (_, _) =>
                        {
                            dialog.WindowState = WindowState.Minimized;
                        };
                    }
                }

                // PasswordDialog 表示中のホットキーで非表示/復帰を切り替え
                _hotkeyService.Toggled = () => Dispatcher.Invoke(() =>
                {
                    if (dialog.IsInvisible)
                    {
                        dialog.RestoreOnScreen();
                    }
                    else if (!dialog.IsActive)
                    {
                        dialog.Activate();
                    }
                    else
                    {
                        dialog.HideOffScreen();
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

                // 認証前トレイアイコン: MainWindow のトレイアイコンは認証完了後にしか
                // 存在しないため、最小化起動中でもアプリの存在と復帰手段をトレイで提供する
                var preAuthTray = CreatePreAuthTrayIcon(dialog);
                bool authenticated = dialog.ShowDialog() == true;
                preAuthTray?.Dispose();

                if (!authenticated)
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

        // 認証完了後に有効化する。起動画面（PasswordDialog）表示中に
        // ロックが発動すると、ログイン直後にロック解除を再要求してしまうため
        viewModel.SetLockTimeout(settings.LockTimeoutMinutes);

        var window = new MainWindow(viewModel, settingsService, settings, _hotkeyService);
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        if (startMinimized)
        {
            // Minimized を経由せず不可視で開始: 透過ウィンドウ（AllowsTransparency）は
            // 表示前に最小化すると復元サイズが壊れ、画面左下にミニウィンドウとして残る。
            // Opacity=0 の Show → Hide で HWND とレイアウトだけ確立する（復帰はトレイ等から）
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Opacity = 0;
            window.Show();
            window.Hide();
            window.Opacity = 1;
            window.ShowActivated = true;
        }
        else
        {
            window.Show();
        }
    }

    // 認証前（PasswordDialog 表示中）のトレイアイコンを作成する。
    // 最小化起動中でもアプリの存在をトレイで可視化し、復帰・終了手段を提供する
    private static System.Windows.Forms.NotifyIcon? CreatePreAuthTrayIcon(PasswordDialog dialog)
    {
        var stream = GetResourceStream(new Uri("pack://application:,,,/winauth.ico"))?.Stream;
        if (stream == null) return null;

        var trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream),
            Text = "WinAuth",
            Visible = true
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(LocalizationService.Loc("Tray_Show"), null,
            (_, _) => dialog.Dispatcher.Invoke(dialog.RestoreOnScreen));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(LocalizationService.Loc("Tray_Exit"), null,
            (_, _) => dialog.Dispatcher.Invoke(() => dialog.DialogResult = false));
        trayIcon.ContextMenuStrip = menu;
        trayIcon.DoubleClick += (_, _) => dialog.Dispatcher.Invoke(dialog.RestoreOnScreen);
        return trayIcon;
    }

    // 現在ユーザーの SID（取得不能時はユーザー名にフォールバック）
    private static string GetCurrentUserSid()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? Environment.UserName;
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
                        else if (Windows.OfType<PasswordDialog>().FirstOrDefault() is { } dialog)
                        {
                            // 認証前: 不可視待機中の PasswordDialog を前面に出す
                            dialog.RestoreOnScreen();
                        }
                    });
                }
                catch (System.IO.IOException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (OperationCanceledException) { break; }  // シャットダウン中の Dispatcher.Invoke
                catch (UnauthorizedAccessException) { break; } // パイプ生成のアクセス拒否
            }
        })
        {
            IsBackground = true,
            Name = "ActivationListener"
        };
        thread.Start();
    }
}
