namespace Forge.Shared;

/// <summary>
/// Wire-level progress line a pipe endpoint may emit before the final ForgeResult line.
/// Shape: {"kind":"forge_progress","commandId":"...","progress":0.5,"total":1.0,"message":"..."}.
/// The pipe client skips these lines while reading the reply and forwards them to whatever
/// progress sink the caller supplied (MCP notifications/progress when a progressToken was sent).
/// </summary>
public sealed record ForgeProgressMessage
{
    public const string KindValue = "forge_progress";

    public required string Kind { get; init; }
    public required string CommandId { get; init; }
    public double Progress { get; init; }
    public double? Total { get; init; }
    public string? Message { get; init; }

    public static ForgeProgressMessage For(string commandId, double progress, double? total = null, string? message = null)
        => new() { Kind = KindValue, CommandId = commandId, Progress = progress, Total = total, Message = message };

    /// <summary>Parse one pipe line as a progress message; null when it is not one.</summary>
    public static ForgeProgressMessage? TryParse(string json)
    {
        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<ForgeProgressMessage>(json, ForgeJson.Options);
            return parsed is { Kind: KindValue } ? parsed : null;
        }
        catch
        {
            return null;
        }
    }
}
