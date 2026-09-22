using System.Globalization;

namespace Forge.Shared;

public static class AccoreConsoleLocator
{
    public const string NotFoundCode = "accoreconsole_not_found";
    public const string YearUnsupportedCode = "autocad_version_unsupported";
    public const string YearEnvironmentVariable = "FORGE_ACCORECONSOLE_YEAR";

    public static int ResolveYear(int? jobYear, int? callYear, string? envYearText)
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

        return int.Parse(ForgeConstants.AutoCadVersion, CultureInfo.InvariantCulture);
    }

    public static Choice Locate(int year, Func<string, string?> readEnv, Func<string, bool> fileExists)
    {
        AutoCadHost host;
        try
        {
            host = AutoCadHostCatalog.ByYear(year);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new Choice(
                false,
                year,
                null,
                YearUnsupportedCode,
                $"AutoCAD {year} is outside the Forge host matrix 2017-2026.");
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
                year,
                exe,
                NotFoundCode,
                $"accoreconsole.exe for AutoCAD {year} was not found at {exe}. Set {host.RootEnvironmentVariable}. This lookup does not fall back to AutoCAD 2026.");
        }

        return new Choice(true, year, exe, null, null);
    }

    public sealed record Choice(bool Found, int Year, string? ExePath, string? ErrorCode, string? Message);
}
