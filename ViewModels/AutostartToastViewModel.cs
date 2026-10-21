using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickStartLauncher.Models;
using QuickStartLauncher.Services.Config;
using QuickStartLauncher.Services.Launch;
namespace QuickStartLauncher.ViewModels;
public sealed partial class AutostartToastViewModel(IConfigService config, ILaunchService launch) : ObservableObject
{
    [ObservableProperty] private string title = "QuickStart";
    [ObservableProperty] private string detail = "";
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool hasErrors;
    public event Action<bool>? Finished;
    public Dictionary<Guid, RunProgress> Results { get; } = [];
    private readonly CancellationTokenSource cancellation = new();
    private readonly TaskCompletionSource begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
    [RelayCommand] private void RunNow() => begin.TrySetResult();
    [RelayCommand] private void Cancel() { cancellation.Cancel(); Finished?.Invoke(false); }
    [RelayCommand] private void OpenLauncher() { cancellation.Cancel(); Finished?.Invoke(true); }
    public async Task StartAsync()
    {
        var profiles = config.Current.Settings.Startup.ProfileIds.Select(id => config.Current.Profiles.FirstOrDefault(p => p.Id == id)).OfType<Profile>().ToArray();
        var count = profiles.SelectMany(p => p.Actions).Count(a => a.Enabled && !a.SkipOnAutostart && a is not DelayAction);
        Detail = string.Join(" · ", profiles.Select(p => p.Name));
        try
        {
            if (profiles.Length == 0)
            {
                // Nothing to run: explain briefly, then hand over to the Launcher instead of leaving the toast stuck.
                Title = "Chưa chọn profile tự chạy"; Detail = "Đang mở Launcher để bạn chọn…"; HasErrors = true;
                await Task.Delay(Math.Max(config.Current.Settings.CloseDelayMs, 2500), cancellation.Token);
                Finished?.Invoke(true); return;
            }
            for (var seconds = config.Current.Settings.Startup.CountdownSeconds; seconds > 0 && !begin.Task.IsCompleted; seconds--)
            {
                Title = $"Mở {count} mục sau {seconds}s…";
                await Task.WhenAny(Task.Delay(1000, cancellation.Token), begin.Task); cancellation.Token.ThrowIfCancellationRequested();
            }
            IsRunning = true;
            var errors = 0;
            foreach (var profile in profiles)
                await foreach (var progress in launch.RunAsync(profile, new(true), cancellation.Token))
                {
                    Title = $"Đang mở · {profile.Name}";
                    var action = profile.Actions.FirstOrDefault(a => a.Id == progress.ActionId);
                    Results[progress.ActionId] = progress;
                    Detail = $"{action?.Name} · {progress.State switch { RunState.Done => "Đã mở", RunState.Failed => "Lỗi", RunState.Skipped => "Bỏ qua", _ => "Đang mở" }}";
                    if (progress.State == RunState.Failed) errors++;
                }
            IsRunning = false; HasErrors = errors > 0;
            Title = errors == 0 ? "Hoàn tất ✓" : $"Có {errors} mục lỗi";
            Detail = errors == 0 ? "Mọi thứ đã sẵn sàng." : "Mở Launcher để xem và chạy lại.";
            if (errors == 0 || !config.Current.Settings.KeepOpenOnError)
            { await Task.Delay(config.Current.Settings.CloseDelayMs, cancellation.Token); Finished?.Invoke(false); }
        }
        catch (OperationCanceledException) { }
    }
    public void Stop() => cancellation.Cancel();
}
