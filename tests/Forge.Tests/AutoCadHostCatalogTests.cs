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

        Assert.Equal("net46", AutoCadHostCatalog.ByYear(2017).TargetFramework);
        Assert.Equal("4.6", AutoCadHostCatalog.ByYear(2017).DocumentedClr);
        Assert.Equal("net46", AutoCadHostCatalog.ByYear(2018).TargetFramework);
        Assert.Equal("4.6", AutoCadHostCatalog.ByYear(2018).DocumentedClr);
        Assert.Equal("net47", AutoCadHostCatalog.ByYear(2019).TargetFramework);
        Assert.Equal("4.7", AutoCadHostCatalog.ByYear(2019).DocumentedClr);
        Assert.Equal("net47", AutoCadHostCatalog.ByYear(2020).TargetFramework);
        Assert.All(new[] { 2021, 2022, 2023, 2024 }, year =>
        {
            Assert.Equal("net48", AutoCadHostCatalog.ByYear(year).TargetFramework);
            Assert.Equal("4.8", AutoCadHostCatalog.ByYear(year).DocumentedClr);
        });
        Assert.Contains("net46", AutoCadHostCatalog.ByYear(2017).LoadNote, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Newtonsoft", AutoCadHostCatalog.ByYear(2017).LoadNote, StringComparison.Ordinal);
        Assert.Contains("not smoke-tested", AutoCadHostCatalog.ByYear(2018).LoadNote, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4.6", AutoCadHostCatalog.ByYear(2018).LoadNote, StringComparison.Ordinal);
        Assert.DoesNotContain("net462", AutoCadHostCatalog.ByYear(2017).LoadNote, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verified", AutoCadHostCatalog.ByYear(2026).LoadNote, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("net462", string.Join("\n", AutoCadHostCatalog.All.Select(host => host.TargetFramework)), StringComparison.Ordinal);
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
    public void FeatureGatesMatchHostClrAndKeep2026PublishAndUndo()
    {
        Assert.Equal(
            new[] { AutoCadHostCatalog.ExecDotNetFeature, AutoCadHostCatalog.PublishDsdFeature, AutoCadHostCatalog.UndoMarkFeature },
            AutoCadHostCatalog.FeatureGates.Select(gate => gate.Feature).ToArray());

        foreach (var year in new[] { 2017, 2018 })
        {
            Assert.False(AutoCadHostCatalog.SupportsFeature(year, AutoCadHostCatalog.ExecDotNetFeature));
            var blocked = AutoCadHostCatalog.TryUnsupported("cmd", year, AutoCadHostCatalog.ExecDotNetFeature);
            Assert.NotNull(blocked);
            Assert.False(blocked!.Ok);
            Assert.Equal("autocad_version_unsupported", blocked.Error!.Code);
            Assert.Contains(year.ToString(), blocked.Error.Message, StringComparison.Ordinal);
            Assert.Contains(AutoCadHostCatalog.ExecDotNetFeature, blocked.Error.Message, StringComparison.Ordinal);
            Assert.Contains("2019", blocked.Error.Suggestion, StringComparison.Ordinal);
        }

        foreach (var year in Enumerable.Range(2019, 8))
        {
            Assert.Null(AutoCadHostCatalog.TryUnsupported("cmd", year, AutoCadHostCatalog.ExecDotNetFeature));
        }

        foreach (var host in AutoCadHostCatalog.All)
        {
            Assert.Null(AutoCadHostCatalog.TryUnsupported("cmd", host.Year, AutoCadHostCatalog.PublishDsdFeature));
            Assert.Null(AutoCadHostCatalog.TryUnsupported("cmd", host.Year, AutoCadHostCatalog.UndoMarkFeature));
            Assert.True(AutoCadHostCatalog.SupportsFeature(host.Year, AutoCadHostCatalog.PublishDsdFeature));
            Assert.True(AutoCadHostCatalog.SupportsFeature(host.Year, AutoCadHostCatalog.UndoMarkFeature));
        }

        var ancientPublish = AutoCadHostCatalog.TryUnsupported("cmd", 2016, AutoCadHostCatalog.PublishDsdFeature);
        Assert.NotNull(ancientPublish);
        Assert.Equal("autocad_version_unsupported", ancientPublish!.Error!.Code);
        Assert.Contains("2016", ancientPublish.Error.Message, StringComparison.Ordinal);
        Assert.Contains(AutoCadHostCatalog.PublishDsdFeature, ancientPublish.Error.Message, StringComparison.Ordinal);
        Assert.Contains("Roslyn", AutoCadHostCatalog.FeatureGates.Single(gate => gate.Feature == AutoCadHostCatalog.ExecDotNetFeature).Note, StringComparison.Ordinal);
        Assert.Contains("UNDO", AutoCadHostCatalog.FeatureGates.Single(gate => gate.Feature == AutoCadHostCatalog.UndoMarkFeature).Note, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageContentsFilterOmitsYearsThatWereNotBuilt()
    {
        var template = File.ReadAllText(Path.Combine(RepoRoot(), "plugin-bundle", "765T-Forge.bundle", "PackageContents.xml"));
        var only2026 = XDocument.Parse(AutoCadHostCatalog.FilterPackageContentsXml(template, new[] { 2026 }));
        var component = Assert.Single(only2026.Descendants("Components"));
        Assert.Equal("R25.1", component.Element("RuntimeRequirements")!.Attribute("SeriesMin")!.Value);
        Assert.Equal("./Contents/Windows/2026/Forge.Plugin.dll", component.Element("ComponentEntry")!.Attribute("ModuleName")!.Value);
        Assert.Contains("CompanyDetails", only2026.Root!.Elements().Select(element => element.Name.LocalName));

        var two = XDocument.Parse(AutoCadHostCatalog.FilterPackageContentsXml(template, new[] { 2017, 2024 }));
        Assert.Equal(
            new[] { "./Contents/Windows/2017/Forge.Plugin.dll", "./Contents/Windows/2024/Forge.Plugin.dll" },
            two.Descendants("ComponentEntry").Select(entry => entry.Attribute("ModuleName")!.Value).ToArray());

        var none = XDocument.Parse(AutoCadHostCatalog.FilterPackageContentsXml(template, Array.Empty<int>()));
        Assert.Empty(none.Descendants("Components"));
    }

    [Fact]
    public void SharedProjectAndBundleScriptsFollowTheCatalog()
    {
        var root = RepoRoot();
        var shared = File.ReadAllText(Path.Combine(root, "src", "Forge.Shared", "Forge.Shared.csproj"));
        Assert.Contains("net8.0;net48;net47;net46", shared);
        Assert.DoesNotContain("net462", shared);
        Assert.Contains("Newtonsoft.Json", shared);
        Assert.Contains("SystemTextJsonNet46.cs", shared);

        foreach (var script in new[] { "pack-release.ps1", "install-plugin.ps1", "smoke-lab.ps1" })
        {
            var text = File.ReadAllText(Path.Combine(root, "scripts", script));
            Assert.Contains("ForgeBundleLayout.ps1", text);
            Assert.Contains("Sync-ForgePackageContents", text);
            Assert.DoesNotContain("Copy-Item (Join-Path $root \"plugin-bundle\\765T-Forge.bundle\\PackageContents.xml\")", text);
        }

        var layout = File.ReadAllText(Path.Combine(root, "scripts", "ForgeBundleLayout.ps1"));
        Assert.Contains("Windows/(\\d+)/Forge\\.Plugin\\.dll", layout);
        var smoke = File.ReadAllText(Path.Combine(root, "scripts", "smoke-lab.ps1"));
        Assert.Contains("AutoCAD 2026 only", smoke);
        Assert.Contains("does not build, NETLOAD, or exercise 2017-2025", smoke);
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
