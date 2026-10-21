using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using QuickStartLauncher.Models;
using QuickStartLauncher.Services.Apps;
using QuickStartLauncher.Services.Config;
using QuickStartLauncher.ViewModels;
using QuickStartLauncher.Views.Dialogs;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
namespace QuickStartLauncher.Views;
public partial class ConfigWindow : FluentWindow
{
    public ConfigViewModel ViewModel { get; }
    private readonly IInstalledAppsService apps;
    private bool closingReady;
    public ConfigWindow(ConfigViewModel vm, IInstalledAppsService apps)
    {
        InitializeComponent(); ViewModel = vm; this.apps = apps; DataContext = vm;
        vm.PickerRequested += ShowPicker;
        Closing += OnClosing;
        Closed += (_, _) => { vm.PickerRequested -= ShowPicker; vm.Stop(); };
    }
    public void ShowPicker(bool replace = false)
    {
        var picker = new AppPickerDialog(new(apps, ViewModel.SelectedProfile?.Model.Actions ?? [], replace)) { Owner = this };
        ((App)Application.Current).Themes.Attach(picker);
        if (picker.ShowDialog() == true)
        {
            if (picker.CustomCommandRequested) ViewModel.AddCommandCommand.Execute(null);
            else ViewModel.AddInstalledApps(picker.SelectedApps, replace);
        }
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closingReady) return;
        e.Cancel = true;
        if (await ViewModel.FlushAsync() || MessageBox.Show(this, "Chưa lưu được cấu hình. Đóng và bỏ các thay đổi chưa lưu?", "QuickStart", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        { closingReady = true; Close(); }
    }
    private void DragTitle(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void SettingsClick(object sender, RoutedEventArgs e) => ViewModel.ShowSettings = !ViewModel.ShowSettings;
    private void ProfileClick(object sender, MouseButtonEventArgs e) => ViewModel.ShowSettings = false;
    private void SelectProfileContext(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox list && System.Windows.Controls.ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is System.Windows.Controls.ListBoxItem item) item.IsSelected = true;
    }
    private void ProfileContextOpening(object sender, System.Windows.Controls.ContextMenuEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox list) FillProfileMenu(list.ContextMenu);
    }
    private void ProfileMenuClick(object sender, RoutedEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        FillProfileMenu(menu); menu.IsOpen = true;
    }
    private void FillProfileMenu(System.Windows.Controls.ContextMenu menu)
    {
        menu.Items.Clear();
        void Add(string title, Action click) { var item = new System.Windows.Controls.MenuItem { Header = title }; item.Click += (_, _) => click(); menu.Items.Add(item); }
        Add("Đổi tên", () => { ViewModel.ShowSettings = false; ProfileName.Focus(); ProfileName.SelectAll(); });
        Add("Nhân bản", () => ViewModel.DuplicateProfileCommand.Execute(null));
        Add("Đặt làm mặc định", () => ViewModel.SetDefaultCommand.Execute(null));
        menu.Items.Add(new System.Windows.Controls.Separator());
        Add("Nhập profile…", () => ImportProfileClick(this, new()));
        Add("Xuất JSON…", () => ExportProfileClick(this, new()));
        menu.Items.Add(new System.Windows.Controls.Separator());
        Add("Xóa profile", () => DeleteProfileClick(this, new()));
    }
    private void DeleteProfileClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Profiles.Count <= 1) { MessageBox.Show(this, "Cần giữ ít nhất một profile.", "QuickStart"); return; }
        if (MessageBox.Show(this, $"Xóa profile “{ViewModel.SelectedProfile?.Name}”?", "QuickStart", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) ViewModel.DeleteProfile();
    }
    private void ImportProfileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Profile JSON|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { ViewModel.ImportProfile(ConfigJson.Read<Profile>(File.ReadAllText(dialog.FileName)) ?? throw new InvalidDataException("Profile rỗng.")); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Không nhập được profile"); }
    }
    private void ExportProfileClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedProfile is null) return;
        var dialog = new SaveFileDialog { Filter = "Profile JSON|*.json", FileName = "profile.json" };
        if (dialog.ShowDialog(this) == true)
            try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(ViewModel.SelectedProfile.Model, ConfigJson.Options)); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Không xuất được profile"); }
    }
    private async void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) { await ViewModel.FlushAsync(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z && Keyboard.FocusedElement is not System.Windows.Controls.TextBox) { ViewModel.UndoCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Escape) Close();
    }
}
