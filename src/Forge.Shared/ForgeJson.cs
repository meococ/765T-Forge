using System.Text.Json;
using System.Text.Json.Serialization;

namespace Forge.Shared;

public static class ForgeJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    static ForgeJson()
    {
        Options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public static JsonElement ToElement<T>(T value)
    {
        return JsonSerializer.SerializeToElement(value, Options);
    }

    public static T? FromElement<T>(JsonElement element)
    {
        return element.Deserialize<T>(Options);
    }
}
