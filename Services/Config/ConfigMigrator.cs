using System.Diagnostics;
using System.Text.Json;
using QuickStartLauncher.Models;
using QuickStartLauncher.Services.Browsers;
using Serilog;
namespace QuickStartLauncher.Services.Config;
public static class ConfigMigrator
{
    public static AppConfig Migrate(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("version", out _)) throw new InvalidDataException("Chỉ migrate config v2 chưa có version.");
        var config = new AppConfig();
        foreach (var oldProfile in root.GetProperty("profiles").EnumerateArray())
        {
            var profile = new Profile { Name = Text(oldProfile, "name", "Default") };
            foreach (var old in oldProfile.GetProperty("actions").EnumerateArray())
            {
                var kind = Text(old, "$type");
                LaunchAction action = kind switch
                {
                    "app" => MigrateApp(old),
                    "command" => new CommandAction
                    {
                        Name = Text(old, "name", Text(old, "command")), Command = Text(old, "command"),
                        Shell = old.TryGetProperty("shell", out var shell) &&
                            (shell.ToString() == "1" || shell.ToString().Equals("powershell", StringComparison.OrdinalIgnoreCase))
                            ? CommandShell.Powershell : CommandShell.Cmd,
                        WorkingDir = Text(old, "workingDir"), RunAsAdmin = Flag(old, "runAsAdmin"), Hidden = true
                    },
                    "website" => new WebsiteAction { Name = Text(old, "name", "Website"), Url = Text(old, "url"), BrowserId = Text(old, "browserId", "default"), NewWindow = Flag(old, "newWindow") },
                    "delay" => new DelayAction { Name = Text(old, "name", "Chờ"), Ms = Number(old, "ms", Number(old, "milliseconds", 1500)) },
                    _ => throw new InvalidDataException($"Không nhận diện action v2: {kind}")
                };
                action.Enabled = Flag(old, "enabled", true);
                action.SkipOnAutostart = Flag(old, "skipOnAutostart");
                action.DelayAfterMs = Number(old, "delayAfterMs", 0);
                profile.Actions.Add(action);
                Log.Information("Migrated {Kind} action {Name}", kind, action.Name);
            }
            config.Profiles.Add(profile);
        }
        config.ActiveProfileId = config.Profiles.FirstOrDefault(p => p.Name == Text(root, "activeProfile"))?.Id
            ?? config.Profiles.FirstOrDefault()?.Id ?? Guid.Empty;
        ConfigJson.Validate(config);
        return config;
    }
    private static AppAction MigrateApp(JsonElement old)
    {
        var path = Text(old, "path");
        var name = Path.GetFileNameWithoutExtension(path);
        try { if (File.Exists(path)) name = FileVersionInfo.GetVersionInfo(path).FileDescription ?? name; } catch { }
        var action = new AppAction
        {
            Name = Text(old, "name", name), Target = new() { Path = path }, Arguments = Text(old, "arguments"),
            WorkingDir = Text(old, "workingDir"), RunAsAdmin = Flag(old, "runAsAdmin")
        };
        if (BrowserCatalog.Identify(path, null) is not null)
            action.Browser = new() { Urls = string.IsNullOrWhiteSpace(Text(old, "url")) ? [] : [Text(old, "url")],
                ProfileDir = Text(old, "profile"), NewWindow = Flag(old, "newWindow") };
        return action;
    }
    private static string Text(JsonElement e, string key, string fallback = "") => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
    private static bool Flag(JsonElement e, string key, bool fallback = false) => e.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;
    private static int Number(JsonElement e, string key, int fallback) => e.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : fallback;
}
