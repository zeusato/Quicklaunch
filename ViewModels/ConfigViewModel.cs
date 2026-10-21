using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickStartLauncher.Models;
using QuickStartLauncher.Services;
using QuickStartLauncher.Services.Apps;
using QuickStartLauncher.Services.Browsers;
using QuickStartLauncher.Services.Config;
using QuickStartLauncher.Services.Launch;
using QuickStartLauncher.Services.Startup;
using Serilog;
namespace QuickStartLauncher.ViewModels;
public sealed partial class ConfigViewModel : ObservableObject
{
    public IConfigService Config { get; }
    private readonly ILaunchService launch;
    private readonly IInstalledAppsService apps;
    private readonly IBrowserService browsers;
    private readonly IStartupService startup;
    private readonly DispatcherTimer saveTimer;
    public SettingsViewModel Settings { get; }
    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = [];
    public ObservableCollection<BrowserChoiceViewModel> Browsers { get; } = [new(new("default", "Mặc định Windows", null))];
    [ObservableProperty] private ProfileItemViewModel? selectedProfile;
    [ObservableProperty] private ActionItemViewModel? selectedAction;
    [ObservableProperty] private bool showSettings;
    [ObservableProperty] private string saveStatus = "Đã lưu ✓";
    [ObservableProperty] private bool isTesting;
    public string? Warning => Config.Warning;
    public event Action<bool>? PickerRequested;
    public event Action? ProfilesReloaded;
    private CancellationTokenSource? testCancellation;
    private (ProfileItemViewModel Profile, List<(int Index, ActionItemViewModel Item)> Items)? undo;
    private long editVersion;
    public ConfigViewModel(IConfigService config, ILaunchService launch, IInstalledAppsService apps, IBrowserService browsers, IStartupService startup, ThemeService themes, AppPaths paths)
    {
        Config = config; this.launch = launch; this.apps = apps; this.browsers = browsers; this.startup = startup;
        saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        saveTimer.Tick += async (_, _) => await FlushAsync();
        Settings = new(config, startup, themes, paths, apps, MarkDirty);
        Reload(); _ = LoadBrowsersAsync();
    }
    public void Reload()
    {
        foreach (var p in Profiles) p.Changed -= MarkDirty;
        Profiles.Clear();
        foreach (var p in Config.Current.Profiles) { var vm = new ProfileItemViewModel(p); vm.Changed += MarkDirty; Profiles.Add(vm); }
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == Config.Current.ActiveProfileId) ?? Profiles.FirstOrDefault();
        Settings.Reload(); OnPropertyChanged(nameof(Warning)); ProfilesReloaded?.Invoke();
    }
    private async Task LoadBrowsersAsync()
    {
        try
        {
            foreach (var b in await browsers.GetInstalledAsync())
            {
                var choice = new BrowserChoiceViewModel(b); Browsers.Add(choice);
                choice.Icon = await apps.GetIconAsync(new(b.Aumid ?? b.ExePath ?? b.Id, b.Name, b.Aumid, b.ExePath, false, b.Id), 24);
            }
        }
        catch (Exception ex) { SaveStatus = "Không đọc được trình duyệt: " + ex.Message; }
    }
    partial void OnSelectedProfileChanged(ProfileItemViewModel? value)
    {
        SelectedAction = value?.Actions.FirstOrDefault(); ShowSettings = false;
        if (value is not null) foreach (var a in value.Actions) _ = a.LoadDetailsAsync(apps, browsers);
    }
    partial void OnSelectedActionChanged(ActionItemViewModel? value) { if (value is not null) _ = value.LoadDetailsAsync(apps, browsers); }
    public void MarkDirty()
    {
        editVersion++; SaveStatus = "Đang chờ lưu…"; saveTimer.Stop(); saveTimer.Start(); Settings.RefreshPreview();
    }
    public async Task<bool> FlushAsync()
    {
        saveTimer.Stop();
        var version = editVersion;
        try
        {
            Config.Current.Profiles = Profiles.Select(p => p.Model).ToList();
            await Config.SaveAsync(); startup.Apply(Config.Current.Settings.Startup);
            SaveStatus = version == editVersion ? "Đã lưu ✓" : "Đang chờ lưu…"; return true;
        }
        catch (Exception ex) { SaveStatus = "Chưa lưu: " + ex.Message; Log.Error(ex, "Saving configuration failed"); return false; }
    }
    [RelayCommand] private void AddProfile()
    {
        var p = new Profile { Name = UniqueName("Profile mới") };
        Config.Current.Profiles.Add(p); var vm = new ProfileItemViewModel(p); vm.Changed += MarkDirty; Profiles.Add(vm);
        SelectedProfile = vm; Settings.Reload(); MarkDirty();
    }
    public string UniqueName(string name)
    {
        var result = name; var index = 2;
        while (Profiles.Any(p => p.Name.Equals(result, StringComparison.OrdinalIgnoreCase))) result = $"{name} ({index++})";
        return result;
    }
    [RelayCommand] private void DuplicateProfile()
    {
        if (SelectedProfile is null) return;
        var p = ConfigJson.Clone(SelectedProfile.Model); p.Id = Guid.NewGuid(); p.Name = UniqueName(p.Name + " — bản sao");
        foreach (var a in p.Actions) a.Id = Guid.NewGuid();
        ImportProfile(p);
    }
    public void ImportProfile(Profile p)
    {
        p.Id = Guid.NewGuid(); p.Name = UniqueName(p.Name);
        foreach (var a in p.Actions) a.Id = Guid.NewGuid();
        var probe = AppConfig.CreateDefault(); probe.Profiles = [p]; probe.ActiveProfileId = p.Id; ConfigJson.Validate(probe);
        Config.Current.Profiles.Add(p); var vm = new ProfileItemViewModel(p); vm.Changed += MarkDirty; Profiles.Add(vm);
        SelectedProfile = vm; Settings.Reload(); MarkDirty();
    }
    public void DeleteProfile()
    {
        if (Profiles.Count <= 1 || SelectedProfile is null) return;
        var p = SelectedProfile; p.Changed -= MarkDirty; Profiles.Remove(p); Config.Current.Profiles.Remove(p.Model);
        Config.Current.Settings.Startup.ProfileIds.Remove(p.Id);
        if (Config.Current.ActiveProfileId == p.Id) Config.Current.ActiveProfileId = Profiles[0].Id;
        SelectedProfile = Profiles[0]; Settings.Reload(); MarkDirty();
    }
    [RelayCommand] private void SetDefault()
    {
        if (SelectedProfile is null) return; Config.Current.ActiveProfileId = SelectedProfile.Id; MarkDirty();
    }
    [RelayCommand] private void AddApps() => PickerRequested?.Invoke(false);
    [RelayCommand] private void ChangeApp() => PickerRequested?.Invoke(true);
    [RelayCommand] private void AddWebsite() => AddAction(new WebsiteAction { Name = "Website", Url = "https://" });
    [RelayCommand] private void AddCommand() => AddAction(new CommandAction { Name = "Lệnh mới" });
    [RelayCommand] private void AddDelay() => AddAction(new DelayAction { Name = "Chờ", Ms = 1500 });
    public void AddAction(LaunchAction action)
    {
        if (SelectedProfile is null) return;
        var vm = new ActionItemViewModel(action); SelectedProfile.Actions.Add(vm); SelectedAction = vm;
    }
    public void AddInstalledApps(IEnumerable<InstalledApp> selected, bool replace)
    {
        var list = selected.ToList();
        if (replace && SelectedAction is { IsApp: true } action && list.Count > 0)
        { action.ReplaceApp(list[0]); _ = action.LoadDetailsAsync(apps, browsers); return; }
        foreach (var a in list) AddAction(new AppAction { Name = a.Name, Target = new() { Aumid = a.Aumid, Path = a.ExePath }, Browser = a.BrowserId is null ? null : new() });
    }
    [RelayCommand] private void DuplicateAction()
    {
        if (SelectedAction is null) return;
        var copy = ConfigJson.Clone<LaunchAction>(SelectedAction.Model); copy.Id = Guid.NewGuid(); AddAction(copy);
    }
    public void DeleteActions(IEnumerable<ActionItemViewModel> items)
    {
        if (SelectedProfile is null) return;
        var deleted = items.Distinct().Where(SelectedProfile.Actions.Contains).Select(a => (SelectedProfile.Actions.IndexOf(a), a)).OrderBy(x => x.Item1).ToList();
        if (deleted.Count == 0) return;
        undo = (SelectedProfile, deleted);
        foreach (var (_, a) in deleted) SelectedProfile.Actions.Remove(a);
        SelectedAction = SelectedProfile.Actions.FirstOrDefault();
        SaveStatus = $"Đã xóa {deleted.Count} mục · Ctrl+Z để hoàn tác"; MarkDirty();
    }
    [RelayCommand] private void Undo()
    {
        if (undo is not { } u || !Profiles.Contains(u.Profile)) return;
        SelectedProfile = u.Profile;
        foreach (var (index, item) in u.Items) u.Profile.Actions.Insert(Math.Min(index, u.Profile.Actions.Count), item);
        SelectedAction = u.Items[0].Item; undo = null;
    }
    [RelayCommand] private void MoveUp() => Move(-1);
    [RelayCommand] private void MoveDown() => Move(1);
    private void Move(int offset)
    {
        if (SelectedProfile is null || SelectedAction is null) return;
        var i = SelectedProfile.Actions.IndexOf(SelectedAction); var next = i + offset;
        if (next >= 0 && next < SelectedProfile.Actions.Count) SelectedProfile.Actions.Move(i, next);
    }
    [RelayCommand(AllowConcurrentExecutions = true)] private async Task TestActionAsync()
    {
        if (IsTesting) { testCancellation?.Cancel(); return; }
        if (SelectedAction is null) return;
        var action = SelectedAction; testCancellation = new(); IsTesting = true; action.State = RunState.Running;
        try
        {
            var result = await launch.RunSingleAsync(ConfigJson.Clone<LaunchAction>(action.Model), testCancellation.Token);
            action.State = result.State; action.Error = result.Error;
            SaveStatus = result.State == RunState.Failed ? "Chạy thử lỗi: " + result.Error : "Chạy thử: " + result.State;
        }
        catch (OperationCanceledException) { action.State = RunState.Skipped; SaveStatus = "Đã hủy chạy thử."; }
        finally { IsTesting = false; testCancellation.Dispose(); testCancellation = null; }
    }
    [RelayCommand(AllowConcurrentExecutions = true)] private async Task TestProfileAsync()
    {
        if (IsTesting) { testCancellation?.Cancel(); return; }
        if (SelectedProfile is null) return;
        var profile = SelectedProfile; testCancellation = new(); IsTesting = true;
        try
        {
            await foreach (var result in launch.RunAsync(profile.Model, new(), testCancellation.Token))
            {
                var item = profile.Actions.FirstOrDefault(a => a.Model.Id == result.ActionId);
                if (item is not null) { item.State = result.State; item.Error = result.Error; }
            }
            SaveStatus = $"Chạy thử xong · {profile.Actions.Count(a => a.State == RunState.Failed)} lỗi";
        }
        catch (OperationCanceledException) { SaveStatus = "Đã hủy chạy thử."; }
        finally { IsTesting = false; testCancellation.Dispose(); testCancellation = null; }
    }
    public void Stop() { saveTimer.Stop(); testCancellation?.Cancel(); }
}
