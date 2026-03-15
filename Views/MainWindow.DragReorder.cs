using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinAuthRemaster.ViewModels;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPanel = System.Windows.Controls.Panel;

namespace WinAuthRemaster.Views;

public partial class MainWindow
{
    // ドラッグ並び替え用フィールド
    private AuthenticatorItemViewModel? _dragItem;
    private FrameworkElement? _dragContainer;
    private bool _isDragging;
    private Point _dragStartPoint;
    private int _dragInitialIndex;
    private int _dragCurrentIndex;
    private double _cardHeight;
    private double _dragStartScrollOffset;
    private const double DRAG_START_THRESHOLD = 5;

    private void InitDragReorder()
    {
        PreviewMouseMove += OnDragMouseMove;
        PreviewMouseLeftButtonUp += OnDragMouseUp;
    }

    /// <summary>ドラッグハンドルの要素かどうか判定（Tag="DragHandle" を持つ祖先を探す）</summary>
    private static bool IsDragHandle(DependencyObject element)
    {
        var current = element;
        while (current != null)
        {
            if (current is FrameworkElement { Tag: "DragHandle" })
                return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    /// <summary>ドラッグ並び替え開始</summary>
    private void StartDragReorder(DependencyObject source, MouseButtonEventArgs e)
    {
        var vm = FindAncestorDataContext<AuthenticatorItemViewModel>(source);
        if (vm == null) return;

        _dragItem = vm;
        _isDragging = false;
        _dragStartPoint = e.GetPosition(MainScroll);
        _dragStartScrollOffset = MainScroll.VerticalOffset;
        _dragInitialIndex = _viewModel.Entries.IndexOf(vm);
        _dragCurrentIndex = _dragInitialIndex;
        _dragContainer = null;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnDragMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItem == null) return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDragReorder(save: false);
            return;
        }

        var pos = e.GetPosition(MainScroll);
        if (!_isDragging && !TryBeginDrag(pos))
            return;

        double deltaY = CalcScrollAdjustedDelta(pos);
        UpdateDragPosition(deltaY);
        AutoScrollDuringDrag(pos.Y);
    }

    /// <summary>閾値を超えたらドラッグ開始。コンテナ取得とZIndex設定</summary>
    private bool TryBeginDrag(Point pos)
    {
        if (Math.Abs(pos.Y - _dragStartPoint.Y) < DRAG_START_THRESHOLD)
            return false;

        _isDragging = true;
        Cursor = System.Windows.Input.Cursors.SizeNS;

        // カード高さは全アイテム均一（同一DataTemplate）前提
        _dragContainer = AuthList.ItemContainerGenerator.ContainerFromItem(_dragItem!) as FrameworkElement;
        _cardHeight = _dragContainer?.ActualHeight ?? 100;
        if (_dragContainer != null)
        {
            _dragContainer.IsHitTestVisible = false;
            WpfPanel.SetZIndex(_dragContainer, 1);
        }
        return true;
    }

    /// <summary>スクロール補正込みのマウス移動量を算出</summary>
    private double CalcScrollAdjustedDelta(Point pos)
    {
        double scrollDelta = MainScroll.VerticalOffset - _dragStartScrollOffset;
        return (pos.Y - _dragStartPoint.Y) + scrollDelta;
    }

    /// <summary>インデックス入れ替え判定 + ドラッグカードのビジュアル追従</summary>
    private void UpdateDragPosition(double deltaY)
    {
        // ターゲットインデックスを算出（カード半分超えで入れ替え発動）
        int posOffset = (int)Math.Round(deltaY / _cardHeight, MidpointRounding.AwayFromZero);
        int targetIndex = Math.Clamp(
            _dragInitialIndex + posOffset, 0, _viewModel.Entries.Count - 1);

        // 入れ替え（先に実行してからビジュアルを更新）
        if (targetIndex != _dragCurrentIndex)
        {
            SwapWithAnimation(_dragCurrentIndex, targetIndex);
            _dragCurrentIndex = targetIndex;
        }

        // ドラッグカードの追従（レイアウト位置からの残余オフセット）
        if (_dragContainer != null)
        {
            double layoutShift = (_dragCurrentIndex - _dragInitialIndex) * _cardHeight;
            double visualOffset = deltaY - layoutShift;
            if (_dragContainer.RenderTransform is TranslateTransform tt)
                tt.Y = visualOffset;
            else
                _dragContainer.RenderTransform = new TranslateTransform(0, visualOffset);
        }
    }

    private void OnDragMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragItem == null) return;
        EndDragReorder(save: _isDragging);
        e.Handled = true;
    }

    private void EndDragReorder(bool save)
    {
        if (_isDragging && _dragContainer != null)
        {
            var container = _dragContainer;
            container.IsHitTestVisible = true;
            WpfPanel.SetZIndex(container, 0);

            if (container.RenderTransform is TranslateTransform tt && Math.Abs(tt.Y) > 1)
            {
                var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                anim.Completed += (_, _) => container.RenderTransform = null;
                tt.BeginAnimation(TranslateTransform.YProperty, anim);
            }
            else
            {
                container.RenderTransform = null;
            }
        }

        ReleaseMouseCapture();
        Cursor = null;
        _dragItem = null;
        _isDragging = false;
        _dragContainer = null;

        if (save)
            _viewModel.SaveConfig();
    }

    /// <summary>コレクション内で Move し、押し出されるカードをスライドアニメーション</summary>
    private void SwapWithAnimation(int fromIndex, int toIndex)
    {
        bool movingDown = fromIndex < toIndex;
        int start = movingDown ? fromIndex + 1 : toIndex;
        int end = movingDown ? toIndex : fromIndex - 1;

        // 押し出されるアイテムを Move 前に記録
        var displaced = new List<AuthenticatorItemViewModel>();
        for (int i = start; i <= end; i++)
            displaced.Add(_viewModel.Entries[i]);

        // ドラッグコンテナの状態を一旦クリア
        if (_dragContainer != null)
        {
            _dragContainer.IsHitTestVisible = true;
            WpfPanel.SetZIndex(_dragContainer, 0);
            _dragContainer.RenderTransform = null;
        }

        _viewModel.Entries.Move(fromIndex, toIndex);

        // 新コンテナを再取得してドラッグ状態を復元
        _dragContainer = AuthList.ItemContainerGenerator.ContainerFromItem(_dragItem!) as FrameworkElement;
        if (_dragContainer != null)
        {
            _dragContainer.IsHitTestVisible = false;
            WpfPanel.SetZIndex(_dragContainer, 1);
        }

        // 押し出されるカードのスライドアニメーション（レイアウト確定後）
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            foreach (var vm in displaced)
            {
                var container = AuthList.ItemContainerGenerator.ContainerFromItem(vm) as FrameworkElement;
                if (container == null) continue;

                double ch = container.ActualHeight;
                double fromY = movingDown ? ch : -ch;

                var transform = new TranslateTransform(0, fromY);
                container.RenderTransform = transform;
                transform.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(0, TimeSpan.FromMilliseconds(150))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    });
            }
        });
    }

    /// <summary>ドラッグ中に上下端付近でスクロールを自動実行</summary>
    private void AutoScrollDuringDrag(double mouseY)
    {
        const double scrollZone = 40;
        const double scrollSpeed = 8;

        double viewHeight = MainScroll.ActualHeight;
        if (mouseY < scrollZone)
            MainScroll.ScrollToVerticalOffset(MainScroll.VerticalOffset - scrollSpeed);
        else if (mouseY > viewHeight - scrollZone)
            MainScroll.ScrollToVerticalOffset(MainScroll.VerticalOffset + scrollSpeed);
    }

    /// <summary>ビジュアルツリーを遡って指定型の DataContext を検索</summary>
    private static T? FindAncestorDataContext<T>(DependencyObject? element) where T : class
    {
        var current = element;
        while (current != null)
        {
            if (current is FrameworkElement { DataContext: T ctx })
                return ctx;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
