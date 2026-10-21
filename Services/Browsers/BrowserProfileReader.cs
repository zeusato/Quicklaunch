using System.Text.Json;
using QuickStartLauncher.Models;
using Serilog;
namespace QuickStartLauncher.Services.Browsers;
public static class BrowserProfileReader
{
    public static IReadOnlyList<BrowserProfile> Read(string id)
    {
        var profiles = new List<BrowserProfile> { new("", "Mặc định") };
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (id == "firefox")
            {
                var ini = Path.Combine(roaming, @"Mozilla\Firefox\profiles.ini");
                if (File.Exists(ini)) profiles.AddRange(ParseFirefox(File.ReadAllText(ini)));
                return profiles;
            }
            var relative = id switch { "chrome" => @"Google\Chrome\User Data", "edge" => @"Microsoft\Edge\User Data",
                "brave" => @"BraveSoftware\Brave-Browser\User Data", "coccoc" => @"CocCoc\Browser\User Data", _ => null };
            if (relative is null) return profiles;
            var file = Path.Combine(local, relative, "Local State");
            if (!File.Exists(file)) return profiles;
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            profiles.AddRange(ParseChromium(reader.ReadToEnd()));
        }
        catch (Exception e) { Log.Warning(e, "Could not read {Browser} profiles", id); }
        return profiles;
    }
    public static IEnumerable<BrowserProfile> ParseChromium(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("profile", out var p) || !p.TryGetProperty("info_cache", out var cache)) return [];
        return cache.EnumerateObject().Select(x => new BrowserProfile(x.Name,
            x.Value.TryGetProperty("name", out var n) ? n.GetString() ?? x.Name : x.Name,
            x.Value.TryGetProperty("user_name", out var u) ? u.GetString() : null)).ToArray();
    }
    public static IEnumerable<BrowserProfile> ParseFirefox(string ini)
    {
        var section = "";
        foreach (var raw in ini.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) section = line;
            if (section.StartsWith("[Profile", StringComparison.OrdinalIgnoreCase) && line.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                yield return new(line[5..], line[5..]);
        }
    }
}
