using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media;
using QuickStartLauncher.Interop;
using QuickStartLauncher.Models;
using QuickStartLauncher.Services.Browsers;
using QuickStartLauncher.Services.Config;
using Serilog;
namespace QuickStartLauncher.Services.Apps;
public interface IInstalledAppsService
{
    Task<IReadOnlyList<InstalledApp>> GetAppsAsync(bool forceRefresh = false);
    Task<ImageSource?> GetIconAsync(InstalledApp app, int size = 48);
}
public sealed class ShellAppsFolderProvider(AppPaths paths, IconCache icons) : IInstalledAppsService
{
    private IReadOnlyList<InstalledApp>? cache;
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<IReadOnlyList<InstalledApp>> GetAppsAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && cache is not null) return cache;
        await gate.WaitAsync();
        try
        {
            if (!forceRefresh && cache is not null) return cache;
            var cacheFile = Path.Combine(paths.CacheDirectory, "apps.json");
            if (!forceRefresh && File.Exists(cacheFile))
            {
                try { cache = JsonSerializer.Deserialize<List<InstalledApp>>(await File.ReadAllTextAsync(cacheFile), ConfigJson.Options); if (cache is { Count: > 0 }) return cache; }
                catch (Exception e) { Log.Warning(e, "App cache could not be read"); }
            }
            var watch = Stopwatch.StartNew();
            cache = await ShellInterop.OnSta(Enumerate);
            Directory.CreateDirectory(paths.CacheDirectory);
            try { await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(cache, ConfigJson.Options)); }
            catch (Exception e) { Log.Warning(e, "Cannot write app cache"); }
            Log.Information("AppsFolder: {Count} apps in {Elapsed} ms", cache.Count, watch.ElapsedMilliseconds);
            return cache;
        }
        finally { gate.Release(); }
    }
    public Task<ImageSource?> GetIconAsync(InstalledApp app, int size = 48) => icons.GetAsync(app, size);
    private static IReadOnlyList<InstalledApp> Enumerate()
    {
        IShellItem? folder = null; IEnumShellItems? enumerator = null;
        var found = new List<InstalledApp>();
        try
        {
            ShellInterop.SHGetKnownFolderItem(ShellInterop.AppsFolder, 0, 0, typeof(IShellItem).GUID, out folder);
            folder.BindToHandler(0, ShellInterop.EnumItems, typeof(IEnumShellItems).GUID, out enumerator);
            while (enumerator.Next(1, out var item, out var fetched) == 0 && fetched == 1)
            {
                try
                {
                    var name = DisplayName(item, 0);
                    var parsing = DisplayName(item, 0x80018001);
                    var appId = Property(item, ShellInterop.AppId);
                    if (string.IsNullOrWhiteSpace(appId)) appId = parsing;
                    var path = ShellInterop.ResolvePath(Property(item, ShellInterop.TargetPath));
                    if (string.IsNullOrWhiteSpace(name) || IsNoise(name, path)) continue;
                    var packaged = appId.Contains('!') && appId.Contains('_');
                    found.Add(new(appId, name, appId, path, packaged, BrowserCatalog.Identify(path, appId)));
                }
                catch (Exception e) { Log.Debug(e, "Skipping unreadable shell item"); }
                finally { ShellInterop.Release(item); }
            }
        }
        finally { ShellInterop.Release(enumerator); ShellInterop.Release(folder); }
        return found.GroupBy(a => a.ExePath ?? a.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(a => !string.IsNullOrEmpty(a.Aumid)).First())
            .OrderBy(a => a.Name, StringComparer.Create(CultureInfo.GetCultureInfo("vi-VN"), true)).ToArray();
    }
    private static string DisplayName(IShellItem item, uint type)
    {
        item.GetDisplayName(type, out var ptr);
        try { return Marshal.PtrToStringUni(ptr) ?? ""; } finally { Marshal.FreeCoTaskMem(ptr); }
    }
    private static string? Property(IShellItem item, PropertyKey key)
    {
        try { ((IShellItem2)item).GetString(key, out var value); return value; } catch { return null; }
    }
    private static bool IsNoise(string name, string? path) =>
        new[] { "uninstall", "gỡ cài đặt", "readme", "documentation", "website" }.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase))
        || name.Equals("Help", StringComparison.OrdinalIgnoreCase)
        || new[] { ".url", ".chm", ".txt", ".pdf", ".html" }.Contains(Path.GetExtension(path)?.ToLowerInvariant());
}
