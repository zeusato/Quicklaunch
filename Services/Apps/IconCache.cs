using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuickStartLauncher.Interop;
using QuickStartLauncher.Models;
using QuickStartLauncher.Services.Config;
using Serilog;
namespace QuickStartLauncher.Services.Apps;
public sealed class IconCache(AppPaths paths)
{
    private readonly ConcurrentDictionary<string, Task<ImageSource?>> memory = new();
    private readonly SemaphoreSlim limit = new(3);
    public Task<ImageSource?> GetAsync(InstalledApp app, int size) => memory.GetOrAdd($"{app.Key}:{size}", _ => LoadAsync(app, size));
    private async Task<ImageSource?> LoadAsync(InstalledApp app, int size)
    {
        await limit.WaitAsync();
        try
        {
            var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{app.Key}:{size}")));
            var file = Path.Combine(paths.IconDirectory, key + ".png");
            return await ShellInterop.OnSta<ImageSource?>(() =>
            {
                if (File.Exists(file))
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(file);
                        bitmap.EndInit(); bitmap.Freeze(); return bitmap;
                    }
                    catch { /* Regenerate a damaged cache entry. */ }
                }
                IShellItem? item = null; nint handle = 0;
                try
                {
                    var target = !string.IsNullOrWhiteSpace(app.Aumid) ? @"shell:AppsFolder\" + app.Aumid : app.ExePath!;
                    ShellInterop.SHCreateItemFromParsingName(target, 0, typeof(IShellItem).GUID, out item);
                    ((IShellItemImageFactory)item).GetImage(new() { Width = size, Height = size }, 0x5, out handle);
                    var source = Imaging.CreateBitmapSourceFromHBitmap(handle, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    try
                    {
                        Directory.CreateDirectory(paths.IconDirectory);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
                        using var stream = File.Create(file); encoder.Save(stream);
                    }
                    catch (Exception e) { Log.Debug(e, "Icon disk cache unavailable"); }
                    return source;
                }
                catch (Exception e) { Log.Debug(e, "Icon unavailable for {App}", app.Name); return null; }
                finally
                {
                    if (handle != 0) Windows.Win32.PInvoke.DeleteObject(new Windows.Win32.Graphics.Gdi.HGDIOBJ(handle));
                    ShellInterop.Release(item);
                }
            });
        }
        finally { limit.Release(); }
    }
}
