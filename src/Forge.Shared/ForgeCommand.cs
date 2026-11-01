using System.Text.Json;
using System.Text.Json.Serialization;

namespace Forge.Shared;

public sealed record ForgeCommand
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Tool { get; init; } = "";
    public JsonElement Args { get; init; } = ForgeJson.ToElement(new Dictionary<string, object?>());
    public string? Document { get; init; }
    public bool DryRun { get; init; }
    public bool UnsafeAcknowledged { get; init; }

    // compat-progress-flag: set by the server only when the MCP caller sent a progressToken;
    // the plugin emits forge_progress lines only when this is true, so servers older than
    // core v2 (single-reply readers) still get exactly one reply line.
    public bool Progress { get; init; }

    public string? AuthToken { get; init; }

    public ForgeCommand WithToken(string token) => this with { AuthToken = token };
}

public sealed record ForgeResult
{
    public string Id { get; init; } = "";
    public bool Ok { get; init; }
    public object? Data { get; init; }
    public ForgeError? Error { get; init; }
    public string? AuditId { get; init; }
    public ForgeVerification? Verification { get; init; }

    public static ForgeResult Success(string id, object? data = null, string? auditId = null, ForgeVerification? verification = null)
    {
        return new ForgeResult
        {
            Id = id,
            Ok = true,
            Data = data,
            AuditId = auditId,
            Verification = verification
        };
    }

    public static ForgeResult Failure(
        string id,
        string code,
        string message,
        string? suggestion = null,
        string? auditId = null,
        object? data = null,
        ForgeVerification? verification = null)
    {
        return new ForgeResult
        {
            Id = id,
            Ok = false,
            Data = data,
            Error = new ForgeError(code, message, suggestion),
            AuditId = auditId,
            Verification = verification
        };
    }
}

public sealed record ForgeError(
    string Code,
    string Message,
    string? Suggestion = null,
    bool? Retryable = null,
    string? Hint = null);

public sealed record ForgeVerification
{
    public bool Attempted { get; init; }
    public bool Passed { get; init; }
    public string? Message { get; init; }
    public object? ReadBack { get; init; }
}
