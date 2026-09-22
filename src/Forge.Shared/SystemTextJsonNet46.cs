using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using NewtonsoftJson = Newtonsoft.Json;
using NewtonsoftConverters = Newtonsoft.Json.Converters;

namespace System.Text.Json.Serialization
{

public enum JsonIgnoreCondition
{
    Never = 0,
    WhenWritingNull = 1
}

public abstract class JsonConverter
{
}

public sealed class JsonStringEnumConverter : JsonConverter
{
    public JsonStringEnumConverter(Json.JsonNamingPolicy? namingPolicy)
    {
        NamingPolicy = namingPolicy;
    }

    public Json.JsonNamingPolicy? NamingPolicy { get; }
}
}

namespace System.Text.Json
{

/// <summary>
/// JSON surface used by Forge on <c>net46</c> only. AutoCAD 2017 and 2018 document
/// .NET Framework 4.6. System.Text.Json 8.0.5 cannot restore for that TFM (NU1202),
/// so this build delegates to Newtonsoft.Json. net47, net48, and net8 keep the real
/// System.Text.Json types; this file is not compiled for them.
/// </summary>
public enum JsonValueKind
{
    Undefined = 0,
    Object = 1,
    Array = 2,
    String = 3,
    Number = 4,
    True = 5,
    False = 6,
    Null = 7
}

public class JsonNamingPolicy
{
    public static JsonNamingPolicy CamelCase { get; } = new();

    public virtual string ConvertName(string name)
    {
        if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
        {
            return name;
        }

        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}

public sealed class JsonSerializerOptions
{
    public JsonNamingPolicy? PropertyNamingPolicy { get; set; }

    public bool PropertyNameCaseInsensitive { get; set; } = true;

    public Serialization.JsonIgnoreCondition DefaultIgnoreCondition { get; set; }

    public bool WriteIndented { get; set; }

    public IList<Serialization.JsonConverter> Converters { get; } = new List<Serialization.JsonConverter>();

    internal NewtonsoftJson.JsonSerializerSettings ToSettings()
    {
        var settings = new NewtonsoftJson.JsonSerializerSettings
        {
            Formatting = WriteIndented ? NewtonsoftJson.Formatting.Indented : NewtonsoftJson.Formatting.None,
            NullValueHandling = DefaultIgnoreCondition == Serialization.JsonIgnoreCondition.WhenWritingNull
                ? NewtonsoftJson.NullValueHandling.Ignore
                : NewtonsoftJson.NullValueHandling.Include,
            MissingMemberHandling = NewtonsoftJson.MissingMemberHandling.Ignore,
            DateParseHandling = NewtonsoftJson.DateParseHandling.None,
            DateTimeZoneHandling = NewtonsoftJson.DateTimeZoneHandling.RoundtripKind,
            ContractResolver = new ForgeNet46ContractResolver(),
            Converters =
            {
                new JsonElementNewtonsoftConverter(),
                new JsonNodeNewtonsoftConverter()
            }
        };

        foreach (var converter in Converters)
        {
            if (converter is Serialization.JsonStringEnumConverter)
            {
                settings.Converters.Add(new NewtonsoftConverters.StringEnumConverter(new CamelCaseNamingStrategy()));
            }
        }

        return settings;
    }
}

public static class JsonSerializer
{
    public static string Serialize<T>(T value, JsonSerializerOptions options)
    {
        return NewtonsoftJson.JsonConvert.SerializeObject(value, options.ToSettings());
    }

    public static T? Deserialize<T>(string json, JsonSerializerOptions options)
    {
        return NewtonsoftJson.JsonConvert.DeserializeObject<T>(json, options.ToSettings());
    }

    public static T? Deserialize<T>(this JsonElement element, JsonSerializerOptions options)
    {
        return Deserialize<T>(element.GetRawText(), options);
    }

    public static JsonElement SerializeToElement<T>(T value, JsonSerializerOptions options)
    {
        return JsonElement.Parse(Serialize(value, options));
    }

    public static Nodes.JsonNode? SerializeToNode<T>(T? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            return null;
        }

        var serializer = NewtonsoftJson.JsonSerializer.Create(options.ToSettings());
        var token = JToken.FromObject(value, serializer);
        return Nodes.JsonNode.FromToken(token);
    }
}

public readonly struct JsonProperty
{
    public JsonProperty(string name, JsonElement value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public JsonElement Value { get; }
}

public readonly struct JsonElement
{
    private readonly JToken? _token;

    internal JsonElement(JToken? token)
    {
        _token = token;
    }

    public static JsonElement Parse(string json)
    {
        return new JsonElement(JToken.Parse(json));
    }

    public JsonValueKind ValueKind => Map(_token);

    public string GetRawText()
    {
        return _token is null ? "" : _token.ToString(NewtonsoftJson.Formatting.None);
    }

    public string? GetString()
    {
        if (_token is null || _token.Type == JTokenType.Null)
        {
            return null;
        }

        if (_token.Type != JTokenType.String)
        {
            throw new InvalidOperationException("The JSON value is not a string.");
        }

        return _token.Value<string>();
    }

    public JsonElement GetProperty(string propertyName)
    {
        if (!TryGetProperty(propertyName, out var value))
        {
            throw new KeyNotFoundException($"Property '{propertyName}' was not found.");
        }

        return value;
    }

    public bool TryGetProperty(string propertyName, out JsonElement value)
    {
        value = default;
        if (_token is not JObject obj)
        {
            return false;
        }

        var property = obj.Property(propertyName);
        if (property is null)
        {
            return false;
        }

        value = new JsonElement(property.Value);
        return true;
    }

    public IReadOnlyList<JsonElement> EnumerateArray()
    {
        if (_token is not JArray array)
        {
            throw new InvalidOperationException("The JSON value is not an array.");
        }

        var items = new List<JsonElement>(array.Count);
        foreach (var item in array)
        {
            items.Add(new JsonElement(item));
        }

        return items;
    }

    public IReadOnlyList<JsonProperty> EnumerateObject()
    {
        if (_token is not JObject obj)
        {
            throw new InvalidOperationException("The JSON value is not an object.");
        }

        var items = new List<JsonProperty>();
        foreach (var property in obj.Properties())
        {
            items.Add(new JsonProperty(property.Name, new JsonElement(property.Value)));
        }

        return items;
    }

    internal void WriteTo(NewtonsoftJson.JsonWriter writer)
    {
        if (_token is null)
        {
            writer.WriteNull();
            return;
        }

        _token.WriteTo(writer);
    }

    private static JsonValueKind Map(JToken? token)
    {
        if (token is null)
        {
            return JsonValueKind.Undefined;
        }

        switch (token.Type)
        {
            case JTokenType.Object:
                return JsonValueKind.Object;
            case JTokenType.Array:
                return JsonValueKind.Array;
            case JTokenType.String:
            case JTokenType.Uri:
            case JTokenType.Guid:
            case JTokenType.Date:
                return JsonValueKind.String;
            case JTokenType.Integer:
            case JTokenType.Float:
                return JsonValueKind.Number;
            case JTokenType.Boolean:
                return token.Value<bool>() ? JsonValueKind.True : JsonValueKind.False;
            case JTokenType.Null:
                return JsonValueKind.Null;
            default:
                return JsonValueKind.Undefined;
        }
    }
}

}

namespace System.Text.Json.Nodes
{

public abstract class JsonNode
{
    internal JsonNode(JToken token)
    {
        Token = token;
    }

    internal JToken Token { get; }

    internal static JsonNode? FromToken(JToken? token)
    {
        if (token is null || token.Type == JTokenType.Null)
        {
            return null;
        }

        if (token is JObject obj)
        {
            return new JsonObject(obj);
        }

        return new JsonValueNode(token);
    }

    public static implicit operator JsonNode(string? value)
    {
        return new JsonValueNode(value is null ? JValue.CreateNull() : new JValue(value));
    }

    internal void WriteTo(NewtonsoftJson.JsonWriter writer)
    {
        Token.WriteTo(writer);
    }
}

public sealed class JsonObject : JsonNode
{
    public JsonObject()
        : base(new JObject())
    {
    }

    internal JsonObject(JObject obj)
        : base(obj)
    {
    }

    public JsonNode? this[string propertyName]
    {
        set => Object[propertyName] = value is null ? JValue.CreateNull() : value.Token;
    }

    private JObject Object => (JObject)Token;
}

internal sealed class JsonValueNode : JsonNode
{
    internal JsonValueNode(JToken token)
        : base(token)
    {
    }
}
}

namespace System.Text.Json
{
    using Nodes;

internal sealed class ForgeNet46ContractResolver : DefaultContractResolver
{
    public ForgeNet46ContractResolver()
    {
        NamingStrategy = new CamelCaseNamingStrategy
        {
            ProcessDictionaryKeys = false,
            OverrideSpecifiedNames = true
        };
    }
}

internal sealed class JsonElementNewtonsoftConverter : NewtonsoftJson.JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(JsonElement);
    }

    public override void WriteJson(NewtonsoftJson.JsonWriter writer, object? value, NewtonsoftJson.JsonSerializer serializer)
    {
        ((JsonElement)value!).WriteTo(writer);
    }

    public override object ReadJson(NewtonsoftJson.JsonReader reader, Type objectType, object? existingValue, NewtonsoftJson.JsonSerializer serializer)
    {
        return new JsonElement(JToken.Load(reader));
    }
}

internal sealed class JsonNodeNewtonsoftConverter : NewtonsoftJson.JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return typeof(JsonNode).IsAssignableFrom(objectType);
    }

    public override void WriteJson(NewtonsoftJson.JsonWriter writer, object? value, NewtonsoftJson.JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        ((JsonNode)value).WriteTo(writer);
    }

    public override object? ReadJson(NewtonsoftJson.JsonReader reader, Type objectType, object? existingValue, NewtonsoftJson.JsonSerializer serializer)
    {
        return JsonNode.FromToken(JToken.Load(reader));
    }
}
}
