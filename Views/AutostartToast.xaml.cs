using System.Windows;
using QuickStartLauncher.ViewModels;
using Wpf.Ui.Controls;
namespace QuickStartLauncher.Views;
public partial class AutostartToast : FluentWindow
{
    public AutostartToast(AutostartToastViewModel vm)
    {
        InitializeComponent(); DataContext = vm;
        Loaded += (_, _) => { var area = SystemParameters.WorkArea; Left = area.Right - Width - 20; Top = area.Bottom - Height - 20; };
        Closed += (_, _) => vm.Stop();
    }
}
