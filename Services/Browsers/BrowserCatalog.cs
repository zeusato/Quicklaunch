using Microsoft.Win32;
using QuickStartLauncher.Models;
using QuickStartLauncher.Services.Apps;
namespace QuickStartLauncher.Services.Browsers;
public interface IBrowserService
{
    Task<IReadOnlyList<BrowserInfo>> GetInstalledAsync();
    IReadOnlyList<BrowserProfile> GetProfiles(string browserId);
    string? DefaultBrowserId { get; }
}
public sealed class BrowserCatalog(IInstalledAppsService apps) : IBrowserService
{
    public static readonly string[] Ids = ["chrome", "edge", "brave", "coccoc", "opera", "firefox"];
    public static string? Identify(string? path, string? aumid)
    {
        if (aumid?.Contains("._crx_", StringComparison.OrdinalIgnoreCase) == true) return null;
        var exe = Path.GetFileName(path)?.ToLowerInvariant();
        var byExe = exe switch
        {
            "chrome.exe" => "chrome", "msedge.exe" => "edge", "brave.exe" => "brave", "firefox.exe" => "firefox",
            "browser.exe" when path!.Contains("CocCoc", StringComparison.OrdinalIgnoreCase) => "coccoc",
            "opera.exe" => "opera",
            "launcher.exe" when path!.Contains("Opera", StringComparison.OrdinalIgnoreCase) => "opera",
            _ => null
        };
        return byExe ?? aumid?.ToLowerInvariant() switch
        {
            "chrome" => "chrome", "msedge" => "edge", "brave" => "brave", "308046b0af4a39cb" => "firefox", _ => null
        };
    }
    public async Task<IReadOnlyList<BrowserInfo>> GetInstalledAsync()
    {
        var all = await apps.GetAppsAsync();
        var result = all.Where(a => a.BrowserId is not null).GroupBy(a => a.BrowserId)
            .Select(g => g.OrderByDescending(a => File.Exists(a.ExePath)).First())
            .Select(a => new BrowserInfo(a.BrowserId!, a.Name, a.ExePath, a.Aumid)).ToList();
        foreach (var (id, exe) in new[] { ("chrome", "chrome.exe"), ("edge", "msedge.exe"), ("brave", "brave.exe"), ("firefox", "firefox.exe"), ("opera", "opera.exe"), ("coccoc", "browser.exe") })
        {
            if (result.Any(b => b.Id == id && File.Exists(b.ExePath))) continue;
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + exe);
                var path = (key?.GetValue("") as string)?.Trim('"');
                if (!File.Exists(path) || Identify(path, null) != id) continue;
                result.RemoveAll(b => b.Id == id);
                result.Add(new(id, id switch { "chrome" => "Google Chrome", "edge" => "Microsoft Edge", "coccoc" => "Cốc Cốc", _ => char.ToUpper(id[0]) + id[1..] }, path));
                break;
            }
        }
        return result;
    }
    public IReadOnlyList<BrowserProfile> GetProfiles(string browserId) => BrowserProfileReader.Read(browserId);
    public string? DefaultBrowserId
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            var id = key?.GetValue("ProgId")?.ToString() ?? "";
            return id switch { var s when s.StartsWith("ChromeHTML") => "chrome", var s when s.StartsWith("MSEdgeHTM") => "edge",
                var s when s.StartsWith("FirefoxURL") => "firefox", var s when s.StartsWith("BraveHTML") => "brave",
                var s when s.StartsWith("CocCocHTML") => "coccoc", var s when s.StartsWith("Opera") => "opera", _ => null };
        }
    }
}
