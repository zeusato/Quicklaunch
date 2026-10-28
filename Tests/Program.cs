using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QuickStartLauncher.Converters;
using QuickStartLauncher.Models;
using QuickStartLauncher.Services;
using QuickStartLauncher.Services.Apps;
using QuickStartLauncher.Services.Browsers;
using QuickStartLauncher.Services.Config;
using QuickStartLauncher.Services.Launch;
using QuickStartLauncher.Services.Search;
using QuickStartLauncher.Services.Startup;
using QuickStartLauncher.ViewModels;
using QuickStartLauncher.Views;
using QuickStartLauncher.Views.Dialogs;
using Wpf.Ui.Markup;

internal static class Program
{
    private static int passed;
    private static readonly List<string> failures = [];
    private static string output = "";
    [STAThread]
    public static int Main(string[] args)
    {
        output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/tests"));
        Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
        app.Resources.MergedDictionaries.Add(new ControlsDictionary());
        foreach (var file in new[] { "Glass.Colors", "Glass.Controls", "Glass.Animations" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/QuickStartLauncher;component/Themes/{file}.xaml", UriKind.Relative) });
        app.Resources["BoolVisibility"] = new BoolVisibilityConverter();
        app.Resources["StateBrush"] = new StateBrushConverter();
        app.Resources["NullVisibility"] = new NullVisibilityConverter();
        app.Resources["EmptyVisibility"] = new EmptyVisibilityConverter();
        app.DispatcherUnhandledException += (_, e) => { failures.Add("UI unhandled: " + e.Exception); Console.WriteLine(e.Exception); File.WriteAllText(Path.Combine(output, "ui-failure.txt"), e.Exception.ToString()); e.Handled = true; app.Shutdown(1); };
        app.Startup += async (_, _) =>
        {
            try { await RunAsync(args); }
            catch (Exception ex) { failures.Add(ex.ToString()); }
            finally
            {
                var report = $"{passed} passed; {failures.Count} failed\n" + string.Join("\n", failures);
                Console.WriteLine(report); File.WriteAllText(Path.Combine(output, "results.txt"), report);
                app.Shutdown(failures.Count == 0 ? 0 : 1);
            }
        };
        return app.Run();
    }
    private static void Check(bool condition, string name)
    {
        if (!condition) { failures.Add(name); Console.WriteLine("FAIL " + name); }
        else { passed++; Console.WriteLine("PASS " + name); }
    }
    private static async Task RunAsync(string[] args)
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/config-v2-real.json"));
        var migrated = ConfigMigrator.Migrate(json);
        var actions = migrated.Profiles[0].Actions;
        Check(actions.Count == 4 && migrated.Version == 3, "real v2: four actions migrated");
        Check(actions[0] is AppAction { Browser.Urls: ["https://www.facebook.com/"] }, "Chrome Facebook URL preserved");
        Check(actions[1] is AppAction a && a.Target.Path!.EndsWith("Zalo.exe"), "Zalo path preserved");
        Check(actions[2] is CommandAction { Command: "start msteams:", Shell: CommandShell.Cmd, Hidden: true }, "Teams hidden cmd preserved");
        Check(actions[3] is AppAction o && o.Target.Path!.EndsWith("OUTLOOK.EXE"), "Outlook path preserved");
        Check(migrated.ActiveProfileId == migrated.Profiles[0].Id && migrated.Settings.Startup.Mode == StartupMode.Off, "active profile and safe startup default");
        var serialized = JsonSerializer.Serialize(migrated, ConfigJson.Options);
        Check(!serialized.Contains("$type") && !serialized.Contains("\"type\"") && serialized.Contains("\"kind\""), "single JSON discriminator");
        Check(ConfigJson.Clone(migrated).Profiles[0].Actions.Count == 4, "v3 polymorphic round trip");
        var unordered = """{"id":"023c8d62-a5a7-440a-9d8f-55a366c9f6e7","ms":120,"kind":"delay"}""";
        Check(ConfigJson.Read<LaunchAction>(unordered) is DelayAction { Ms: 120 }, "JSON discriminator accepted in any property order");
        Check(new ActionItemViewModel(new AppAction { Target = new() { Path = @"Z:\unavailable.exe" } }).ValidationMessage.Contains("Không tìm thấy"), "imported missing application has visible warning");

        var configRoot = Path.Combine(output, "config-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(configRoot); Directory.CreateDirectory(paths.ConfigDirectory);
        File.WriteAllText(paths.ConfigFile, json);
        var config = new ConfigService(paths); config.Load();
        Check(File.ReadAllText(Path.Combine(paths.ConfigDirectory, "config.v2.bak.json")) == json, "migration preserves byte content backup");
        Check(JsonDocument.Parse(File.ReadAllText(paths.ConfigFile)).RootElement.GetProperty("version").GetInt32() == 3, "migration writes v3");
        var id = config.Current.ActiveProfileId;
        config.Current.Profiles[0].Name = "Renamed"; config.Current.Settings.Startup.ProfileIds = [id];
        await config.SaveAsync();
        var reloaded = new ConfigService(paths); reloaded.Load();
        Check(reloaded.Current.Settings.Startup.ProfileIds.Single() == id, "rename retains startup identity");
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => config.SaveAsync()));
        Check(JsonDocument.Parse(File.ReadAllText(paths.ConfigFile)).RootElement.GetProperty("profiles").GetArrayLength() == 1 && !File.Exists(paths.ConfigFile + ".tmp"), "20 serialized atomic saves leave valid config");
        File.WriteAllText(paths.ConfigFile, "{broken");
        var broken = new ConfigService(paths); broken.Load();
        Check(broken.IsReadOnly && broken.Warning is not null, "broken config reports warning and blocks writes");
        try { await broken.SaveAsync(); Check(false, "broken save blocked"); } catch (InvalidOperationException) { Check(File.ReadAllText(paths.ConfigFile) == "{broken", "broken config retained"); }
        await broken.ImportAsync(serialized);
        Check(!broken.IsReadOnly && broken.Current.Profiles[0].Actions.Count == 4, "import recovers protected config");
        var unknown = serialized.Replace("\"version\": 3", "\"version\": 99");
        try { await broken.ImportAsync(unknown); Check(false, "future config rejected"); } catch (InvalidDataException) { Check(true, "future config rejected"); }
        var fakeApps = new FakeApps();
        var entries = fakeApps.Items.Select(a => new SearchEntry(a)).ToList();
        foreach (var (query, expected) in new[] { ("chrmoe", "chrome"), ("outlok", "outlook"), ("zalo", "zalo"), ("teams", "teams"), ("coc coc", "coccoc"), ("coccoc", "coccoc"), ("vsc", "code"), ("CỐC CỐC", "coccoc") })
            Check(FuzzySearch.Find(entries, query).Take(3).Any(a => a.Key == expected), "fuzzy " + query);
        Check(TextNormalizer.Normalize("Đặng  Văn") == "dang van", "Vietnamese accent normalization");
        Check(BrowserCatalog.Identify(@"C:\x\Chrome.exe", null) == "chrome", "case independent browser detection");
        Check(BrowserCatalog.Identify(@"C:\x\browser.exe", null) is null && BrowserCatalog.Identify(@"C:\CocCoc\Browser\browser.exe", null) == "coccoc", "Coc Coc disambiguation");
        Check(BrowserCatalog.Identify(@"C:\chrome.exe", "Chrome._crx_test") is null, "PWA not classified as browser");
        var browserOptions = new BrowserOptions { ProfileDir = "Profile 1", NewWindow = true, Urls = ["https://example.org/a?q=hello%20world", "https://example.org/b"] };
        var browserArgs = BrowserArguments.Build("chrome", browserOptions);
        Check(browserArgs.Contains("--profile-directory=\"Profile 1\"") && browserArgs.Contains("--new-window") && browserArgs.Contains("\"https://example.org/b\""), "Chromium profile + new window + two quoted URLs");
        Check(BrowserArguments.Build("firefox", browserOptions).Contains("-new-tab"), "Firefox subsequent URL uses new tab");
        Check(!BrowserArguments.IsValidUrl("javascript:alert(1)") && !BrowserArguments.IsValidUrl("--load-extension=x") && BrowserArguments.IsValidUrl("file:///C:/test.html"), "URL scheme validation");
        var profiles = BrowserProfileReader.ParseChromium("""{"profile":{"info_cache":{"Profile 1":{"name":"Work","user_name":"work@example.org"}}}}""").ToList();
        Check(profiles.Single().DisplayName == "Work (work@example.org)", "Chromium profile display name/email");
        Check(BrowserProfileReader.ParseFirefox("[Profile0]\nName=work\n[Install1]\nDefault=x").Single().Directory == "work", "Firefox profile parser");
        var processes = new FakeProcess();
        var launch = new LaunchService(fakeApps, new FakeBrowsers(), processes);
        var exe = Environment.ProcessPath!;
        var sequence = new Profile { Actions = [
            new AppAction { Name = "valid", Target = new() { Path = exe }, DelayAfterMs = 80 },
            new AppAction { Name = "bad", Target = new() { Path = @"Z:\missing-app.exe" } },
            new DelayAction { Ms = 150 },
            new CommandAction { Command = "echo after-error", Hidden = true },
            new CommandAction { Command = "disabled", Enabled = false },
            new CommandAction { Command = "skip-auto", SkipOnAutostart = true }
        ] };
        var watch = Stopwatch.StartNew(); var progress = new List<RunProgress>();
        await foreach (var p in launch.RunAsync(sequence, new(true), CancellationToken.None)) progress.Add(p);
        Check(progress.Count(p => p.State == RunState.Failed) == 1 && processes.Started.Count == 2, "failure isolation and autostart skip/disabled");
        Check(progress.Any(p => p.ActionId == sequence.Actions[2].Id && p.State == RunState.Skipped), "autostart skips explicit delay actions");
        var manualSequence = new Profile { Actions = sequence.Actions.Where(a => !a.SkipOnAutostart).ToList() };
        progress.Clear(); processes.Started.Clear(); watch.Restart();
        await foreach (var p in launch.RunAsync(manualSequence, new(), CancellationToken.None)) progress.Add(p);
        Check(progress.Where(p => p.State is RunState.Done or RunState.Failed or RunState.Skipped).Select(p => p.ActionId).SequenceEqual(manualSequence.Actions.Select(a => a.Id)), "manual run retains sequential action order");
        Check(watch.ElapsedMilliseconds >= 220 && watch.ElapsedMilliseconds < 600, "delay + delayAfter timing ±150 ms allowance");
        Check(progress.Any(p => p.RemainingMs > 0), "delay countdown progress");
        var cmd = processes.Started.Last(); Check(!cmd.UseShellExecute && cmd.CreateNoWindow && cmd.WindowStyle == ProcessWindowStyle.Hidden, "hidden command has no console");
        processes.ThrowUac = true;
        var uac = await launch.RunSingleAsync(new AppAction { Target = new() { Path = exe }, RunAsAdmin = true }, CancellationToken.None);
        Check(uac.State == RunState.Skipped, "UAC decline becomes skipped"); processes.ThrowUac = false;
        using (var cancellation = new CancellationTokenSource(50))
        {
            var startCount = processes.Started.Count;
            try { await foreach (var _ in launch.RunAsync(new() { Actions = [new DelayAction { Ms = 1000 }, new CommandAction { Command = "must-not-run" }] }, new(), cancellation.Token)) { } Check(false, "cancel stops queue"); }
            catch (OperationCanceledException) { Check(processes.Started.Count == startCount, "cancel stops queue"); }
        }
        await launch.RunSingleAsync(new WebsiteAction { Url = "https://example.org", BrowserId = "default" }, CancellationToken.None);
        Check(processes.Started.Last().FileName == "https://example.org" && processes.Started.Last().UseShellExecute, "default browser uses Windows URL handler");
        await launch.RunSingleAsync(new AppAction { Target = new() { Aumid = "Package_test!App" } }, CancellationToken.None);
        Check(processes.Activated == "Package_test!App", "Store uses activation manager");
        var fallback = await launch.RunSingleAsync(new AppAction { Target = new() { Aumid = "SpecialApp" }, Arguments = "--danger-of-silent-loss" }, CancellationToken.None);
        Check(fallback.State == RunState.Failed, "unsupported AUMID arguments explicitly rejected");
        var ps = LaunchService.BuildCommand(new() { Shell = CommandShell.Powershell, Command = "Write-Output 'a \"quoted\" string'\nWrite-Output b" });
        Check(Encoding.Unicode.GetString(Convert.FromBase64String(ps.Arguments.Split(' ').Last())).Contains("'a \"quoted\" string'"), "PowerShell quoting and multiline preserved");

        var registryRoot = @"Software\QuickStartLauncher.Tests\" + Guid.NewGuid().ToString("N");
        var executablePath = @"C:\test one\QuickStartLauncher.exe";
        try
        {
            var registryStartup = new StartupService(registryRoot + @"\Run", registryRoot + @"\Approved", () => executablePath);
            registryStartup.Apply(new() { Mode = StartupMode.ShowLauncher });
            using var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(registryRoot + @"\Run");
            Check((string?)run!.GetValue("QuickStartLauncher") == "\"C:\\test one\\QuickStartLauncher.exe\" --autostart", "startup registration quotes executable path");
            executablePath = @"D:\moved\QuickStartLauncher.exe";
            registryStartup.Apply(new() { Mode = StartupMode.RunProfiles });
            Check(((string?)run.GetValue("QuickStartLauncher"))!.StartsWith("\"D:\\moved"), "startup path self-heals after move");
            using var approved = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(registryRoot + @"\Approved");
            approved.SetValue("QuickStartLauncher", new byte[] { 3, 0, 0, 0 }, Microsoft.Win32.RegistryValueKind.Binary);
            Check(registryStartup.IsBlockedByWindows, "StartupApproved disabled warning");
            registryStartup.Apply(new() { Mode = StartupMode.Off });
            Check(run.GetValue("QuickStartLauncher") is null, "startup off removes value");
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(registryRoot, false); }
        var autoConfig = new ConfigService(new AppPaths(Path.Combine(output, "autostart-" + Guid.NewGuid().ToString("N"))));
        var p1 = new Profile { Name = "A", Actions = [new CommandAction { Command = "A" }, new CommandAction { Command = "skip", SkipOnAutostart = true }] };
        var p2 = new Profile { Name = "B", Actions = [new CommandAction { Command = "B" }] };
        autoConfig.Current.Profiles = [p1, p2]; autoConfig.Current.ActiveProfileId = p1.Id;
        autoConfig.Current.Settings.Startup = new() { Mode = StartupMode.RunProfiles, ProfileIds = [p1.Id, p2.Id], CountdownSeconds = 5 };
        autoConfig.Current.Settings.CloseDelayMs = 0;
        var autoProcesses = new FakeProcess();
        var autoLaunch = new LaunchService(fakeApps, new FakeBrowsers(), autoProcesses);
        var autoVm = new AutostartToastViewModel(autoConfig, autoLaunch);
        autoVm.CancelCommand.Execute(null); await autoVm.StartAsync();
        Check(autoProcesses.Started.Count == 0, "toast cancelled before dispatch opens nothing");
        autoVm = new(autoConfig, autoLaunch); await autoVm.StartAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Check(autoProcesses.Started.Select(p => p.Arguments).Order().SequenceEqual(new[] { "/d /c A", "/d /c B" }), "autostart dispatches all profiles without legacy countdown");
        Check(autoVm.Results.Values.Count(p => p.State == RunState.Skipped) == 1, "toast retains per-action status");
        autoVm = new(autoConfig, autoLaunch); var openedLauncher = false; autoVm.Finished += open => openedLauncher = open;
        autoVm.OpenLauncherCommand.Execute(null); await autoVm.StartAsync();
        Check(openedLauncher && autoProcesses.Started.Count == 2, "toast open launcher cancels undispatched work");
        await AutostartChecks.RunAsync(Check, output);
        var launcherVm = new LauncherViewModel(autoConfig, autoLaunch, fakeApps, new FakeBrowsers()); launcherVm.Reload();
        launcherVm.SelectedProfile = launcherVm.Profiles[1];
        Check(autoConfig.Current.ActiveProfileId == p1.Id, "launcher profile switch does not overwrite default profile");
        autoConfig.Current.Settings.Startup.ProfileIds = [];
        autoVm = new(autoConfig, autoLaunch); bool? emptyFinished = null; autoVm.Finished += open => emptyFinished = open;
        await autoVm.StartAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Check(emptyFinished == true && autoProcesses.Started.Count == 2, "autostart with no profiles hands over to launcher");
        autoConfig.Current.Settings.Startup.ProfileIds = [p1.Id, p2.Id];
        var releaseJson = JsonDocument.Parse("""{"tag_name":"v3.2.1","draft":false,"prerelease":false,"html_url":"https://x/r","body":"n","assets":[{"name":"QuickStartLauncher-win-x64.zip","browser_download_url":"https://x/z"},{"name":"QuickStartLauncher-win-x64.zip.sha256","browser_download_url":"https://x/s"}]}""").RootElement;
        var parsed = QuickStartLauncher.Services.Update.UpdateService.Parse(releaseJson);
        Check(parsed is { Version.Major: 3, Version.Minor: 2, Version.Build: 1, ZipUrl: "https://x/z", Sha256Url: "https://x/s" }, "update release parsed (tag, zip, sha256)");
        var preJson = JsonDocument.Parse("""{"tag_name":"v9.0.0","prerelease":true,"html_url":"","assets":[{"name":"QuickStartLauncher-win-x64.zip","browser_download_url":"https://x/z"}]}""").RootElement;
        Check(QuickStartLauncher.Services.Update.UpdateService.Parse(preJson) is null, "prerelease ignored by updater");
        var forwardPaths = new AppPaths(Path.Combine(output, "forward-" + Guid.NewGuid().ToString("N")));
        QuickStartLauncher.App.ForwardArgs(forwardPaths, ["--profile", "Làm việc"]);
        Check(QuickStartLauncher.App.TakeForwardedArgs(forwardPaths).SequenceEqual(new[] { "--profile", "Làm việc" }) && QuickStartLauncher.App.TakeForwardedArgs(forwardPaths).Length == 0, "second instance args forwarded once");
        var realPaths = new AppPaths(Path.Combine(output, "shell-cache"));
        var realApps = new ShellAppsFolderProvider(realPaths, new IconCache(realPaths));
        watch.Restart(); var installed = await realApps.GetAppsAsync(true);
        var enumerateMs = watch.ElapsedMilliseconds;
        Check(installed.Count > 0, "real Windows AppsFolder enumeration");
        Check(installed.Any(a => a.IsPackaged), "real Store applications enumerated");
        var iconApp = installed.FirstOrDefault(a => a.Name.Contains("Chrome")) ?? installed.First();
        var icon = await realApps.GetIconAsync(iconApp, 64);
        Check(icon is not null && icon.IsFrozen, "real shell icon extracted/frozen");
        watch.Restart(); await realApps.GetAppsAsync(); var cachedMs = watch.ElapsedMilliseconds;
        Check(cachedMs <= 200, "cached app inventory <=200ms");
        File.WriteAllText(Path.Combine(output, "performance.txt"), $"Apps: {installed.Count}\nEnumeration: {enumerateMs}ms\nCached inventory: {cachedMs}ms\n");
        Console.WriteLine($"PERF {installed.Count} apps; enumerate={enumerateMs}ms; cached={cachedMs}ms");
        File.WriteAllText(Path.Combine(output, "apps.json"), JsonSerializer.Serialize(installed, ConfigJson.Options));
        var realBrowsers = new BrowserCatalog(realApps);
        var installedBrowsers = await realBrowsers.GetInstalledAsync();
        Check(installedBrowsers.Any(b => b.Id == "chrome"), "real installed Chrome discovered");
        File.WriteAllText(Path.Combine(output, "browsers.json"), JsonSerializer.Serialize(installedBrowsers, ConfigJson.Options));
        await UiChecksAsync(broken, launch, realApps, realBrowsers, paths);
        if (args.Contains("--launch-real"))
        {
            var actual = new LaunchService(realApps, realBrowsers, new ProcessLauncher());
            await foreach (var p in actual.RunAsync(migrated.Profiles[0], new(), CancellationToken.None))
                if (p.State is RunState.Done or RunState.Failed or RunState.Skipped) Check(p.State != RunState.Failed, "real v2 action " + p.ActionId + " " + p.Error);
        }
    }
    private static async Task UiChecksAsync(IConfigService config, ILaunchService launch, IInstalledAppsService apps, IBrowserService browsers, AppPaths paths)
    {
        var bindingErrors = new BindingTrace(); PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        var theme = new ThemeService(); theme.Apply(ThemeMode.Dark);
        var vm = new LauncherViewModel(config, launch, apps, browsers); vm.Reload();
        var launcher = new LauncherWindow(vm) { Topmost = false, ShowActivated = false }; theme.Attach(launcher);
        launcher.Show(); await Task.Delay(400); Render(launcher, "launcher-dark");
        Check(launcher.IsLoaded && vm.SelectedProfile?.Actions.Count == 4, "launcher loads migrated four actions");
        launcher.Close();
        var startup = new FakeStartup();
        var edit = new ConfigViewModel(config, launch, apps, browsers, startup, theme, paths);
        var window = new ConfigWindow(edit, apps) { ShowActivated = false }; theme.Attach(window); window.Show(); await Task.Delay(500);
        Render(window, "config-dark"); Check(((FrameworkElement)window.FindName("ActionsPage")).IsVisible && !edit.ShowSettings, "actions initially selected");
        var savedBrowserProfile = ((AppAction)edit.SelectedAction!.Model).Browser!.ProfileDir = "Profile 1"; await edit.SelectedAction.LoadDetailsAsync(apps, browsers); await Task.Delay(50); Check(edit.SelectedAction.ProfileDir == savedBrowserProfile, "profile dropdown refresh preserves selected directory");
        edit.AddDelayCommand.Execute(null); edit.SelectedAction!.Ms = 320;
        var delay = edit.SelectedAction;
        edit.DeleteActions([delay]); edit.UndoCommand.Execute(null);
        Check(edit.SelectedProfile!.Actions.Contains(delay), "delete undo restores action");
        edit.MoveUpCommand.Execute(null);
        Check(edit.SelectedProfile.Actions.IndexOf(delay) == 3, "keyboard reorder updates action collection");
        var original = edit.SelectedProfile; var id = original.Id;
        edit.DuplicateProfileCommand.Execute(null);
        Check(edit.Profiles.Count == 2 && edit.SelectedProfile!.Id != id && edit.SelectedProfile.Actions[0].Model.Id != original.Actions[0].Model.Id, "duplicate profile generates fresh IDs");
        edit.DeleteProfile(); edit.DeleteProfile(); Check(edit.Profiles.Count == 1, "last profile protected");
        edit.SelectedAction = edit.SelectedProfile!.Actions.OfType<ActionItemViewModel>().First();
        theme.Apply(ThemeMode.Light);
        Check(Contrast("#5142B5", "#F9FAFF") >= 4.5 && Contrast("#555B70", "#F9FAFF") >= 4.5 && Contrast("#121426", "#F9FAFF") >= 4.5, "light text palette contrast >=4.5");
        Check(Contrast("#A3AFFF", "#222943") >= 4.5 && Contrast("#B7BED7", "#222943") >= 4.5 && Contrast("#F5F7FF", "#222943") >= 4.5, "dark text palette contrast >=4.5");
        await Task.Delay(120); Render(window, "config-light");
        window.Width = 850; await Task.Delay(120); Render(window, "config-drawer");
        window.Width = 1120; edit.ShowSettings = true; await Task.Delay(120); Render(window, "settings-light");
        edit.Settings.Mode = StartupMode.RunProfiles;
        edit.Settings.StartupProfiles[0].Selected = true;
        await edit.FlushAsync();
        await Task.Delay(120); Render(window, "settings-autostart-light");
        Check(startup.Last?.Mode == StartupMode.RunProfiles && config.Current.Settings.Startup.ProfileIds.Count == 1, "settings persist selected startup profiles");
        startup.Throw = true; edit.MarkDirty();
        Check(await edit.FlushAsync() && edit.SaveStatus.StartsWith("Đã lưu"), "startup registry failure does not report config as unsaved");
        startup.Throw = false;
        theme.Apply(ThemeMode.Dark);
                edit.ShowSettings = false;
        edit.AddWebsiteCommand.Execute(null); edit.SelectedAction!.Url = "https://example.org"; await Task.Delay(100); Render(window, "editor-website");
        edit.AddCommandCommand.Execute(null); edit.SelectedAction!.Command = "start msteams:"; await Task.Delay(100); Render(window, "editor-command");
        edit.AddDelayCommand.Execute(null); await Task.Delay(100); Render(window, "editor-delay");
        var pickerVm = new AppPickerViewModel(apps, original.Model.Actions, false);
        var picker = new AppPickerDialog(pickerVm) { Owner = window, ShowActivated = false }; theme.Attach(picker);
        var pickerWatch = Stopwatch.StartNew(); picker.Show(); await Dispatcher.Yield(DispatcherPriority.Render); var pickerOpenMs = pickerWatch.ElapsedMilliseconds; File.AppendAllText(Path.Combine(output, "performance.txt"), $"Picker initial render: {pickerOpenMs}ms\n"); await Task.Delay(1000);
        for (var i = 0; i < 20 && pickerVm.IsRefreshing; i++) await Task.Delay(100);
        Render(picker, "picker-dark");
        pickerVm.Query = "chrmoe"; await Task.Delay(200);
        Check(pickerVm.Results.Take(3).Any(a => a.Name.Contains("Chrome", StringComparison.OrdinalIgnoreCase)), "real picker fuzzy Chrome");
        Render(picker, "picker-search");
        pickerVm.Query = ""; await Task.Delay(200);
        var selection = pickerVm.Results.Take(3).ToArray();
        foreach (var item in selection) item.Selected = true;
        Check(pickerVm.Selection.Select(a => a.Key).SequenceEqual(selection.Select(a => a.App.Key)), "picker preserves multi-select order");
        edit.AddInstalledApps(pickerVm.Selection, false);
        Check(edit.SelectedProfile!.Actions.TakeLast(3).Select(a => ((AppAction)a.Model).Target.Aumid).SequenceEqual(selection.Select(a => a.App.Aumid)), "picker adds three app actions in selection order");
                picker.Close();
        var secondPicker = new AppPickerDialog(new(apps, original.Model.Actions, false)) { Owner = window, ShowActivated = false };
        theme.Attach(secondPicker); pickerWatch.Restart(); secondPicker.Show(); await Dispatcher.Yield(DispatcherPriority.Render);
        File.AppendAllText(Path.Combine(output, "performance.txt"), $"Picker second render: {pickerWatch.ElapsedMilliseconds}ms\n");
        secondPicker.Close();
        #if DEBUG
        var demo = new DesignPreviewWindow { ShowActivated = false }; theme.Attach(demo); demo.Show(); await Task.Delay(100); Render(demo, "design-gallery"); demo.Close();
#endif
        await edit.FlushAsync(); window.Close(); await Task.Delay(100);
        Check(bindingErrors.Errors.Count == 0, "WPF bindings: " + string.Join(" | ", bindingErrors.Errors.Take(5)));
        File.WriteAllLines(Path.Combine(output, "binding-errors.txt"), bindingErrors.Errors);
        PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingErrors);
    }
    private static double Contrast(string foreground, string background)
    {
        static double Luminance(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            static double Channel(byte v) { var n = v / 255.0; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
            return .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        }
        var a = Luminance(foreground); var b = Luminance(background);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    private static void Render(Window window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name + ".png")); encoder.Save(stream);
    }
}
internal sealed class FakeApps : IInstalledAppsService
{
    public InstalledApp[] Items = [
        new("chrome", "Google Chrome", "Chrome", null, false, "chrome"), new("outlook", "Microsoft Outlook", null, null, false, null),
        new("zalo", "Zalo", null, null, false, null), new("teams", "Microsoft Teams", "MSTeams_test!App", null, true, null),
        new("coccoc", "Cốc Cốc", null, null, false, "coccoc"), new("code", "Visual Studio Code", null, null, false, null)];
    public Task<IReadOnlyList<InstalledApp>> GetAppsAsync(bool forceRefresh = false) => Task.FromResult<IReadOnlyList<InstalledApp>>(Items);
    public Task<ImageSource?> GetIconAsync(InstalledApp app, int size = 48) => Task.FromResult<ImageSource?>(null);
}
internal sealed class FakeBrowsers : IBrowserService
{
    public string? DefaultBrowserId => "chrome";
    public Task<IReadOnlyList<BrowserInfo>> GetInstalledAsync() => Task.FromResult<IReadOnlyList<BrowserInfo>>([]);
    public IReadOnlyList<BrowserProfile> GetProfiles(string id) => [];
}
internal sealed class FakeProcess : IProcessLauncher
{
    public readonly System.Collections.Concurrent.ConcurrentQueue<ProcessStartInfo> Started = new();
    public bool ThrowUac;
    public string? Activated;
    public void Start(ProcessStartInfo info) { if (ThrowUac) throw new Win32Exception(1223); Started.Enqueue(info); }
    public Task ActivateAsync(string aumid, string args) { Activated = aumid; return Task.CompletedTask; }
}
internal sealed class FakeStartup : IStartupService
{
    public StartupSettings? Last;
    public bool Throw;
    public bool IsBlockedByWindows => false;
    public void Apply(StartupSettings settings) { if (Throw) throw new UnauthorizedAccessException("registry denied"); Last = settings; }
}
internal sealed class BindingTrace : TraceListener
{
    public readonly List<string> Errors = [];
    public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message); }
    public override void WriteLine(string? message) => Write(message);
}
