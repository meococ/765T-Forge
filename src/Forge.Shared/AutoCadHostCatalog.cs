using System.Xml.Linq;

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

    public const string ExecDotNetFeature = "forge_exec_dotnet";

    public const string PublishDsdFeature = "Publisher.PublishDsd";

    public const string UndoMarkFeature = "undo_mark";

    private static readonly AutoCadHost[] Hosts =
    [
        Host(2017, "R21.0", "net46", "4.6",
            "Targets net46, the .NET Framework 4.6 CLR documented for AutoCAD 2017 (R21.0). System.Text.Json 8.0.5 returns NU1202 for net46, so JSON uses Newtonsoft.Json. Not smoke-tested. forge_exec_dotnet returns autocad_version_unsupported."),
        Host(2018, "R22.0", "net46", "4.6",
            "Targets net46, the .NET Framework 4.6 CLR documented for AutoCAD 2018 (R22.0). Autodesk lists 4.6, not 4.7. System.Text.Json 8.0.5 returns NU1202 for net46, so JSON uses Newtonsoft.Json. Not smoke-tested. forge_exec_dotnet returns autocad_version_unsupported."),
        Host(2019, "R23.0", "net47", "4.7",
            "Targets net47, the AutoCAD 2019 CLR. Build target only; not smoke-tested."),
        Host(2020, "R23.1", "net47", "4.7",
            "Targets net47, the AutoCAD 2020 CLR. Build target only; not smoke-tested."),
        Host(2021, "R24.0", "net48", "4.8",
            "Targets net48, the AutoCAD 2021 CLR. Build target only; not smoke-tested."),
        Host(2022, "R24.1", "net48", "4.8",
            "Targets net48, the AutoCAD 2022 CLR. Build target only; not smoke-tested."),
        Host(2023, "R24.2", "net48", "4.8",
            "Targets net48, the AutoCAD 2023 CLR. Build target only; not smoke-tested."),
        Host(2024, "R24.3", "net48", "4.8",
            "Targets net48, the AutoCAD 2024 CLR. Build target only; not smoke-tested."),
        Host(2025, "R25.0", "net8.0-windows", "net8.0",
            "Targets net8.0-windows, the AutoCAD 2025 runtime. Not smoke-tested and not interchangeable with the 2026 binary."),
        Host(2026, "R25.1", "net8.0-windows", "net8.0",
            "Default verified host. Smoke-tested separately from the 2017–2025 build targets.")
    ];

    private static readonly AutoCadFeatureGate[] Gates =
    [
        new(
            ExecDotNetFeature,
            2019,
            "Microsoft.CodeAnalysis.CSharp.Scripting 4.12 targets netstandard2.0, which .NET Framework 4.6 cannot load. The net46 build does not reference Roslyn."),
        new(
            PublishDsdFeature,
            2017,
            "Publisher.PublishDsd(string, PlotProgressDialog) is the managed publish API on AutoCAD 2017–2026. The 2026 path calls it directly."),
        new(
            UndoMarkFeature,
            2017,
            "Grouped undo uses Editor.Command _.UNDO _M and _E on every year. Document.StartUndoMark and EndUndoMark are not called; the net8 Document surface this repo compiles against does not expose them.")
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

    /// <summary>
    /// Null only when <paramref name="acadVer"/> parses and matches <paramref name="builtForYear"/>.
    /// Missing or unparseable ACADVER is a failure. Callers must not treat null acadVer as a pass.
    /// </summary>
    public static ForgeResult? DecideHostMismatch(string commandId, string tool, int builtForYear, string? acadVer)
    {
        var built = ByYear(builtForYear);
        if (TryParseAcadVer(acadVer, out var major, out var minor) && built.MatchesProduct(major, minor))
        {
            return null;
        }

        return HostMismatch(commandId, tool, built, acadVer);
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

    /// <summary>
    /// Drops autoloader components whose <c>Contents/Windows/&lt;year&gt;</c> folder was not built.
    /// The repository template still lists every year; pack and install write this filtered copy.
    /// </summary>
    public static string FilterPackageContentsXml(string packageContentsXml, IEnumerable<int> builtYears)
    {
        var years = new HashSet<int>(builtYears);
        var doc = XDocument.Parse(packageContentsXml, LoadOptions.PreserveWhitespace);
        var removed = doc.Descendants("Components").Where(component =>
        {
            var module = component.Element("ComponentEntry")?.Attribute("ModuleName")?.Value ?? "";
            return !TryModuleYear(module, out var year) || !years.Contains(year);
        }).ToList();
        foreach (var node in removed)
        {
            node.Remove();
        }

        return doc.Declaration is null
            ? doc.ToString()
            : doc.Declaration + Environment.NewLine + doc;
    }

    private static bool TryModuleYear(string module, out int year)
    {
        year = 0;
        const string marker = "Windows/";
        var index = module.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        var start = index + marker.Length;
        var end = start;
        while (end < module.Length && char.IsDigit(module[end]))
        {
            end++;
        }

        if (end == start || end >= module.Length || module[end] != '/')
        {
            return false;
        }

        return int.TryParse(module.Substring(start, end - start), out year);
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

    private static AutoCadHost Host(int year, string series, string tfm, string documentedClr, string loadNote)
    {
        return new AutoCadHost(
            year,
            series,
            tfm,
            $"AUTOCAD_{year}_ROOT",
            $@"C:\Program Files\Autodesk\AutoCAD {year}",
            documentedClr,
            loadNote);
    }
}

public sealed record AutoCadFeatureGate(string Feature, int MinimumYear, string Note);

public sealed record AutoCadHost(
    int Year,
    string Series,
    string TargetFramework,
    string RootEnvironmentVariable,
    string DefaultInstallDirectory,
    string DocumentedClr,
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
