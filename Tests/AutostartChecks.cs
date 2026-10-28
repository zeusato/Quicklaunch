using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using QuickStartLauncher.Models;
using QuickStartLauncher.Services.Apps;
using QuickStartLauncher.Services.Browsers;
using QuickStartLauncher.Services.Config;
using QuickStartLauncher.Services.Launch;
using QuickStartLauncher.ViewModels;

internal static class AutostartChecks
{
    public static async Task RunAsync(Action<bool, string> check, string output)
    {
        // Hold one actual launch call open, like a busy shell/UAC prompt. Every
        // other action and profile must dispatch before that call is released.
        using var process = new GatedProcess();
        var launch = new LaunchService(new FakeApps(), new FakeBrowsers(), process);
        var config = new ConfigService(new AppPaths(Path.Combine(output, "immediate-autostart")));
        var slow = new CommandAction { Name = "Blocked", Command = "blocked", DelayAfterMs = 60000 };
        var fast = new CommandAction { Command = "same-profile", DelayAfterMs = 60000 };
        var failed = new AppAction { Target = new() { Path = @"Z:\missing-immediate-test.exe" } };
        var delay = new DelayAction { Ms = 60000 };
        var first = new Profile { Actions = [slow, delay, fast, failed,
            new CommandAction { Command = "disabled", Enabled = false },
            new CommandAction { Command = "skip", SkipOnAutostart = true }] };
        var second = new Profile { Actions = [new CommandAction { Command = "other-profile" }] };
        config.Current.Profiles = [first, second];
        config.Current.Settings.Startup = new() { Mode = StartupMode.RunProfiles, ProfileIds = [first.Id, second.Id], DelaySeconds = 120, CountdownSeconds = 15 };
        config.Current.Settings.CloseDelayMs = 0;
        config.Current.Settings.KeepOpenOnError = true;
        var vm = new AutostartToastViewModel(config, launch);
        var run = vm.StartAsync();
        try
        {
            await process.AllDispatched.Task.WaitAsync(TimeSpan.FromSeconds(3));
            check(!run.IsCompleted && process.Started.Count == 3, "blocked app does not hold up same/other profile; all three requests dispatched");
            check(!process.Started.Any(p => p.Arguments.EndsWith("disabled") || p.Arguments.EndsWith("skip")), "parallel autostart honors disabled and skip flags");
        }
        finally { process.Release.Set(); await run.WaitAsync(TimeSpan.FromSeconds(5)); }
        check(vm.Results[slow.Id].State == RunState.Done && vm.Results[fast.Id].State == RunState.Done && vm.Results[delay.Id].State == RunState.Skipped,
            "legacy startup/countdown/action delays bypassed without changing config");
        check(vm.HasErrors && vm.Results[failed.Id].State == RunState.Failed, "parallel failures retain results without stopping other launches");
        check(config.Current.Settings.Startup.DelaySeconds == 120 && fast.DelayAfterMs == 60000, "manual delay settings preserved on disk model");
        await vm.StartAsync();
        check(process.Started.Count == 3, "repeated toast start cannot launch apps twice");

        // A valid browser executable and a Store AUMID need no AppsFolder scan.
        var noInventory = new NoInventoryApps();
        var directProcess = new FakeProcess();
        var direct = new LaunchService(noInventory, new FakeBrowsers(), directProcess);
        var browserPath = Path.Combine(output, "chrome.exe");
        File.WriteAllText(browserPath, "Test target; FakeProcess never executes this file.");
        var browser = await direct.RunSingleAsync(new AppAction {
            Target = new() { Path = browserPath, Aumid = "Chrome" },
            Browser = new() { Urls = ["https://example.org/"], ProfileDir = "Profile 1", NewWindow = true }
        }, CancellationToken.None);
        var store = await direct.RunSingleAsync(new AppAction { Target = new() { Aumid = "Package_test!App" } }, CancellationToken.None);
        check(browser.State == RunState.Done && store.State == RunState.Done && noInventory.Calls == 0,
            "known browser and Store app dispatch without enumerating installed apps");
        check(directProcess.Started.Single().Arguments.Contains("--profile-directory=\"Profile 1\"") && directProcess.Activated == "Package_test!App",
            "direct dispatch retains browser options and Store activation");
        var browserLookup = new DirectBrowserLookup(browserPath);
        var websiteLaunch = new LaunchService(noInventory, browserLookup, directProcess);
        var website = await websiteLaunch.RunSingleAsync(new WebsiteAction { Url = "https://example.org/site", BrowserId = "chrome" }, CancellationToken.None);
        check(website.State == RunState.Done && browserLookup.LookupCalls == 1 && noInventory.Calls == 0,
            "website resolves only its selected browser before dispatch");

        // Cancellation must return promptly even when a native launch call has
        // not returned yet; it cannot retract a request already sent to Windows.
        using var cancelledProcess = new GatedProcess();
        using var cancellation = new CancellationTokenSource();
        var cancelledLaunch = new LaunchService(new FakeApps(), new FakeBrowsers(), cancelledProcess);
        async Task ConsumeAsync()
        {
            await foreach (var _ in cancelledLaunch.RunAsync(new() { Actions = [slow] }, new(true), cancellation.Token)) { }
        }
        var pending = ConsumeAsync();
        try
        {
            await cancelledProcess.BlockedEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            cancellation.Cancel();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(2)); check(false, "parallel cancellation returns promptly"); }
            catch (OperationCanceledException) { check(true, "parallel cancellation returns promptly"); }
        }
        finally { cancelledProcess.Release.Set(); }
    }

    private sealed class NoInventoryApps : IInstalledAppsService
    {
        public int Calls;
        public Task<IReadOnlyList<InstalledApp>> GetAppsAsync(bool forceRefresh = false)
        { Calls++; throw new InvalidOperationException("Unexpected full app inventory scan"); }
        public Task<ImageSource?> GetIconAsync(InstalledApp app, int size = 48) => Task.FromResult<ImageSource?>(null);
    }

    private sealed class DirectBrowserLookup(string path) : IBrowserService
    {
        public int LookupCalls;
        public string? DefaultBrowserId => "chrome";
        public Task<IReadOnlyList<BrowserInfo>> GetInstalledAsync() => throw new InvalidOperationException("Unexpected full browser list lookup");
        public Task<BrowserInfo?> FindAsync(string id)
        { LookupCalls++; return Task.FromResult<BrowserInfo?>(id == "chrome" ? new("chrome", "Chrome", path) : null); }
        public IReadOnlyList<BrowserProfile> GetProfiles(string browserId) => [];
    }

    private sealed class GatedProcess : IProcessLauncher, IDisposable
    {
        public ConcurrentQueue<ProcessStartInfo> Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public TaskCompletionSource BlockedEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllDispatched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim blockedExited = new();
        public void Start(ProcessStartInfo info)
        {
            Started.Enqueue(info);
            if (Started.Count == 3) AllDispatched.TrySetResult();
            if (info.Arguments == "/d /c blocked")
            {
                BlockedEntered.TrySetResult();
                try { if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test gate not released"); }
                finally { blockedExited.Set(); }
            }
        }
        public Task ActivateAsync(string aumid, string args) => Task.CompletedTask;
        public void Dispose()
        {
            Release.Set();
            if (BlockedEntered.Task.IsCompleted) blockedExited.Wait(TimeSpan.FromSeconds(5));
            Release.Dispose(); blockedExited.Dispose();
        }
    }
}
