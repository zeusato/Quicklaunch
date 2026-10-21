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
    private readonly CancellationTokenSource startupDelay = new();
    public ThemeService Themes => services!.GetRequiredService<ThemeService>();
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var watch = Stopwatch.StartNew();
        try
        {
            activation = new(false, EventResetMode.AutoReset, @"Global\QuickStartLauncher.Activate");
            instance = new(true, @"Global\QuickStartLauncher.SingleInstance", out ownsMutex);
            if (!ownsMutex) { activation.Set(); Shutdown(); return; }
            var paths = new AppPaths();
            Directory.CreateDirectory(paths.LogDirectory);
            Log.Logger = new LoggerConfiguration().MinimumLevel.Information()
                .WriteTo.File(Path.Combine(paths.LogDirectory, ".log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14).CreateLogger();
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
                .AddTransient<LauncherViewModel>().AddTransient<ConfigViewModel>().AddTransient<AutostartToastViewModel>();
            services = collection.BuildServiceProvider();
            var config = services.GetRequiredService<IConfigService>(); config.Load();
            Themes.Apply(config.Current.Settings.Theme);
            if (!config.IsReadOnly)
                try { services.GetRequiredService<IStartupService>().Apply(config.Current.Settings.Startup); }
                catch (Exception ex) { Log.Warning(ex, "Startup registration unavailable"); }
            wait = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => Dispatcher.BeginInvoke(() =>
            {
                startupDelay.Cancel();
                if (configWindow is { IsVisible: true }) { configWindow.WindowState = WindowState.Normal; configWindow.Activate(); }
                else { toast?.Close(); toast = null; ShowLauncher(); }
            }), null, Timeout.Infinite, false);
            var args = e.Args;
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
                try { await Task.Delay(config.Current.Settings.Startup.DelaySeconds * 1000, startupDelay.Token); }
                catch (OperationCanceledException) { return; }
                if (config.Current.Settings.Startup.Mode == StartupMode.ShowLauncher) ShowLauncher();
                else ShowToast();
            }
            else
            {
                ShowLauncher();
                var index = Array.IndexOf(args, "--profile");
                if (index >= 0)
                {
                    var name = index + 1 < args.Length ? args[index + 1] : "";
                    var profile = launcher!.ViewModel.Profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || p.Id.ToString().Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (profile is null) launcher.ViewModel.Status = "Không tìm thấy profile: " + name;
                    else { launcher.ViewModel.SelectedProfile = profile; await launcher.ViewModel.RunAsync(profile.Model); }
                }
            }
            Log.Information("Startup flow completed in {Elapsed} ms", watch.ElapsedMilliseconds);

        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failed"); MessageBox.Show(ex.ToString(), "QuickStart · Không khởi động được"); Shutdown(1);
        }
    }
    private void ShowLauncher()
    {
        if (launcher is null)
        {
            var vm = services!.GetRequiredService<LauncherViewModel>(); vm.Reload();
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
    private void ShowToast()
    {
        var vm = services!.GetRequiredService<AutostartToastViewModel>();
        toast = new(vm); Themes.Attach(toast); MainWindow = toast;
        vm.Finished += open => { toast?.Close(); toast = null; if (open) { ShowLauncher(); launcher!.ViewModel.ApplyResults(vm.Results); } else Shutdown(); };
        toast.Show();
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
        startupDelay.Cancel(); wait?.Unregister(null); activation?.Dispose();
        if (ownsMutex) instance?.ReleaseMutex(); instance?.Dispose();
        Log.Information("QuickStart exiting with code {Code}", e.ApplicationExitCode); services?.Dispose(); Log.CloseAndFlush(); base.OnExit(e);
    }
}
