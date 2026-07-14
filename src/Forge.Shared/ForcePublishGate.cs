namespace Forge.Shared;

/// <summary>
/// Env gate for publish/recipe <c>force=true</c> (ADR 0004). Default deny.
/// </summary>
public static class ForcePublishGate
{
    public const string DenyCode = "force_not_allowed";
    public const string DenyMessage =
        "force=true is refused unless FORGE_ALLOW_FORCE_PUBLISH=true (ops accept — not an agent convenience).";
    public const string DenySuggestion =
        "Fix preflight findings, or have an operator set FORGE_ALLOW_FORCE_PUBLISH=true on both server and AutoCAD processes.";

    public static bool IsAllowed(bool forceRequested, bool allowForcePublish)
        => !forceRequested || allowForcePublish;
}
