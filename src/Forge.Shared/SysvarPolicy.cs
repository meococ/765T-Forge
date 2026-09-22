namespace Forge.Shared;

/// <summary>
/// Block list for the typed <c>forge_system_setvar</c> tool.
/// This is not the command denylist. <see cref="SafetyPolicy"/> regexes stay unchanged.
/// </summary>
public static class SysvarPolicy
{
    public static readonly string[] ForbiddenNames =
    [
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
    ];

    public static bool IsForbidden(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        foreach (var forbidden in ForbiddenNames)
        {
            if (forbidden.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
