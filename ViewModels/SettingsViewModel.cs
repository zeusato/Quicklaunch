using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickStartLauncher.Models;
using QuickStartLauncher.Services;
using QuickStartLauncher.Services.Apps;
using QuickStartLauncher.Services.Config;
using QuickStartLauncher.Services.Startup;
namespace QuickStartLauncher.ViewModels;
public sealed partial class StartupProfileViewModel : ObservableObject
{
    public Guid Id { get; }
    public string Name { get; }
    [ObservableProperty] private bool selected;
    public StartupProfileViewModel(Guid id, string name, bool selected) { Id = id; Name = name; this.selected = selected; }
}
public sealed class SettingsViewModel : ObservableObject
{
    private readonly IConfigService config;
    private readonly IStartupService startup;
    private readonly ThemeService theme;
    private readonly AppPaths paths;
    private readonly IInstalledAppsService apps;
    private readonly Action changed;
    private bool loading;
    public ObservableCollection<StartupProfileViewModel> StartupProfiles { get; } = [];
    public Array StartupModes => Enum.GetValues<StartupMode>();
    public Array Themes => Enum.GetValues<ThemeMode>();
    public bool IsAutoRun => Mode == StartupMode.RunProfiles;
    public bool StartupOff { get => Mode == StartupMode.Off; set { if (value) Mode = StartupMode.Off; } }
    public bool StartupRun { get => Mode == StartupMode.RunProfiles; set { if (value) Mode = StartupMode.RunProfiles; } }
    public bool StartupShow { get => Mode == StartupMode.ShowLauncher; set { if (value) Mode = StartupMode.ShowLauncher; } }
    public StartupMode Mode { get => config.Current.Settings.Startup.Mode; set { config.Current.Settings.Startup.Mode = value; OnPropertyChanged(); OnPropertyChanged(nameof(StartupOff)); OnPropertyChanged(nameof(StartupRun)); OnPropertyChanged(nameof(StartupShow)); OnPropertyChanged(nameof(IsAutoRun)); RefreshPreview(); changed(); } }
    public int DelaySeconds { get => config.Current.Settings.Startup.DelaySeconds; set { config.Current.Settings.Startup.DelaySeconds = Math.Clamp(value, 0, 120); OnPropertyChanged(); RefreshPreview(); changed(); } }
    public int CountdownSeconds { get => config.Current.Settings.Startup.CountdownSeconds; set { config.Current.Settings.Startup.CountdownSeconds = Math.Clamp(value, 0, 15); OnPropertyChanged(); changed(); } }
    public int CloseDelayMs { get => config.Current.Settings.CloseDelayMs; set { config.Current.Settings.CloseDelayMs = Math.Clamp(value, 0, 60000); OnPropertyChanged(); changed(); } }
    public bool CloseWhenDone { get => config.Current.Settings.CloseWhenDone; set { config.Current.Settings.CloseWhenDone = value; OnPropertyChanged(); changed(); } }
    public bool KeepOpenOnError { get => config.Current.Settings.KeepOpenOnError; set { config.Current.Settings.KeepOpenOnError = value; OnPropertyChanged(); changed(); } }
    public ThemeMode Theme { get => config.Current.Settings.Theme; set { config.Current.Settings.Theme = value; theme.Apply(value); OnPropertyChanged(); changed(); } }
    public string StartupWarning => startup.IsBlockedByWindows ? "Windows đang chặn mục khởi động này – bật lại trong Task Manager > Startup apps." : "";
    public string UacWarning => config.Current.Profiles.Where(p => config.Current.Settings.Startup.ProfileIds.Contains(p.Id))
        .SelectMany(p => p.Actions).Any(a => a.Enabled && !a.SkipOnAutostart && a is AppAction { RunAsAdmin: true } or CommandAction { RunAsAdmin: true })
        ? "Một số tác vụ yêu cầu quản trị: Windows sẽ hiện UAC khi tự khởi động." : "";
    public string Preview
    {
        get
        {
            if (Mode == StartupMode.Off) return "Không khởi động cùng Windows.";
            if (Mode == StartupMode.ShowLauncher) return $"Sau khi đăng nhập {DelaySeconds}s sẽ mở Launcher.";
            var profiles = config.Current.Settings.Startup.ProfileIds.Select(id => config.Current.Profiles.FirstOrDefault(p => p.Id == id)).OfType<Profile>().ToArray();
            var actions = profiles.SelectMany(p => p.Actions).ToArray();
            return profiles.Length == 0 ? "Chọn ít nhất một profile để tự chạy." :
                $"Sau khi đăng nhập {DelaySeconds}s sẽ mở {actions.Count(a => a.Enabled && !a.SkipOnAutostart && a is not DelayAction)} mục từ {string.Join(", ", profiles.Select(p => p.Name))} ({actions.Count(a => !a.Enabled || a.SkipOnAutostart)} mục bị bỏ qua).";
        }
    }
    /// <summary>Set by the app; null in tests.</summary>
    public UpdateViewModel? Updates { get; set; }
    public RelayCommand OpenConfigFolderCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public AsyncRelayCommand RefreshAppsCommand { get; }
    public SettingsViewModel(IConfigService config, IStartupService startup, ThemeService theme, AppPaths paths, IInstalledAppsService apps, Action changed)
    {
        this.config = config; this.startup = startup; this.theme = theme; this.paths = paths; this.apps = apps; this.changed = changed;
        OpenConfigFolderCommand = new(() => OpenFolder(paths.ConfigDirectory));
        OpenLogsCommand = new(() => OpenFolder(paths.LogDirectory));
        RefreshAppsCommand = new(async () => { await apps.GetAppsAsync(true); });
        StartupProfiles.CollectionChanged += (_, _) => SyncProfiles();
    }
    public void Reload()
    {
        loading = true; StartupProfiles.Clear();
        var selected = config.Current.Settings.Startup.ProfileIds;
        foreach (var p in config.Current.Profiles.OrderBy(p => selected.Contains(p.Id) ? selected.IndexOf(p.Id) : int.MaxValue))
        {
            var item = new StartupProfileViewModel(p.Id, p.Name, selected.Contains(p.Id));
            item.PropertyChanged += (_, _) => SyncProfiles(); StartupProfiles.Add(item);
        }
        loading = false; OnPropertyChanged(""); RefreshPreview();
    }
    private void SyncProfiles()
    {
        if (loading) return;
        config.Current.Settings.Startup.ProfileIds = StartupProfiles.Where(p => p.Selected).Select(p => p.Id).ToList();
        RefreshPreview(); changed();
    }
    public void RefreshPreview() { OnPropertyChanged(nameof(Preview)); OnPropertyChanged(nameof(UacWarning)); OnPropertyChanged(nameof(StartupWarning)); }
    private static void OpenFolder(string path) { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
}
