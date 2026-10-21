using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using QuickStartLauncher.Models;
using QuickStartLauncher.Services.Apps;
using QuickStartLauncher.Services.Search;
namespace QuickStartLauncher.ViewModels;
public sealed partial class AppTileViewModel(InstalledApp app, bool added) : ObservableObject
{
    public InstalledApp App { get; } = app;
    public string Name => App.Name;
    public string Badge => added ? "Đã thêm" : App.BrowserId is not null ? "Trình duyệt" : App.IsPackaged ? "Store" : "";
    public bool Added => added;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasIcon))] private ImageSource? icon;
    public bool HasIcon => Icon is not null;
    [ObservableProperty] private bool selected;
}
public sealed partial class AppPickerViewModel : ObservableObject
{
    private readonly IInstalledAppsService apps;
    private readonly HashSet<string> added;
    private readonly DispatcherTimer debounce;
    private List<SearchEntry> search = [];
    private readonly Dictionary<string, AppTileViewModel> tiles = [];
    public ObservableCollection<AppTileViewModel> Results { get; } = [];
    public List<InstalledApp> Selection { get; } = [];
    public string[] Filters { get; } = ["Tất cả", "Trình duyệt", "Store", "Đã thêm"];
    [ObservableProperty] private string query = "";
    [ObservableProperty] private string filter = "Tất cả";
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string status = "Đang đọc danh sách ứng dụng…";
    public bool SingleSelect { get; }
    public string AddLabel => $"Thêm {Selection.Count} ứng dụng";
    public AppPickerViewModel(IInstalledAppsService apps, IEnumerable<LaunchAction> existing, bool singleSelect)
    {
        this.apps = apps; SingleSelect = singleSelect;
        added = existing.OfType<AppAction>().SelectMany(a => new[] { a.Target.Aumid, a.Target.Path }).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        debounce = new() { Interval = TimeSpan.FromMilliseconds(120) };
        debounce.Tick += (_, _) => { debounce.Stop(); ApplyFilter(); };
    }
    partial void OnQueryChanged(string value) { debounce.Stop(); debounce.Start(); }
    partial void OnFilterChanged(string value) => ApplyFilter();
    public async Task LoadAsync()
    {
        IsRefreshing = true;
        try { SetApps(await apps.GetAppsAsync()); SetApps(await apps.GetAppsAsync(true)); }
        catch (Exception ex) { Status = "Không đọc được ứng dụng: " + ex.Message; }
        finally { IsRefreshing = false; }
    }
    private void SetApps(IReadOnlyList<InstalledApp> list)
    {
        search = list.Select(a => new SearchEntry(a)).ToList();
        foreach (var app in list)
            if (!tiles.ContainsKey(app.Key))
            {
                var tile = new AppTileViewModel(app, added.Contains(app.Key) || app.ExePath is not null && added.Contains(app.ExePath));
                tile.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(tile.Selected)) SelectionChanged(tile); };
                tiles.Add(app.Key, tile);
            }
        ApplyFilter();
    }
    private void SelectionChanged(AppTileViewModel tile)
    {
        if (tile.Selected)
        {
            if (SingleSelect) foreach (var other in tiles.Values.Where(t => t != tile && t.Selected)) other.Selected = false;
            if (!Selection.Any(a => a.Key == tile.App.Key)) Selection.Add(tile.App);
        }
        else Selection.RemoveAll(a => a.Key == tile.App.Key);
        OnPropertyChanged(nameof(AddLabel)); Status = $"Đã chọn {Selection.Count} · {Results.Count} ứng dụng";
    }
    private void ApplyFilter()
    {
        var found = FuzzySearch.Find(search, Query);
        Results.Clear();
        foreach (var app in found)
        {
            var tile = tiles[app.Key];
            if (Filter == "Trình duyệt" && app.BrowserId is null || Filter == "Store" && !app.IsPackaged || Filter == "Đã thêm" && !tile.Added) continue;
            Results.Add(tile);
        }
        Status = $"Đã chọn {Selection.Count} · {Results.Count} ứng dụng";
    }
    public async Task LoadIconAsync(AppTileViewModel tile) { if (tile.Icon is null) tile.Icon = await apps.GetIconAsync(tile.App, 64); }
    public void Stop() => debounce.Stop();
}
