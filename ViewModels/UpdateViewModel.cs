using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickStartLauncher.Services.Update;
using Serilog;
namespace QuickStartLauncher.ViewModels;
/// <summary>Single shared update state, bound by both the Launcher banner and the Settings page.</summary>
public sealed partial class UpdateViewModel(IUpdateService updates) : ObservableObject
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(ShowBanner), nameof(BannerText))] private UpdateInfo? available;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowBanner))] private bool dismissed;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private double progress;
    public bool HasUpdate => Available is not null;
    public bool ShowBanner => HasUpdate && !Dismissed;
    public string BannerText => Available is null ? "" : $"Có bản mới v{Available.Version}";
    public string CurrentVersionText => $"Phiên bản hiện tại: v{updates.CurrentVersion}";
    /// <summary>Raised after the exe was swapped; the app restarts into the given path.</summary>
    public event Action<string>? RestartRequested;

    public async Task AutoCheckAsync()
    {
        try { var found = await updates.CheckAsync(false); if (found is not null) Available = found; }
        catch (Exception ex) { Log.Debug(ex, "Background update check failed"); } // offline etc.: stay silent
    }
    [RelayCommand] private async Task CheckAsync()
    {
        if (IsBusy) return;
        IsBusy = true; Status = "Đang kiểm tra…";
        try
        {
            Available = await updates.CheckAsync(true);
            Status = Available is null ? $"Bạn đang dùng bản mới nhất (v{updates.CurrentVersion})." : $"Có bản mới v{Available.Version}.";
        }
        catch (Exception ex) { Status = "Không kiểm tra được: " + ex.Message; Log.Warning(ex, "Update check failed"); }
        finally { IsBusy = false; }
    }
    [RelayCommand] private async Task InstallAsync()
    {
        if (IsBusy || Available is null) return;
        IsBusy = true; Progress = 0; Status = $"Đang tải v{Available.Version}…";
        try
        {
            var exe = await updates.InstallAsync(Available, new Progress<double>(p => { Progress = p; Status = $"Đang tải v{Available.Version}… {p:P0}"; }));
            Status = "Đang khởi động lại…";
            RestartRequested?.Invoke(exe);
        }
        catch (Exception ex) { Status = "Cập nhật thất bại: " + ex.Message; Log.Error(ex, "Update install failed"); IsBusy = false; }
    }
    [RelayCommand] private void Dismiss() => Dismissed = true;
}
