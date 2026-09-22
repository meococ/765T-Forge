namespace Forge.Shared;

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
