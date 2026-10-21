using QuickStartLauncher.Core;
using QuickStartLauncher.Services;
using QuickStartLauncher.Views;
using System.Windows.Input;
using System.Collections.ObjectModel;

using QuickStartLauncher.Models;
using System.Linq;
using System.Windows;

namespace QuickStartLauncher.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IConfigService _configService;
        private readonly IActionExecutor _actionExecutor;
        private readonly IAppIndexService _appIndexService;
        private readonly ILogService _logService;

        private AppConfig _appConfig;
        private bool _isExecuting;

        public ObservableCollection<Profile> Profiles { get; set; }
        private Profile _activeProfile;
        public Profile ActiveProfile
        {
            get => _activeProfile;
            set
            {
                if (_activeProfile != value)
                {
                    _activeProfile = value;
                    OnPropertyChanged();
                    if (_appConfig != null) _appConfig.ActiveProfile = value.Name;
                }
            }
        }

        private string _statusText = "Ready";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public ICommand BeginExecutionCommand { get; }
        public ICommand ShowConfigCommand { get; }

        public MainViewModel(IConfigService configService, IActionExecutor actionExecutor, IAppIndexService appIndexService, ILogService logService)
        {
            _configService = configService;
            _actionExecutor = actionExecutor;
            _appIndexService = appIndexService;
            _logService = logService;

            _appConfig = _configService.LoadConfig();
            Profiles = new ObservableCollection<Profile>(_appConfig.Profiles);
            _activeProfile = Profiles.FirstOrDefault(p => p.Name == _appConfig.ActiveProfile) ?? Profiles.First();

            BeginExecutionCommand = new RelayCommand(ExecuteActions, CanExecuteActions);
            ShowConfigCommand = new RelayCommand(ShowConfig);
        }

        private bool CanExecuteActions(object? parameter) => !_isExecuting;

        private async void ExecuteActions(object? parameter)
        {
            _isExecuting = true;
            _logService.Info("Execution started.");
            var activeProfile = ActiveProfile;

            if (activeProfile == null || activeProfile.Actions.Count == 0)
            {
                StatusText = "No actions to run.";
                _isExecuting = false;
                return;
            }

            int errorCount = 0;
            int completedCount = 0;
            int totalActions = activeProfile.Actions.Count;

            await _actionExecutor.ExecuteActions(activeProfile.Actions,
                onActionStart: (displayName) =>
                {
                    StatusText = $"({completedCount}/{totalActions}) Running: {displayName}...";
                },
                onActionComplete: (displayName, success) =>
                {
                    completedCount++;
                    if (!success) errorCount++;
                });

            StatusText = $"Execution finished. {errorCount} error(s).";
            _logService.Info($"Execution finished. Completed: {completedCount}, Errors: {errorCount}");

            // Exit the application
            Application.Current.Shutdown();
        }

        private void ShowConfig(object? parameter)
        {
            var configViewModel = new ConfigViewModel(_configService, _appIndexService, _logService);
            var configWindow = new ConfigWindow
            {
                DataContext = configViewModel,
                Owner = Application.Current.MainWindow
            };
            configWindow.ShowDialog();

            // Reload config after the window is closed
            _appConfig = _configService.LoadConfig();
            Profiles.Clear();
            foreach (var p in _appConfig.Profiles)
            {
                Profiles.Add(p);
            }
            ActiveProfile = Profiles.FirstOrDefault(p => p.Name == _appConfig.ActiveProfile) ?? Profiles.First();
            StatusText = "Ready";
        }
    }
}