using System.Globalization;
using System.Text;
using QuickStartLauncher.Models;
namespace QuickStartLauncher.Services.Search;
public static class TextNormalizer
{
    public static string Normalize(string? value)
    {
        var b = new StringBuilder();
        foreach (var c in (value ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) b.Append(c == 'đ' ? 'd' : c);
            else if (b.Length > 0 && b[^1] != ' ') b.Append(' ');
        }
        return b.ToString().Trim();
    }
}
public sealed class SearchEntry(InstalledApp app)
{
    public InstalledApp App { get; } = app;
    public string Name { get; } = TextNormalizer.Normalize(app.Name);
    public string Exe { get; } = TextNormalizer.Normalize(Path.GetFileNameWithoutExtension(app.ExePath));
    public string Acronym { get; } = string.Concat(TextNormalizer.Normalize(app.Name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t[0]));
}
public static class FuzzySearch
{
    public static IReadOnlyList<InstalledApp> Find(IEnumerable<SearchEntry> entries, string query)
    {
        var q = TextNormalizer.Normalize(query);
        return entries.Select(e => (e.App, Score: Score(e, q))).Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.App.Name, StringComparer.Create(CultureInfo.GetCultureInfo("vi-VN"), true))
            .Select(x => x.App).ToList();
    }
    public static double Score(SearchEntry entry, string query)
    {
        if (query.Length == 0) return 1;
        var values = new[] { entry.Name, entry.Exe, entry.Name.Replace(" ", "") };
        if (values.Contains(query)) return 100;
        if (values.Any(v => v.StartsWith(query, StringComparison.Ordinal))) return 90;
        var words = values.SelectMany(v => v.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Distinct().ToArray();
        if (words.Any(v => v.StartsWith(query, StringComparison.Ordinal))) return 80;
        if (entry.Acronym == query) return 75;
        if (values.Any(v => v.Contains(query, StringComparison.Ordinal))) return 60;
        double score = 0;
        foreach (var word in words)
        {
            var distance = DamerauLevenshtein(word, query);
            var length = Math.Max(query.Length, word.Length);
            if (distance <= Math.Max(1, query.Length / 3))
                score = Math.Max(score, 50.0 * (1 - (double)distance / length));
        }
        return score;
    }
    public static int DamerauLevenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }
}
