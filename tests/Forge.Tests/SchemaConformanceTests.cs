using System.Text.Json;

namespace Forge.Tests;

/// <summary>
/// Forge MCP core v2 item 2: every advertised schema is strict JSON Schema with NO boolean
/// schemas in schema position (Zod-based clients reject `true`/`false` subschemas at
/// list-time and drop the tool). Validates over the wire against the real server.
/// </summary>
public sealed class SchemaConformanceTests
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

    [Fact]
    public async Task EveryToolHasObjectOutputSchema()
    {
        var exe = ToolsetTests.FindServerExe();
        await using var server = await ToolsetTests.McpStdio.SpawnAsync(exe, toolsets: "all");
        var tools = await server.ListToolsRawAsync();

        var missing = new List<string>();
        var badShape = new List<string>();
        foreach (var tool in tools.GetProperty("tools").EnumerateArray())
        {
            var name = tool.GetProperty("name").GetString()!;
            if (!tool.TryGetProperty("outputSchema", out var os) || os.ValueKind != JsonValueKind.Object)
            {
                missing.Add(name);
                continue;
            }

            if (!os.TryGetProperty("type", out var type) || type.GetString() != "object")
            {
                badShape.Add($"{name} -> {os.GetRawText()}");
            }
        }

        Assert.True(missing.Count == 0, "Tools without outputSchema: " + string.Join(", ", missing));
        Assert.True(badShape.Count == 0, "outputSchema not {\"type\":\"object\"}: " + string.Join("; ", badShape));
    }

    [Fact]
    public async Task NoBooleanSchemasInSchemaPositions()
    {
        var exe = ToolsetTests.FindServerExe();
        await using var server = await ToolsetTests.McpStdio.SpawnAsync(exe, toolsets: "all");
        var tools = await server.ListToolsRawAsync();

        var hits = new List<string>();
        foreach (var tool in tools.GetProperty("tools").EnumerateArray())
        {
            var name = tool.GetProperty("name").GetString()!;
            foreach (var h in BooleanSchemaPaths(tool.GetProperty("inputSchema")))
            {
                hits.Add($"{name}.input{h}");
            }

            if (tool.TryGetProperty("outputSchema", out var os))
            {
                foreach (var h in BooleanSchemaPaths(os))
                {
                    hits.Add($"{name}.output{h}");
                }
            }
        }

        Assert.True(hits.Count == 0, "Boolean schemas: " + string.Join(", ", hits));
    }

    /// <summary>
    /// A tools/call result carries structuredContent (object, matching outputSchema) AND a
    /// JSON text mirror for text-only clients. Uses forge_system_health (pipe may be down —
    /// both envelopes still arrive).
    /// </summary>
    [Fact]
    public async Task CallResultCarriesStructuredContentAndTextMirror()
    {
        var exe = ToolsetTests.FindServerExe();
        await using var server = await ToolsetTests.McpStdio.SpawnAsync(exe, toolsets: "all");
        var reply = await server.CallToolAsync("forge_system_health", "{}");
        using var doc = JsonDocument.Parse(reply);
        var result = doc.RootElement.GetProperty("result");

        Assert.Equal(JsonValueKind.Object, result.GetProperty("structuredContent").ValueKind);
        var structured = result.GetProperty("structuredContent");
        Assert.True(structured.TryGetProperty("ok", out _), "structuredContent is not a ForgeResult envelope");

        var texts = result.GetProperty("content").EnumerateArray()
            .Where(b => b.GetProperty("type").GetString() == "text")
            .Select(b => b.GetProperty("text").GetString()!)
            .ToArray();
        Assert.True(texts.Length >= 1, "no text block mirrors the structured result");
        using var mirror = JsonDocument.Parse(texts[0]);
        Assert.Equal(
            structured.GetProperty("ok").GetRawText(),
            mirror.RootElement.GetProperty("ok").GetRawText());
    }

    /// <summary>A business failure still sets isError and keeps the ForgeResult envelope.</summary>
    [Fact]
    public async Task BusinessFailureStillSetsIsError()
    {
        var exe = ToolsetTests.FindServerExe();
        await using var server = await ToolsetTests.McpStdio.SpawnAsync(exe, toolsets: "all");
        // Unknown query arg shape -> typed failure before any AutoCAD work.
        var reply = await server.CallToolAsync("forge_toolset_enable", """{"names":["nope-not-a-set"]}""");
        using var doc = JsonDocument.Parse(reply);
        var result = doc.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        var structured = result.GetProperty("structuredContent");
        Assert.Equal("unknown_toolset", structured.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>
    /// Paths (within the given schema) where a boolean literal occupies a schema position.
    /// Annotation values — default, const, enum, examples — are skipped.
    /// </summary>
    private static IEnumerable<string> BooleanSchemaPaths(JsonElement schema)
    {
        if (schema.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            yield return ".";
            yield break;
        }

        if (schema.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var prop in schema.EnumerateObject())
        {
            if (prop.Name == "properties" && prop.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var child in prop.Value.EnumerateObject())
                {
                    foreach (var h in BooleanSchemaPaths(child.Value))
                    {
                        yield return $".properties.{child.Name}{h}";
                    }
                }
            }
            else if (SchemaValueKeys.Contains(prop.Name))
            {
                foreach (var h in BooleanSchemaPaths(prop.Value))
                {
                    yield return $".{prop.Name}{h}";
                }
            }
            else if (CompositeKeys.Contains(prop.Name))
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    var i = 0;
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        foreach (var h in BooleanSchemaPaths(item))
                        {
                            yield return $".{prop.Name}[{i}]{h}";
                        }

                        i++;
                    }
                }
                else if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var child in prop.Value.EnumerateObject())
                    {
                        foreach (var h in BooleanSchemaPaths(child.Value))
                        {
                            yield return $".{prop.Name}.{child.Name}{h}";
                        }
                    }
                }
            }
        }
    }
}
