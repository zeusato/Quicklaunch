using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickStartLauncher.Models;
using QuickStartLauncher.Services.Config;
using QuickStartLauncher.Services.Launch;
using Serilog;
namespace QuickStartLauncher.ViewModels;
public sealed partial class AutostartToastViewModel(IConfigService config, ILaunchService launch) : ObservableObject
{
    [ObservableProperty] private string title = "QuickStart";
    [ObservableProperty] private string detail = "";
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool hasErrors;
    public event Action<bool>? Finished;
    public event Action<bool>? VisibilityRequested;
    public event Action? OpenLauncherRequested;
    public Dictionary<Guid, RunProgress> Results { get; } = [];
    private readonly CancellationTokenSource cancellation = new();
    private readonly HashSet<(Guid Profile, Guid Action)> dispatched = [];
    private readonly List<string> failures = [];
    private CancellationTokenSource? hideDelay;
    private Task hideTask = Task.CompletedTask;
    private bool started, finished, summaryShown, dismissed;
    private int count;

    [RelayCommand] private void Dismiss()
    {
        dismissed = true; hideDelay?.Cancel(); VisibilityRequested?.Invoke(false);
        // Closing a notification must never cancel an in-flight launch.
        if (started && !IsRunning) Finish(false);
    }
    [RelayCommand] private void OpenLauncher()
    {
        OpenLauncherRequested?.Invoke();
        Dismiss();
    }
    private void Finish(bool open)
    {
        if (finished) return;
        finished = true; Finished?.Invoke(open);
    }
    private void ReportDispatched(Guid profile, Guid action)
    {
        if (finished || cancellation.IsCancellationRequested || !dispatched.Add((profile, action))) return;
        if (dispatched.Count == count && !HasErrors && !summaryShown)
        {
            summaryShown = true;
            Title = $"Đã gửi {count} lệnh mở ứng dụng";
            Detail = "Các ứng dụng đang tự khởi động.";
            Log.Information("Autostart dispatched all {Count} requests; notification will hide while monitoring continues", count);
            if (!dismissed) hideTask = HideAfterDelayAsync();
        }
    }
    private async Task HideAfterDelayAsync()
    {
        hideDelay?.Cancel();
        using var delay = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        hideDelay = delay;
        try
        {
            await Task.Delay(config.Current.Settings.CloseDelayMs, delay.Token);
            VisibilityRequested?.Invoke(false);
            Log.Information("Autostart notification hidden; monitoring={Monitoring}", IsRunning);
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(hideDelay, delay)) hideDelay = null; }
    }
    public async Task StartAsync()
    {
        if (started) return;
        started = true;
        var profiles = config.Current.Settings.Startup.ProfileIds.Select(id => config.Current.Profiles.FirstOrDefault(p => p.Id == id)).OfType<Profile>().ToArray();
        count = profiles.SelectMany(p => p.Actions).Count(a => a.Enabled && !a.SkipOnAutostart && a is not DelayAction);
        Detail = string.Join(" · ", profiles.Select(p => p.Name));
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (profiles.Length == 0)
            {
                Title = "Chưa chọn profile tự chạy"; Detail = "Đang mở Launcher để chọn profile…"; HasErrors = true;
                await Task.Delay(Math.Max(config.Current.Settings.CloseDelayMs, 2500), cancellation.Token);
                Finish(true); return;
            }
            IsRunning = true;
            Title = $"Đang gửi {count} lệnh mở ứng dụng…";
            async Task RunProfileAsync(Profile profile)
            {
                // Progress captures the UI context; native calls report dispatch
                // from worker threads before waiting for Windows to return.
                var sent = new Progress<Guid>(id => ReportDispatched(profile.Id, id));
                await foreach (var progress in launch.RunAsync(profile, new(true, sent), cancellation.Token))
                {
                    Results[progress.ActionId] = progress;
                    if (progress.State != RunState.Failed) continue;
                    var name = profile.Actions.FirstOrDefault(a => a.Id == progress.ActionId)?.Name ?? profile.Name;
                    failures.Add(name); HasErrors = true; dismissed = false;
                    hideDelay?.Cancel();
                    Title = $"Có {failures.Count} lệnh mở bị lỗi";
                    Detail = string.Join(", ", failures) + ". Mở Launcher để xem và thử lại.";
                    VisibilityRequested?.Invoke(true);
                    Log.Warning("Autostart error notification shown for {Name}", name);
                    if (!config.Current.Settings.KeepOpenOnError) hideTask = HideAfterDelayAsync();
                }
            }
            await Task.WhenAll(profiles.Select(RunProfileAsync));
            IsRunning = false;
            if (HasErrors && config.Current.Settings.KeepOpenOnError && !dismissed) return;
            if (!HasErrors && !summaryShown)
            {
                // Empty/all-skipped profiles and completion racing posted progress.
                summaryShown = true;
                Title = count == 0 ? "Không có ứng dụng cần tự mở" : "Đã xử lý các lệnh mở ứng dụng";
                Detail = "";
                if (!dismissed) hideTask = HideAfterDelayAsync();
            }
            await hideTask;
            Finish(false);
        }
        catch (OperationCanceledException) { }
    }
    public void Stop() { cancellation.Cancel(); hideDelay?.Cancel(); }
}
