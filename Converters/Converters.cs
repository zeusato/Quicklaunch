using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using QuickStartLauncher.Models;
namespace QuickStartLauncher.Converters;
public sealed class BoolVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type type, object parameter, CultureInfo culture) => (value is true) != (parameter?.ToString() == "Invert") ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
public sealed class StateBrushConverter : IValueConverter
{
    public object Convert(object value, Type type, object parameter, CultureInfo culture) => Application.Current.TryFindResource(value switch
    { RunState.Done => "State.Success", RunState.Failed => "State.Error", RunState.Running => "Accent", _ => "Text.Secondary" }) ?? Brushes.Gray;
    public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
