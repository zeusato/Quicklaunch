using QuickStartLauncher.Services;
using QuickStartLauncher.ViewModels;
using QuickStartLauncher.Views;
using System.Windows;

namespace QuickStartLauncher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // --- Dependency Injection Setup (Poor Man's DI) ---
            ILogService logService = new LogService();
            IConfigService configService = new ConfigService(logService);
            IActionExecutor actionExecutor = new ActionExecutorService(logService);
            IAppIndexService appIndexService = new AppIndexService(logService);

            // --- Create Main Window and ViewModel ---
            var mainViewModel = new MainViewModel(configService, actionExecutor, appIndexService, logService);
            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            mainWindow.Show();

            base.OnStartup(e);
        }
    }
}