using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Forge.Server;

public static class ForgeCallToolResults
{
    /// <summary>
    /// SDK 1.3.0 leaves IsError unset when a tool returns a plain object.
    /// Tools do not advertise an output schema, so the ForgeResult is JSON text.
    /// Business failure still sets isError and keeps that object as structured content.
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

        result.Content = [new TextContentBlock { Text = parts.Count == 0 ? "Forge call failed." : string.Join("\n", parts) }];
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
