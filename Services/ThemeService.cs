using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using QuickStartLauncher.Interop;
using QuickStartLauncher.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
namespace QuickStartLauncher.Services;
public sealed class ThemeService : IDisposable
{
    public ThemeMode Mode { get; private set; }
    private readonly List<FluentWindow> windows = [];
    private readonly HashSet<FluentWindow> watched = [];
    public ThemeService() => SystemEvents.UserPreferenceChanged += SystemChanged;
    private void SystemChanged(object sender, UserPreferenceChangedEventArgs e) => Application.Current?.Dispatcher.BeginInvoke(() => Apply(Mode));
    public void Attach(FluentWindow window)
    {
        windows.Add(window);
        // Set once before HWND creation. Replacing WindowChrome in Loaded is re-entrant in WPF 8.
        window.WindowBackdropType = SupportsBackdrop() ? (window is Views.ConfigWindow ? WindowBackdropType.Mica : WindowBackdropType.Acrylic) : WindowBackdropType.None;
        window.Loaded += (_, _) => { ApplyWindow(window); WindowMotion.Enter(window); };
        window.Closing += (_, e) => { if (!e.Cancel) Unwatch(window); };
        window.Closed += (_, _) => windows.Remove(window);
    }
    public void Apply(ThemeMode mode)
    {
        Mode = mode;
        var light = mode == ThemeMode.Light || mode == ThemeMode.System && IsLight();
        ApplicationThemeManager.Apply(light ? ApplicationTheme.Light : ApplicationTheme.Dark);
        var resources = Application.Current.Resources;
        Set(resources, "Accent", light ? "#5142B5" : "#A3AFFF");
        Set(resources, "Text.Primary", light ? "#121426" : "#F5F7FF");
        Set(resources, "Text.Secondary", light ? "#555B70" : "#B7BED7");
        Set(resources, "Glass.Fill", light ? "#DDF9FAFF" : "#E5222943");
        Set(resources, "Glass.FillHover", light ? "#FFFFFFFF" : "#FF343D5D");
        Set(resources, "Glass.Stroke", light ? "#FFCBD1E8" : "#FF454F70");
        Set(resources, "Surface.Input", light ? "#FFF3F5FF" : "#FF181E33");
        Set(resources, "State.Success", light ? "#147A3A" : "#4ADE80");
        Set(resources, "State.Error", light ? "#BA2635" : "#F87171");
        Set(resources, "State.Warning", light ? "#875400" : "#FBBF24");
        resources["Bg.Gradient"] = Gradient(light, false); resources["Bg.Tint"] = Gradient(light, SupportsBackdrop());
        foreach (var window in windows.ToArray()) if (window.IsLoaded) ApplyWindow(window);
    }
    private static void Set(ResourceDictionary resources, string key, string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); resources[key] = brush;
    }
    private static LinearGradientBrush Gradient(bool light, bool translucent)
    {
        var a = (Color)ColorConverter.ConvertFromString(light ? "#F4F6FF" : "#0B1020");
        var b = (Color)ColorConverter.ConvertFromString(light ? "#E9EEFF" : "#1B1F3A");
        if (translucent) { a.A = 170; b.A = 185; }
        var brush = new LinearGradientBrush(a, b, 45); brush.Freeze(); return brush;
    }
    private static bool IsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
    }
    private static bool SupportsBackdrop()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return Environment.OSVersion.Version.Build >= 22621 && key?.GetValue("EnableTransparency") is not 0 && !SystemParameters.IsRemoteSession;
    }
    private void ApplyWindow(FluentWindow window)
    {
        var light = Mode == ThemeMode.Light || Mode == ThemeMode.System && IsLight();
        var backdrop = SupportsBackdrop() ? (window is Views.ConfigWindow ? 2 : 3) : 1;
        var applied = DwmInterop.Apply(window, !light, backdrop);
        window.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Text.Primary");
        window.Background = Gradient(light, applied && backdrop != 1);
        if (Mode == ThemeMode.System && watched.Add(window)) SystemThemeWatcher.Watch(window, window.WindowBackdropType, false);
        else if (Mode != ThemeMode.System) Unwatch(window);
    }
    private void Unwatch(FluentWindow window) { if (watched.Remove(window) && window.IsLoaded) SystemThemeWatcher.UnWatch(window); }
    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= SystemChanged;
        foreach (var window in watched.ToArray()) Unwatch(window);
    }
}
