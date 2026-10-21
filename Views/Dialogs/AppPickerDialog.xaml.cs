using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using QuickStartLauncher.Models;
using QuickStartLauncher.Services.Browsers;
using QuickStartLauncher.ViewModels;
using Wpf.Ui.Controls;
namespace QuickStartLauncher.Views.Dialogs;
public partial class AppPickerDialog : FluentWindow
{
    private readonly AppPickerViewModel vm;
    public IReadOnlyList<InstalledApp> SelectedApps { get; private set; } = [];
    public bool CustomCommandRequested { get; private set; }
    public AppPickerDialog(AppPickerViewModel vm)
    {
        InitializeComponent(); this.vm = vm; DataContext = vm;
        Loaded += async (_, _) => { SearchBox.Focus(); await vm.LoadAsync(); };
        Closed += (_, _) => vm.Stop();
    }
    private async void TileLoaded(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { DataContext: AppTileViewModel tile }) await vm.LoadIconAsync(tile); }
    private void DragTitle(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
    private void AddClick(object sender, RoutedEventArgs e)
    {
        if (vm.Selection.Count == 0 && AppList.SelectedItem is AppTileViewModel tile) tile.Selected = true;
        if (vm.Selection.Count == 0 && SearchBox.IsKeyboardFocusWithin && vm.Query.Length > 0 && vm.Results.Count > 0) vm.Results[0].Selected = true;
        if (vm.Selection.Count == 0) return;
        SelectedApps = vm.Selection.ToArray(); DialogResult = true;
    }
    private void ItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AppList.SelectedItem is not AppTileViewModel tile) return;
        SelectedApps = [tile.App]; DialogResult = true;
    }
    private void BrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Ứng dụng Windows|*.exe", Multiselect = !vm.SingleSelect, CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        var selected = dialog.FileNames.Select(path => new InstalledApp(path, Path.GetFileNameWithoutExtension(path), null, path, false, BrowserCatalog.Identify(path, null))).ToArray();
        SelectedApps = vm.SingleSelect ? selected : vm.Selection.Concat(selected).DistinctBy(a => a.Key).ToArray();
        DialogResult = true;
    }
    private void CommandClick(object sender, RoutedEventArgs e) { CustomCommandRequested = true; DialogResult = true; }
    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        if (!SearchBox.IsKeyboardFocusWithin && !string.IsNullOrEmpty(e.Text) && Keyboard.Modifiers == ModifierKeys.None)
        {
            SearchBox.Focus(); SearchBox.CaretIndex = SearchBox.Text.Length; SearchBox.SelectedText = e.Text; SearchBox.CaretIndex = SearchBox.Text.Length; e.Handled = true;
        }
        base.OnPreviewTextInput(e);
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        else if (e.Key == Key.Enter) { AddClick(sender, e); e.Handled = true; }
        else if (e.Key == Key.Space && AppList.IsKeyboardFocusWithin && Keyboard.FocusedElement is not System.Windows.Controls.CheckBox)
        { if (AppList.SelectedItem is AppTileViewModel tile) tile.Selected = !tile.Selected; e.Handled = true; }
        else if (e.Key == Key.Down && SearchBox.IsKeyboardFocusWithin && AppList.Items.Count > 0)
        { AppList.SelectedIndex = 0; AppList.Focus(); e.Handled = true; }
    }
}
