using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickStartLauncher.Models;
using QuickStartLauncher.Services.Apps;
using QuickStartLauncher.Services.Browsers;
using QuickStartLauncher.Services.Config;
namespace QuickStartLauncher.ViewModels;
public sealed partial class UrlItemViewModel : ObservableObject
{
    [ObservableProperty] private string value = "";
    public UrlItemViewModel(string value) => this.value = value;
}
public sealed partial class ActionItemViewModel : ObservableObject
{
    public LaunchAction Model { get; }
    public event Action? Changed;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasIcon))] private ImageSource? icon;
    public bool HasIcon => Icon is not null;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StatusGlyph))] private RunState state;
    [ObservableProperty] private string? error;
    [ObservableProperty] private int? remainingMs;
    [ObservableProperty] private string newUrl = "";
    [ObservableProperty] private UrlItemViewModel? selectedUrl;
    public ObservableCollection<UrlItemViewModel> Urls { get; } = [];
    public ObservableCollection<BrowserProfile> BrowserProfiles { get; } = [];
    public bool IsApp => Model is AppAction;
    public bool IsWebsite => Model is WebsiteAction;
    public bool IsCommand => Model is CommandAction;
    public bool IsDelay => Model is DelayAction;
    public bool IsBrowser => Model is AppAction { Browser: not null };
    public string KindLabel => Model switch { AppAction => "ỨNG DỤNG", WebsiteAction => "WEBSITE", CommandAction => "LỆNH", _ => "CHỜ" };
    public string FallbackIcon => Model switch { AppAction => "▣", WebsiteAction => "↗", CommandAction => "⌘", _ => "◷" };
    public string StatusGlyph => State switch { RunState.Running => "◌", RunState.Done => "✓", RunState.Failed => "✕", RunState.Skipped => "–", _ => "·" };
    public string Name { get => Model.Name; set => Update(value, Model.Name, v => Model.Name = v); }
    public bool Enabled { get => Model.Enabled; set => Update(value, Model.Enabled, v => Model.Enabled = v); }
    public bool SkipOnAutostart { get => Model.SkipOnAutostart; set => Update(value, Model.SkipOnAutostart, v => Model.SkipOnAutostart = v); }
    public int DelayAfterMs { get => Model.DelayAfterMs; set => Update(Math.Clamp(value, 0, 60000), Model.DelayAfterMs, v => Model.DelayAfterMs = v); }
    public string Target => Model is AppAction a ? a.Target.Path ?? a.Target.Aumid ?? "" : "";
    public string Arguments { get => (Model as AppAction)?.Arguments ?? ""; set { if (Model is AppAction a) Update(value, a.Arguments, v => a.Arguments = v); } }
    public string WorkingDir
    {
        get => Model switch { AppAction a => a.WorkingDir ?? "", CommandAction c => c.WorkingDir ?? "", _ => "" };
        set { if (Model is AppAction a) Update(value, a.WorkingDir, v => a.WorkingDir = v); if (Model is CommandAction c) Update(value, c.WorkingDir, v => c.WorkingDir = v); }
    }
    public bool RunAsAdmin
    {
        get => Model is AppAction { RunAsAdmin: true } or CommandAction { RunAsAdmin: true };
        set { if (Model is AppAction a) Update(value, a.RunAsAdmin, v => a.RunAsAdmin = v); if (Model is CommandAction c) Update(value, c.RunAsAdmin, v => c.RunAsAdmin = v); }
    }
    public bool Hidden { get => (Model as CommandAction)?.Hidden ?? false; set { if (Model is CommandAction c) Update(value, c.Hidden, v => c.Hidden = v); } }
    public string Command { get => (Model as CommandAction)?.Command ?? ""; set { if (Model is CommandAction c) Update(value, c.Command, v => c.Command = v); } }
    public CommandShell Shell { get => (Model as CommandAction)?.Shell ?? CommandShell.Cmd; set { if (Model is CommandAction c) Update(value, c.Shell, v => c.Shell = v); } }
    public Array Shells => Enum.GetValues<CommandShell>().Where(s => s != CommandShell.Pwsh || Shell == CommandShell.Pwsh || (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Any(p => File.Exists(Path.Combine(p, "pwsh.exe")))).ToArray();
    public int Ms { get => (Model as DelayAction)?.Ms ?? 0; set { if (Model is DelayAction d) Update(Math.Clamp(value, 0, 60000), d.Ms, v => d.Ms = v); } }
    public string Url { get => (Model as WebsiteAction)?.Url ?? ""; set { if (Model is WebsiteAction w) Update(value, w.Url, v => w.Url = v); } }
    public string BrowserId { get => (Model as WebsiteAction)?.BrowserId ?? "default"; set { if (Model is WebsiteAction w) Update(value, w.BrowserId, v => w.BrowserId = v); } }
    public string ProfileDir { get => (Model as AppAction)?.Browser?.ProfileDir ?? ""; set { if (value is not null && Model is AppAction { Browser: { } b }) Update(value, b.ProfileDir, v => b.ProfileDir = v); } }
    public bool NewWindow
    {
        get => Model is AppAction { Browser: { NewWindow: true } } or WebsiteAction { NewWindow: true };
        set { if (Model is AppAction { Browser: { } b }) Update(value, b.NewWindow, v => b.NewWindow = v); if (Model is WebsiteAction w) Update(value, w.NewWindow, v => w.NewWindow = v); }
    }
    public string Description => Model switch
    {
        AppAction { Browser: { Urls.Count: > 0 } b } => string.Join(" · ", b.Urls.Select(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) ? uri.Host : u)),
        AppAction a => a.Target.Path ?? a.Target.Aumid ?? "Chọn ứng dụng",
        WebsiteAction w => w.Url, CommandAction c => c.Command, DelayAction d => $"Chờ {d.Ms / 1000.0:0.##} giây", _ => ""
    };
    public string ValidationMessage => Model switch
    {
        AppAction a when !string.IsNullOrWhiteSpace(a.Target.Path) && !File.Exists(a.Target.Path) && string.IsNullOrWhiteSpace(a.Target.Aumid) => "Không tìm thấy ứng dụng. Chọn lại ứng dụng trên máy này.",
        AppAction { Browser: { } b } when b.Urls.Any(u => !BrowserArguments.IsValidUrl(u)) => "URL phải bắt đầu bằng http://, https:// hoặc file://.",
        WebsiteAction w when !BrowserArguments.IsValidUrl(w.Url) => "URL phải bắt đầu bằng http://, https:// hoặc file://.",
        CommandAction c when string.IsNullOrWhiteSpace(c.Command) => "Chưa nhập lệnh.",
        _ => ""
    };
    public bool SupportsArguments => Model is not AppAction a || !string.IsNullOrWhiteSpace(a.Target.Path) || a.Target.Aumid?.Contains('!') == true;
    public ActionItemViewModel(LaunchAction model)
    {
        Model = model;
        if (model is AppAction { Browser: { } b })
            foreach (var url in b.Urls) AddUrlItem(new(url));
        Urls.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null) foreach (UrlItemViewModel item in e.NewItems) item.PropertyChanged += UrlChanged;
            if (e.OldItems is not null) foreach (UrlItemViewModel item in e.OldItems) item.PropertyChanged -= UrlChanged;
            SyncUrls();
        };
    }
    private void AddUrlItem(UrlItemViewModel item) { item.PropertyChanged += UrlChanged; Urls.Add(item); }
    private void UrlChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => SyncUrls();
    private void SyncUrls()
    {
        if (Model is AppAction { Browser: { } b }) b.Urls = Urls.Select(u => u.Value).ToList();
        NotifyChange();
    }
    [RelayCommand] private void AddUrl() { Urls.Add(new(NewUrl)); NewUrl = ""; }
    [RelayCommand] private void RemoveUrl(UrlItemViewModel? item) { if (item is not null) Urls.Remove(item); }
    [RelayCommand] private void MoveUrlUp() => MoveUrl(-1);
    [RelayCommand] private void MoveUrlDown() => MoveUrl(1);
    private void MoveUrl(int offset)
    {
        if (SelectedUrl is null) return;
        var index = Urls.IndexOf(SelectedUrl); var next = index + offset;
        if (next >= 0 && next < Urls.Count) Urls.Move(index, next);
    }
    public void ReplaceApp(InstalledApp app)
    {
        if (Model is not AppAction a) return;
        var previousBrowser = BrowserCatalog.Identify(a.Target.Path, a.Target.Aumid);
        a.Target = new() { Aumid = app.Aumid, Path = app.ExePath }; a.Name = app.Name;
        a.Browser = app.BrowserId is null ? null : a.Browser ?? new();
        if (a.Browser is not null && previousBrowser != app.BrowserId) a.Browser.ProfileDir = "";
        var urls = a.Browser?.Urls.ToArray() ?? []; Urls.Clear(); foreach (var url in urls) Urls.Add(new(url));
        Icon = null; OnPropertyChanged(""); NotifyChange();
    }
    public async Task LoadDetailsAsync(IInstalledAppsService apps, IBrowserService browsers)
    {
        if (Model is not AppAction a) return;
        var app = new InstalledApp(a.Target.Aumid ?? a.Target.Path ?? a.Id.ToString(), a.Name, a.Target.Aumid, a.Target.Path, a.Target.Aumid?.Contains('!') == true, BrowserCatalog.Identify(a.Target.Path, a.Target.Aumid));
        var target = a.Target; var loadedIcon = await apps.GetIconAsync(app); if (a.Target != target) return; Icon = loadedIcon;
        var selectedProfile = ProfileDir; BrowserProfiles.Clear();
        if (app.BrowserId is not null) foreach (var p in browsers.GetProfiles(app.BrowserId)) BrowserProfiles.Add(p);
        if (selectedProfile.Length > 0 && BrowserProfiles.All(p => p.Directory != selectedProfile)) BrowserProfiles.Add(new(selectedProfile, selectedProfile));
        if (a.Browser is not null) a.Browser.ProfileDir = selectedProfile; OnPropertyChanged(nameof(ProfileDir));
    }
    private void Update<T>(T value, T current, Action<T> set, [CallerMemberName] string property = "")
    {
        if (EqualityComparer<T>.Default.Equals(value, current)) return;
        set(value); OnPropertyChanged(property); NotifyChange();
    }
    private void NotifyChange()
    {
        OnPropertyChanged(nameof(Description)); OnPropertyChanged(nameof(ValidationMessage)); OnPropertyChanged(nameof(IsBrowser));
        Changed?.Invoke();
    }
}
