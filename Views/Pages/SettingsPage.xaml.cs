using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using QuickStartLauncher.Services.Config;
using QuickStartLauncher.ViewModels;
namespace QuickStartLauncher.Views.Pages;
public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();
    private void MoveStartupUp(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveStartupDown(object sender, RoutedEventArgs e) => Move(1);
    private void Move(int offset)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var index = StartupList.SelectedIndex; var next = index + offset;
        if (index >= 0 && next >= 0 && next < vm.StartupProfiles.Count) vm.StartupProfiles.Move(index, next);
    }
    private async void ExportConfig(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not ConfigWindow window) return;
        await window.ViewModel.FlushAsync();
        var dialog = new SaveFileDialog { Filter = "Config JSON|*.json", FileName = "quickstart-config.json" };
        if (dialog.ShowDialog(window) != true) return;
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(window.ViewModel.Config.Current, ConfigJson.Options)); }
        catch (Exception ex) { MessageBox.Show(window, ex.Message, "Không xuất được config"); }
    }
    private async void ImportConfig(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not ConfigWindow window) return;
        var dialog = new OpenFileDialog { Filter = "Config JSON v3|*.json" };
        if (dialog.ShowDialog(window) != true) return;
        if (MessageBox.Show(window, "Thay toàn bộ cấu hình bằng file đã chọn? Config hiện tại sẽ được backup.", "Nhập config", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await window.ViewModel.FlushAsync();
            await window.ViewModel.Config.ImportAsync(File.ReadAllText(dialog.FileName));
            window.ViewModel.Reload();
            ((App)Application.Current).Themes.Apply(window.ViewModel.Config.Current.Settings.Theme);
            await window.ViewModel.FlushAsync();
        }
        catch (Exception ex) { MessageBox.Show(window, ex.Message, "Không nhập được config"); }
    }
}
