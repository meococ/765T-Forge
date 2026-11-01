using System.Text.Json;
using System.Text.Json.Nodes;

namespace Forge.Server;

/// <summary>
/// Rewrites boolean JSON Schemas (literal true/false in schema position) on the wire to an
/// explicit any-type union. The generator emits `true` for object?/JsonElement members —
/// Zod-based clients reject boolean schemas and drop the whole tool at list-time. The
/// replacement is semantically identical ("any JSON value"), just expressed without a
/// boolean schema.
/// </summary>
public static class ForgeSchemaSanitizer
{
    private static readonly HashSet<string> SchemaValueKeys = new(StringComparer.Ordinal)
    {
        "items", "prefixItems", "additionalItems", "additionalProperties",
        "unevaluatedItems", "unevaluatedProperties", "contains", "propertyNames",
        "if", "then", "else", "not"
    };

    private static readonly HashSet<string> CompositeKeys = new(StringComparer.Ordinal)
    {
        "allOf", "anyOf", "oneOf", "$defs", "dependentSchemas", "patternProperties"
    };

    private static readonly JsonObject AnyType = new()
    {
        ["type"] = new JsonArray("string", "number", "boolean", "array", "object", "null")
    };

    /// <summary>Sanitize one schema element; returns the input unchanged when nothing was a boolean schema.</summary>
    public static JsonElement Sanitize(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return schema;
        }

        var node = JsonNode.Parse(schema.GetRawText());
        if (node is null)
        {
            return schema;
        }

        if (!Fix(node))
        {
            return schema;
        }

        return JsonSerializer.SerializeToElement(node);
    }

    /// <summary>True when a node is a literal true/false occupying a schema position.</summary>
    private static bool IsBooleanSchema(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<bool>(out _);

    /// <summary>Recursive rewrite; returns whether any node changed.</summary>
    private static bool Fix(JsonNode schema)
    {
        if (schema is not JsonObject obj)
        {
            return false;
        }

        var changed = false;
        foreach (var prop in obj.ToList())
        {
            if (prop.Key == "properties" && prop.Value is JsonObject props)
            {
                var replacements = new List<(string Name, JsonNode? Value)>();
                foreach (var child in props)
                {
                    if (IsBooleanSchema(child.Value))
                    {
                        replacements.Add((child.Key, AnyType.DeepClone()));
                    }
                    else if (child.Value is JsonObject childObj && Fix(childObj))
                    {
                        changed = true;
                    }
                }

                foreach (var (name, value) in replacements)
                {
                    props[name] = value;
                    changed = true;
                }
            }
            else if (SchemaValueKeys.Contains(prop.Key))
            {
                if (IsBooleanSchema(prop.Value))
                {
                    obj[prop.Key] = AnyType.DeepClone();
                    changed = true;
                }
                else if (prop.Value is JsonObject sub && Fix(sub))
                {
                    changed = true;
                }
            }
            else if (CompositeKeys.Contains(prop.Key))
            {
                if (prop.Value is JsonArray arr)
                {
                    for (var i = 0; i < arr.Count; i++)
                    {
                        if (IsBooleanSchema(arr[i]))
                        {
                            arr[i] = AnyType.DeepClone();
                            changed = true;
                        }
                        else if (arr[i] is JsonObject item && Fix(item))
                        {
                            changed = true;
                        }
                    }
                }
                else if (prop.Value is JsonObject map)
                {
                    foreach (var child in map.ToList())
                    {
                        if (IsBooleanSchema(child.Value))
                        {
                            map[child.Key] = AnyType.DeepClone();
                            changed = true;
                        }
                        else if (child.Value is JsonObject childObj && Fix(childObj))
                        {
                            changed = true;
                        }
                    }
                }
            }
        }

        return changed;
    }
}
