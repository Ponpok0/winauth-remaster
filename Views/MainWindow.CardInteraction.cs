using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinAuthRemaster.Crypto;
using WinAuthRemaster.Models;
using WinAuthRemaster.ViewModels;
using static WinAuthRemaster.Services.LocalizationService;
using WpfButton = System.Windows.Controls.Button;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfContextMenu = System.Windows.Controls.ContextMenu;

namespace WinAuthRemaster.Views;

public partial class MainWindow
{
    // カードメニューが開かれた時の対象アイテム（サブメニューからも参照）
    private AuthenticatorItemViewModel? _cardMenuTarget;

    // ハンバーガーメニューを開く
    private void OnHamburgerClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    // ハンバーガーメニューの「新規作成」
    private void OnMenuAddClick(object sender, RoutedEventArgs e)
    {
        _viewModel.AddCommand.Execute(null);
    }

    // ▼ カードメニューボタンのクリックで ContextMenu を開く
    private void OnMoreActionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { ContextMenu: { } menu, Tag: AuthenticatorItemViewModel vm } button)
        {
            _cardMenuTarget = vm;
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void OnSetCardColorClick(object sender, RoutedEventArgs e)
    {
        if (_cardMenuTarget is not { } item) return;
        var color = (sender as WpfMenuItem)?.Tag as string;
        item.CardColor = string.IsNullOrEmpty(color) ? null : color;
        _viewModel.SaveConfig();
    }

    private void OnMenuRenameClick(object sender, RoutedEventArgs e)
    {
        if (_cardMenuTarget is { } item)
            _viewModel.RenameCommand.Execute(item);
    }

    private void OnMenuDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_cardMenuTarget is { } item)
            _viewModel.DeleteCommand.Execute(item);
    }

    // コピーボタンクリック時にカードフラッシュ + ボタンテキスト一時変更
    private void OnCopyButtonClick(object sender, RoutedEventArgs e)
    {
        FlashCard(sender as DependencyObject);
        if (sender is WpfButton btn)
            AnimateCopyButtonText(btn);
    }

    private static void FlashCard(DependencyObject? source)
    {
        if (source == null) return;
        // 親方向（ボタンクリック時）と子方向（スロットコピー時）の両方で探す
        var flash = FindCardFlash(source)
                    ?? FindDescendant<System.Windows.Controls.Border>(source, b => b.Name == "cardFlash");
        if (flash == null) return;
        flash.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0.35, 0, new Duration(TimeSpan.FromMilliseconds(200))));
    }

    private void AnimateCopyButtonText(WpfButton btn)
    {
        string original = Loc("Btn_Copy");
        double currentWidth = btn.ActualWidth;

        btn.Content = Loc("Toast_Copied");
        btn.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        double expandedWidth = btn.DesiredSize.Width;

        if (expandedWidth > currentWidth)
        {
            btn.BeginAnimation(WidthProperty, new DoubleAnimation(currentWidth, expandedWidth, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }

        // 0.5秒後にテキストと幅を元に戻す
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            btn.Content = original;
            double restoreWidth = expandedWidth > currentWidth ? expandedWidth : btn.ActualWidth;
            var shrink = new DoubleAnimation(restoreWidth, currentWidth, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            shrink.Completed += (_, _) => btn.BeginAnimation(WidthProperty, null);
            btn.BeginAnimation(WidthProperty, shrink);
        };
        timer.Start();
    }

    // VisualTree からコピーボタンを探す（スロットコピーのフィードバック用）
    private WpfButton? FindCopyButton(DependencyObject? container)
    {
        if (container == null) return null;
        var copyStyle = TryFindResource("CopyButton") as System.Windows.Style;
        return FindDescendant<WpfButton>(container, btn => btn.Style == copyStyle);
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && predicate(match))
                return match;
            var found = FindDescendant(child, predicate);
            if (found != null) return found;
        }
        return null;
    }

    private static System.Windows.Controls.Border? FindCardFlash(DependencyObject? start)
    {
        var current = start;
        while (current != null)
        {
            var parent = VisualTreeHelper.GetParent(current);
            if (parent is System.Windows.Controls.Grid grid)
            {
                for (int i = VisualTreeHelper.GetChildrenCount(grid) - 1; i >= 0; i--)
                {
                    if (VisualTreeHelper.GetChild(grid, i) is System.Windows.Controls.Border { Name: "cardFlash" } found)
                        return found;
                }
            }
            current = parent;
        }
        return null;
    }

    // Delete confirmation

    private bool OnConfirmDeleteRequested(string entryName)
    {
        var dialog = new ConfirmDeleteDialog(entryName) { Owner = this };
        return dialog.ShowDialog() == true;
    }

    // Rename

    private string? OnRenameRequested(string currentName)
    {
        var dialog = new RenameDialog(currentName) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.NewName : null;
    }

    // Add authenticator

    private Task<(string Name, string Secret, HmacAlgorithm Algorithm, int Period, int Digits)?> OnAddRequested()
    {
        var dialog = new AddAuthenticatorDialog { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            return Task.FromResult<(string, string, HmacAlgorithm, int, int)?>
                ((dialog.AuthName, dialog.Secret, dialog.Algorithm, dialog.Period, dialog.Digits));
        }
        return Task.FromResult<(string, string, HmacAlgorithm, int, int)?>(null);
    }
}
