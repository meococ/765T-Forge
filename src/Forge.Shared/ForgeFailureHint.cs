namespace Forge.Shared;

/// <summary>
/// Attaches retryable/hint to every failure result (B3 of the outcome eval). The one rule a
/// caller must honor: a failure with retryable=false will fail identically with identical
/// arguments — repeating it is wasted work (the 12.5M-token t3 retry loop).
///
/// Only transport failures that prove the call never reached the plugin are retryable. A
/// response/request timeout means the work may already be inside AutoCAD, so those steer the
/// caller to forge_qa_readback_after_timeout instead of a blind retry.
/// </summary>
public static class ForgeFailureHint
{
    private const string NonRetryableHint =
        "Same arguments will fail again — change inputs or report the error code; do not repeat the call.";

    private const string TimeoutHint =
        "The operation may already be inside AutoCAD — call forge_qa_readback_after_timeout before any retry; do not repeat a write blindly.";

    private const string TransientHint =
        "Transport did not reach the plugin — one retry is reasonable; then check forge_system_health.";

    public static bool IsRetryable(string? code) => code is "plugin_connect_timeout" or "plugin_unavailable";

    public static ForgeError? Annotate(ForgeError? error)
    {
        if (error is null)
        {
            return null;
        }

        if (error.Retryable is not null && error.Hint is not null)
        {
            return error;
        }

        var retryable = error.Retryable ?? IsRetryable(error.Code);
        var hint = error.Hint ?? (retryable
            ? TransientHint
            : error.Code is "plugin_request_timeout" or "plugin_response_timeout" or "plugin_request_stale"
                ? TimeoutHint
                : NonRetryableHint);
        return error with { Retryable = retryable, Hint = hint };
    }

    public static ForgeResult Annotate(ForgeResult result)
    {
        if (result.Ok || result.Error is null)
        {
            return result;
        }

        return result with { Error = Annotate(result.Error) };
    }
}
