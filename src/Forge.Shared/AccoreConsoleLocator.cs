using System.Globalization;

namespace Forge.Shared;

/// <summary>
/// Deterministic AccoreConsole selection. Resolution order is exact and explicit:
/// job-level year, then call-level year, then <c>FORGE_ACCORECONSOLE_YEAR</c>, then the
/// discovered default install. There is deliberately no fallback to a newer AutoCAD year.
/// </summary>
public static class AccoreConsoleLocator
{
    public const string NotFoundCode = "accoreconsole_not_found";
    public const string YearUnsupportedCode = "autocad_version_unsupported";
    public const string YearEnvironmentVariable = "FORGE_ACCORECONSOLE_YEAR";

    /// <summary>
    /// Job year wins over call year; the environment selection wins over the discovered default.
    /// A year outside 2017-2026 is returned as-is so <see cref="Locate"/> can report it verbatim.
    /// </summary>
    public static int? ResolveYear(int? jobYear, int? callYear, string? envYearText, int? discoveredYear)
    {
        if (jobYear is int job)
        {
            return job;
        }

        if (callYear is int call)
        {
            return call;
        }

        if (int.TryParse(envYearText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var envYear))
        {
            return envYear;
        }

        return discoveredYear;
    }

    /// <summary>
    /// Resolves <c>accoreconsole.exe</c> for <paramref name="year"/> from
    /// <c>AUTOCAD_&lt;year&gt;_ROOT</c>, else that year's default install directory.
    /// A null year, an unsupported year, or a missing exe is a determinate failure;
    /// no other year's console is ever substituted.
    /// </summary>
    public static Choice Locate(int? year, Func<string, string?> readEnv, Func<string, bool> fileExists)
    {
        if (year is null)
        {
            return new Choice(
                false,
                null,
                null,
                NotFoundCode,
                $"No AutoCAD year was resolved for accoreconsole. Pass autoCadYear, set {YearEnvironmentVariable}, or set FORGE_AUTOCAD_ROOT to an installed 'AutoCAD <year>' directory. This lookup does not fall back to a newer AutoCAD.");
        }

        var requested = year.Value;
        var host = AutoCadHostCatalog.TryByYear(requested);
        if (host is null)
        {
            return new Choice(
                false,
                requested,
                null,
                YearUnsupportedCode,
                $"AutoCAD {requested} is outside the Forge host matrix 2017-2026.");
        }

        var root = readEnv(host.RootEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = host.DefaultInstallDirectory;
        }

        var exe = Path.Combine(root, "accoreconsole.exe");
        if (!fileExists(exe))
        {
            return new Choice(
                false,
                requested,
                exe,
                NotFoundCode,
                $"accoreconsole.exe for AutoCAD {requested} was not found at {exe}. Set {host.RootEnvironmentVariable}. This lookup does not fall back to a newer AutoCAD.");
        }

        return new Choice(true, requested, exe, null, null);
    }

    public sealed record Choice(bool Found, int? Year, string? ExePath, string? ErrorCode, string? Message);
}
