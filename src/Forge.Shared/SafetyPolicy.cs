namespace Forge.Shared;

public sealed record SafetyDecision(bool Allowed, string Code, string Message, string? Suggestion = null)
{
    public static SafetyDecision Allow() => new(true, "allowed", "Allowed");

    public static SafetyDecision Deny(string code, string message, string? suggestion = null)
    {
        return new SafetyDecision(false, code, message, suggestion);
    }
}

public sealed class SafetyPolicy
{
    /// <summary>
    /// Deterministic capability gate. Executors that can run arbitrary user text
    /// (forge_exec_command, forge_exec_lisp, forge_run_script, forge_batch_run,
    /// forge_exec_dotnet) are gated as a whole: they require both the process-level
    /// FORGE_ENABLE_UNSAFE_OPS switch and a per-call unsafeAcknowledged=true.
    /// There is deliberately NO text inspection: keyword matching cannot model
    /// AutoCAD command aliases, abbreviations, menu macros, or AutoLISP.
    /// </summary>
    public SafetyDecision Evaluate(ForgeCommand command, bool unsafeOpsEnabled)
    {
        var metadata = ForgeToolRegistry.Get(command.Tool);
        if (metadata.ReadOnly)
        {
            return SafetyDecision.Allow();
        }

        if (metadata.Unsafe && !(unsafeOpsEnabled && command.UnsafeAcknowledged))
        {
            return SafetyDecision.Deny(
                "unsafe_not_acknowledged",
                $"{command.Tool} runs arbitrary user-authored text and is disabled unless unsafe operations are enabled and acknowledged.",
                "Set FORGE_ENABLE_UNSAFE_OPS=true in the MCP server environment and pass unsafeAcknowledged=true. Only do this for a session you control.");
        }

        return SafetyDecision.Allow();
    }
}
