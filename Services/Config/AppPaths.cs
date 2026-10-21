namespace QuickStartLauncher.Services.Config;
public sealed class AppPaths
{
    public string Root { get; }
    public string ConfigDirectory => Path.Combine(Root, "config");
    public string ConfigFile => Path.Combine(ConfigDirectory, "config.json");
    public string CacheDirectory => Path.Combine(Root, "cache");
    public string IconDirectory => Path.Combine(CacheDirectory, "icons");
    public string LogDirectory => Path.Combine(Root, "logs");
    public AppPaths(string? root = null) => Root = root ?? (File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"))
        ? AppContext.BaseDirectory : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickStart"));
}
