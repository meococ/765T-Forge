using System.Text.Json;
using Forge.Shared;
using ModelContextProtocol.Protocol;

namespace Forge.Server;

/// <summary>
/// Maps business failures (<c>ForgeResult.Ok == false</c>) onto MCP's <c>CallToolResult.IsError</c>
/// flag so MCP hosts do not treat failed gates or safety denials as successful tool calls.
/// </summary>
public static class ForgeCallToolResults
{
    /// <summary>
    /// Tools do not advertise an output schema, so a ForgeResult arrives either as structured
    /// content or as JSON text. Business failure sets IsError and keeps the ForgeResult object
    /// as structured content; the text content becomes [short summary, full JSON envelope].
    /// </summary>
    public static CallToolResult MarkBusinessFailure(CallToolResult result)
    {
        var hadStructured = result.StructuredContent is { ValueKind: JsonValueKind.Object };
        if (!TryReadForgeObject(result, out var structured))
        {
            return result;
        }

        if (!TryProperty(structured, "ok", "Ok", out var ok) || ok.ValueKind != JsonValueKind.False)
        {
            return result;
        }

        string? code = null;
        string? message = null;
        string? suggestion = null;
        if (TryProperty(structured, "error", "Error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            if (TryProperty(error, "code", "Code", out var codeEl))
            {
                code = codeEl.GetString();
            }

            if (TryProperty(error, "message", "Message", out var messageEl))
            {
                message = messageEl.GetString();
            }

            if (TryProperty(error, "suggestion", "Suggestion", out var suggestionEl))
            {
                suggestion = suggestionEl.GetString();
            }
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(code))
        {
            parts.Add(code);
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            parts.Add(message);
        }

        if (!string.IsNullOrWhiteSpace(suggestion))
        {
            parts.Add(suggestion);
        }

        result.IsError = true;
        if (!hadStructured)
        {
            result.StructuredContent = structured;
        }

        // Text-only MCP clients never see StructuredContent: block 1 is the short human summary,
        // block 2 is the full ForgeResult envelope (error + data + verification) as JSON.
        var summary = parts.Count == 0 ? "Forge call failed." : string.Join("\n", parts);
        var envelope = JsonSerializer.Serialize(structured, ForgeJson.Options);
        var content = new List<ContentBlock>
        {
            new TextContentBlock { Text = summary },
            new TextContentBlock { Text = envelope }
        };
        if (result.Content is { Count: > 0 } original)
        {
            // Preserve non-text blocks (e.g. images); the original JSON text is superseded by the envelope.
            content.AddRange(original.Where(block => block is not TextContentBlock));
        }

        result.Content = content;
        return result;
    }

    private static bool TryReadForgeObject(CallToolResult result, out JsonElement structured)
    {
        if (result.StructuredContent is { ValueKind: JsonValueKind.Object } existing)
        {
            structured = existing;
            return true;
        }

        structured = default;
        if (result.Content is not { Count: > 0 } content || content[0] is not TextContentBlock block || string.IsNullOrWhiteSpace(block.Text))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(block.Text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            structured = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryProperty(JsonElement element, string camel, string pascal, out JsonElement value)
    {
        if (element.TryGetProperty(camel, out value) || element.TryGetProperty(pascal, out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}
