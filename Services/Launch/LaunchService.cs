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
        if (context.IsAutostart)
        {
            // Queue every launch before waiting for any result. Shell calls, UAC,
            // missing network paths and app discovery must not hold up other apps.
            var pending = new List<Task<RunResult>>();
            foreach (var action in actions)
            {
                ct.ThrowIfCancellationRequested();
                if (!action.Enabled || action.SkipOnAutostart || action is DelayAction)
                {
                    yield return new(action.Id, RunState.Skipped); continue;
                }
                pending.Add(Task.Run(() => RunSingleCoreAsync(action, ct, () => context.Dispatched?.Report(action.Id)), ct));
                yield return new(action.Id, RunState.Running);
            }
            Log.Information("Autostart queued {Count} launch requests for profile {ProfileId}; configured delays bypassed", pending.Count, profile.Id);
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending).WaitAsync(ct);
                pending.Remove(completed);
                var result = await completed;
                yield return new(result.ActionId, result.State, result.Error);
            }
            yield break;
        }
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
    public Task<RunResult> RunSingleAsync(LaunchAction action, CancellationToken ct) => RunSingleCoreAsync(action, ct);
    private async Task<RunResult> RunSingleCoreAsync(LaunchAction action, CancellationToken ct, Action? onDispatched = null)
    {
        if (!action.Enabled) return new(action.Id, RunState.Skipped);
        var watch = Stopwatch.StartNew();
        try
        {
            ct.ThrowIfCancellationRequested();
            switch (action)
            {
                case AppAction app: await StartAppAsync(app, ct, onDispatched); break;
                case WebsiteAction web:
                    if (!BrowserArguments.IsValidUrl(web.Url)) throw new ArgumentException("URL phải dùng http://, https:// hoặc file://.");
                    if (web.BrowserId == "default") await StartProcessAsync(new(web.Url) { UseShellExecute = true }, ct, onDispatched);
                    else
                    {
                        var browser = await browsers.FindAsync(web.BrowserId).WaitAsync(ct)
                            ?? throw new FileNotFoundException("Không tìm thấy trình duyệt đã chọn.");
                        await StartAppAsync(new() { Target = new() { Path = browser.ExePath, Aumid = browser.Aumid },
                            Browser = new() { Urls = [web.Url], NewWindow = web.NewWindow } }, ct, onDispatched);
                    }
                    break;
                case CommandAction command:
                    if (string.IsNullOrWhiteSpace(command.Command)) throw new ArgumentException("Chưa nhập lệnh.");
                    var psi = BuildCommand(command); await StartProcessAsync(psi, ct, onDispatched); break;
                case DelayAction delay:
                    if (delay.Ms is < 0 or > 60000) throw new ArgumentOutOfRangeException(nameof(delay.Ms));
                    await Task.Delay(delay.Ms, ct); break;
                default: throw new NotSupportedException("Loại action không hỗ trợ.");
            }
            Log.Information("Action {ActionId} {Name} completed in {Elapsed} ms (launch request returned)", action.Id, action.Name, watch.ElapsedMilliseconds);
            return new(action.Id, RunState.Done);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { Log.Warning("UAC cancelled for {Name}", action.Name); return new(action.Id, RunState.Skipped, "Đã hủy yêu cầu quản trị."); }
        catch (Exception ex)
        { Log.Error(ex, "Action {ActionId} {Name} failed", action.Id, action.Name); return new(action.Id, RunState.Failed, ex.Message); }
    }
    private async Task StartAppAsync(AppAction action, CancellationToken ct, Action? onDispatched = null)
    {
        var path = action.Target.Path;
        var aumid = action.Target.Aumid;
        var hasPath = File.Exists(path);
        var id = BrowserCatalog.Identify(path, aumid);
        InstalledApp? installed = null;
        // A known executable or Store AUMID is already a launch target; do not
        // enumerate the entire AppsFolder just to open it at Windows logon.
        if (!string.IsNullOrWhiteSpace(aumid) &&
            ((!hasPath && !aumid.Contains('!')) ||
             (action.Browser is not null && id is null)))
            installed = (await apps.GetAppsAsync().WaitAsync(ct)).FirstOrDefault(a => a.Aumid == aumid);
        if (!hasPath && File.Exists(installed?.ExePath)) { path = installed!.ExePath; hasPath = true; }
        id = BrowserCatalog.Identify(path, aumid) ?? installed?.BrowserId;
        var args = action.Arguments ?? "";
        if (action.Browser is not null)
        {
            if (id is null) throw new InvalidOperationException("Không nhận diện được trình duyệt cho các URL đã lưu.");
            args = (args + " " + BrowserArguments.Build(id, action.Browser)).Trim();
        }
        ct.ThrowIfCancellationRequested();
        if (hasPath)
        {
            await StartProcessAsync(new(path!) { Arguments = args, UseShellExecute = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(action.WorkingDir) ? Path.GetDirectoryName(path)! : action.WorkingDir,
                Verb = action.RunAsAdmin ? "runas" : "" }, ct, onDispatched);
            return;
        }
        if (string.IsNullOrWhiteSpace(aumid)) throw new FileNotFoundException("Không tìm thấy ứng dụng: " + path);
        if (action.RunAsAdmin) throw new InvalidOperationException("Ứng dụng Store/AUMID không hỗ trợ chạy quản trị; chọn file .exe.");
        if (aumid.Contains('!'))
        {
            Log.Information("Dispatching Store activation for action {ActionId}", action.Id);
            onDispatched?.Invoke();
            try { await process.ActivateAsync(aumid, args); return; }
            catch (COMException ex) when (string.IsNullOrWhiteSpace(args))
            {
                Log.Warning(ex, "Activation manager unavailable; trying AppsFolder for {Aumid}", aumid);
            }
        }
        if (!string.IsNullOrWhiteSpace(args)) throw new InvalidOperationException("Ứng dụng này không có đường dẫn .exe để truyền tham số. Chọn lại bằng Browse.");
        await StartProcessAsync(new("explorer.exe") { Arguments = BrowserArguments.Quote(@"shell:AppsFolder\" + aumid), UseShellExecute = true }, ct, onDispatched);
    }
    private Task StartProcessAsync(ProcessStartInfo info, CancellationToken ct, Action? onDispatched = null) => ShellInterop.OnSta(() =>
    {
        ct.ThrowIfCancellationRequested();
        Log.Information("Dispatching process launch: {Target}", info.FileName);
        onDispatched?.Invoke();
        process.Start(info); return true;
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
