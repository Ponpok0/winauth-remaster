using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using WinAuthRemaster.Crypto;
using WinAuthRemaster.Models;
using WinAuthRemaster.Services;
using static WinAuthRemaster.Services.LocalizationService;

namespace WinAuthRemaster.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    private readonly ClipboardService _clipboardService;
    private readonly DispatcherTimer _timer;

    private AppConfig _config;
    private SecureString? _password;
    private bool _isLocked;
    private int _lockTimeoutMinutes = 5;
    private DateTime _lastActivity = DateTime.UtcNow;
    private AuthenticatorItemViewModel? _selectedEntry;
    private string _filterText = "";

    public MainViewModel(ConfigService configService, ClipboardService clipboardService)
    {
        _configService = configService;
        _clipboardService = clipboardService;
        _config = new AppConfig();

        Entries = [];
        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = FilterEntries;

        CopyCommand = new RelayCommand(OnCopy, _ => _ is AuthenticatorItemViewModel);
        AddCommand = new RelayCommand(OnAdd);
        DeleteCommand = new RelayCommand(OnDelete, _ => _ is AuthenticatorItemViewModel);
        MoveUpCommand = new RelayCommand(OnMoveUp, _ => _ is AuthenticatorItemViewModel);
        MoveDownCommand = new RelayCommand(OnMoveDown, _ => _ is AuthenticatorItemViewModel);
        ToggleVisibilityCommand = new RelayCommand(OnToggleVisibility, _ => _ is AuthenticatorItemViewModel);
        RenameCommand = new RelayCommand(OnRename, _ => _ is AuthenticatorItemViewModel);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => OnTimerTick();
        _timer.Start();
    }

    public ObservableCollection<AuthenticatorItemViewModel> Entries { get; }
    public ICollectionView EntriesView { get; }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value))
                EntriesView.Refresh();
        }
    }

    public AuthenticatorItemViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set => SetField(ref _selectedEntry, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        private set
        {
            if (SetField(ref _isLocked, value))
                LockStateChanged?.Invoke(value);
        }
    }

    public bool HasEntries => Entries.Count > 0;
    public bool HasPassword => _config.Protection is ProtectionType.Password or ProtectionType.DpapiAndPassword;

    public ICommand CopyCommand { get; }
    public ICommand AddCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand ToggleVisibilityCommand { get; }
    public ICommand RenameCommand { get; }

    public event Func<Task<(string Name, string Secret, HmacAlgorithm Algorithm, int Period, int Digits)?>>? AddRequested;
    public event Action<string>? StatusMessage;
    public event Func<string, bool>? ConfirmDeleteRequested;
    public event Func<string, string?>? RenameRequested;
    public event Action<bool>? LockStateChanged;

    public void LoadConfig(SecureString? password)
    {
        _password = password;
        using var access = password.Reveal();
        _config = _configService.Load(access.Value);

        Entries.Clear();
        foreach (var entry in _config.Entries.OrderBy(e => e.SortOrder))
            Entries.Add(new AuthenticatorItemViewModel(entry));

        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(HasPassword));
    }

    public void InitEmpty()
    {
        _config = new AppConfig();
        Entries.Clear();
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(HasPassword));
    }

    public void SaveConfig()
    {
        _config.Entries = Entries.Select((vm, i) =>
        {
            vm.Entry.SortOrder = i;
            return vm.Entry;
        }).ToList();

        using var access = _password.Reveal();
        _configService.Save(_config, access.Value);
    }

    public void SetProtection(ProtectionType protection, SecureString? password)
    {
        _config.Protection = protection;
        _password = password;
        OnPropertyChanged(nameof(HasPassword));
        SaveConfig();
    }

    public void AddEntry(AuthenticatorEntry entry)
    {
        entry.SortOrder = Entries.Count;
        _config.Entries.Add(entry);
        Entries.Add(new AuthenticatorItemViewModel(entry));
        OnPropertyChanged(nameof(HasEntries));
        SaveConfig();
    }

    public void ImportEntries(IEnumerable<AuthenticatorEntry> entries)
    {
        int sortOrder = Entries.Count;
        foreach (var entry in entries)
        {
            entry.SortOrder = sortOrder++;
            _config.Entries.Add(entry);
            Entries.Add(new AuthenticatorItemViewModel(entry));
        }
        OnPropertyChanged(nameof(HasEntries));
        SaveConfig();
    }

    public void UpdateConfigPath(string newPath)
    {
        _configService.ConfigFilePath = newPath;
    }

    public void SetLockTimeout(int minutes)
    {
        _lockTimeoutMinutes = minutes;
    }

    public void ReportActivity()
    {
        _lastActivity = DateTime.UtcNow;
    }

    public void Lock()
    {
        IsLocked = true;
        foreach (var entry in Entries)
            entry.IsCodeVisible = false;
    }

    public bool TryUnlock(SecureString? password)
    {
        if (HasPassword)
        {
            using var input = password.Reveal();
            using var stored = _password.Reveal();
            if (input.Value != stored.Value)
                return false;
        }

        IsLocked = false;
        _lastActivity = DateTime.UtcNow;
        return true;
    }

    private void OnCopy(object? param)
    {
        if (param is AuthenticatorItemViewModel vm)
        {
            _clipboardService.CopyWithAutoClear(vm.CurrentCode);
        }
    }

    private async void OnAdd(object? _)
    {
        if (AddRequested is null) return;
        var result = await AddRequested.Invoke();
        if (result is null) return;

        var (name, secret, algorithm, period, digits) = result.Value;
        var entry = new AuthenticatorEntry
        {
            Name = name,
            SecretKey = Crypto.Base32.Decode(secret),
            Algorithm = algorithm,
            Period = period,
            Digits = digits
        };
        AddEntry(entry);
    }

    private void OnDelete(object? param)
    {
        if (param is not AuthenticatorItemViewModel vm) return;

        if (ConfirmDeleteRequested?.Invoke(vm.Name) != true) return;

        _configService.CreateBackup();

        _config.Entries.RemoveAll(e => e.Id == vm.Id);
        Entries.Remove(vm);
        OnPropertyChanged(nameof(HasEntries));
        SaveConfig();
        StatusMessage?.Invoke(Loc("Status_Deleted", vm.Name));
    }

    private void OnMoveUp(object? param)
    {
        if (param is not AuthenticatorItemViewModel vm) return;
        int index = Entries.IndexOf(vm);
        if (index <= 0) return;
        Entries.Move(index, index - 1);
        SaveConfig();
    }

    private void OnMoveDown(object? param)
    {
        if (param is not AuthenticatorItemViewModel vm) return;
        int index = Entries.IndexOf(vm);
        if (index < 0 || index >= Entries.Count - 1) return;
        Entries.Move(index, index + 1);
        SaveConfig();
    }

    private void OnRename(object? param)
    {
        if (param is not AuthenticatorItemViewModel vm) return;
        string? newName = RenameRequested?.Invoke(vm.Name);
        if (newName is null || newName == vm.Name) return;
        vm.Entry.Name = newName;
        vm.OnPropertyChanged(nameof(vm.Name));
        SaveConfig();
    }

    private void OnToggleVisibility(object? param)
    {
        if (param is AuthenticatorItemViewModel vm)
            vm.IsCodeVisible = !vm.IsCodeVisible;
    }

    private bool FilterEntries(object obj)
    {
        if (string.IsNullOrEmpty(_filterText)) return true;
        return obj is AuthenticatorItemViewModel vm &&
               vm.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase);
    }

    private void OnTimerTick()
    {
        foreach (var entry in Entries)
            entry.Refresh();

        if (!IsLocked && _lockTimeoutMinutes > 0)
        {
            var elapsed = DateTime.UtcNow - _lastActivity;
            if (elapsed.TotalMinutes >= _lockTimeoutMinutes)
                Lock();
        }
    }
}
