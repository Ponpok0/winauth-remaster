using WinAuthRemaster.Crypto;
using WinAuthRemaster.Models;

namespace WinAuthRemaster.ViewModels;

public sealed class AuthenticatorItemViewModel : ViewModelBase
{
    private readonly AuthenticatorEntry _entry;
    private bool _isCodeVisible;

    public AuthenticatorItemViewModel(AuthenticatorEntry entry)
    {
        _entry = entry;
    }

    public Guid Id => _entry.Id;
    public string Name => _entry.Name;
    public string Issuer => _entry.Issuer;
    public int Period => _entry.Period;
    public AuthenticatorEntry Entry => _entry;

    public string? CardColor
    {
        get => _entry.CardColor;
        set
        {
            if (_entry.CardColor == value) return;
            _entry.CardColor = value;
            OnPropertyChanged();
        }
    }

    public bool IsCodeVisible
    {
        get => _isCodeVisible;
        set
        {
            if (SetField(ref _isCodeVisible, value))
                OnPropertyChanged(nameof(DisplayCode));
        }
    }

    public string CurrentCode
    {
        get
        {
            if (_entry.SecretKey.Length == 0) return "------";
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return TotpGenerator.GenerateCode(_entry.SecretKey, nowMs, _entry.Period, _entry.Digits, _entry.Algorithm);
        }
    }

    public string FormattedCode => AuthenticatorEntry.FormatCode(CurrentCode);

    public string DisplayCode => IsCodeVisible ? FormattedCode : MaskedCode;

    private string MaskedCode => _entry.Digits == 8 ? "**** ****" : "*** ***";

    public int RemainingSeconds => TotpGenerator.GetRemainingSeconds(_entry.Period);

    public double Progress => (double)RemainingSeconds / _entry.Period;

    public new void OnPropertyChanged(string? name = null)
        => base.OnPropertyChanged(name);

    public void Refresh()
    {
        OnPropertyChanged(nameof(CurrentCode));
        OnPropertyChanged(nameof(FormattedCode));
        OnPropertyChanged(nameof(DisplayCode));
        OnPropertyChanged(nameof(RemainingSeconds));
        OnPropertyChanged(nameof(Progress));
    }
}
