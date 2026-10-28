using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using QuickStartLauncher.Models;
using QuickStartLauncher.Services;
using QuickStartLauncher.Services.Apps;
using QuickStartLauncher.Services.Browsers;
using QuickStartLauncher.Services.Config;
using QuickStartLauncher.Services.Launch;
using QuickStartLauncher.Services.Startup;
using QuickStartLauncher.Services.Update;
using QuickStartLauncher.ViewModels;
using QuickStartLauncher.Views;
using Serilog;
namespace QuickStartLauncher;
public partial class App : Application
{
    private ServiceProvider? services;
    private Mutex? instance;
    private EventWaitHandle? activation;
    private RegisteredWaitHandle? wait;
    private bool ownsMutex;
    private LauncherWindow? launcher;
    private ConfigWindow? configWindow;
    private AutostartToast? toast;
    public ThemeService Themes => services!.GetRequiredService<ThemeService>();
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var watch = Stopwatch.StartNew();
        try
        {
            // Self-update, step 2: this is the staged "exe.new". Swap it in and hand over; no UI, no mutex.
            int? WaitPid() { var i = Array.IndexOf(e.Args, "--wait-pid"); return i >= 0 && i + 1 < e.Args.Length && int.TryParse(e.Args[i + 1], out var pid) ? pid : null; }
            if (e.Args.Contains("--apply-update"))
            {
                var applyPaths = new AppPaths(); Directory.CreateDirectory(applyPaths.LogDirectory);
                Log.Logger = new LoggerConfiguration().MinimumLevel.Information()
                    .WriteTo.File(Path.Combine(applyPaths.LogDirectory, ".log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14, shared: true).CreateLogger();
                Log.Information("Applying staged update");
                var target = UpdateService.ApplyStaged(WaitPid());
                Process.Start(new ProcessStartInfo(target, $"--updated --wait-pid {Environment.ProcessId}") { UseShellExecute = false });
                Log.CloseAndFlush(); Shutdown(); return;
            }
            // Step 3: the real exe after an update removes the staged copy and stale extraction folders.
            var updated = e.Args.Contains("--updated");
            // Local\ = per Windows logon session, so another user (fast user switching) runs their own instance.
            activation = new(false, EventResetMode.AutoReset, @"Local\QuickStartLauncher.Activate");
            instance = new(true, @"Local\QuickStartLauncher.SingleInstance", out ownsMutex);
            var paths = new AppPaths();
            if (!ownsMutex) { ForwardArgs(paths, e.Args); activation.Set(); Shutdown(); return; }
            Directory.CreateDirectory(paths.LogDirectory);
            Log.Logger = new LoggerConfiguration().MinimumLevel.Information()
                .WriteTo.File(Path.Combine(paths.LogDirectory, ".log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14, shared: true).CreateLogger();
            Log.Information("QuickStart starting: executable={Executable}, config={ConfigFile}, autostart={Autostart}",
                Environment.ProcessPath, paths.ConfigFile, e.Args.Contains("--autostart"));
            TakeForwardedArgs(paths); // drop leftovers from a second launch that raced a previous shutdown
            if (updated) UpdateService.CleanupAfterUpdate(WaitPid()); // step 3: drop exe.new + stale extractions
            else _ = Task.Run(UpdateService.CleanOldExtractions); // also covers manual upgrades from a zip
            DispatcherUnhandledException += (_, args) =>
            {
                Log.Error(args.Exception, "Unhandled UI error"); args.Handled = true;
                MessageBox.Show(args.Exception.Message, "QuickStart · Có lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Fatal(args.ExceptionObject as Exception, "Unhandled process error");
            TaskScheduler.UnobservedTaskException += (_, args) => { Log.Error(args.Exception, "Background task error"); args.SetObserved(); };
            var collection = new ServiceCollection();
            collection.AddSingleton(paths).AddSingleton<IConfigService, ConfigService>().AddSingleton<IconCache>()
                .AddSingleton<IInstalledAppsService, ShellAppsFolderProvider>().AddSingleton<IBrowserService, BrowserCatalog>()
                .AddSingleton<IProcessLauncher, ProcessLauncher>().AddSingleton<ILaunchService, LaunchService>()
                .AddSingleton<IStartupService, StartupService>().AddSingleton<ThemeService>()
                .AddSingleton<IUpdateService, UpdateService>().AddSingleton<UpdateViewModel>()
                .AddTransient<LauncherViewModel>().AddTransient<ConfigViewModel>().AddTransient<AutostartToastViewModel>();
            services = collection.BuildServiceProvider();
            services.GetRequiredService<UpdateViewModel>().RestartRequested += exe => _ = RestartAfterUpdateAsync(exe);
            var config = services.GetRequiredService<IConfigService>(); config.Load();
            Log.Information("Startup settings: mode={Mode}, selectedProfiles={ProfileCount}, delay={DelaySeconds}s, countdown={CountdownSeconds}s, readOnly={ReadOnly}",
                config.Current.Settings.Startup.Mode, config.Current.Settings.Startup.ProfileIds.Count,
                config.Current.Settings.Startup.DelaySeconds, config.Current.Settings.Startup.CountdownSeconds, config.IsReadOnly);
            Themes.Apply(config.Current.Settings.Theme);
            if (!config.IsReadOnly)
                try { services.GetRequiredService<IStartupService>().Apply(config.Current.Settings.Startup); }
                catch (Exception ex) { Log.Warning(ex, "Startup registration unavailable"); }
            wait = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => Dispatcher.BeginInvoke(() => OnActivated(TakeForwardedArgs(paths))), null, Timeout.Infinite, false);
            var args = e.Args;
            if (!args.Contains("--autostart"))
                _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => _ = ResolveAppTargetsAsync()));
            #if DEBUG
            if (args.Contains("--design"))
            {
                var demo = new DesignPreviewWindow(); Themes.Attach(demo); demo.Closed += (_, _) => Shutdown(); demo.Show(); return;
            }
#endif
            if (args.Contains("--config")) ShowConfig();
            else if (args.Contains("--autostart"))
            {
                if (config.Current.Settings.Startup.Mode == StartupMode.Off) { Shutdown(); return; }
                Log.Information("Autostart dispatch beginning {Elapsed} ms after process start; legacy startup delays bypassed",
                    (DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalMilliseconds);
                if (config.Current.Settings.Startup.Mode == StartupMode.ShowLauncher) ShowLauncher();
                else await ShowToastAsync();
            }
            else
            {
                ShowLauncher();
                if (updated) launcher!.ViewModel.Status = $"Đã cập nhật lên v{services.GetRequiredService<IUpdateService>().CurrentVersion} ✓";
                await RunProfileArgAsync(args);
            }
            Log.Information("Startup flow completed in {Elapsed} ms", watch.ElapsedMilliseconds);

        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failed"); MessageBox.Show(ex.ToString(), "QuickStart · Không khởi động được"); Shutdown(1);
        }
    }
    private async void OnActivated(string[] args)
    {
        try
        {
            // Windows may invoke multiple startup entries. A duplicate automatic
            // activation must not interrupt or repeat the first instance's run.
            if (args.Contains("--autostart") && !args.Contains("--config"))
            {
                Log.Information("Duplicate autostart activation ignored; existing instance retains control");
                return;
            }
            // An autostart run in progress keeps going; a second launch only brings its toast forward.
            if (toast is not null) { toast.Activate(); return; }
            if (args.Contains("--config")) { ShowConfig(); return; }
            if (configWindow is { IsVisible: true }) { configWindow.WindowState = WindowState.Normal; configWindow.Activate(); return; }
            ShowLauncher();
            await RunProfileArgAsync(args);
        }
        catch (Exception ex) { Log.Error(ex, "Handling activation from a second instance failed"); }
    }
    private async Task RunProfileArgAsync(string[] args)
    {
        var index = Array.IndexOf(args, "--profile");
        if (index < 0 || launcher is null) return;
        var name = index + 1 < args.Length ? args[index + 1] : "";
        var profile = launcher.ViewModel.Profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || p.Id.ToString().Equals(name, StringComparison.OrdinalIgnoreCase));
        if (profile is null) launcher.ViewModel.Status = "Không tìm thấy profile: " + name;
        else if (launcher.ViewModel.IsRunning) launcher.ViewModel.Status = "Đang chạy, thử lại sau khi xong.";
        else { launcher.ViewModel.SelectedProfile = profile; await launcher.ViewModel.RunAsync(profile.Model); }
    }
    private async Task RestartAfterUpdateAsync(string exe)
    {
        try
        {
            if (configWindow is not null) await configWindow.ViewModel.FlushAsync();
            var config = services!.GetRequiredService<IConfigService>(); if (!config.IsReadOnly) await config.SaveAsync();
        }
        catch (Exception ex) { Log.Error(ex, "Save before update restart failed"); }
        Process.Start(new ProcessStartInfo(exe, $"--apply-update --wait-pid {Environment.ProcessId}") { UseShellExecute = false });
        Shutdown();
    }
    public static string ForwardFile(AppPaths paths) => Path.Combine(paths.Root, "activate.args.json");
    public static void ForwardArgs(AppPaths paths, string[] args)
    {
        try { Directory.CreateDirectory(paths.Root); File.WriteAllText(ForwardFile(paths), System.Text.Json.JsonSerializer.Serialize(args)); }
        catch (Exception ex) { Debug.WriteLine(ex); /* Still activate; the args are just lost. */ }
    }
    public static string[] TakeForwardedArgs(AppPaths paths)
    {
        try
        {
            var file = ForwardFile(paths);
            if (!File.Exists(file)) return [];
            var args = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(file)) ?? [];
            File.Delete(file); return args;
        }
        catch (Exception ex) { Log.Warning(ex, "Could not read forwarded arguments"); return []; }
    }
    private void ShowLauncher()
    {
        if (launcher is null)
        {
            var vm = services!.GetRequiredService<LauncherViewModel>(); vm.Reload();
            vm.Updates = services!.GetRequiredService<UpdateViewModel>(); _ = vm.Updates.AutoCheckAsync();
            launcher = new(vm); Themes.Attach(launcher);
            launcher.ContentRendered += (_, _) => Log.Information("Launcher first render from process start: {Elapsed} ms", (DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalMilliseconds);
            vm.CloseRequested += () => launcher?.Close();
            vm.ConfigRequested += picker => ShowConfig(picker);
            launcher.Closed += async (_, _) =>
            {
                launcher = null;
                try { var c = services!.GetRequiredService<IConfigService>(); if (!c.IsReadOnly) await c.SaveAsync(); } catch (Exception e) { Log.Error(e, "Final save failed"); }
                if (configWindow is null) Shutdown();
            };
        }
        MainWindow = launcher; launcher.Show(); launcher.WindowState = WindowState.Normal; launcher.Activate();
    }
    private void ShowConfig(bool picker = false)
    {
        if (configWindow is null)
        {
            var vm = services!.GetRequiredService<ConfigViewModel>();
            vm.Settings.Updates = services!.GetRequiredService<UpdateViewModel>();
            configWindow = new(vm, services!.GetRequiredService<IInstalledAppsService>()); Themes.Attach(configWindow);
            configWindow.Closed += (_, _) =>
            {
                configWindow = null;
                if (launcher is not null) { launcher.ViewModel.Reload(); ShowLauncher(); } else Shutdown();
            };
        }
        launcher?.Hide(); MainWindow = configWindow; configWindow.Show(); configWindow.Activate();
        if (picker) configWindow.ShowPicker();
    }
    private async Task ShowToastAsync()
    {
        var vm = services!.GetRequiredService<AutostartToastViewModel>();
        var finished = false;
        vm.Finished += open => { finished = true; toast?.Close(); toast = null; if (open) { ShowLauncher(); launcher!.ViewModel.ApplyResults(vm.Results); } else Shutdown(); };
        // Send requests before constructing/rendering the notification window.
        var run = vm.StartAsync();
        if (!finished)
        {
            toast = new(vm); Themes.Attach(toast); MainWindow = toast; toast.Show();
        }
        await run;
    }
    private async Task ResolveAppTargetsAsync()
    {
        try
        {
            var config = services!.GetRequiredService<IConfigService>();
            if (config.IsReadOnly) return;
            var apps = await services!.GetRequiredService<IInstalledAppsService>().GetAppsAsync();
            var changed = false;
            foreach (var a in config.Current.Profiles.SelectMany(p => p.Actions).OfType<AppAction>())
            {
                if (!string.IsNullOrWhiteSpace(a.Target.Aumid) || string.IsNullOrWhiteSpace(a.Target.Path)) continue;
                var installed = apps.FirstOrDefault(app => string.Equals(app.ExePath, a.Target.Path, StringComparison.OrdinalIgnoreCase));
                if (installed is null) continue;
                a.Target.Aumid = installed.Aumid; changed = true;
            }
            if (changed) await config.SaveAsync();
        }
        catch (Exception ex) { Log.Warning(ex, "Background app enrichment failed"); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        wait?.Unregister(null); activation?.Dispose();
        if (ownsMutex) instance?.ReleaseMutex(); instance?.Dispose();
        Log.Information("QuickStart exiting with code {Code}", e.ApplicationExitCode); services?.Dispose(); Log.CloseAndFlush(); base.OnExit(e);
    }
}
