using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
namespace QuickStartLauncher.Interop;
internal static class DwmInterop
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    internal static bool Apply(Window window, bool dark, int backdrop)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return false;
        var darkValue = dark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref darkValue, sizeof(int));
        var rounded = 2; DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
        return DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int)) >= 0;
    }
}
