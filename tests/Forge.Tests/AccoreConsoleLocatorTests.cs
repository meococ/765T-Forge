using Forge.Server;
using Forge.Shared;

namespace Forge.Tests;

public sealed class AccoreConsoleLocatorTests
{
    public static IEnumerable<object[]> CatalogYears()
        => AutoCadHostCatalog.All.Select(host => new object[] { host.Year });

    [Fact]
    public void ResolveYear_ArgumentBeatsEnvAndDefaultIs2026()
    {
        Assert.Equal(2024, AccoreConsoleLocator.ResolveYear(2024, 2025, "2026"));
        Assert.Equal(2025, AccoreConsoleLocator.ResolveYear(null, 2025, "2026"));
        Assert.Equal(2023, AccoreConsoleLocator.ResolveYear(null, null, "2023"));
        Assert.Equal(2026, AccoreConsoleLocator.ResolveYear(null, null, null));
        Assert.Equal(2026, AccoreConsoleLocator.ResolveYear(null, null, "nope"));
    }

    [Fact]
    public void Locate_Requested2024DoesNotUse2026Exe()
    {
        var root2026 = Path.Combine(Path.GetTempPath(), "forge-acad-2026-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root2026);
        var exe2026 = Path.Combine(root2026, "accoreconsole.exe");
        File.WriteAllBytes(exe2026, []);
        try
        {
            string? Read(string name) => name == "AUTOCAD_2026_ROOT" ? root2026 : null;
            bool Exists(string path) => string.Equals(path, exe2026, StringComparison.OrdinalIgnoreCase);
            var choice = AccoreConsoleLocator.Locate(2024, Read, Exists);
            Assert.False(choice.Found);
            Assert.Equal(AccoreConsoleLocator.NotFoundCode, choice.ErrorCode);
            Assert.Equal(2024, choice.Year);
            Assert.NotNull(choice.ExePath);
            Assert.Contains("2024", choice.ExePath, StringComparison.Ordinal);
            Assert.NotEqual(exe2026, choice.ExePath);
        }
        finally
        {
            Directory.Delete(root2026, recursive: true);
        }
    }

    [Fact]
    public void Locate_UsesYearRootWhenExeExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "forge-acad-2024-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var exe = Path.Combine(root, "accoreconsole.exe");
        File.WriteAllBytes(exe, []);
        try
        {
            string? Read(string name) => name == "AUTOCAD_2024_ROOT" ? root : null;
            var choice = AccoreConsoleLocator.Locate(2024, Read, File.Exists);
            Assert.True(choice.Found);
            Assert.Equal(exe, choice.ExePath);
            Assert.Null(choice.ErrorCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(CatalogYears))]
    public void Locate_EachCatalogYearUsesThatYearsRootVariable(int year)
    {
        var root = Path.Combine(Path.GetTempPath(), $"forge-acad-year-{year}-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var exe = Path.Combine(root, "accoreconsole.exe");
        File.WriteAllBytes(exe, []);
        try
        {
            var variable = $"AUTOCAD_{year}_ROOT";
            string? Read(string name) => name == variable ? root : null;
            var choice = AccoreConsoleLocator.Locate(year, Read, File.Exists);
            Assert.True(choice.Found);
            Assert.Equal(year, choice.Year);
            Assert.Equal(exe, choice.ExePath);
            if (year != 2026)
            {
                Assert.DoesNotContain("2026", choice.ExePath!, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Locate_YearOutsideMatrixFailsBeforePath()
    {
        var choice = AccoreConsoleLocator.Locate(2016, _ => "C:\\should-not-use", _ => true);
        Assert.False(choice.Found);
        Assert.Equal(AccoreConsoleLocator.YearUnsupportedCode, choice.ErrorCode);
        Assert.Null(choice.ExePath);
    }

    [Fact]
    public async Task RunScriptDryRunUsesRequestedYearNotAutoCadRoot()
    {
        var root2026 = Path.Combine(Path.GetTempPath(), "forge-run-2026-" + Guid.NewGuid().ToString("N"));
        var root2024 = Path.Combine(Path.GetTempPath(), "forge-run-2024-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root2026);
        Directory.CreateDirectory(root2024);
        File.WriteAllBytes(Path.Combine(root2026, "accoreconsole.exe"), []);
        var exe2024 = Path.Combine(root2024, "accoreconsole.exe");
        File.WriteAllBytes(exe2024, []);
        var dwg = Path.Combine(root2024, "a.dwg");
        var scr = Path.Combine(root2024, "a.scr");
        await File.WriteAllTextAsync(dwg, "not a real dwg");
        await File.WriteAllTextAsync(scr, "ZOOM *\n");
        string? Read(string name) => name switch
        {
            "AUTOCAD_2026_ROOT" => root2026,
            "AUTOCAD_2024_ROOT" => root2024,
            _ => null
        };
        var runner = new HeadlessAccoreConsoleRunner(
            new ForgeEnvironment { BackupDirectory = root2024, AuditDirectory = root2024, AutoCadRoot = root2026 },
            new BackupPlanner(root2024),
            new SafetyPolicy(),
            Read);
        try
        {
            var result = await runner.RunScriptAsync(new ForgeCommand
            {
                Tool = "forge_run_script",
                DryRun = true,
                Args = ForgeJson.ToElement(new { dwgPath = dwg, scriptPath = scr, autoCadYear = 2024 })
            });
            Assert.True(result.Ok);
            var json = System.Text.Json.JsonSerializer.Serialize(result.Data);
            Assert.Contains(exe2024, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Path.Combine(root2026, "accoreconsole.exe"), json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root2026, recursive: true);
            Directory.Delete(root2024, recursive: true);
        }
    }

    [Fact]
    public async Task RunScriptDryRunMissing2024DoesNotReport2026()
    {
        var root2026 = Path.Combine(Path.GetTempPath(), "forge-miss-2026-" + Guid.NewGuid().ToString("N"));
        var work = Path.Combine(Path.GetTempPath(), "forge-miss-work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root2026);
        Directory.CreateDirectory(work);
        var exe2026 = Path.Combine(root2026, "accoreconsole.exe");
        File.WriteAllBytes(exe2026, []);
        var dwg = Path.Combine(work, "a.dwg");
        var scr = Path.Combine(work, "a.scr");
        await File.WriteAllTextAsync(dwg, "x");
        await File.WriteAllTextAsync(scr, "ZOOM *\n");
        string? Read(string name) => name == "AUTOCAD_2026_ROOT" ? root2026 : null;
        var runner = new HeadlessAccoreConsoleRunner(
            new ForgeEnvironment { BackupDirectory = work, AuditDirectory = work, AutoCadRoot = root2026 },
            new BackupPlanner(work),
            new SafetyPolicy(),
            Read);
        try
        {
            var result = await runner.RunScriptAsync(new ForgeCommand
            {
                Tool = "forge_run_script",
                DryRun = true,
                Args = ForgeJson.ToElement(new { dwgPath = dwg, scriptPath = scr, autoCadYear = 2024 })
            });
            Assert.False(result.Ok);
            Assert.Equal("accoreconsole_not_found", result.Error!.Code);
            Assert.Contains("2024", result.Error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(exe2026, result.Error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root2026, recursive: true);
            Directory.Delete(work, recursive: true);
        }
    }
}
