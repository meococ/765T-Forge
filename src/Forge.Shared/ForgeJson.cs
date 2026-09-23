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

    /// <summary>
    /// Deserializes tool arguments for a tool whose parameters are all optional.
    /// Exactly two shapes yield a default instance: the property is absent
    /// (<see cref="JsonValueKind.Undefined"/>) or explicitly JSON <c>null</c>. Every other
    /// shape is deserialized strictly and a malformed payload throws
    /// <see cref="JsonException"/> instead of being silently replaced by a default instance.
    /// Use this instead of <c>FromElement&lt;T&gt;(...) ?? new T()</c>: that idiom throws
    /// <see cref="InvalidOperationException"/> on an absent property and hides a
    /// deserialization failure, so the same input produced two different outcomes.
    /// </summary>
    public static T ArgsOrDefault<T>(JsonElement element) where T : new()
    {
        if (element.ValueKind == JsonValueKind.Undefined || element.ValueKind == JsonValueKind.Null)
        {
            return new T();
        }

        return element.Deserialize<T>(Options)
            ?? throw new JsonException(
                $"Arguments deserialized to null for {typeof(T).Name}; expected a JSON object.");
    }
}
