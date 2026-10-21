using System.Text.Json;
using QuickStartLauncher.Models;
using Serilog;
namespace QuickStartLauncher.Services.Config;
public interface IConfigService
{
    AppConfig Current { get; }
    string? Warning { get; }
    bool IsReadOnly { get; }
    void Load();
    Task SaveAsync();
    Task ImportAsync(string json);
}
public sealed class ConfigService(AppPaths paths) : IConfigService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public AppConfig Current { get; private set; } = AppConfig.CreateDefault();
    public string? Warning { get; private set; }
    public bool IsReadOnly { get; private set; }
    public void Load()
    {
        if (!File.Exists(paths.ConfigFile)) return;
        try
        {
            var json = File.ReadAllText(paths.ConfigFile);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("version", out _))
            {
                var migrated = ConfigMigrator.Migrate(json);
                var backup = Path.Combine(paths.ConfigDirectory, "config.v2.bak.json");
                if (!File.Exists(backup)) File.Copy(paths.ConfigFile, backup);
                else if (File.ReadAllText(backup) != json) File.Copy(paths.ConfigFile, Path.Combine(paths.ConfigDirectory, $"config.v2.{DateTime.Now:yyyyMMdd-HHmmss-fff}.bak.json"));
                Current = migrated;
                WriteAtomic(JsonSerializer.Serialize(Current, ConfigJson.Options));
                Log.Information("Config v2 migrated; backup {Backup}", backup);
            }
            else
            {
                var config = ConfigJson.Read<AppConfig>(json) ?? throw new InvalidDataException("Config rỗng.");
                ConfigJson.Validate(config); Current = config;
            }
        }
        catch (Exception ex)
        {
            IsReadOnly = true;
            Warning = "Không đọc được config. File gốc được giữ nguyên; dùng Nhập config để khôi phục. " + ex.Message;
            Current = AppConfig.CreateDefault();
            Log.Error(ex, "Config load failed; writes blocked to preserve original");
        }
    }
    public async Task SaveAsync()
    {
        if (IsReadOnly) throw new InvalidOperationException(Warning);
        ConfigJson.Validate(Current);
        // Snapshot before awaiting: edits made during I/O belong to the next save.
        var json = JsonSerializer.Serialize(Current, ConfigJson.Options);
        await gate.WaitAsync();
        try { await Task.Run(() => WriteAtomic(json)); } finally { gate.Release(); }
    }
    public async Task ImportAsync(string json)
    {
        var next = ConfigJson.Read<AppConfig>(json) ?? throw new InvalidDataException("Config rỗng.");
        ConfigJson.Validate(next);
        await gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(paths.ConfigDirectory);
            if (File.Exists(paths.ConfigFile)) File.Copy(paths.ConfigFile, Path.Combine(paths.ConfigDirectory, $"config.before-import.{DateTime.Now:yyyyMMdd-HHmmss-fff}.json"));
            await Task.Run(() => WriteAtomic(JsonSerializer.Serialize(next, ConfigJson.Options)));
            Current = next; IsReadOnly = false; Warning = null;
        }
        finally { gate.Release(); }
    }
    private void WriteAtomic(string json)
    {
        Directory.CreateDirectory(paths.ConfigDirectory);
        var temp = paths.ConfigFile + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write(json); writer.Flush(); stream.Flush(true);
        }
        if (File.Exists(paths.ConfigFile)) File.Replace(temp, paths.ConfigFile, null);
        else File.Move(temp, paths.ConfigFile);
    }
}
