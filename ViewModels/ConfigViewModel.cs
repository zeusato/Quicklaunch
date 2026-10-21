using QuickStartLauncher.Core;
using QuickStartLauncher.Models;
using QuickStartLauncher.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace QuickStartLauncher.ViewModels
{
    public class ConfigViewModel : ViewModelBase
    {
        private readonly IConfigService _configService;
        private readonly IAppIndexService _appIndexService;
        private readonly ILogService _logService;
        private AppConfig _appConfig;

        public ObservableCollection<Profile> Profiles { get; set; }
        public List<IndexedApp> IndexedApps { get; private set; }
        public ObservableCollection<string> BrowserProfiles { get; private set; }
        private ObservableCollection<IndexedApp> _filteredApps;
        public ObservableCollection<IndexedApp> FilteredApps
        {
            get => _filteredApps;
            set
            {
                _filteredApps = value;
                OnPropertyChanged();
            }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                FilterApps();
            }
        }

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

        private ActionBase? _selectedAction;
        public ActionBase? SelectedAction
        {
            get => _selectedAction;
            set
            {
                _selectedAction = value;
                OnPropertyChanged();
                ((RelayCommand)RemoveActionCommand).RaiseCanExecuteChanged();
            }
        }

        private Profile? _selectedProfileForEditing;
        public Profile? SelectedProfileForEditing
        {
            get => _selectedProfileForEditing;
            set
            {
                _selectedProfileForEditing = value;
                OnPropertyChanged();
                ((RelayCommand)RemoveProfileCommand).RaiseCanExecuteChanged();
            }
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand AddActionCommand { get; }
        public ICommand RemoveActionCommand { get; }
        public ICommand AddProfileCommand { get; }
        public ICommand RemoveProfileCommand { get; }
        public ICommand BrowseCommand { get; }

        public ConfigViewModel(IConfigService configService, IAppIndexService appIndexService, ILogService logService)
        {
            _configService = configService;
            _appIndexService = appIndexService;
            _logService = logService;

            _appConfig = _configService.LoadConfig();
            Profiles = new ObservableCollection<Profile>(_appConfig.Profiles);
            _activeProfile = Profiles.FirstOrDefault(p => p.Name == _appConfig.ActiveProfile) ?? Profiles.First();
            IndexedApps = _appIndexService.GetIndexedAppsAsync().Result;
            _filteredApps = new ObservableCollection<IndexedApp>(IndexedApps);
            BrowserProfiles = new ObservableCollection<string>();

            SaveCommand = new RelayCommand(SaveChanges);
            CancelCommand = new RelayCommand(p => { if (p is Window w) w.Close(); });
            AddActionCommand = new RelayCommand(AddAction);
            RemoveActionCommand = new RelayCommand(RemoveAction, CanRemoveAction);
            AddProfileCommand = new RelayCommand(AddProfile);
            RemoveProfileCommand = new RelayCommand(RemoveProfile, CanRemoveProfile);
            BrowseCommand = new RelayCommand(Browse);

            foreach (var action in ActiveProfile.Actions)
            {
                action.PropertyChanged += OnActionPropertyChanged;
            }
        }

        private void AddAction(object? parameter)
        {
            var newAction = new AppAction { Path = "New Action" };
            newAction.PropertyChanged += OnActionPropertyChanged;
            ActiveProfile.Actions.Add(newAction);
            SelectedAction = newAction;
        }

        private void RemoveAction(object? parameter)
        {
            if (SelectedAction != null)
            {
                SelectedAction.PropertyChanged -= OnActionPropertyChanged;
                ActiveProfile.Actions.Remove(SelectedAction);
            }
        }

        private bool CanRemoveAction(object? parameter) => SelectedAction != null;

        private void AddProfile(object? parameter)
        {
            var newProfile = new Profile { Name = "New Profile" };
            Profiles.Add(newProfile);
            _appConfig.Profiles.Add(newProfile);
            SelectedProfileForEditing = newProfile;
        }

        private void RemoveProfile(object? parameter)
        {
            if (SelectedProfileForEditing != null && Profiles.Count > 1)
            {
                if (ActiveProfile == SelectedProfileForEditing)
                {
                    var newActiveProfile = Profiles.FirstOrDefault(p => p != SelectedProfileForEditing);
                    if (newActiveProfile != null)
                    {
                        ActiveProfile = newActiveProfile;
                    }
                }
                var profileToRemove = _appConfig.Profiles.FirstOrDefault(p => p.Name == SelectedProfileForEditing.Name);
                if (profileToRemove != null)
                {
                    _appConfig.Profiles.Remove(profileToRemove);
                }
                Profiles.Remove(SelectedProfileForEditing);
                SelectedProfileForEditing = null;
            }
        }

        private bool CanRemoveProfile(object? parameter) => SelectedProfileForEditing != null && Profiles.Count > 1;

        private void FilterApps()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                FilteredApps = new ObservableCollection<IndexedApp>(IndexedApps);
            }
            else
            {
                FilteredApps = new ObservableCollection<IndexedApp>(
                    IndexedApps.Where(app => app.AppName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));
            }
        }

        private void SaveChanges(object? parameter)
        {
            // Remove empty actions before saving
            foreach (var profile in _appConfig.Profiles)
            {
                var actionsToRemove = profile.Actions.Where(a =>
                    (a is AppAction app && string.IsNullOrWhiteSpace(app.Path)) ||
                    (a is CommandAction cmd && string.IsNullOrWhiteSpace(cmd.Command))
                ).ToList();

                foreach (var action in actionsToRemove)
                {
                    profile.Actions.Remove(action);
                }
            }

            _logService.Info("Saving configuration changes.");
            _configService.SaveConfig(_appConfig);

            if (parameter is Window window)
            {
                window.Close();
            }
        }
        private void Browse(object? parameter)
        {
            if (parameter is AppAction appAction)
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*"
                };

                if (dialog.ShowDialog() == true)
                {
                    appAction.Path = dialog.FileName;
                }
            }
        }

        private void OnActionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is AppAction appAction && e.PropertyName == nameof(AppAction.Path))
            {
                UpdateActionType(appAction, appAction.Path);
            }
            else if (sender is CommandAction commandAction && e.PropertyName == nameof(CommandAction.Command))
            {
                UpdateActionType(commandAction, commandAction.Command);
            }
        }

        private void UpdateActionType(ActionBase oldAction, string input)
        {
            ActionBase newAction;
            if (input.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || input.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                if (oldAction is AppAction) return;
                newAction = new AppAction { Path = input };
            }
            else
            {
                if (oldAction is CommandAction) return;
                newAction = new CommandAction { Command = input };
            }

            var index = ActiveProfile.Actions.IndexOf(oldAction);
            if (index != -1)
            {
                oldAction.PropertyChanged -= OnActionPropertyChanged;
                newAction.PropertyChanged += OnActionPropertyChanged;
                ActiveProfile.Actions[index] = newAction;
                SelectedAction = newAction;
            }
        }
    }
}