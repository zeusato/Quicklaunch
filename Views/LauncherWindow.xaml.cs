using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using QuickStartLauncher.ViewModels;
using Wpf.Ui.Controls;
namespace QuickStartLauncher.Views;
public partial class LauncherWindow : FluentWindow
{
    public LauncherViewModel ViewModel { get; }
    public LauncherWindow(LauncherViewModel vm)
    {
        InitializeComponent(); ViewModel = vm; DataContext = vm;
        Loaded += (_, _) => ((Storyboard)FindResource("WindowEntrance")).Begin(this);
        Closed += (_, _) => vm.Cancel();
    }
    private void DragTitle(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Controls.Button) DragMove(); }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { if (ViewModel.IsRunning) ViewModel.Cancel(); else Close(); e.Handled = true; }
        else if (e.Key == Key.Enter) { ViewModel.BeginCommand.Execute(null); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.OemComma) { ViewModel.OpenConfigCommand.Execute(null); e.Handled = true; }
        else if (!ViewModel.IsRunning && e.Key >= Key.D1 && e.Key <= Key.D9)
        { var index = e.Key - Key.D1; if (index < ViewModel.Profiles.Count) ViewModel.SelectedProfile = ViewModel.Profiles[index]; e.Handled = true; }
    }
}
