using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using QuickStartLauncher.Models;
namespace QuickStartLauncher.ViewModels;
public sealed class ProfileItemViewModel : ObservableObject
{
    public Profile Model { get; }
    public ObservableCollection<ActionItemViewModel> Actions { get; } = [];
    public event Action? Changed;
    public Guid Id => Model.Id;
    public string Name { get => Model.Name; set { if (value == Model.Name || string.IsNullOrWhiteSpace(value)) return; Model.Name = value; OnPropertyChanged(); OnPropertyChanged(nameof(Label)); Changed?.Invoke(); } }
    public string Icon { get => Model.Icon; set { if (value == Model.Icon) return; Model.Icon = value; OnPropertyChanged(); OnPropertyChanged(nameof(Label)); Changed?.Invoke(); } }
    public string Label => $"{Icon}  {Name}";
    public ProfileItemViewModel(Profile model)
    {
        Model = model;
        foreach (var a in model.Actions) { var vm = new ActionItemViewModel(a); vm.Changed += OnChanged; Actions.Add(vm); }
        Actions.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null) foreach (ActionItemViewModel a in e.NewItems) a.Changed += OnChanged;
            if (e.OldItems is not null) foreach (ActionItemViewModel a in e.OldItems) a.Changed -= OnChanged;
            Model.Actions = Actions.Select(a => a.Model).ToList(); OnChanged();
        };
    }
    private void OnChanged() => Changed?.Invoke();
}
