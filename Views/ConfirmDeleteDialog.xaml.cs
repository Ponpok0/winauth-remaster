using System.Windows;
using System.Windows.Input;
using WinAuthRemaster.Extensions;
using static WinAuthRemaster.Services.LocalizationService;

namespace WinAuthRemaster.Views;

public partial class ConfirmDeleteDialog : Window
{
    public ConfirmDeleteDialog(string entryName)
    {
        InitializeComponent();
        this.MakeLocalBrushesOpaque();
        MessageText.Text = Loc("Delete_Message", entryName);
        ConfirmInput.Focus();
    }

    private void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        DeleteButton.IsEnabled = string.Equals(
            ConfirmInput.Text.Trim(), "delete me", StringComparison.OrdinalIgnoreCase);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DeleteButton.IsEnabled)
            OnDelete(sender, e);
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

}
