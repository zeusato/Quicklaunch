using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using QuickStartLauncher.Models;
namespace QuickStartLauncher.ViewModels;
public sealed partial class BrowserChoiceViewModel(BrowserInfo browser) : ObservableObject
{
    public string Id => browser.Id;
    public string Name => browser.Name;
    [ObservableProperty] private ImageSource? icon;
}
