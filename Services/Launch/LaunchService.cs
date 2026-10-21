using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using QuickStartLauncher.Interop;
using QuickStartLauncher.Models;
using QuickStartLauncher.Services.Apps;
using QuickStartLauncher.Services.Browsers;
using QuickStartLauncher.Services.Config;
using Serilog;
namespace QuickStartLauncher.Services.Launch;
public interface ILaunchService
{
    IAsyncEnumerable<RunProgress> RunAsync(Profile profile, RunContext context, CancellationToken ct);
    Task<RunResult> RunSingleAsync(LaunchAction action, CancellationToken ct);
}
public interface IProcessLauncher
{
    void Start(ProcessStartInfo info);
    Task ActivateAsync(string aumid, string args);
}
public sealed class ProcessLauncher : IProcessLauncher
{
    public void Start(ProcessStartInfo info) { using var process = Process.Start(info); }
    public Task ActivateAsync(string aumid, string args) => ShellInterop.OnSta(() =>
    {
        var manager = (IApplicationActivationManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"))!)!;
        try { manager.ActivateApplication(aumid, args, 0, out _); return true; }
        finally { ShellInterop.Release(manager); }
    });
}
public sealed class LaunchService(IInstalledAppsService apps, IBrowserService browsers, IProcessLauncher process) : ILaunchService
{
    public async IAsyncEnumerable<RunProgress> RunAsync(Profile profile, RunContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        // Freeze the queue so editing/reordering another window cannot change an in-flight run.
        var actions = profile.Actions.Select(ConfigJson.Clone<LaunchAction>).ToArray();
        Log.Information("Starting profile {ProfileId} {Name}, {Count} actions, autostart={Autostart}", profile.Id, profile.Name, actions.Length, context.IsAutostart);
        foreach (var action in actions) yield return new(action.Id, RunState.Pending);
        foreach (var action in actions)
        {
            ct.ThrowIfCancellationRequested();
            if (!action.Enabled || context.IsAutostart && action.SkipOnAutostart)
            {
                yield return new(action.Id, RunState.Skipped); continue;
            }
            yield return new(action.Id, RunState.Running);
            RunResult result;
            if (action is DelayAction { Ms: >= 0 and <= 60000 } delay)
            {
                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < delay.Ms)
                {
                    var remaining = delay.Ms - (int)watch.ElapsedMilliseconds;
                    yield return new(action.Id, RunState.Running, RemainingMs: remaining);
                    await Task.Delay(Math.Min(100, remaining), ct);
                }
                result = new(action.Id, RunState.Done); Log.Information("Delay {ActionId} completed after {Ms} ms", action.Id, delay.Ms);
            }
            else result = await RunSingleAsync(action, ct);
            yield return new(result.ActionId, result.State, result.Error);
            if (action.DelayAfterMs > 0) await Task.Delay(Math.Clamp(action.DelayAfterMs, 0, 60000), ct);
        }
    }
    public async Task<RunResult> RunSingleAsync(LaunchAction action, CancellationToken ct)
    {
        if (!action.Enabled) return new(action.Id, RunState.Skipped);
        try
        {
            ct.ThrowIfCancellationRequested();
            switch (action)
            {
                case AppAction app: await StartAppAsync(app, ct); break;
                case WebsiteAction web:
                    if (!BrowserArguments.IsValidUrl(web.Url)) throw new ArgumentException("URL phải dùng http://, https:// hoặc file://.");
                    if (web.BrowserId == "default") await StartProcessAsync(new(web.Url) { UseShellExecute = true }, ct);
                    else
                    {
                        var browser = (await browsers.GetInstalledAsync()).FirstOrDefault(b => b.Id == web.BrowserId)
                            ?? throw new FileNotFoundException("Không tìm thấy trình duyệt đã chọn.");
                        await StartAppAsync(new() { Target = new() { Path = browser.ExePath, Aumid = browser.Aumid },
                            Browser = new() { Urls = [web.Url], NewWindow = web.NewWindow } }, ct);
                    }
                    break;
                case CommandAction command:
                    if (string.IsNullOrWhiteSpace(command.Command)) throw new ArgumentException("Chưa nhập lệnh.");
                    var psi = BuildCommand(command); await StartProcessAsync(psi, ct); break;
                case DelayAction delay:
                    if (delay.Ms is < 0 or > 60000) throw new ArgumentOutOfRangeException(nameof(delay.Ms));
                    await Task.Delay(delay.Ms, ct); break;
                default: throw new NotSupportedException("Loại action không hỗ trợ.");
            }
            Log.Information("Action {ActionId} {Name} completed", action.Id, action.Name);
            return new(action.Id, RunState.Done);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { Log.Warning("UAC cancelled for {Name}", action.Name); return new(action.Id, RunState.Skipped, "Đã hủy yêu cầu quản trị."); }
        catch (Exception ex)
        { Log.Error(ex, "Action {ActionId} {Name} failed", action.Id, action.Name); return new(action.Id, RunState.Failed, ex.Message); }
    }
    private async Task StartAppAsync(AppAction action, CancellationToken ct)
    {
        var path = action.Target.Path;
        InstalledApp? installed = null;
        if (!string.IsNullOrWhiteSpace(action.Target.Aumid) && (!File.Exists(path) || action.Browser is not null))
            installed = (await apps.GetAppsAsync()).FirstOrDefault(a => a.Aumid == action.Target.Aumid);
        if (!File.Exists(path) && File.Exists(installed?.ExePath)) path = installed!.ExePath;
        var id = BrowserCatalog.Identify(path, action.Target.Aumid) ?? installed?.BrowserId;
        var args = action.Arguments ?? "";
        if (action.Browser is not null)
        {
            if (id is null) throw new InvalidOperationException("Không nhận diện được trình duyệt cho các URL đã lưu.");
            args = (args + " " + BrowserArguments.Build(id, action.Browser)).Trim();
        }
        ct.ThrowIfCancellationRequested();
        if (File.Exists(path))
        {
            await StartProcessAsync(new(path) { Arguments = args, UseShellExecute = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(action.WorkingDir) ? Path.GetDirectoryName(path)! : action.WorkingDir,
                Verb = action.RunAsAdmin ? "runas" : "" }, ct);
            return;
        }
        var aumid = action.Target.Aumid;
        if (string.IsNullOrWhiteSpace(aumid)) throw new FileNotFoundException("Không tìm thấy ứng dụng: " + path);
        if (action.RunAsAdmin) throw new InvalidOperationException("Ứng dụng Store/AUMID không hỗ trợ chạy quản trị; chọn file .exe.");
        if (aumid.Contains('!'))
        {
                        try { await process.ActivateAsync(aumid, args); return; }
            catch (COMException ex) when (string.IsNullOrWhiteSpace(args))
            {
                Log.Warning(ex, "Activation manager unavailable; trying AppsFolder for {Aumid}", aumid);
            }
        }
        if (!string.IsNullOrWhiteSpace(args)) throw new InvalidOperationException("Ứng dụng này không có đường dẫn .exe để truyền tham số. Chọn lại bằng Browse.");
        await StartProcessAsync(new("explorer.exe") { Arguments = BrowserArguments.Quote(@"shell:AppsFolder\" + aumid), UseShellExecute = true }, ct);
    }
    private Task StartProcessAsync(ProcessStartInfo info, CancellationToken ct) => ShellInterop.OnSta(() =>
    {
        ct.ThrowIfCancellationRequested(); process.Start(info); return true;
    });
    public static ProcessStartInfo BuildCommand(CommandAction c)
    {
        var shell = c.Shell switch { CommandShell.Cmd => "cmd.exe", CommandShell.Powershell => "powershell.exe", CommandShell.Pwsh => "pwsh.exe", _ => throw new ArgumentException("Shell không hỗ trợ.") };
        // EncodedCommand preserves quotes/newlines without an extra shell parsing layer.
        var args = c.Shell == CommandShell.Cmd ? "/d /c " + c.Command :
            "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(c.Command));
        return new(shell) { Arguments = args, UseShellExecute = c.RunAsAdmin, Verb = c.RunAsAdmin ? "runas" : "",
            CreateNoWindow = c.Hidden && !c.RunAsAdmin, WindowStyle = c.Hidden && !c.RunAsAdmin ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
            WorkingDirectory = string.IsNullOrWhiteSpace(c.WorkingDir) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : c.WorkingDir };
    }
}
