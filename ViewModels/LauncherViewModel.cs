using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickStartLauncher.Models;
using QuickStartLauncher.Services.Apps;
using QuickStartLauncher.Services.Browsers;
using QuickStartLauncher.Services.Config;
using QuickStartLauncher.Services.Launch;
namespace QuickStartLauncher.ViewModels;
public sealed partial class LauncherViewModel(IConfigService config, ILaunchService launch, IInstalledAppsService apps, IBrowserService browsers) : ObservableObject
{
    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsEmpty))] private ProfileItemViewModel? selectedProfile;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(BeginLabel))] [NotifyPropertyChangedFor(nameof(CanSelectProfile))] private bool isRunning;
    [ObservableProperty] private string status = "Sẵn sàng cho một ngày mới.";
    [ObservableProperty] private int completed;
    [ObservableProperty] private int total;
    [ObservableProperty] private int errors;
    [ObservableProperty] private bool canRetry;
    public bool IsEmpty => SelectedProfile?.Actions.Count is null or 0;
    public bool CanSelectProfile => !IsRunning;
    public string BeginLabel => IsRunning ? "■  Hủy" : "▶  Begin";
    public string? Warning => config.Warning;
    public event Action? CloseRequested;
    public event Action<bool>? ConfigRequested;
    private CancellationTokenSource? cancellation;
    public void Reload()
    {
        Profiles.Clear();
        foreach (var p in config.Current.Profiles) Profiles.Add(new(p));
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == config.Current.ActiveProfileId) ?? Profiles.FirstOrDefault();
        Status = "Sẵn sàng cho một ngày mới."; CanRetry = false; Errors = 0; Completed = 0;
    }
    partial void OnSelectedProfileChanged(ProfileItemViewModel? value)
    {
        if (value is null) return;
        config.Current.ActiveProfileId = value.Id; Errors = value.Actions.Count(a => a.State == RunState.Failed); CanRetry = Errors > 0; Completed = value.Actions.Count(a => a.State is RunState.Done or RunState.Skipped or RunState.Failed);
        Total = value.Actions.Count;
        _ = LoadIconsAsync(value);
    }
    private async Task LoadIconsAsync(ProfileItemViewModel profile)
    {
        foreach (var a in profile.Actions) await a.LoadDetailsAsync(apps, browsers);
    }
    [RelayCommand] private void OpenConfig() => ConfigRequested?.Invoke(false);
    [RelayCommand] private void AddApps() => ConfigRequested?.Invoke(true);
    [RelayCommand(AllowConcurrentExecutions = true)] private async Task BeginAsync()
    {
        if (IsRunning) { cancellation?.Cancel(); return; }
        if (SelectedProfile is null || IsEmpty) { AddApps(); return; }
        await RunAsync(SelectedProfile.Model);
    }
    [RelayCommand] private async Task RetryAsync()
    {
        if (IsRunning || SelectedProfile is null) return;
        var retry = new Profile { Actions = SelectedProfile.Actions.Where(a => a.State == RunState.Failed).Select(a => a.Model).ToList() };
        await RunAsync(retry);
    }
    public async Task RunAsync(Profile profile, bool autostart = false, bool closeAfter = true)
    {
        if (IsRunning) return;
        cancellation = new(); IsRunning = true; CanRetry = false; Completed = 0; Total = profile.Actions.Count; Errors = 0;
        var cancelled = false;
        try
        {
            await foreach (var p in launch.RunAsync(profile, new(autostart), cancellation.Token))
            {
                var item = SelectedProfile?.Actions.FirstOrDefault(a => a.Model.Id == p.ActionId);
                if (item is not null) { item.State = p.State; item.Error = p.Error; item.RemainingMs = p.RemainingMs; }
                if (p.State is RunState.Done or RunState.Failed or RunState.Skipped) Completed++;
                if (p.State == RunState.Failed) Errors++;
                Status = p.RemainingMs is int remaining ? $"Chờ {remaining / 1000.0:0.0}s…" : $"{Completed}/{Total} · {Errors} lỗi";
            }
            CanRetry = Errors > 0;
            Status = Errors == 0 ? "Hoàn tất ✓" : $"Hoàn tất · {Errors} mục lỗi";
            if (closeAfter && config.Current.Settings.CloseWhenDone && (Errors == 0 || !config.Current.Settings.KeepOpenOnError))
            { await Task.Delay(config.Current.Settings.CloseDelayMs, cancellation.Token); CloseRequested?.Invoke(); }
        }
        catch (OperationCanceledException) { cancelled = true; Status = "Đã hủy các mục chưa chạy."; }
        finally
        {
            IsRunning = false;
            if (cancelled && SelectedProfile is not null)
                foreach (var a in SelectedProfile.Actions.Where(a => a.State == RunState.Running)) a.State = RunState.Skipped;
            cancellation.Dispose(); cancellation = null;
        }
    }
    public void ApplyResults(IReadOnlyDictionary<Guid, RunProgress> results)
    {
        foreach (var action in Profiles.SelectMany(p => p.Actions))
            if (results.TryGetValue(action.Model.Id, out var result)) { action.State = result.State; action.Error = result.Error; }
        SelectedProfile = Profiles.FirstOrDefault(p => p.Actions.Any(a => a.State == RunState.Failed)) ?? SelectedProfile;
        Errors = SelectedProfile?.Actions.Count(a => a.State == RunState.Failed) ?? 0; CanRetry = Errors > 0;
        Status = Errors > 0 ? $"Tự khởi động: {Errors} mục lỗi trong profile này" : "Đã mở Launcher.";
    }
    public void Cancel() => cancellation?.Cancel();
}
