using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using QuickStartLauncher.Services.Config;
using Serilog;
namespace QuickStartLauncher.Services.Update;

public sealed record UpdateInfo(Version Version, string Tag, string ZipUrl, string? Sha256Url, string PageUrl, string Notes);

public interface IUpdateService
{
    Version CurrentVersion { get; }
    /// <summary>Returns a newer release, or null. Without <paramref name="force"/> GitHub is queried at most once a day.</summary>
    Task<UpdateInfo?> CheckAsync(bool force, CancellationToken ct = default);
    /// <summary>Downloads and verifies the release, stages it as "QuickStartLauncher.exe.new" and returns that path.</summary>
    Task<string> InstallAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default);
}

/// <summary>
/// Portable self-update from GitHub Releases, leaving nothing behind:
/// 1. running app downloads the zip to %TEMP%, verifies SHA256, extracts "QuickStartLauncher.exe.new", deletes the zip, exits;
/// 2. "exe.new --apply-update" waits for it, copies itself over "QuickStartLauncher.exe", starts it, exits;
/// 3. the real exe deletes "exe.new" and older %TEMP%\.net extraction folders.
/// </summary>
public sealed class UpdateService(AppPaths paths) : IUpdateService
{
    public const string Repository = "zeusato/Quicklaunch";
    public const string AssetName = "QuickStartLauncher-win-x64.zip";
    private const string ExeName = "QuickStartLauncher.exe";
    private static readonly HttpClient http = CreateClient();
    public Version CurrentVersion { get; } = Normalize(typeof(UpdateService).Assembly.GetName().Version ?? new(0, 0, 0));
    private string StampFile => Path.Combine(paths.CacheDirectory, "update-check.txt");

    public async Task<UpdateInfo?> CheckAsync(bool force, CancellationToken ct = default)
    {
        if (!force && File.Exists(StampFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(StampFile) < TimeSpan.FromDays(1)) return null;
        // QUICKSTART_UPDATE_URL lets a local server stand in for the GitHub API when testing the update flow.
        var feed = Environment.GetEnvironmentVariable("QUICKSTART_UPDATE_URL") ?? $"https://api.github.com/repos/{Repository}/releases/latest";
        using var request = new HttpRequestMessage(HttpMethod.Get, feed);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) { Touch(); return null; } // no release published yet
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Touch();
        var update = Parse(json.RootElement);
        return update is not null && update.Version > CurrentVersion ? update : null;
    }

    public static UpdateInfo? Parse(JsonElement release)
    {
        if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        if (release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        string? zip = null, sha = null;
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString();
            if (string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase)) zip = url;
            else if (string.Equals(name, AssetName + ".sha256", StringComparison.OrdinalIgnoreCase)) sha = url;
        }
        if (zip is null) return null;
        return new(Normalize(version), tag, zip, sha, release.GetProperty("html_url").GetString() ?? "",
            release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "");
    }

    public async Task<string> InstallAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Không xác định được vị trí ứng dụng.");
        var dir = Path.GetDirectoryName(exe)!;
        var newExe = exe + ".new";
        var temp = Path.Combine(Path.GetTempPath(), $"QuickStart-update-{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadAsync(update.ZipUrl, temp, progress, ct);
            if (update.Sha256Url is not null)
            {
                var expected = (await http.GetStringAsync(update.Sha256Url, ct)).Trim().Split(' ', '\t', '\r', '\n')[0];
                await using var stream = File.OpenRead(temp);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("File tải về không khớp mã SHA256, đã hủy cập nhật.");
            }
            using var archive = ZipFile.OpenRead(temp);
            var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals(ExeName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("Gói cập nhật không chứa " + ExeName + ".");
            // The running single-file exe keeps reading itself by path, so it must not be renamed or replaced
            // while alive. Stage the new build next to it; ApplyStaged (run from the staged copy) swaps it in.
            entry.ExtractToFile(newExe, true);
            Log.Information("Staged {To} at {Path}", update.Version, newExe);
            return newExe;
        }
        catch (UnauthorizedAccessException ex)
        {
            TryDelete(newExe);
            throw new UnauthorizedAccessException($"Không có quyền ghi vào {dir}. Hãy chuyển ứng dụng ra thư mục của bạn (ví dụ Documents) hoặc tải bản mới thủ công.", ex);
        }
        catch { TryDelete(newExe); throw; }
        finally { TryDelete(temp); }
    }

    /// <summary>
    /// Runs inside "QuickStartLauncher.exe.new --apply-update": waits for the old instance, copies itself over the
    /// real exe and returns that path (the untouched original when the copy fails, so the app still starts).
    /// </summary>
    public static string ApplyStaged(int? waitForPid)
    {
        var staged = Environment.ProcessPath!;
        var target = staged.EndsWith(".new", StringComparison.OrdinalIgnoreCase) ? staged[..^4] : staged;
        WaitFor(waitForPid);
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try { File.Copy(staged, target, true); Log.Information("Applied update to {Target}", target); return target; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Thread.Sleep(250); }
        }
        Log.Error("Could not replace {Target}; keeping the current version", target);
        return target;
    }

    /// <summary>Runs in the restarted real exe: removes the staged copy and older single-file extraction folders.</summary>
    public static void CleanupAfterUpdate(int? waitForPid)
    {
        WaitFor(waitForPid);
        var exe = Environment.ProcessPath; if (exe is null) return;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try { DeleteIfExists(exe + ".new"); DeleteIfExists(exe + ".old"); break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Thread.Sleep(250); }
        }
        CleanOldExtractions();
    }

    /// <summary>
    /// Self-contained single-file WPF extracts its native DLLs to %TEMP%\.net\&lt;exe name&gt;\&lt;bundle id&gt;, one folder per
    /// version (and one more for the staged "QuickStartLauncher.exe.new" helper). Keep only the folder in use.
    /// </summary>
    public static void CleanOldExtractions()
    {
        try
        {
            // The host puts the extraction folder on the native search path before any managed code runs.
            var mine = (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.TrimEnd('\\', '/'))
                .FirstOrDefault(d => d.Contains(Path.DirectorySeparatorChar + ".net" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (mine is null) return; // not a single-file build (dev run)
            var root = Path.GetDirectoryName(mine)!;              // ...\.net\QuickStartLauncher
            var netDir = Path.GetDirectoryName(root)!;            // ...\.net
            if (!Path.GetFileName(root).Equals("QuickStartLauncher", StringComparison.OrdinalIgnoreCase)) return;
            foreach (var dir in Directory.GetDirectories(root))
                if (!string.Equals(dir, mine, StringComparison.OrdinalIgnoreCase)) TryDeleteDirectory(dir);
            TryDeleteDirectory(Path.Combine(netDir, "QuickStartLauncher.exe.new"));
        }
        catch (Exception ex) { Log.Debug(ex, "Extraction cleanup skipped"); }
    }
    private static void TryDeleteDirectory(string dir)
    {
        try { if (Directory.Exists(dir)) { Directory.Delete(dir, true); Log.Information("Removed stale extraction {Dir}", dir); } }
        catch (Exception ex) { Log.Debug(ex, "Stale extraction {Dir} still in use", dir); }
    }

    private static void WaitFor(int? pid)
    {
        if (pid is not int id) return;
        var watch = Stopwatch.StartNew();
        try { using var previous = Process.GetProcessById(id); previous.WaitForExit(15000); } catch (ArgumentException) { /* already gone */ }
        Log.Information("Waited {Ms} ms for process {Pid} to exit", watch.ElapsedMilliseconds, id);
    }
    private static void DeleteIfExists(string path) { if (File.Exists(path)) File.Delete(path); }

    private static async Task DownloadAsync(string url, string target, IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(target);
        var buffer = new byte[81920]; long read = 0; int n;
        while ((n = await source.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, n), ct); read += n;
            if (total > 0) progress?.Report((double)read / total.Value);
        }
    }
    private void Touch()
    {
        try { Directory.CreateDirectory(paths.CacheDirectory); File.WriteAllText(StampFile, DateTime.UtcNow.ToString("O")); }
        catch (Exception ex) { Log.Debug(ex, "Update stamp unavailable"); }
    }
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ } }
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("QuickStartLauncher", typeof(UpdateService).Assembly.GetName().Version?.ToString() ?? "0"));
        return client;
    }
}
