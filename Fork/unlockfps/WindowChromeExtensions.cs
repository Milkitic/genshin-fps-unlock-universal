using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Platform;

namespace UnlockFps;

public static class WindowChromeExtensions
{
    public static void SetSystemChrome(this Window window)
    {
        window.Loaded += WindowLoaded;
        window.Unloaded += WindowUnloaded;

        SetWindowChrome(window);

        void OnPlatformSettingsOnColorValuesChanged(object? sender, PlatformColorValues e)
        {
            SetWindowChrome(window, e);
        }

        void WindowLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            window.Activated += WindowActivated;
            window.Deactivated += WindowDeactivated;
            if (window.PlatformSettings != null)
            {
                window.PlatformSettings.ColorValuesChanged += OnPlatformSettingsOnColorValuesChanged;
            }
        }

        void WindowUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            window.Activated -= WindowActivated;
            window.Deactivated -= WindowDeactivated;
            if (window.PlatformSettings != null)
            {
                window.PlatformSettings.ColorValuesChanged -= OnPlatformSettingsOnColorValuesChanged;
            }
        }

        void WindowActivated(object? sender, EventArgs e)
        {
            SetWindowChrome(window);
        }

        void WindowDeactivated(object? sender, EventArgs e)
        {
            SetWindowChrome(window);
        }
    }

    private static async void SetWindowChrome(Window window, PlatformColorValues? platformSettings = null)
    {
        await Task.Delay(1);
        var platformColorValues = platformSettings ?? window.PlatformSettings?.GetColorValues();
        var isDark = platformColorValues?.ThemeVariant == PlatformThemeVariant.Dark;
        if (!OperatingSystem.IsWindows()) return;

        var isWindows11OrLater = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
        var isWindows10OrLater = !isWindows11OrLater && OperatingSystem.IsWindowsVersionAtLeast(10);
        if (window.IsActive)
        {
            if (isWindows11OrLater)
            {
                window.Background = isDark
                    ? SolidColorBrush.Parse("#80202020")
                    : SolidColorBrush.Parse("#DDF3F3F3");
                window.TransparencyLevelHint = [WindowTransparencyLevel.Mica];
            }
            else if (isWindows10OrLater)
            {
                window.Background = isDark
                    ? SolidColorBrush.Parse("#80202020")
                    : SolidColorBrush.Parse("#DDF3F3F3");
                window.TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur];
            }
        }
        else
        {
            window.Background = isDark
                ? SolidColorBrush.Parse("#202020")
                : SolidColorBrush.Parse("#F3F3F3");
            await Task.Delay(100);
            window.ClearValue(TopLevel.TransparencyLevelHintProperty);
            await Task.Delay(1);
            window.ClearValue(TemplatedControl.BackgroundProperty);
        }
    }
}