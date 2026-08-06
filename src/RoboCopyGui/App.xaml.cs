using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace RoboCopyGui;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplySystemTheme();
    }

    private void ApplySystemTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var isDark = key?.GetValue("AppsUseLightTheme") is int useLightTheme && useLightTheme == 0;
        if (!isDark)
        {
            return;
        }

        Resources["WindowBackgroundBrush"] = Brush("#FF0F172A");
        Resources["SurfaceBrush"] = Brush("#FF172033");
        Resources["SurfaceAlternativeBrush"] = Brush("#FF202B40");
        Resources["TextBrush"] = Brush("#FFF1F5F9");
        Resources["MutedTextBrush"] = Brush("#FF9BA9BD");
        Resources["BorderBrush"] = Brush("#FF344159");
        Resources["AccentSoftBrush"] = Brush("#FF1E3A66");
        Resources["DangerSoftBrush"] = Brush("#FF4A2028");
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
}
