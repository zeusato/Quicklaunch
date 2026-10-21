using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using QuickStartLauncher.ViewModels;
namespace QuickStartLauncher.Views.Editors;
public partial class WebsiteActionEditor : UserControl
{
 public WebsiteActionEditor() => InitializeComponent();
 private void BrowseFolder(object sender, RoutedEventArgs e)
 {
  var dialog = new OpenFolderDialog();
  if (dialog.ShowDialog(Window.GetWindow(this)) == true && DataContext is ActionItemViewModel vm) vm.WorkingDir = dialog.FolderName;
 }
}
