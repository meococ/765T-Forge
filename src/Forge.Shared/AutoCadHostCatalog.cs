namespace Forge.Shared;

/// <summary>
/// Build-time hosts for AutoCAD 2017–2026. One managed plugin binary cannot NETLOAD
/// across major years. <see cref="ForgeConstants.AutoCadVersion"/> stays 2026, the only
/// host this repo smoke-tests. Other years are compile targets when
/// <c>AUTOCAD_&lt;year&gt;_ROOT</c> points at that install.
/// </summary>
public static class AutoCadHostCatalog
{
    public const string VersionUnsupportedCode = "autocad_version_unsupported";

    private static readonly AutoCadHost[] Hosts =
    [
        Host(2017, "R21.0", "net462",
            "Compiled as net462, not net46: System.Text.Json 8.0.5 cannot restore for net46 (NU1202). AutoCAD 2017 documents .NET Framework 4.6, so this binary is not claimed to NETLOAD and is not a net48 build. Not smoke-tested."),
        Host(2018, "R22.0", "net462",
            "Compiled as net462, not net46: System.Text.Json 8.0.5 cannot restore for net46 (NU1202). AutoCAD 2018 documents .NET Framework 4.6, so this binary is not claimed to NETLOAD and is not a net48 build. Not smoke-tested."),
        Host(2019, "R23.0", "net47",
            "Targets net47, the AutoCAD 2019 CLR. Build target only; not smoke-tested."),
        Host(2020, "R23.1", "net47",
            "Targets net47, the AutoCAD 2020 CLR. Build target only; not smoke-tested."),
        Host(2021, "R24.0", "net48",
            "Targets net48, the AutoCAD 2021 CLR. Build target only; not smoke-tested."),
        Host(2022, "R24.1", "net48",
            "Targets net48, the AutoCAD 2022 CLR. Build target only; not smoke-tested."),
        Host(2023, "R24.2", "net48",
            "Targets net48, the AutoCAD 2023 CLR. Build target only; not smoke-tested."),
        Host(2024, "R24.3", "net48",
            "Targets net48, the AutoCAD 2024 CLR. Build target only; not smoke-tested."),
        Host(2025, "R25.0", "net8.0-windows",
            "Targets net8.0-windows, the AutoCAD 2025 runtime. Not smoke-tested and not interchangeable with the 2026 binary."),
        Host(2026, "R25.1", "net8.0-windows",
            "Default verified host. Smoke-tested separately from the 2017–2025 build targets.")
    ];

    public static IReadOnlyList<AutoCadHost> All => Hosts;

    /// <summary>Host named by <see cref="ForgeConstants.AutoCadVersion"/> (2026).</summary>
    public static AutoCadHost Default => ByYear(int.Parse(ForgeConstants.AutoCadVersion, System.Globalization.CultureInfo.InvariantCulture));

    public static AutoCadHost ByYear(int year)
    {
        foreach (var host in Hosts)
        {
            if (host.Year == year)
            {
                return host;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(year), year, "Forge builds plugins for AutoCAD 2017 through 2026.");
    }

    public static bool TryParseAcadVer(string? acadVer, out int major, out int minor)
    {
        major = 0;
        minor = 0;
        if (string.IsNullOrWhiteSpace(acadVer))
        {
            return false;
        }

        var text = acadVer!;
        var i = 0;
        while (i < text.Length && char.IsDigit(text[i]))
        {
            i++;
        }

        if (i == 0 || i >= text.Length || text[i] != '.')
        {
            return false;
        }

        if (!int.TryParse(text.Substring(0, i), out major))
        {
            return false;
        }

        var start = i + 1;
        var j = start;
        while (j < text.Length && char.IsDigit(text[j]))
        {
            j++;
        }

        if (j == start)
        {
            return false;
        }

        return int.TryParse(text.Substring(start, j - start), out minor);
    }

    public static ForgeResult HostMismatch(string commandId, string feature, AutoCadHost builtFor, string? runningAcadVer)
    {
        var running = string.IsNullOrWhiteSpace(runningAcadVer) ? "unknown" : runningAcadVer!.Trim();
        return ForgeResult.Failure(
            commandId,
            VersionUnsupportedCode,
            $"AutoCAD {running} cannot run {feature} with Forge.Plugin built for AutoCAD {builtFor.Year} ({builtFor.Series}).",
            "NETLOAD the Forge.Plugin.dll built for this AutoCAD year. Plugin binaries are not interchangeable across years.");
    }

    public static ForgeResult UnsupportedFeature(string commandId, int year, string feature, int minimumYear)
    {
        return ForgeResult.Failure(
            commandId,
            VersionUnsupportedCode,
            $"AutoCAD {year} does not support {feature}.",
            $"Use AutoCAD {minimumYear} or newer for {feature}.");
    }

    private static AutoCadHost Host(int year, string series, string tfm, string loadNote)
    {
        return new AutoCadHost(
            year,
            series,
            tfm,
            $"AUTOCAD_{year}_ROOT",
            $@"C:\Program Files\Autodesk\AutoCAD {year}",
            loadNote);
    }
}

public sealed record AutoCadHost(
    int Year,
    string Series,
    string TargetFramework,
    string RootEnvironmentVariable,
    string DefaultInstallDirectory,
    string LoadNote)
{
    public string SeriesMin => Series;

    public string SeriesMax => Series;

    public bool MatchesProduct(int major, int minor)
    {
        return AutoCadHostCatalog.TryParseAcadVer(Series.Substring(1), out var seriesMajor, out var seriesMinor)
            && seriesMajor == major
            && seriesMinor == minor;
    }
}
