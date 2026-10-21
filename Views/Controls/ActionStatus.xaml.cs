using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using QuickStartLauncher.Models;
using QuickStartLauncher.ViewModels;
namespace QuickStartLauncher.Views.Controls;
public partial class ActionStatus : UserControl
{
    private ActionItemViewModel? vm;
    public ActionStatus()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => { if (vm is not null) vm.PropertyChanged -= Changed; };
    }
    private void Attach()
    {
        if (vm is not null) vm.PropertyChanged -= Changed;
        vm = DataContext as ActionItemViewModel;
        if (vm is not null) vm.PropertyChanged += Changed;
        Update();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(ActionItemViewModel.State)) Update(); }
    private void Update()
    {
        var done = vm?.State == RunState.Done;
        Check.Visibility = done ? Visibility.Visible : Visibility.Collapsed;
        Glyph.Visibility = done ? Visibility.Collapsed : Visibility.Visible;
        var rotate = (RotateTransform)Glyph.RenderTransform;
        rotate.BeginAnimation(RotateTransform.AngleProperty, vm?.State == RunState.Running
            ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever } : null);
        if (done) Check.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(200)));
    }
}
