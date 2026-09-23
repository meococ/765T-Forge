using System.Text.Json;
using System.Xml.Linq;

namespace Forge.Tests;

/// <summary>
/// Guards the single source of version truth. The product version is duplicated as data in
/// Directory.Build.props, plugin-bundle/765T-Forge.bundle/PackageContents.xml and
/// .mcp/server.json; this test makes drift between those copies deterministic instead of
/// relying on release-time proofreading.
/// </summary>
public sealed class VersionConsistencyTests
{
    [Fact]
    public void ProductVersionIsIdenticalAcrossBuildBundleAndMcpManifests()
    {
        var root = RepoRoot();

        var buildPropsPath = Path.Combine(root, "Directory.Build.props");
        var buildPropsText = File.ReadAllText(buildPropsPath);
        var version = XDocument.Parse(buildPropsText).Descendants("Version").Single().Value;

        var bundlePath = Path.Combine(root, "plugin-bundle", "765T-Forge.bundle", "PackageContents.xml");
        var bundleText = File.ReadAllText(bundlePath);
        var appVersion = XDocument.Parse(bundleText)
            .Descendants("ApplicationPackage")
            .Single()
            .Attribute("AppVersion")?.Value ?? "";
        Assert.Equal(version, appVersion);
        Assert.Contains($"AppVersion=\"{version}\"", bundleText, StringComparison.Ordinal);

        var mcpPath = Path.Combine(root, ".mcp", "server.json");
        var mcpText = File.ReadAllText(mcpPath);
        using var mcp = JsonDocument.Parse(mcpText);
        var mcpVersion = mcp.RootElement.GetProperty("version").GetString() ?? "";
        Assert.Equal(version, mcpVersion);

        var mcpPackageVersion = mcp.RootElement
            .GetProperty("packages")[0]
            .GetProperty("version")
            .GetString() ?? "";
        Assert.Equal(version, mcpPackageVersion);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
