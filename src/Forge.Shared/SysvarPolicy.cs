namespace Forge.Shared;

/// <summary>
/// Exact-name refusal for trust and startup system variables. Membership is a
/// case-insensitive exact string comparison against a fixed set; there is no
/// pattern, prefix, or substring matching anywhere in this policy.
/// </summary>
public static class SysvarPolicy
{
    public const string DenyCode = "deny_sysvar";

    private static readonly HashSet<string> Denied = new(StringComparer.OrdinalIgnoreCase)
    {
        "SECURELOAD",
        "TRUSTEDPATHS",
        "TRUSTEDDOMAINS",
        "LEGACYCODESEARCH",
        "ACADLSPASDOC",
        "SAFEMODE",
        "TEXTEVAL",
        "DEMANDLOAD",
        "APPAUTOLOAD",
        "AUTOLOAD",
        "EXPERT"
    };

    /// <summary>
    /// Null unless <paramref name="name"/> is exactly one of the denied variables.
    /// A null or whitespace name is not this policy's failure to report.
    /// </summary>
    public static ForgeResult? Reject(string commandId, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name!.Trim();
        if (!Denied.Contains(trimmed))
        {
            return null;
        }

        return ForgeResult.Failure(
            commandId,
            DenyCode,
            $"System variable {trimmed} cannot be set through forge_system_setvar.",
            "Leave trust and startup variables unchanged. Use a typed tool for drawing state.");
    }
}
