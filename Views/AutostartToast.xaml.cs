using System.Windows;
using QuickStartLauncher.ViewModels;
using Wpf.Ui.Controls;
namespace QuickStartLauncher.Views;
public partial class AutostartToast : FluentWindow
{
    private bool allowClose;
    public AutostartToast(AutostartToastViewModel vm)
    {
        InitializeComponent(); DataContext = vm;
        Loaded += (_, _) => { var area = SystemParameters.WorkArea; Left = area.Right - Width - 20; Top = area.Bottom - Height - 20; };
        Closing += (_, e) =>
        {
            if (allowClose) return;
            e.Cancel = true; Hide();
            Dispatcher.BeginInvoke(() => vm.DismissCommand.Execute(null));
        };
    }
    public void CloseAfterMonitoring() { allowClose = true; Close(); }
}
