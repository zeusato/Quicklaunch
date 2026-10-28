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
    public ThemeMode Mode { get; private set; } = ThemeMode.Dark;
    private readonly List<FluentWindow> windows = [];
    private readonly HashSet<FluentWindow> watched = [];
    public ThemeService() => SystemEvents.UserPreferenceChanged += SystemChanged;
    private void SystemChanged(object sender, UserPreferenceChangedEventArgs e) => Application.Current?.Dispatcher.BeginInvoke(() => Apply(Mode));
    public void Attach(FluentWindow window)
    {
        windows.Add(window);
        // Set once before HWND creation. Replacing WindowChrome in Loaded is re-entrant in WPF 8.
        // Acrylic everywhere: Mica only tints with the wallpaper colour and is never see-through.
        window.WindowBackdropType = SupportsBackdrop() ? WindowBackdropType.Acrylic : WindowBackdropType.None;
        window.Loaded += (_, _) => { ApplyWindow(window); WindowMotion.Enter(window); };
        window.Closing += (_, e) => { if (!e.Cancel) Unwatch(window); };
        window.Closed += (_, _) => windows.Remove(window);
    }
    public void Apply(ThemeMode mode)
    {
        Mode = mode;
        var light = mode == ThemeMode.Light || mode == ThemeMode.System && IsLight();
        ApplicationThemeManager.Apply(light ? ApplicationTheme.Light : ApplicationTheme.Dark);
        // WPF-UI controls (ToggleSwitch, ProgressRing…) follow the Windows accent by default; pin them to the app accent.
        ApplicationAccentColorManager.Apply((Color)ColorConverter.ConvertFromString("#6C7BF0"), light ? ApplicationTheme.Light : ApplicationTheme.Dark);
        var resources = Application.Current.Resources;
        // Glass = translucent white layers over the DWM acrylic backdrop. Without a backdrop the same
        // layers sit on an opaque gradient, so they must be a bit stronger to keep cards distinguishable.
        var glass = SupportsBackdrop();
        Set(resources, "Accent", light ? "#5142B5" : "#A3AFFF");
        Set(resources, "Text.Primary", light ? "#121426" : "#F5F7FF");
        Set(resources, "Text.Secondary", light ? "#4A5068" : "#C2C8E0");
        Set(resources, "Glass.Fill", light ? (glass ? "#73FFFFFF" : "#B3FFFFFF") : (glass ? "#14FFFFFF" : "#1FFFFFFF"));
        Set(resources, "Glass.FillHover", light ? "#B3FFFFFF" : "#26FFFFFF");
        Set(resources, "Glass.Stroke", light ? "#40000000" : "#2EFFFFFF");
        Set(resources, "Surface.Input", light ? "#99FFFFFF" : "#33000000");
        Set(resources, "Surface.Popup", light ? "#F7FAFBFF" : "#F21C2135");
        Set(resources, "State.Success", light ? "#147A3A" : "#4ADE80");
        Set(resources, "State.Error", light ? "#BA2635" : "#F87171");
        Set(resources, "State.Warning", light ? "#875400" : "#FBBF24");
        resources["Glass.Highlight"] = Highlight(light);
        resources["Bg.Gradient"] = Gradient(light, false); resources["Bg.Tint"] = Gradient(light, glass);
        foreach (var window in windows.ToArray()) if (window.IsLoaded) ApplyWindow(window);
    }
    private static void Set(ResourceDictionary resources, string key, string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); resources[key] = brush;
    }
    private static LinearGradientBrush Gradient(bool light, bool translucent)
    {
        // Translucent variant is only a colour wash over the acrylic: keep alpha low or the blur disappears.
        var a = (Color)ColorConverter.ConvertFromString(translucent ? (light ? "#40C9D2FF" : "#382A3A8C") : (light ? "#F4F6FF" : "#0B1020"));
        var b = (Color)ColorConverter.ConvertFromString(translucent ? (light ? "#40EBD5FF" : "#38512A7A") : (light ? "#E9EEFF" : "#1B1F3A"));
        var brush = new LinearGradientBrush(a, b, 45); brush.Freeze(); return brush;
    }
    private static LinearGradientBrush Highlight(bool light)
    {
        var top = (Color)ColorConverter.ConvertFromString(light ? "#E6FFFFFF" : "#59FFFFFF");
        var bottom = (Color)ColorConverter.ConvertFromString(light ? "#33FFFFFF" : "#0DFFFFFF");
        var brush = new LinearGradientBrush(top, bottom, 90); brush.Freeze(); return brush;
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
        var backdrop = SupportsBackdrop() ? 3 : 1;
        var applied = DwmInterop.Apply(window, !light, backdrop) && backdrop != 1;
        window.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Text.Primary");
        // With a backdrop the window itself must stay fully transparent; the Bg.Tint wash is drawn by the root Grid.
        window.Background = applied ? Brushes.Transparent : Gradient(light, false);
        if (!applied) Application.Current.Resources["Bg.Tint"] = Brushes.Transparent;
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
