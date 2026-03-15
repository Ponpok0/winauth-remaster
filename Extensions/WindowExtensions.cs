using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace WinAuthRemaster.Extensions;

public static class WindowExtensions
{
    private static readonly string[] TransparentBrushKeys =
        ["BackgroundBrush", "SurfaceBrush", "SurfaceHoverBrush"];

    /// <summary>
    /// アプリレベルの透過ブラシがダイアログに漏れないよう、ローカルResourcesで不透明に上書きする。
    /// SettingsDialogではテーマ・透過度プレビュー変更時にも呼び出す。
    /// </summary>
    public static void MakeLocalBrushesOpaque(this FrameworkElement element)
    {
        foreach (string key in TransparentBrushKeys)
        {
            if (Application.Current.Resources[key] is SolidColorBrush brush)
            {
                var c = brush.Color;
                element.Resources[key] = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
            }
        }
    }
}
