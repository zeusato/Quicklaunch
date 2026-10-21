using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using QuickStartLauncher.ViewModels;
namespace QuickStartLauncher.Views.Editors;
public partial class AppActionEditor : UserControl
{
 public AppActionEditor() => InitializeComponent();
 private void BrowseFolder(object sender, RoutedEventArgs e)
 {
  var dialog = new OpenFolderDialog();
  if (dialog.ShowDialog(Window.GetWindow(this)) == true && DataContext is ActionItemViewModel vm) vm.WorkingDir = dialog.FolderName;
 }
}
