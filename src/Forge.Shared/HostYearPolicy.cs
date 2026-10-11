using System.Globalization;

namespace Forge.Shared;

/// <summary>
/// Year-mismatch policy (docs/cad-job-tools.md C2): a plugin dispatch is decided from two
/// typed facts — the build's <c>ForgeTargetYear</c> assembly stamp and the host year mapped
/// from <c>Application.Version</c> through the table below — never from name or path
/// inference. A mismatch refuses with a named error until a live smoke proves the year
/// pair; proven pairs enter <see cref="ProvenPairs"/> in the same change as the smoke.
/// </summary>
public static class HostYearPolicy
{
    public const string MismatchCode = "HOST_YEAR_MISMATCH";
    public const string StampMissingCode = "FORGE_YEAR_STAMP_MISSING";
    public const string HostYearUnknownCode = "HOST_YEAR_UNKNOWN";

    /// <summary>
    /// Managed version-string prefix to AutoCAD year. The leading token of
    /// <c>Application.Version</c> ("25.0.171.0.0" on the 2025 machine measured 2026-10-07,
    /// registry R25.0) is matched to the year by exact major.minor prefix with a non-digit
    /// boundary — a typed table, not a guess. A year this table cannot name is
    /// <see cref="HostYearUnknownCode"/>, never a fallback.
    /// </summary>
    private static readonly (string Prefix, int Year)[] VersionTable =
    {
        ("24.3", 2024),
        ("25.0", 2025),
        ("25.1", 2026),
    };

    /// <summary>Year pairs proven by a live smoke; empty until C8 proves one.</summary>
    private static readonly HashSet<(int Target, int Host)> ProvenPairs = new();

    /// <summary>Marks a year pair proven by a live smoke; the proof is recorded in the change that calls this.</summary>
    public static void ProvePair(int targetYear, int hostYear) => ProvenPairs.Add((targetYear, hostYear));

    public static bool TryMapHostYear(string? versionText, out int year)
    {
        year = 0;
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return false;
        }

        var text = versionText.Trim();
        if (text.Length > 0 && (text[0] is 'R' or 'r'))
        {
            text = text[1..];
        }

        foreach (var (prefix, mapped) in VersionTable)
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal)
                && (text.Length == prefix.Length || !char.IsDigit(text[prefix.Length])))
            {
                year = mapped;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Decides whether a plugin built for <paramref name="targetYear"/> may dispatch on a host
    /// whose version string is <paramref name="hostVersionRaw"/>. Diagnostic answers
    /// (health/version) are the caller's choice; this policy only states the typed decision.
    /// </summary>
    public static (bool Allowed, string? Code, string? Message) Check(
        int? targetYear,
        int? hostYear,
        string? hostVersionRaw,
        IReadOnlyCollection<(int Target, int Host)>? provenPairs = null)
    {
        if (targetYear is null)
        {
            return (false, StampMissingCode,
                "This Forge.Plugin build carries no ForgeTargetYear stamp. Rebuild with build-plugin.ps1 -Year.");
        }

        if (hostYear is null)
        {
            return (false, HostYearUnknownCode,
                $"AutoCAD host year could not be mapped from Application.Version \"{hostVersionRaw ?? "<unavailable>"}\" " +
                "— add the year's managed version prefix to HostYearPolicy.VersionTable.");
        }

        if (targetYear == hostYear || ProvenPairs.Contains((targetYear.Value, hostYear.Value))
            || (provenPairs?.Contains((targetYear.Value, hostYear.Value)) ?? false))
        {
            return (true, null, null);
        }

        return (false, MismatchCode,
            $"Plugin built for AutoCAD {targetYear} but running on AutoCAD {hostYear}. " +
            $"Rebuild with build-plugin.ps1 -Year {hostYear}, or record the proven pair in HostYearPolicy after a live smoke.");
    }
}
