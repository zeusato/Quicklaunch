using Microsoft.Win32;
using QuickStartLauncher.Models;
namespace QuickStartLauncher.Services.Startup;
public interface IStartupService
{
    bool IsBlockedByWindows { get; }
    void Apply(StartupSettings settings);
}
public sealed class StartupService : IStartupService
{
    private readonly string runKey;
    private readonly string approvedKey;
    private readonly Func<string> executable;
    public StartupService() : this(@"Software\Microsoft\Windows\CurrentVersion\Run",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", GetExecutable) { }
    // Injectable locations keep integration tests out of Windows' real startup list.
    public StartupService(string runKey, string approvedKey, Func<string> executable)
    { this.runKey = runKey; this.approvedKey = approvedKey; this.executable = executable; }
    public bool IsBlockedByWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(approvedKey);
            return key?.GetValue("QuickStartLauncher") is byte[] { Length: > 0 } bytes && (bytes[0] == 3 || bytes[0] == 7);
        }
    }
    public void Apply(StartupSettings settings)
    {
        using var key = Registry.CurrentUser.CreateSubKey(runKey);
        if (settings.Mode == StartupMode.Off) key.DeleteValue("QuickStartLauncher", false);
        else key.SetValue("QuickStartLauncher", $"\"{executable()}\" --autostart");
    }
    private static string GetExecutable()
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("Không tìm thấy đường dẫn launcher.");
        return Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(AppContext.BaseDirectory, "QuickStartLauncher.exe") : path;
    }
}
