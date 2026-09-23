using System.Globalization;

namespace Forge.Shared;

/// <summary>
/// Factual AutoCAD host table for 2017-2026 plus the two plugin build targets this repo ships.
/// A single managed plugin binary cannot NETLOAD across the .NET Framework / .NET split, so
/// Forge builds exactly two assemblies: <c>net462</c> against AutoCAD 2017 reference assemblies
/// (AutoCAD 2017-2024) and <c>net8.0-windows</c> against the AutoCAD 2025/2026 reference
/// assemblies (AutoCAD 2025-2027). There is no per-year plugin binary.
/// </summary>
public static class AutoCadHostCatalog
{
    /// <summary>ACADVER parses but names a release outside the loaded plugin build's series.</summary>
    public const string VersionUnsupportedCode = "autocad_version_unsupported";

    /// <summary>ACADVER is missing, empty, whitespace, or unparseable, so the host cannot be confirmed.</summary>
    public const string HostMismatchCode = "autocad_host_mismatch";

    public const string ExecDotNetFeature = "forge_exec_dotnet";

    public const string PublishDsdFeature = "Publisher.PublishDsd";

    public const string UndoMarkFeature = "undo_mark";

    private static readonly AutoCadHost[] Hosts =
    [
        Host(2017, "R21.0", "4.6",
            "Documented runtime .NET Framework 4.6. Covered by the net462 plugin build compiled against AutoCAD 2017 reference assemblies. That component is prepared but not runtime-verified; forge_exec_dotnet is not smoke-tested on this year."),
        Host(2018, "R22.0", "4.6",
            "Documented runtime .NET Framework 4.6. Covered by the net462 plugin build compiled against AutoCAD 2017 reference assemblies. Not smoke-tested."),
        Host(2019, "R23.0", "4.7",
            "Documented runtime .NET Framework 4.7. Covered by the net462 plugin build. Not smoke-tested."),
        Host(2020, "R23.1", "4.7",
            "Documented runtime .NET Framework 4.7. Covered by the net462 plugin build. Not smoke-tested."),
        Host(2021, "R24.0", "4.8",
            "Documented runtime .NET Framework 4.8. Covered by the net462 plugin build. Not smoke-tested."),
        Host(2022, "R24.1", "4.8",
            "Documented runtime .NET Framework 4.8. Covered by the net462 plugin build. Not smoke-tested."),
        Host(2023, "R24.2", "4.8",
            "Documented runtime .NET Framework 4.8. Covered by the net462 plugin build. Not smoke-tested."),
        Host(2024, "R24.3", "4.8",
            "Documented runtime .NET Framework 4.8. Covered by the net462 plugin build. Not smoke-tested."),
        Host(2025, "R25.0", "net8.0",
            "Runtime .NET 8. Served by the same net8.0-windows assembly as 2026. Not separately smoke-tested."),
        Host(2026, "R25.1", "net8.0",
            "Runtime .NET 8. Default smoke-lab host (scripts/smoke-lab.ps1 -AcadYear 2026) and the year the net8.0-windows plugin is built against.")
    ];

    private static readonly AutoCadFeatureGate[] Gates =
    [
        new(
            ExecDotNetFeature,
            2017,
            "forge_exec_dotnet needs a plugin build that can load Roslyn: the net462 build (AutoCAD 2017-2024) or the net8.0-windows build (AutoCAD 2025-2027). Microsoft.CodeAnalysis.CSharp.Scripting 4.12 ships a netstandard2.0 asset, which .NET Framework 4.6.2 can load; this repo does not ship a net46 (4.6 CLR) plugin binary. The 2017-2024 path is prepared but not smoke-tested."),
        new(
            PublishDsdFeature,
            2017,
            "Publisher.PublishDsd is the managed publish API on AutoCAD 2017-2026. The net462 and net8.0-windows builds both call it; DSD seat behavior is documented as partial."),
        new(
            UndoMarkFeature,
            2017,
            "Grouped undo uses Editor.Command _.UNDO _BE (Begin) and then _.UNDO _E (End) on every supported year. _M is Mark (undo back to the mark), so _M/_E is not a pair; Forge does not call Document.StartUndoMark/EndUndoMark.")
    ];

    public static IReadOnlyList<AutoCadHost> All => Hosts;

    /// <summary>
    /// Host discovered from <c>FORGE_AUTOCAD_ROOT</c> (or the deprecated alias), then from the
    /// highest installed <c>C:\Program Files\Autodesk\AutoCAD &lt;year&gt;</c> containing
    /// <c>accoreconsole.exe</c>. Null when no validated install is found or the discovered year is
    /// outside the 2017-2026 table. Never throws when nothing is installed.
    /// </summary>
    public static AutoCadHost? Default => ResolveDefaultHost(ForgeEnvironment.DefaultAutoCadRoot());

    /// <summary>Testable core of <see cref="Default"/>: an install root in, a host or null out.</summary>
    public static AutoCadHost? ResolveDefaultHost(string? autoCadRoot)
    {
        var year = ForgeEnvironment.TryExtractAutoCadYear(autoCadRoot);
        if (year is null)
        {
            return null;
        }

        return int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? TryByYear(parsed)
            : null;
    }

    /// <summary>Host for <paramref name="year"/>, or null when the year is outside 2017-2026.</summary>
    public static AutoCadHost? TryByYear(int year)
    {
        foreach (var host in Hosts)
        {
            if (host.Year == year)
            {
                return host;
            }
        }

        return null;
    }

    public static AutoCadHost ByYear(int year)
    {
        return TryByYear(year)
               ?? throw new ArgumentOutOfRangeException(nameof(year), year, "Forge hosts are AutoCAD 2017 through 2026.");
    }

    /// <summary>
    /// Exact ACADVER shape: one or more digits, a dot, then one or more digits. The release id on
    /// AutoCAD 2026 is <c>25.1s (LMS Tech)</c>, so the trailing text after the minor number is
    /// ignored, but a missing or partially numeric value is not accepted.
    /// </summary>
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

        if (!int.TryParse(text.Substring(0, i), NumberStyles.None, CultureInfo.InvariantCulture, out major))
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

        return int.TryParse(text.Substring(start, j - start), NumberStyles.None, CultureInfo.InvariantCulture, out minor);
    }

    public static ForgeResult HostMismatch(string commandId, string feature, AutoCadHost builtFor, string? runningAcadVer)
    {
        var running = string.IsNullOrWhiteSpace(runningAcadVer) ? "unknown" : runningAcadVer!.Trim();
        return ForgeResult.Failure(
            commandId,
            VersionUnsupportedCode,
            $"AutoCAD {running} cannot run {feature} with Forge.Plugin built for AutoCAD {builtFor.Year} ({builtFor.Series}).",
            "NETLOAD the Forge.Plugin.dll built for this AutoCAD release. Plugin binaries are not interchangeable across releases.");
    }

    /// <summary>
    /// Null only when <paramref name="acadVer"/> parses and falls inside
    /// <paramref name="buildTarget"/>'s supported series. A missing, empty, whitespace, or
    /// unparseable ACADVER is a failure (<see cref="HostMismatchCode"/>), never a pass.
    /// </summary>
    public static ForgeResult? DecideHostMismatch(string commandId, string tool, AutoCadBuildTarget buildTarget, string? acadVer)
    {
        if (!TryParseAcadVer(acadVer, out var major, out var minor))
        {
            var running = string.IsNullOrWhiteSpace(acadVer) ? "missing" : acadVer!.Trim();
            return ForgeResult.Failure(
                commandId,
                HostMismatchCode,
                $"Cannot confirm the running AutoCAD release for {tool}: ACADVER is {running}.",
                "Run this command inside AutoCAD with the plugin build for that release: net462 for AutoCAD 2017-2024, net8.0-windows for AutoCAD 2025-2027.");
        }

        if (!buildTarget.Supports(major, minor))
        {
            return ForgeResult.Failure(
                commandId,
                VersionUnsupportedCode,
                $"{tool} is running in AutoCAD {major}.{minor}, outside the {buildTarget.Name} plugin build's supported series {buildTarget.SupportedSeriesMin}-{buildTarget.SupportedSeriesMax}.",
                $"NETLOAD the matching plugin build: {AutoCadBuildTarget.LegacyNet462.Name} for AutoCAD 2017-2024, {AutoCadBuildTarget.ModernNet8.Name} for AutoCAD 2025-2027.");
        }

        return null;
    }

    public static ForgeResult UnsupportedFeature(string commandId, int year, string feature, int minimumYear)
    {
        return ForgeResult.Failure(
            commandId,
            VersionUnsupportedCode,
            $"AutoCAD {year} does not support {feature}.",
            $"Use AutoCAD {minimumYear} or newer for {feature}.");
    }

    public static IReadOnlyList<AutoCadFeatureGate> FeatureGates => Gates;

    public static bool SupportsFeature(int year, string feature)
    {
        return year >= FeatureMinimumYear(feature);
    }

    public static int FeatureMinimumYear(string feature)
    {
        return FindGate(feature).MinimumYear;
    }

    /// <summary>
    /// Null when <paramref name="year"/> can run <paramref name="feature"/>.
    /// Otherwise a failed <see cref="ForgeResult"/> with <see cref="VersionUnsupportedCode"/>.
    /// </summary>
    public static ForgeResult? TryUnsupported(string commandId, int year, string feature)
    {
        var gate = FindGate(feature);
        if (year >= gate.MinimumYear)
        {
            return null;
        }

        return UnsupportedFeature(commandId, year, feature, gate.MinimumYear);
    }

    private static AutoCadFeatureGate FindGate(string feature)
    {
        foreach (var gate in Gates)
        {
            if (string.Equals(gate.Feature, feature, StringComparison.Ordinal))
            {
                return gate;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(feature), feature, "Unknown Forge AutoCAD feature gate.");
    }

    private static AutoCadHost Host(int year, string series, string runtime, string loadNote)
    {
        return new AutoCadHost(
            year,
            series,
            runtime,
            $"AUTOCAD_{year}_ROOT",
            $@"C:\Program Files\Autodesk\AutoCAD {year}",
            loadNote);
    }
}

public sealed record AutoCadFeatureGate(string Feature, int MinimumYear, string Note);

/// <summary>
/// One shipped plugin build. The same assembly serves every year whose Series falls inside
/// <see cref="SupportedSeriesMin"/>/<see cref="SupportedSeriesMax"/>; these are not per-year binaries.
/// </summary>
public sealed record AutoCadBuildTarget(
    string Name,
    string ReferenceAssemblies,
    string SupportedSeriesMin,
    string SupportedSeriesMax)
{
    /// <summary>AutoCAD 2017-2024 (.NET Framework 4.6-4.8), compiled against AutoCAD 2017 references.</summary>
    public static AutoCadBuildTarget LegacyNet462 { get; } =
        new("net462", "AutoCAD 2017 reference assemblies", "R21.0", "R24.3");

    /// <summary>AutoCAD 2025-2027 (.NET 8+), compiled against AutoCAD 2025/2026 .NET 8 references.</summary>
    public static AutoCadBuildTarget ModernNet8 { get; } =
        new("net8.0-windows", "AutoCAD 2025/2026 .NET 8 reference assemblies", "R25.0", "R26.0");

    public bool Supports(int major, int minor)
    {
        var key = major * 100 + minor;
        return TrySeriesKey(SupportedSeriesMin, out var minimum)
               && TrySeriesKey(SupportedSeriesMax, out var maximum)
               && key >= minimum
               && key <= maximum;
    }

    private static bool TrySeriesKey(string series, out int key)
    {
        key = 0;
        if (series.Length < 2 || series[0] != 'R')
        {
            return false;
        }

        if (!AutoCadHostCatalog.TryParseAcadVer(series.Substring(1), out var major, out var minor))
        {
            return false;
        }

        key = major * 100 + minor;
        return true;
    }
}

public sealed record AutoCadHost(
    int Year,
    string Series,
    string Runtime,
    string RootEnvironmentVariable,
    string DefaultInstallDirectory,
    string LoadNote)
{
    public bool MatchesProduct(int major, int minor)
    {
        return AutoCadHostCatalog.TryParseAcadVer(Series.Substring(1), out var seriesMajor, out var seriesMinor)
               && seriesMajor == major
               && seriesMinor == minor;
    }
}
