using System.Text;
using QuickStartLauncher.Models;
namespace QuickStartLauncher.Services.Browsers;
public static class BrowserArguments
{
    // Windows CommandLineToArgvW escaping, including backslashes before closing quotes.
    public static string Quote(string value)
    {
        var b = new StringBuilder("\""); var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { b.Append('\\', slashes * 2 + 1); b.Append('"'); slashes = 0; continue; }
            b.Append('\\', slashes); slashes = 0; b.Append(c);
        }
        b.Append('\\', slashes * 2); return b.Append('"').ToString();
    }
    public static bool IsValidUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "https" or "http" or "file" && !value!.Any(c => c is '\r' or '\n' or '"');
    public static string Build(string browserId, BrowserOptions options)
    {
        if (options.Urls.Any(u => !IsValidUrl(u))) throw new ArgumentException("URL phải dùng http://, https:// hoặc file://.");
        var args = new List<string>();
        if (browserId == "firefox")
        {
            if (!string.IsNullOrWhiteSpace(options.ProfileDir)) { args.Add("-P"); args.Add(Quote(options.ProfileDir)); }
            for (var i = 0; i < options.Urls.Count; i++)
            {
                args.Add(options.NewWindow && i == 0 ? "-new-window" : "-new-tab"); args.Add(Quote(options.Urls[i]));
            }
            if (options.NewWindow && options.Urls.Count == 0) { args.Add("-new-window"); args.Add(Quote("about:blank")); }
        }
        else
        {
            if (browserId != "opera" && !string.IsNullOrWhiteSpace(options.ProfileDir)) args.Add("--profile-directory=" + Quote(options.ProfileDir));
            if (options.NewWindow) args.Add("--new-window");
            args.AddRange(options.Urls.Select(Quote));
        }
        return string.Join(" ", args);
    }
}
