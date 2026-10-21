using System.Text.Json.Serialization;
namespace QuickStartLauncher.Models;
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AppAction), "app")]
[JsonDerivedType(typeof(WebsiteAction), "website")]
[JsonDerivedType(typeof(CommandAction), "command")]
[JsonDerivedType(typeof(DelayAction), "delay")]
public abstract class LaunchAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool SkipOnAutostart { get; set; }
    public int DelayAfterMs { get; set; }
}
public sealed class AppAction : LaunchAction
{
    public AppTarget Target { get; set; } = new();
    public string Arguments { get; set; } = "";
    public string? WorkingDir { get; set; }
    public bool RunAsAdmin { get; set; }
    public BrowserOptions? Browser { get; set; }
}
public sealed class AppTarget
{
    public string? Aumid { get; set; }
    public string? Path { get; set; }
}
public sealed class BrowserOptions
{
    public List<string> Urls { get; set; } = [];
    public bool NewWindow { get; set; }
    public string? ProfileDir { get; set; }
}
public sealed class WebsiteAction : LaunchAction
{
    public string Url { get; set; } = "";
    public string BrowserId { get; set; } = "default";
    public bool NewWindow { get; set; }
}
public enum CommandShell { Cmd, Powershell, Pwsh }
public sealed class CommandAction : LaunchAction
{
    public string Command { get; set; } = "";
    public CommandShell Shell { get; set; }
    public string? WorkingDir { get; set; }
    public bool Hidden { get; set; } = true;
    public bool RunAsAdmin { get; set; }
}
public sealed class DelayAction : LaunchAction
{
    public int Ms { get; set; } = 1500;
}
