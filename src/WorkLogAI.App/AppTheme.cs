using System.Windows;
using System.Windows.Media;
using FontFamily = System.Windows.Media.FontFamily;

namespace WorkLogAI.App;

/// <summary>
/// Applies the shared light-theme look (window background, base font, pixel
/// snapping) to a window. Call at the very start of each window's constructor —
/// any control that needs a different value (e.g. QuickCaptureWindow's larger
/// input FontSize) sets it as a local value afterward, which always wins over the
/// inherited window-level value regardless of call order.
/// </summary>
public static class AppTheme
{
    private static readonly SolidColorBrush WindowBackground = new(Color.FromRgb(0xF7, 0xF8, 0xFA));
    private static readonly FontFamily BaseFontFamily = new("Yu Gothic UI, Segoe UI, sans-serif");

    public static void Apply(Window window)
    {
        window.Background = WindowBackground;
        window.FontFamily = BaseFontFamily;
        window.FontSize = 13;
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;
    }
}
