namespace QuickStartLauncher.Models;
public sealed record InstalledApp(string Key, string Name, string? Aumid, string? ExePath, bool IsPackaged, string? BrowserId);
public sealed record BrowserInfo(string Id, string Name, string? ExePath, string? Aumid = null);
public sealed record BrowserProfile(string Directory, string Name, string? Email = null)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Email) ? Name : $"{Name} ({Email})";
}
public enum RunState { Pending, Running, Done, Failed, Skipped }
public sealed record RunResult(Guid ActionId, RunState State, string? Error = null);
public sealed record RunProgress(Guid ActionId, RunState State, string? Error = null, int? RemainingMs = null);
public sealed record RunContext(bool IsAutostart = false);
