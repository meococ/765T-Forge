using System.Globalization;
using System.Xml.Linq;
using Forge.Shared;

namespace Forge.Tests;

public sealed class AutoCadHostCatalogTests
{
    private static readonly int[] Net48Years = [2021, 2022, 2023, 2024];
    private static readonly string[] BundleModuleNames =
    [
        "./Contents/2017/Forge.Plugin.dll",
        "./Contents/2025/Forge.Plugin.dll"
    ];

    [Fact]
    public void CatalogLists2017Through2026WithRuntimeFamilies()
    {
        var years = AutoCadHostCatalog.All.Select(host => host.Year).ToArray();
        Assert.Equal(Enumerable.Range(2017, 10), years);
        Assert.Equal(10, AutoCadHostCatalog.All.Select(host => host.Series).Distinct().Count());

        foreach (var host in AutoCadHostCatalog.All)
        {
            Assert.StartsWith("R", host.Series);
            Assert.Equal($"AUTOCAD_{host.Year}_ROOT", host.RootEnvironmentVariable);
            Assert.Equal($@"C:\Program Files\Autodesk\AutoCAD {host.Year}", host.DefaultInstallDirectory);
        }

        Assert.Equal("4.6", AutoCadHostCatalog.ByYear(2017).Runtime);
        Assert.Equal("4.6", AutoCadHostCatalog.ByYear(2018).Runtime);
        Assert.Equal("4.7", AutoCadHostCatalog.ByYear(2019).Runtime);
        Assert.Equal("4.7", AutoCadHostCatalog.ByYear(2020).Runtime);
        Assert.All(Net48Years, year => Assert.Equal("4.8", AutoCadHostCatalog.ByYear(year).Runtime));
        Assert.Equal("net8.0", AutoCadHostCatalog.ByYear(2025).Runtime);
        Assert.Equal("net8.0", AutoCadHostCatalog.ByYear(2026).Runtime);

        // No per-year binary claim anywhere: the runtime column is a CLR fact, not a build target.
        Assert.DoesNotContain("net462", string.Join("\n", AutoCadHostCatalog.All.Select(host => host.Runtime)), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("net46", string.Join("\n", AutoCadHostCatalog.All.Select(host => host.Runtime)), StringComparison.OrdinalIgnoreCase);

        Assert.Contains("not runtime-verified", AutoCadHostCatalog.ByYear(2017).LoadNote, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("net462", AutoCadHostCatalog.ByYear(2017).LoadNote, StringComparison.Ordinal);
        Assert.Contains("Not separately smoke-tested", AutoCadHostCatalog.ByYear(2025).LoadNote, StringComparison.Ordinal);
        Assert.Contains("smoke-lab", AutoCadHostCatalog.ByYear(2026).LoadNote, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("21.0s (LMS Tech)", 2017)]
    [InlineData("22.0s (LMS Tech)", 2018)]
    [InlineData("23.0s (LMS Tech)", 2019)]
    [InlineData("23.1s (LMS Tech)", 2020)]
    [InlineData("24.0s (LMS Tech)", 2021)]
    [InlineData("24.1s (LMS Tech)", 2022)]
    [InlineData("24.2s (LMS Tech)", 2023)]
    [InlineData("24.3s (LMS Tech)", 2024)]
    [InlineData("25.0s (LMS Tech)", 2025)]
    [InlineData("25.1s (LMS Tech)", 2026)]
    public void AcadVerMatchesOnlyThatRelease(string acadVer, int year)
    {
        Assert.True(AutoCadHostCatalog.TryParseAcadVer(acadVer, out var major, out var minor));
        Assert.True(AutoCadHostCatalog.ByYear(year).MatchesProduct(major, minor));
        foreach (var other in AutoCadHostCatalog.All.Where(host => host.Year != year))
        {
            Assert.False(other.MatchesProduct(major, minor));
        }
    }

    [Fact]
    public void VersionMismatchUsesUnsupportedCodeAndNamesTheFeature()
    {
        var result = AutoCadHostCatalog.HostMismatch(
            "cmd-1",
            "forge_plot_publish",
            AutoCadHostCatalog.ByYear(2024),
            "25.1s (LMS Tech)");

        Assert.False(result.Ok);
        Assert.Equal(AutoCadHostCatalog.VersionUnsupportedCode, result.Error!.Code);
        Assert.Equal("autocad_version_unsupported", result.Error.Code);
        Assert.Contains("forge_plot_publish", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("2024", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("25.1", result.Error.Message, StringComparison.Ordinal);

        var feature = AutoCadHostCatalog.UnsupportedFeature("cmd-2", 2016, "forge_exec_dotnet", 2017);
        Assert.False(feature.Ok);
        Assert.Equal("autocad_version_unsupported", feature.Error!.Code);
        Assert.Contains("2016", feature.Error.Message, StringComparison.Ordinal);
        Assert.Contains("forge_exec_dotnet", feature.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FeatureGatesDocumentTheBuildTargetRequirementAndKeepUndoPair()
    {
        Assert.Equal(
            new[] { AutoCadHostCatalog.ExecDotNetFeature, AutoCadHostCatalog.PublishDsdFeature, AutoCadHostCatalog.UndoMarkFeature },
            AutoCadHostCatalog.FeatureGates.Select(gate => gate.Feature).ToArray());

        // The exec_dotnet gate is a build-target requirement (net462+), not a per-year block.
        foreach (var year in Enumerable.Range(2017, 10))
        {
            Assert.True(AutoCadHostCatalog.SupportsFeature(year, AutoCadHostCatalog.ExecDotNetFeature));
            Assert.Null(AutoCadHostCatalog.TryUnsupported("cmd", year, AutoCadHostCatalog.ExecDotNetFeature));
        }

        var ancientDotNet = AutoCadHostCatalog.TryUnsupported("cmd", 2016, AutoCadHostCatalog.ExecDotNetFeature);
        Assert.NotNull(ancientDotNet);
        Assert.False(ancientDotNet!.Ok);
        Assert.Equal(AutoCadHostCatalog.VersionUnsupportedCode, ancientDotNet.Error!.Code);
        Assert.Contains("2016", ancientDotNet.Error.Message, StringComparison.Ordinal);
        Assert.Contains(AutoCadHostCatalog.ExecDotNetFeature, ancientDotNet.Error.Message, StringComparison.Ordinal);
        Assert.Contains("2017", ancientDotNet.Error.Suggestion, StringComparison.Ordinal);

        foreach (var host in AutoCadHostCatalog.All)
        {
            Assert.Null(AutoCadHostCatalog.TryUnsupported("cmd", host.Year, AutoCadHostCatalog.PublishDsdFeature));
            Assert.Null(AutoCadHostCatalog.TryUnsupported("cmd", host.Year, AutoCadHostCatalog.UndoMarkFeature));
            Assert.True(AutoCadHostCatalog.SupportsFeature(host.Year, AutoCadHostCatalog.PublishDsdFeature));
            Assert.True(AutoCadHostCatalog.SupportsFeature(host.Year, AutoCadHostCatalog.UndoMarkFeature));
        }

        var execNote = AutoCadHostCatalog.FeatureGates.Single(gate => gate.Feature == AutoCadHostCatalog.ExecDotNetFeature).Note;
        Assert.Contains("Roslyn", execNote, StringComparison.Ordinal);
        Assert.Contains("net462", execNote, StringComparison.Ordinal);
        Assert.Contains("netstandard2.0", execNote, StringComparison.Ordinal);
        Assert.Contains("4.6.2", execNote, StringComparison.Ordinal);

        var undoNote = AutoCadHostCatalog.FeatureGates.Single(gate => gate.Feature == AutoCadHostCatalog.UndoMarkFeature).Note;
        Assert.Contains("_.UNDO _BE", undoNote, StringComparison.Ordinal);
        Assert.Contains("_.UNDO _E", undoNote, StringComparison.Ordinal);
        Assert.Contains("_M is Mark", undoNote, StringComparison.Ordinal);
        Assert.DoesNotContain("_.UNDO _M and _E", undoNote, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nope")]
    [InlineData("25")]
    [InlineData("25.")]
    public void DecideHostMismatchUnparseableAcadVerFailsClosed(string? acadVer)
    {
        var legacy = AutoCadHostCatalog.DecideHostMismatch("cmd", "forge_system_health", AutoCadBuildTarget.LegacyNet462, acadVer);
        Assert.NotNull(legacy);
        Assert.False(legacy!.Ok);
        Assert.Equal(AutoCadHostCatalog.HostMismatchCode, legacy.Error!.Code);

        var modern = AutoCadHostCatalog.DecideHostMismatch("cmd", "forge_system_health", AutoCadBuildTarget.ModernNet8, acadVer);
        Assert.NotNull(modern);
        Assert.False(modern!.Ok);
        Assert.Equal(AutoCadHostCatalog.HostMismatchCode, modern.Error!.Code);
    }

    [Theory]
    [InlineData("21.0s (LMS Tech)")]
    [InlineData("24.3s (LMS Tech)")]
    public void DecideHostMismatchLegacyTargetAccepts2017Through2024(string acadVer)
    {
        Assert.Null(AutoCadHostCatalog.DecideHostMismatch("cmd", "forge_system_health", AutoCadBuildTarget.LegacyNet462, acadVer));
    }

    [Theory]
    [InlineData("25.0s (LMS Tech)")]
    [InlineData("25.1s (LMS Tech)")]
    [InlineData("26.0s (LMS Tech)")]
    public void DecideHostMismatchModernTargetAccepts2025Through2027(string acadVer)
    {
        Assert.Null(AutoCadHostCatalog.DecideHostMismatch("cmd", "forge_system_health", AutoCadBuildTarget.ModernNet8, acadVer));
    }

    [Theory]
    [InlineData("LegacyNet462", "25.1s (LMS Tech)")]
    [InlineData("LegacyNet462", "26.0s (LMS Tech)")]
    [InlineData("LegacyNet462", "20.0")]
    [InlineData("ModernNet8", "24.3s (LMS Tech)")]
    [InlineData("ModernNet8", "21.0")]
    public void DecideHostMismatchDifferentReleaseFails(string targetName, string acadVer)
    {
        var target = string.Equals(targetName, "LegacyNet462", StringComparison.Ordinal)
            ? AutoCadBuildTarget.LegacyNet462
            : AutoCadBuildTarget.ModernNet8;

        var result = AutoCadHostCatalog.DecideHostMismatch("cmd", "forge_doc_open", target, acadVer);
        Assert.NotNull(result);
        Assert.False(result!.Ok);
        Assert.Equal("autocad_version_unsupported", result.Error!.Code);
        Assert.Contains(target.Name, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveDefaultHostIsDeterminateWithoutAnInstall()
    {
        Assert.Null(AutoCadHostCatalog.ResolveDefaultHost(null));
        Assert.Null(AutoCadHostCatalog.ResolveDefaultHost(""));
        Assert.Null(AutoCadHostCatalog.ResolveDefaultHost("   "));
        Assert.Null(AutoCadHostCatalog.ResolveDefaultHost(@"D:\CAD\custom"));
        Assert.Null(AutoCadHostCatalog.ResolveDefaultHost(@"C:\Program Files\Autodesk\AutoCAD 2016"));

        Assert.Equal(2024, AutoCadHostCatalog.ResolveDefaultHost(@"C:\Program Files\Autodesk\AutoCAD 2024")!.Year);
        Assert.Equal(2024, AutoCadHostCatalog.ResolveDefaultHost(@"C:\Program Files\Autodesk\AutoCAD 2024\")!.Year);
        Assert.Equal(2026, AutoCadHostCatalog.ResolveDefaultHost(@"C:\Program Files\Autodesk\AutoCAD 2026")!.Year);
    }

    [Fact]
    public void TryByYearIsDeterminateOutsideTheMatrix()
    {
        Assert.Null(AutoCadHostCatalog.TryByYear(2016));
        Assert.Null(AutoCadHostCatalog.TryByYear(2027));
        Assert.Throws<ArgumentOutOfRangeException>(() => AutoCadHostCatalog.ByYear(2027));
    }

    [Fact]
    public void RejectIfHostMismatchSourceFailsClosed()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Forge.Plugin", "PluginCommandProcessor.cs"));
        var start = source.IndexOf("private static ForgeResult? RejectIfHostMismatch", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("private static", start + 40, StringComparison.Ordinal);
        Assert.True(end > start);
        var body = source[start..end];
        Assert.Contains("DecideHostMismatch", body, StringComparison.Ordinal);
        Assert.Contains("ACADVER", body, StringComparison.Ordinal);
        Assert.DoesNotContain("return null", body, StringComparison.Ordinal);
    }

    [Fact]
    public void PluginSelectsTheBuildTargetByCompileTimeFramework()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Forge.Plugin", "PluginCommandProcessor.cs"));
        Assert.Contains("#if NETFRAMEWORK", source, StringComparison.Ordinal);
        Assert.Contains("AutoCadBuildTarget.LegacyNet462", source, StringComparison.Ordinal);
        Assert.Contains("AutoCadBuildTarget.ModernNet8", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BundleComponentsMatchTheTwoBuildTargets()
    {
        var doc = XDocument.Load(Path.Combine(FindRepoRoot(), "plugin-bundle", "765T-Forge.bundle", "PackageContents.xml"));
        var ranges = doc.Descendants("RuntimeRequirements")
            .Select(requirement => (Min: requirement.Attribute("SeriesMin")!.Value, Max: requirement.Attribute("SeriesMax")!.Value))
            .OrderBy(range => range.Min)
            .ToArray();
        Assert.Equal(2, ranges.Length);
        Assert.Equal("R21.0", ranges[0].Min);
        Assert.Equal("R24.3", ranges[0].Max);
        Assert.Equal("R25.0", ranges[1].Min);
        Assert.Equal("R26.0", ranges[1].Max);

        var modules = doc.Descendants("ComponentEntry")
            .Select(entry => entry.Attribute("ModuleName")!.Value)
            .OrderBy(value => value)
            .ToArray();
        Assert.Equal(BundleModuleNames, modules);
    }

    [AutoCadInstallFact]
    public void DefaultResolvesTheDiscoveredInstall()
    {
        var root = ForgeEnvironment.DefaultAutoCadRoot();
        var host = AutoCadHostCatalog.Default;
        Assert.NotNull(host);
        Assert.Equal(ForgeEnvironment.TryExtractAutoCadYear(root), host!.Year.ToString(CultureInfo.InvariantCulture));
        Assert.True(File.Exists(Path.Combine(root, "accoreconsole.exe")));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "765T-Forge.ServerOnly.slnf")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
