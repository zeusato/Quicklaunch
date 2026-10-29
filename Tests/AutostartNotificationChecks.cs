using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using QuickStartLauncher.Models;
using QuickStartLauncher.Services.Config;
using QuickStartLauncher.Services.Launch;
using QuickStartLauncher.ViewModels;
using QuickStartLauncher.Views;

internal static class AutostartNotificationChecks
{
    public static async Task RunAsync(Action<bool, string> check, string output)
    {
        foreach (var fail in new[] { false, true })
        {
            using var process = new DelayedProcess(fail);
            var launch = new LaunchService(new FakeApps(), new FakeBrowsers(), process);
            var config = new ConfigService(new AppPaths(Path.Combine(output, "notification-test")));
            var action = new CommandAction { Name = "Slow app", Command = "test-only" };
            var profile = new Profile { Actions = [action] };
            config.Current.Profiles = [profile];
            config.Current.Settings.Startup = new() { Mode = StartupMode.RunProfiles, ProfileIds = [profile.Id] };
            config.Current.Settings.CloseDelayMs = 60;
            config.Current.Settings.KeepOpenOnError = true;
            var vm = new AutostartToastViewModel(config, launch);
            var window = new AutostartToast(vm) { Topmost = false, ShowActivated = false };
            var hidden = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finishCount = 0; var errorPopups = 0; var navigated = false;
            vm.VisibilityRequested += visible =>
            {
                if (visible) { errorPopups++; window.Show(); }
                else { window.Hide(); hidden.TrySetResult(); }
            };
            vm.OpenLauncherRequested += () => navigated = true;
            vm.Finished += _ => { finishCount++; window.CloseAfterMonitoring(); finished.TrySetResult(); };
            window.Show();
            var run = vm.StartAsync();
            try
            {
                await process.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                await hidden.Task.WaitAsync(TimeSpan.FromSeconds(3));
                check(!window.IsVisible && !run.IsCompleted && finishCount == 0 && vm.Title == "Đã gửi 1 lệnh mở ứng dụng",
                    $"notification hides after dispatch while native request is blocked (lateError={fail})");
                if (!fail)
                {
                    window.Show(); Capture(window, Path.Combine(output, "autostart-dispatched.png")); window.Hide();
                }
                vm.DismissCommand.Execute(null);
                vm.OpenLauncherCommand.Execute(null);
                check(navigated && !run.IsCompleted && finishCount == 0, "dismiss/open launcher does not cancel background launch monitoring");
                process.Release.Set();
                await run.WaitAsync(TimeSpan.FromSeconds(3));
                if (fail)
                {
                    check(window.IsVisible && errorPopups == 1 && vm.HasErrors && finishCount == 0 && vm.Results[action.Id].State == RunState.Failed,
                        "late native failure reopens notification with retained error result");
                    Capture(window, Path.Combine(output, "autostart-late-error.png"));
                    // Alt+F4/titlebar close must safely dismiss, without recursively
                    // closing the WPF window from within its Closing event.
                    window.Close();
                    await finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    check(!window.IsVisible && finishCount == 1, "error notification can be closed once monitoring has finished");
                }
                else
                    check(!window.IsVisible && errorPopups == 0 && finishCount == 1 && vm.Results[action.Id].State == RunState.Done,
                        "late success finishes silently without reopening notification");
            }
            finally
            {
                process.Release.Set(); vm.Stop();
                await run.WaitAsync(TimeSpan.FromSeconds(3));
                window.CloseAfterMonitoring();
            }
        }

        // Queuing work is not the same as sending it to Windows. Do not show
        // 'sent' while an action is still resolving its executable.
        var inventory = new DeferredInventory();
        var deferredConfig = new ConfigService(new AppPaths(Path.Combine(output, "notification-resolve")));
        var deferredProfile = new Profile { Actions = [new AppAction { Target = new() { Path = @"Z:\missing-notification.exe", Aumid = "Deferred" } }] };
        deferredConfig.Current.Profiles = [deferredProfile];
        deferredConfig.Current.Settings.Startup.ProfileIds = [deferredProfile.Id];
        deferredConfig.Current.Settings.CloseDelayMs = 0;
        var deferredVm = new AutostartToastViewModel(deferredConfig, new LaunchService(inventory, new FakeBrowsers(), new FakeProcess()));
        var deferredHidden = false;
        deferredVm.VisibilityRequested += visible => { if (!visible) deferredHidden = true; };
        var pending = deferredVm.StartAsync();
        try
        {
            await inventory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            check(!deferredHidden && deferredVm.Title.StartsWith("Đang gửi"), "queued action is not reported as dispatched before target resolution");
        }
        finally
        {
            inventory.Resolved.TrySetResult([new("Deferred", "Resolved app", "Deferred", Environment.ProcessPath, false, null)]);
            await pending.WaitAsync(TimeSpan.FromSeconds(3));
        }
        check(deferredHidden && deferredVm.Results.Values.Single().State == RunState.Done, "resolved action dispatches and notification dismisses normally");
    }

    private static void Capture(System.Windows.Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }

    private sealed class DeferredInventory : QuickStartLauncher.Services.Apps.IInstalledAppsService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IReadOnlyList<InstalledApp>> Resolved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<InstalledApp>> GetAppsAsync(bool forceRefresh = false) { Entered.TrySetResult(); return Resolved.Task; }
        public Task<System.Windows.Media.ImageSource?> GetIconAsync(InstalledApp app, int size = 48) => Task.FromResult<System.Windows.Media.ImageSource?>(null);
    }
    private sealed class DelayedProcess(bool fail) : IProcessLauncher, IDisposable
    {
        public ManualResetEventSlim Release { get; } = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Start(ProcessStartInfo info)
        {
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test gate not released");
            if (fail) throw new Win32Exception(5, "Late test error");
        }
        public Task ActivateAsync(string aumid, string args) => Task.CompletedTask;
        public void Dispose() => Release.Dispose();
    }
}
