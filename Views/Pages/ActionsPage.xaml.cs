using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickStartLauncher.ViewModels;
namespace QuickStartLauncher.Views.Pages;
public partial class ActionsPage : UserControl
{
    private ConfigViewModel? vm;
    public ActionsPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (vm is not null) vm.PropertyChanged -= VmChanged;
            vm = DataContext as ConfigViewModel;
            if (vm is not null) vm.PropertyChanged += VmChanged;
        };
    }
    private void VmChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(ConfigViewModel.SelectedAction)) Adapt(); }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Adapt();
    private void Adapt()
    {
        if (EditorPanel is null) return;
        var drawer = ActualWidth < 750;
        EditorColumn.Width = drawer ? new GridLength(0) : new GridLength(330);
        Grid.SetColumn(EditorPanel, drawer ? 0 : 1);
        EditorPanel.Width = drawer ? Math.Min(340, ActualWidth) : double.NaN;
        EditorPanel.HorizontalAlignment = drawer ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        EditorPanel.Visibility = vm?.SelectedAction is null ? Visibility.Collapsed : Visibility.Visible;
        CloseDrawer.Visibility = drawer ? Visibility.Visible : Visibility.Collapsed;
    }
    private void CloseDrawerClick(object sender, RoutedEventArgs e) => EditorPanel.Visibility = Visibility.Collapsed;
    private void ActionMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ActionItemViewModel action } button || vm is null) return;
        vm.SelectedAction = action;
        var menu = new ContextMenu { PlacementTarget = button };
        void Add(string name, Action click) { var item = new MenuItem { Header = name }; item.Click += (_, _) => click(); menu.Items.Add(item); }
        Add("Sửa", () => { EditorPanel.Visibility = Visibility.Visible; EditorPanel.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); });
        Add("Nhân bản", () => vm.DuplicateActionCommand.Execute(null));
        Add("Chạy thử / Hủy", () => vm.TestActionCommand.Execute(null));
        Add("Xóa", () => vm.DeleteActions([action]));
        menu.IsOpen = true;
    }
    private void DeleteClick(object sender, RoutedEventArgs e) => vm?.DeleteActions(ActionList.SelectedItems.Cast<ActionItemViewModel>().ToArray());
    private void ActionSelectionChanged(object sender, SelectionChangedEventArgs e) { if (ActionList.SelectedItem is not null) ActionList.ScrollIntoView(ActionList.SelectedItem); }
    private void ListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { DeleteClick(sender, e); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.Up) { vm?.MoveUpCommand.Execute(null); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.Down) { vm?.MoveDownCommand.Execute(null); e.Handled = true; }
    }
}
