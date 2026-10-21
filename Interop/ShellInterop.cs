using System.Runtime.InteropServices;
namespace QuickStartLauncher.Interop;
internal static class ShellInterop
{
    internal static readonly Guid AppsFolder = new("1e87508d-89c2-42f0-8a7e-645a0f50ca58");
    internal static readonly Guid EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");
    internal static readonly PropertyKey AppId = new(new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    internal static readonly PropertyKey TargetPath = new(new("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25"), 2);
    [DllImport("shell32.dll", PreserveSig = false)]
    internal static extern void SHGetKnownFolderItem(in Guid folder, uint flags, nint token, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    internal static extern void SHCreateItemFromParsingName(string path, nint context, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SHGetKnownFolderPath(in Guid folder, uint flags, nint token, out nint path);
    internal static string? ResolvePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = Environment.ExpandEnvironmentVariables(path);
        if (path.StartsWith('{') && path.IndexOf('}') is var end && end > 0 && Guid.TryParse(path[..(end + 1)], out var folder))
        {
            try
            {
                SHGetKnownFolderPath(folder, 0, 0, out var pointer);
                try { path = Path.Combine(Marshal.PtrToStringUni(pointer)!, path[(end + 1)..].TrimStart('\\')); }
                finally { Marshal.FreeCoTaskMem(pointer); }
            }
            catch { return null; }
        }
        return Path.IsPathRooted(path) ? path : null;
    }
    internal static void Release(object? com) { if (com is not null && Marshal.IsComObject(com)) Marshal.FinalReleaseComObject(com); }
    internal static Task<T> OnSta<T>(Func<T> work)
    {
        var source = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { source.SetResult(work()); } catch (Exception e) { source.SetException(e); } }) { IsBackground = true, Name = "QuickStart Shell STA" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return source.Task;
    }
}
[StructLayout(LayoutKind.Sequential)]
internal readonly struct PropertyKey(Guid format, uint id)
{
    public readonly Guid Format = format;
    public readonly uint Id = id;
}
[StructLayout(LayoutKind.Sequential)]
internal struct NativeSize { public int Width; public int Height; }
[ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(nint context, in Guid handler, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IEnumShellItems result);
    void GetParent(out IShellItem parent);
    void GetDisplayName(uint type, out nint name);
    void GetAttributes(uint mask, out uint attributes);
    void Compare(IShellItem other, uint hint, out int order);
}
[ComImport, Guid("7E9FB0D3-919F-4307-AB2E-9B1860310C93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem2
{
    void BindToHandler(nint context, in Guid handler, in Guid iid, out nint result);
    void GetParent(out IShellItem parent);
    void GetDisplayName(uint type, out nint name);
    void GetAttributes(uint mask, out uint attributes);
    void Compare(IShellItem other, uint hint, out int order);
    void GetPropertyStore(uint flags, in Guid iid, out nint store);
    void GetPropertyStoreWithCreateObject(uint flags, nint create, in Guid iid, out nint store);
    void GetPropertyStoreForKeys(nint keys, uint count, uint flags, in Guid iid, out nint store);
    void GetPropertyDescriptionList(in PropertyKey key, in Guid iid, out nint list);
    void Update(nint context);
    void GetProperty(in PropertyKey key, nint value);
    void GetCLSID(in PropertyKey key, out Guid value);
    void GetFileTime(in PropertyKey key, out long value);
    void GetInt32(in PropertyKey key, out int value);
    void GetString(in PropertyKey key, [MarshalAs(UnmanagedType.LPWStr)] out string value);
}
[ComImport, Guid("70629033-E363-4A28-A567-0DB78006E6D7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumShellItems
{
    [PreserveSig] int Next(uint count, out IShellItem item, out uint fetched);
    void Skip(uint count); void Reset(); void Clone(out IEnumShellItems result);
}
[ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    void GetImage(NativeSize size, uint flags, out nint bitmap);
}
[ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IApplicationActivationManager
{
    void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appId, [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
    void ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appId, nint items, [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint processId);
    void ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string appId, nint items, out uint processId);
}
