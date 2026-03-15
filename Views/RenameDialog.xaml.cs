using System.Windows;
using System.Windows.Input;
using WinAuthRemaster.Extensions;

namespace WinAuthRemaster.Views;

public partial class RenameDialog : Window
{
    public string NewName => NameInput.Text.Trim();

    public RenameDialog(string currentName)
    {
        InitializeComponent();
        this.MakeLocalBrushesOpaque();
        NameInput.Text = currentName;
        Loaded += (_, _) =>
        {
            NameInput.Focus();
            NameInput.SelectAll();
        };
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameInput.Text)) return;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnOk(sender, e);
        else if (e.Key == Key.Escape) OnCancel(sender, e);
    }

}
