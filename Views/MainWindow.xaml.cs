using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using WinAuthRemaster.Models;
using WinAuthRemaster.Services;
using WinAuthRemaster.ViewModels;
using static WinAuthRemaster.Services.LocalizationService;

namespace WinAuthRemaster.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ImportService _importService = new();
    private readonly ExportService _exportService = new();
    private readonly SettingsService _settingsService;
    private AppSettings _settings;

    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private bool _isExiting;

    // 慣性スクロール用
    private double _scrollVelocity;
    private readonly DispatcherTimer _scrollTimer;

    // ステータスメッセージ自動消去用
    private readonly DispatcherTimer _statusClearTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    // パスワード試行のブルートフォース対策
    private UnlockCooldown? _unlockCooldown;

    // グローバルホットキー
    private readonly GlobalHotkeyService _hotkeyService = new();

    public MainWindow(MainViewModel viewModel, SettingsService settingsService, AppSettings settings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settingsService = settingsService;
        _settings = settings;
        DataContext = viewModel;

        _statusClearTimer.Tick += (_, _) => { _statusClearTimer.Stop(); StatusText.Text = ""; };
        viewModel.StatusMessage += ShowStatus;
        viewModel.AddRequested += OnAddRequested;
        viewModel.ConfirmDeleteRequested += OnConfirmDeleteRequested;
        viewModel.RenameRequested += OnRenameRequested;
        viewModel.LockStateChanged += OnLockStateChanged;

        PreviewMouseMove += (_, _) => viewModel.ReportActivity();
        PreviewMouseDown += (_, _) => viewModel.ReportActivity();
        PreviewKeyDown += OnPreviewKeyDown;

        // ドラッグ移動（トンネリングイベントで確実に捕捉）
        PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;

        // 慣性スクロール用タイマー（16ms ≈ 60fps）
        _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _scrollTimer.Tick += OnScrollTimerTick;

        InitDragReorder();
        SetupTrayIcon();
        UpdateLockUI(viewModel.IsLocked);
        ApplyTheme(settings.IsDarkMode, settings.WindowOpacity);
        RestoreWindowState(settings);

        // ウィンドウハンドル確定後にホットキーを登録
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            _hotkeyService.Toggled = OnHotkeyToggle;
            _hotkeyService.Initialize(hwnd);
            if (settings.HotkeyModifiers != null && settings.HotkeyKey != null)
                _hotkeyService.Register(settings.HotkeyModifiers.Value, settings.HotkeyKey.Value);
        };
    }

    // ウィンドウ位置・サイズ・ピン留めの復元
    private void RestoreWindowState(AppSettings settings)
    {
        // ピン留め復元
        if (settings.IsAlwaysOnTop)
        {
            Topmost = true;
            PinIcon.Text = "\uE718";
            PinIcon.Foreground = (Brush)FindResource("PrimaryBrush");
        }

        // サイズ復元
        if (settings.WindowHeight is > 0 and double h)
        {
            Height = Math.Clamp(h, MinHeight, SystemParameters.VirtualScreenHeight);
        }

        // 位置復元（画面内に収まるか検証）
        if (settings.WindowTop is double top && settings.WindowLeft is double left)
        {
            // 少なくともウィンドウの一部（50px）が見える位置にあるか
            bool isVisible =
                left + Width > SystemParameters.VirtualScreenLeft + 50 &&
                left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50 &&
                top > SystemParameters.VirtualScreenTop - 10 &&
                top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50;

            if (isVisible)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Top = top;
                Left = left;
            }
        }
    }

    // ウィンドウ状態を設定に保存
    private void SaveWindowState()
    {
        _settings.WindowTop = Top;
        _settings.WindowLeft = Left;
        _settings.WindowHeight = Height;
        _settings.IsAlwaysOnTop = Topmost;
        _settingsService.Save(_settings);
    }

    // テーマとOpacityを一括適用（Application レベルに設定）
    // Background/Surface/SurfaceHover はアルファで透過。テキスト系は不透明
    // ダイアログ側は MakeLocalBrushesOpaque で不透明に上書きして漏れを防ぐ
    private void ApplyTheme(bool isDark, double opacity)
    {
        var res = Application.Current.Resources;
        byte alpha = (byte)(Math.Clamp(opacity, 0.3, 1.0) * 255);

        if (isDark)
        {
            // Catppuccin Mocha — 背景系はアルファ付き
            res["BackgroundBrush"] = new SolidColorBrush(Color.FromArgb(alpha, 0x1E, 0x1E, 0x2E));
            res["SurfaceBrush"] = new SolidColorBrush(Color.FromArgb(alpha, 0x2A, 0x2A, 0x3C));
            res["SurfaceHoverBrush"] = new SolidColorBrush(Color.FromArgb(alpha, 0x35, 0x35, 0x4A));
            res["PrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x1E, 0x90, 0xFF));
            res["SecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
            res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4));
            res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0xA6, 0xAD, 0xC8));
            res["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x6C, 0x70, 0x86));
            res["WarningBrush"] = new SolidColorBrush(Color.FromRgb(0xFA, 0xB3, 0x87));
            res["DangerBrush"] = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
            res["BorderBrush"] = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A));
            res["MenuSurfaceBrush"] = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x3C));
            res["LockOverlayBrush"] = new SolidColorBrush(Color.FromArgb(0xE6, 0x1E, 0x1E, 0x2E));
        }
        else
        {
            // Catppuccin Latte — 背景系はアルファ付き
            res["BackgroundBrush"] = new SolidColorBrush(Color.FromArgb(alpha, 0xEF, 0xF1, 0xF5));
            res["SurfaceBrush"] = new SolidColorBrush(Color.FromArgb(alpha, 0xE6, 0xE9, 0xEF));
            res["SurfaceHoverBrush"] = new SolidColorBrush(Color.FromArgb(alpha, 0xDC, 0xE0, 0xE8));
            res["PrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x1E, 0x90, 0xFF));
            res["SecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x40, 0xA0, 0x2B));
            res["TextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0x4C, 0x4F, 0x69));
            res["TextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0x5C, 0x5F, 0x77));
            res["TextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x8C, 0x8F, 0xA1));
            res["WarningBrush"] = new SolidColorBrush(Color.FromRgb(0xFE, 0x64, 0x0B));
            res["DangerBrush"] = new SolidColorBrush(Color.FromRgb(0xD2, 0x0F, 0x39));
            res["BorderBrush"] = new SolidColorBrush(Color.FromRgb(0xBC, 0xC0, 0xCC));
            res["MenuSurfaceBrush"] = new SolidColorBrush(Color.FromRgb(0xE6, 0xE9, 0xEF));
            res["LockOverlayBrush"] = new SolidColorBrush(Color.FromArgb(0xE6, 0xEF, 0xF1, 0xF5));
        }
    }

    // ボタン等以外の領域でウィンドウをドラッグ移動
    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;

        // ドラッグハンドルのクリック → カード並び替え開始
        if (IsDragHandle(source))
        {
            StartDragReorder(source, e);
            return;
        }

        if (!IsInteractiveElement(source))
        {
            // フィルターボックス等からフォーカスを外し、ウィンドウ自体にフォーカスを移す
            // （ClearFocus だと PreviewKeyDown が発火しなくなるため Focus(this) を使う）
            FocusManager.SetFocusedElement(this, this);
            Keyboard.Focus(this);

            // DragMove中にマウスボタンが離れると例外が出る場合がある
            try { DragMove(); }
            catch (InvalidOperationException) { }
        }
    }

    private static bool IsInteractiveElement(DependencyObject element)
    {
        var current = element as DependencyObject;
        while (current != null)
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase
                or System.Windows.Controls.TextBox
                or System.Windows.Controls.PasswordBox
                or System.Windows.Controls.Primitives.ScrollBar
                or System.Windows.Controls.Slider
                or System.Windows.Controls.ComboBox)
                return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    // 慣性スクロール — ホイール入力で速度を加算し、タイマーで減衰
    private void OnScrollViewerWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        _scrollVelocity += -e.Delta * 0.1;
        if (!_scrollTimer.IsEnabled)
            _scrollTimer.Start();
    }

    private void OnScrollTimerTick(object? sender, EventArgs e)
    {
        MainScroll.ScrollToVerticalOffset(MainScroll.VerticalOffset + _scrollVelocity);
        _scrollVelocity *= 0.85; // 減衰係数

        if (Math.Abs(_scrollVelocity) < 0.5)
        {
            _scrollVelocity = 0;
            _scrollTimer.Stop();
        }
    }

    // System tray

    private void SetupTrayIcon()
    {
        var stream = Application.GetResourceStream(
            new Uri("pack://application:,,,/winauth.ico"))?.Stream;
        if (stream == null) return;

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream),
            Text = "WinAuth",
            Visible = true
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(Loc("Tray_Show"), null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(Loc("Tray_Exit"), null, (_, _) => Dispatcher.Invoke(ExitApplication));
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnHotkeyToggle()
    {
        if (IsVisible)
            Hide();
        else
            ShowFromTray();
    }

    // 数字キー 1-9 で該当スロットのコードをコピー
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        _viewModel.ReportActivity();

        // テキスト入力中は無視
        if (e.OriginalSource is System.Windows.Controls.TextBox
            or System.Windows.Controls.PasswordBox)
            return;

        // ロック中・修飾キー付きは無視
        if (_viewModel.IsLocked || Keyboard.Modifiers != ModifierKeys.None)
            return;

        int slot = e.Key switch
        {
            Key.D1 => 0, Key.D2 => 1, Key.D3 => 2, Key.D4 => 3, Key.D5 => 4,
            Key.D6 => 5, Key.D7 => 6, Key.D8 => 7, Key.D9 => 8,
            _ => -1
        };
        // フィルター適用中は表示中のアイテムを対象にする
        var visibleItems = _viewModel.EntriesView.Cast<AuthenticatorItemViewModel>().ToList();
        if (slot < 0 || slot >= visibleItems.Count) return;

        var vm = visibleItems[slot];
        _viewModel.CopyCommand.Execute(vm);

        // カードフラッシュ + コピーボタンテキストアニメーション
        var container = AuthList.ItemContainerGenerator.ContainerFromItem(vm);
        if (container != null)
        {
            FlashCard(container);
            var copyBtn = FindCopyButton(container);
            if (copyBtn != null)
                AnimateCopyButtonText(copyBtn);
        }

        e.Handled = true;
    }

    private void ExitApplication()
    {
        _isExiting = true;
        SaveWindowState();
        _hotkeyService.Dispose();
        _trayIcon?.Dispose();
        _trayIcon = null;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _trayIcon?.Dispose();
        _trayIcon = null;
        base.OnClosing(e);
    }

    // Lock / Unlock

    private void OnLockStateChanged(bool isLocked)
    {
        Dispatcher.Invoke(() => UpdateLockUI(isLocked));
    }

    private void UpdateLockUI(bool isLocked)
    {
        // ロック画面はオーバーレイではなく独立画面。メインコンテンツと排他的に切り替える
        var mainVis = isLocked ? Visibility.Collapsed : Visibility.Visible;
        Toolbar.Visibility = mainVis;
        MainScroll.Visibility = mainVis;
        StatusBar.Visibility = mainVis;
        LockScreen.Visibility = isLocked ? Visibility.Visible : Visibility.Collapsed;

        // ロック画面は常に不透明（ウィンドウ透過度に依存しない）
        if (isLocked)
        {
            bool isDark = _settings.IsDarkMode;
            LockScreen.Background = new SolidColorBrush(isDark
                ? Color.FromRgb(0x1E, 0x1E, 0x2E)
                : Color.FromRgb(0xEF, 0xF1, 0xF5));
        }

        if (!isLocked)
        {
            _unlockCooldown?.Reset();
            return;
        }

        bool hasPassword = _viewModel.HasPassword;
        LockPasswordPanel.Visibility = hasPassword ? Visibility.Visible : Visibility.Collapsed;
        SimpleLockButton.Visibility = hasPassword ? Visibility.Collapsed : Visibility.Visible;
        LockErrorText.Visibility = Visibility.Collapsed;
        if (hasPassword)
        {
            _unlockCooldown ??= new UnlockCooldown(LockPasswordInput, LockErrorText);
            _unlockCooldown.Reset();
            LockPasswordInput.Clear();
            LockPasswordInput.Focus();
        }
    }

    private void OnLockPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            OnUnlockClick(sender, e);
    }

    private void OnUnlockClick(object sender, RoutedEventArgs e)
    {
        if (_unlockCooldown?.IsCooldown == true) return;

        if (_viewModel.TryUnlock(LockPasswordInput.SecurePassword))
        {
            LockPasswordInput.Clear();
            _unlockCooldown?.Reset();
            // ロック解除後にウィンドウへフォーカスを移す（数字キーコピー等を即座に使えるように）
            FocusManager.SetFocusedElement(this, this);
            Keyboard.Focus(this);
            return;
        }

        _unlockCooldown?.OnFailed();
    }

    private void OnSimpleUnlockClick(object sender, RoutedEventArgs e)
    {
        _viewModel.TryUnlock(null);
        FocusManager.SetFocusedElement(this, this);
        Keyboard.Focus(this);
    }

    // Pin toggle

    private void OnPinToggle(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinIcon.Text = Topmost ? "\uE718" : "\uE77A";
        PinIcon.Foreground = Topmost
            ? (Brush)FindResource("PrimaryBrush")
            : (Brush)FindResource("TextMutedBrush");
    }

    // Settings

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        string resolvedPath = _settingsService.GetConfigFilePath(_settings);
        bool originalDarkMode = _settings.IsDarkMode;
        double originalOpacity = _settings.WindowOpacity;

        var dialog = new SettingsDialog(_settings, resolvedPath)
        {
            Owner = this,
            ImportRequested = () => OnImportClick(this, e),
            ExportRequested = () => OnExportClick(this, e),
            ChangePasswordRequested = () => OnChangePasswordClick(this, e),
            ThemePreview = isDark => ApplyTheme(isDark, originalOpacity),
            OpacityPreview = op => ApplyTheme(_settings.IsDarkMode, op)
        };
        if (dialog.ShowDialog() != true)
        {
            ApplyTheme(originalDarkMode, originalOpacity);
            return;
        }

        var newSettings = dialog.Result;
        string oldPath = _settingsService.GetConfigFilePath(_settings);
        string newPath = _settingsService.GetConfigFilePath(newSettings);

        if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            if (System.IO.File.Exists(oldPath))
            {
                string? dir = System.IO.Path.GetDirectoryName(newPath);
                if (dir != null && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                System.IO.File.Copy(oldPath, newPath, overwrite: true);
            }
            _viewModel.UpdateConfigPath(newPath);
        }

        _viewModel.SetLockTimeout(newSettings.LockTimeoutMinutes);
        ApplyTheme(newSettings.IsDarkMode, newSettings.WindowOpacity);
        if (newSettings.Language != _settings.Language)
            LocalizationService.ApplyLanguage(newSettings.Language);
        // ホットキーの再登録
        bool hotkeyChanged = newSettings.HotkeyModifiers != _settings.HotkeyModifiers
                          || newSettings.HotkeyKey != _settings.HotkeyKey;
        _settings = newSettings;
        _settingsService.Save(newSettings);

        if (hotkeyChanged)
        {
            _hotkeyService.Unregister();
            if (newSettings.HotkeyModifiers != null && newSettings.HotkeyKey != null)
            {
                if (!_hotkeyService.Register(newSettings.HotkeyModifiers.Value, newSettings.HotkeyKey.Value))
                    ShowStatus(Loc("Settings_HotkeyFailed"));
            }
        }

        ShowStatus(Loc("Status_SettingsSaved"));
    }

    // ステータスメッセージを表示し、3秒後に自動消去
    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        _statusClearTimer.Stop();
        _statusClearTimer.Start();
    }

    // トレイ格納
    private void OnCloseToTrayClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    // Exit
    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        ExitApplication();
    }
}
