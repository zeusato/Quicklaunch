namespace QuickStartLauncher.Models;
public sealed class AppConfig
{
    public int Version { get; set; } = 3;
    public AppSettings Settings { get; set; } = new();
    public Guid ActiveProfileId { get; set; }
    public List<Profile> Profiles { get; set; } = [];
    public static AppConfig CreateDefault()
    {
        var p = new Profile { Name = "Làm việc" };
        return new() { ActiveProfileId = p.Id, Profiles = [p] };
    }
}
public sealed class Profile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Profile mới";
    public string Icon { get; set; } = "💼";
    public List<LaunchAction> Actions { get; set; } = [];
}
public enum ThemeMode { System, Light, Dark }
public enum StartupMode { Off, RunProfiles, ShowLauncher }
public sealed class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;
    public StartupSettings Startup { get; set; } = new();
    public bool CloseWhenDone { get; set; } = true;
    public int CloseDelayMs { get; set; } = 1200;
    public bool KeepOpenOnError { get; set; } = true;
}
public sealed class StartupSettings
{
    public StartupMode Mode { get; set; }
    public List<Guid> ProfileIds { get; set; } = [];
    // Retained for v3 config compatibility; immediate autostart ignores these values.
    public int DelaySeconds { get; set; }
    public int CountdownSeconds { get; set; }
}
