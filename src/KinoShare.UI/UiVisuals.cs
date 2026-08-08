namespace KinoShare.UI;

using KinoShare.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

/// <summary>
/// Static helpers used by x:Bind expressions in MainWindow.xaml. x:Bind can
/// call public static methods with Mode=OneWay bindings.
/// </summary>
public static class UiVisuals
{
    /// <summary>Returns the dot color for a session status.</summary>
    public static Brush SessionDotBrush(SessionStatus status) => status switch
    {
        SessionStatus.Running => new SolidColorBrush(Color.FromArgb(255, 46, 125, 50)),
        SessionStatus.Starting => new SolidColorBrush(Color.FromArgb(255, 124, 77, 255)),
        SessionStatus.Failed => new SolidColorBrush(Color.FromArgb(255, 198, 40, 40)),
        SessionStatus.Stopped => new SolidColorBrush(Color.FromArgb(255, 90, 90, 90)),
        _ => new SolidColorBrush(Color.FromArgb(255, 158, 158, 158)),
    };

    /// <summary>Returns the glyph for the theme toggle (sun/moon).</summary>
    public static string ThemeGlyph(bool isDark) => isDark
        ? "\uE706"  // Sunny
        : "\uE708"; // Clear night

    /// <summary>Returns the glyph for a transfer direction.</summary>
    public static string DirectionGlyph(bool isReceived) => isReceived
        ? "\uE896"  // Download
        : "\uE898"; // Upload

    /// <summary>Returns the brush for a transfer direction.</summary>
    public static Brush DirectionBrush(bool isReceived) => isReceived
        ? new SolidColorBrush(Color.FromArgb(255, 46, 125, 50))
        : new SolidColorBrush(Color.FromArgb(255, 124, 77, 255));

    /// <summary>Converts a bool to a Visibility.</summary>
    public static Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
}
