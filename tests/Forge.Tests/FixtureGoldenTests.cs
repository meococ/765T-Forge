using System.Text.Json;
using Forge.Shared;

namespace Forge.Tests;

public sealed class FixtureGoldenTests
{
    [Fact]
    public void HealthFixtureHasOkStatus()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "health-ok.json");
        if (!File.Exists(path))
        {
            path = FindRepoFile("tests/Forge.Tests/Fixtures/health-ok.json");
        }

        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void PreflightFailFixtureIsNotPassed()
    {
        var path = FindRepoFile("tests/Forge.Tests/Fixtures/preflight-fail.json");
        var report = JsonSerializer.Deserialize<QaReport>(File.ReadAllText(path), ForgeJson.Options);
        Assert.NotNull(report);
        Assert.False(report!.Passed);
        Assert.Contains(report.Findings, f => f.Code == "titleblock_tag_missing");
    }

    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(relative);
    }
}
