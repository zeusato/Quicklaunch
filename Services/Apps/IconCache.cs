using System.Collections.Concurrent;
using System.Runtime.InteropServices;
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
            // "v3" invalidates PNGs written before alpha was preserved (they have black backgrounds).
            var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"v3:{app.Key}:{size}")));
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
                    var source = FromShellBitmap(handle);
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
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [DllImport("gdi32.dll")] private static extern int GetObject(nint handle, int size, out BITMAP bitmap);
    // Imaging.CreateBitmapSourceFromHBitmap ignores the alpha channel of the shell's 32bpp DIB section,
    // which turns transparent icon backgrounds black. Copy the premultiplied BGRA bits ourselves instead.
    private static BitmapSource FromShellBitmap(nint handle)
    {
        if (GetObject(handle, Marshal.SizeOf<BITMAP>(), out var bm) == 0 || bm.BitsPixel != 32 || bm.Bits == 0)
            return Imaging.CreateBitmapSourceFromHBitmap(handle, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        var height = Math.Abs(bm.Height); var stride = bm.WidthBytes;
        var pixels = new byte[stride * height];
        Marshal.Copy(bm.Bits, pixels, 0, pixels.Length);
        // Most shell bitmaps are premultiplied, but some legacy icons come back with straight alpha
        // (colour channels > alpha). Treating those as premultiplied paints their transparent area white.
        bool hasAlpha = false, premultiplied = true;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            var a = pixels[i]; if (a != 0) hasAlpha = true;
            if (pixels[i - 1] > a || pixels[i - 2] > a || pixels[i - 3] > a) premultiplied = false;
        }
        if (bm.Height > 0)
        {
            // Bottom-up DIB: flip rows so the icon isn't upside down.
            var flipped = new byte[pixels.Length];
            for (var row = 0; row < height; row++) Buffer.BlockCopy(pixels, row * stride, flipped, (height - 1 - row) * stride, stride);
            pixels = flipped;
        }
        return BitmapSource.Create(bm.Width, height, 96, 96, !hasAlpha ? PixelFormats.Bgr32 : premultiplied ? PixelFormats.Pbgra32 : PixelFormats.Bgra32, null, pixels, stride);
    }
}
