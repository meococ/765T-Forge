using System.Text.Json;

namespace Forge.Shared;

public static class AuditDecisions
{
    public static bool BlocksDispatchWhenUnwritten(ToolMetadata metadata)
        => !metadata.ReadOnly && (metadata.Destructive || metadata.RequiresBackup);

    /// <summary>
    /// Completion code for a finished call. Success is <c>result_recorded</c>.
    /// <c>completed</c> is reserved for the exec payload flag and is not used when the call failed.
    /// </summary>
    public static string For(ForgeResult result, bool dryRun)
    {
        var data = AsElement(result.Data);
        if (dryRun || ReadBool(data, "dryRun", false))
        {
            return "dry_run";
        }

        var queued = ReadBool(data, "queued", false);
        var completed = ReadBool(data, "completed", false);
        if (queued && !completed)
        {
            return "queued_not_completed";
        }

        if (!result.Ok)
        {
            return string.IsNullOrWhiteSpace(result.Error?.Code) ? "failed" : result.Error!.Code;
        }

        return "result_recorded";
    }

    private static JsonElement AsElement(object? data)
    {
        if (data is null)
        {
            return default;
        }

        if (data is JsonElement element)
        {
            return element;
        }

        return ForgeJson.ToElement(data);
    }

    private static bool ReadBool(JsonElement element, string name, bool fallback)
    {
        if (element.ValueKind != JsonValueKind.Object || !TryProperty(element, name, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    private static bool TryProperty(JsonElement element, string camel, out JsonElement value)
    {
        if (element.TryGetProperty(camel, out value))
        {
            return true;
        }

        var pascal = char.ToUpperInvariant(camel[0]) + camel[1..];
        return element.TryGetProperty(pascal, out value);
    }
}
