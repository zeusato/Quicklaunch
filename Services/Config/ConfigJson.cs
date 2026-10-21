using System.Text.Json;
using System.Text.Json.Serialization;
using QuickStartLauncher.Models;
namespace QuickStartLauncher.Services.Config;
public static class ConfigJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
    public static T Read<T>(string json)
    {
        // .NET 8 requires metadata first, but JSON object property order is not semantic.
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteOrdered(document.RootElement, writer);
        return JsonSerializer.Deserialize<T>(stream.ToArray(), Options) ?? throw new InvalidDataException("JSON rỗng.");
    }
    private static void WriteOrdered(JsonElement value, Utf8JsonWriter writer)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            if (value.TryGetProperty("kind", out var kind)) { writer.WritePropertyName("kind"); kind.WriteTo(writer); }
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name == "kind") continue;
                writer.WritePropertyName(property.Name); WriteOrdered(property.Value, writer);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) WriteOrdered(item, writer); writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }

    public static void Validate(AppConfig config)
    {
        if (config.Version != 3) throw new InvalidDataException("Phiên bản config không được hỗ trợ.");
        if (config.Settings?.Startup is null || config.Profiles is null || config.Profiles.Count == 0)
            throw new InvalidDataException("Config phải có settings và ít nhất một profile.");
        var ids = new HashSet<Guid>();
        foreach (var p in config.Profiles)
        {
            if (p is null || p.Id == Guid.Empty || !ids.Add(p.Id) || string.IsNullOrWhiteSpace(p.Name) || p.Actions is null)
                throw new InvalidDataException("Profile không hợp lệ hoặc trùng ID.");
            var actionIds = new HashSet<Guid>();
            foreach (var a in p.Actions)
            {
                if (a is null || a.Id == Guid.Empty || !actionIds.Add(a.Id) || a.DelayAfterMs is < 0 or > 60000)
                    throw new InvalidDataException("Action không hợp lệ hoặc trùng ID.");
                if (a is AppAction { Target: null } || a is DelayAction { Ms: < 0 or > 60000 })
                    throw new InvalidDataException("Target hoặc thời gian chờ không hợp lệ.");
                if (a is AppAction { Browser: { Urls: null } } || a is CommandAction c && !Enum.IsDefined(c.Shell))
                    throw new InvalidDataException("Tùy chọn action không hợp lệ.");
            }
        }
        if (!ids.Contains(config.ActiveProfileId)) config.ActiveProfileId = config.Profiles[0].Id;
        var s = config.Settings.Startup;
        if (s.ProfileIds is null || s.DelaySeconds is < 0 or > 120 || s.CountdownSeconds is < 0 or > 15 ||
            config.Settings.CloseDelayMs is < 0 or > 60000 || !Enum.IsDefined(s.Mode) || !Enum.IsDefined(config.Settings.Theme))
            throw new InvalidDataException("Cài đặt không hợp lệ.");
        s.ProfileIds = s.ProfileIds.Where(ids.Contains).Distinct().ToList();
    }
}
