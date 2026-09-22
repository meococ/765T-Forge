using System.Xml.Linq;
using Forge.Shared;

namespace Forge.Tests;

public sealed class AutoCadHostCatalogTests
{
    [Fact]
    public void CatalogLists2017Through2026WithDistinctSeriesAndFrameworkFamilies()
    {
        var years = AutoCadHostCatalog.All.Select(host => host.Year).ToArray();
        Assert.Equal(Enumerable.Range(2017, 10), years);
        Assert.Equal(10, AutoCadHostCatalog.All.Select(host => host.Series).Distinct().Count());
        Assert.Equal("2026", ForgeConstants.AutoCadVersion);
        Assert.Equal(2026, AutoCadHostCatalog.Default.Year);
        Assert.Equal("net8.0-windows", AutoCadHostCatalog.Default.TargetFramework);
        Assert.Equal("R25.1", AutoCadHostCatalog.Default.Series);
        Assert.Equal("AUTOCAD_2026_ROOT", AutoCadHostCatalog.Default.RootEnvironmentVariable);
        Assert.Equal(@"C:\Program Files\Autodesk\AutoCAD 2026", AutoCadHostCatalog.Default.DefaultInstallDirectory);

        foreach (var host in AutoCadHostCatalog.All)
        {
            Assert.Equal(host.Series, host.SeriesMin);
            Assert.Equal(host.Series, host.SeriesMax);
            Assert.Equal($"AUTOCAD_{host.Year}_ROOT", host.RootEnvironmentVariable);
            Assert.Equal($@"C:\Program Files\Autodesk\AutoCAD {host.Year}", host.DefaultInstallDirectory);
            Assert.StartsWith("R", host.Series);
            if (host.Year is 2025 or 2026)
            {
                Assert.Equal("net8.0-windows", host.TargetFramework);
            }
            else
            {
                Assert.StartsWith("net4", host.TargetFramework);
                Assert.DoesNotContain("net8", host.TargetFramework);
            }
        }

        Assert.Equal("net462", AutoCadHostCatalog.ByYear(2017).TargetFramework);
        Assert.Equal("net462", AutoCadHostCatalog.ByYear(2018).TargetFramework);
        Assert.Equal("net47", AutoCadHostCatalog.ByYear(2019).TargetFramework);
        Assert.Equal("net47", AutoCadHostCatalog.ByYear(2020).TargetFramework);
        Assert.All(new[] { 2021, 2022, 2023, 2024 }, year => Assert.Equal("net48", AutoCadHostCatalog.ByYear(year).TargetFramework));
        Assert.Contains("not claimed to NETLOAD", AutoCadHostCatalog.ByYear(2017).LoadNote, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verified", AutoCadHostCatalog.ByYear(2026).LoadNote, StringComparison.OrdinalIgnoreCase);
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

        var feature = AutoCadHostCatalog.UnsupportedFeature("cmd-2", 2017, "forge_exec_dotnet", 2019);
        Assert.False(feature.Ok);
        Assert.Equal("autocad_version_unsupported", feature.Error!.Code);
        Assert.Contains("2017", feature.Error.Message, StringComparison.Ordinal);
        Assert.Contains("forge_exec_dotnet", feature.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageContentsHasOneNonOverlappingRuntimePerYear()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "plugin-bundle", "765T-Forge.bundle", "PackageContents.xml"));
        var components = doc.Descendants("Components").ToList();
        Assert.Equal(AutoCadHostCatalog.All.Count, components.Count);

        var seen = new HashSet<int>();
        foreach (var component in components)
        {
            var requirement = Assert.Single(component.Elements("RuntimeRequirements"));
            var entry = Assert.Single(component.Elements("ComponentEntry"));
            var seriesMin = requirement.Attribute("SeriesMin")!.Value;
            var seriesMax = requirement.Attribute("SeriesMax")!.Value;
            Assert.Equal(seriesMin, seriesMax);
            Assert.Equal("Win64", requirement.Attribute("OS")!.Value);
            Assert.Equal("AutoCAD*", requirement.Attribute("Platform")!.Value);

            var host = Assert.Single(AutoCadHostCatalog.All.Where(item => item.Series == seriesMin));
            var module = entry.Attribute("ModuleName")!.Value;
            Assert.Equal($"./Contents/Windows/{host.Year}/Forge.Plugin.dll", module);
            Assert.True(seen.Add(SeriesKey(seriesMin)));
        }

        Assert.Equal(AutoCadHostCatalog.All.Count, seen.Count);
        Assert.Equal(
            AutoCadHostCatalog.All.Select(host => host.Series).OrderBy(value => value).ToArray(),
            components.Select(component => component.Elements("RuntimeRequirements").Single().Attribute("SeriesMin")!.Value).OrderBy(value => value).ToArray());
    }

    [Fact]
    public void PluginProjectEncodesTheSameYearTable()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Forge.Plugin", "Forge.Plugin.csproj"));
        Assert.Contains("<AutoCadYear Condition=\"'$(AutoCadYear)' == ''\">2026</AutoCadYear>", text);
        Assert.Contains("bin\\$(Configuration)\\autocad-$(AutoCadYear)\\", text);
        Assert.Contains("$(AutoCadRoot)\\AcCoreMgd.dll", text);
        Assert.Contains("<Private>false</Private>", text);
        Assert.DoesNotContain("net9.0", text);

        foreach (var host in AutoCadHostCatalog.All)
        {
            Assert.Contains($"'$(AutoCadYear)' == '{host.Year}'", text);
            Assert.Contains($">{host.TargetFramework}<", text);
            Assert.Contains(host.RootEnvironmentVariable, text);
            Assert.Contains($">{host.Series}<", text);
        }
    }

    private static int SeriesKey(string series)
    {
        Assert.True(AutoCadHostCatalog.TryParseAcadVer(series.Substring(1), out var major, out var minor));
        return (major * 100) + minor;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "765T-Forge.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
