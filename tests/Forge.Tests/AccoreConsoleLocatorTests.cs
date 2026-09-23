using System.Text.Json;
using Forge.Server;
using Forge.Shared;

namespace Forge.Tests;

public sealed class AccoreConsoleLocatorTests
{
    public static IEnumerable<object[]> CatalogYears()
        => AutoCadHostCatalog.All.Select(host => new object[] { host.Year });

    [Theory]
    [InlineData("*Cancel*", null)]
    [InlineData(null, "Unknown command \"FOO\"")]
    [InlineData("ok\n*Invalid*", "")]
    public void ScriptErrorMarkersFailClosed(string? stdout, string? stderr)
    {
        Assert.True(AccoreConsoleScriptCheck.HasScriptError(stdout, stderr));
    }

    [Fact]
    public void CleanConsoleOutputIsNotAScriptError()
    {
        Assert.False(AccoreConsoleScriptCheck.HasScriptError("Command: ZOOM\n", ""));
    }

    [Fact]
    public void ScriptCheckSourceStatesTheLimitation()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Forge.Server", "AccoreConsoleScriptCheck.cs"));
        Assert.Contains("does NOT prove the script succeeded", source, StringComparison.Ordinal);
        Assert.Contains("forge_qa_readback", source, StringComparison.Ordinal);
        Assert.Contains("OrdinalIgnoreCase", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RunOneAsyncSourceTreatsScriptMarkersAsFailure()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Forge.Server", "HeadlessAccoreConsoleRunner.cs"));
        var start = source.IndexOf("private async Task<ForgeResult> RunOneAsync", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var body = source[start..];
        var marker = body.IndexOf("AccoreConsoleScriptCheck.HasScriptError", StringComparison.Ordinal);
        var success = body.IndexOf("exitCode = process.ExitCode", StringComparison.Ordinal);
        Assert.True(marker >= 0 && success > marker);
        Assert.Contains("AccoreConsoleScriptCheck.ErrorCode", body, StringComparison.Ordinal);
        Assert.Contains("does NOT prove the script succeeded", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ClampJobTimeoutUsesJobValueOrDefault300()
    {
        Assert.Equal(30, HeadlessAccoreConsoleRunner.ClampJobTimeout(30));
        Assert.Equal(300, HeadlessAccoreConsoleRunner.ClampJobTimeout(null));
        Assert.Equal(5, HeadlessAccoreConsoleRunner.ClampJobTimeout(0));
        Assert.Equal(3600, HeadlessAccoreConsoleRunner.ClampJobTimeout(99999));
    }

    [Fact]
    public void BatchRunSourcePassesJobTimeout()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Forge.Server", "HeadlessAccoreConsoleRunner.cs"));
        var start = source.IndexOf("public async Task<ForgeResult> RunBatchAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task<ForgeResult> RunOneAsync", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var body = source[start..end];
        Assert.Contains("JobTimeout(args, index)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("timeoutSeconds: null", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveYearJobBeatsCallBeatsEnvBeatsDiscoveredDefault()
    {
        Assert.Equal(2024, AccoreConsoleLocator.ResolveYear(2024, 2025, "2026", 2027));
        Assert.Equal(2025, AccoreConsoleLocator.ResolveYear(null, 2025, "2026", 2027));
        Assert.Equal(2023, AccoreConsoleLocator.ResolveYear(null, null, "2023", 2027));
        Assert.Equal(2026, AccoreConsoleLocator.ResolveYear(null, null, null, 2026));
        Assert.Equal(2026, AccoreConsoleLocator.ResolveYear(null, null, "nope", 2026));
        Assert.Null(AccoreConsoleLocator.ResolveYear(null, null, null, null));
    }

    [Fact]
    public void LocateNoResolvedYearFailsWithTheEnvironmentVariablesToSet()
    {
        var choice = AccoreConsoleLocator.Locate(null, _ => "C:\\should-not-use", _ => true);
        Assert.False(choice.Found);
        Assert.Equal(AccoreConsoleLocator.NotFoundCode, choice.ErrorCode);
        Assert.Null(choice.ExePath);
        Assert.Contains(AccoreConsoleLocator.YearEnvironmentVariable, choice.Message, StringComparison.Ordinal);
        Assert.Contains("FORGE_AUTOCAD_ROOT", choice.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LocateRequested2024DoesNotUse2026Exe()
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
            Assert.Contains("AUTOCAD_2024_ROOT", choice.Message, StringComparison.Ordinal);
            Assert.Contains("does not fall back", choice.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root2026, recursive: true);
        }
    }

    [Fact]
    public void LocateUsesYearRootWhenExeExists()
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
    public void LocateEachCatalogYearUsesThatYearsRootVariable(int year)
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
    public void LocateYearOutsideMatrixFailsBeforePath()
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
            new ForgeEnvironment
            {
                BackupDirectory = root2024,
                AuditDirectory = root2024,
                AutoCadRoot = root2026,
                AutoCadYear = "2026",
                EnableUnsafeOps = true
            },
            new BackupPlanner(root2024),
            new SafetyPolicy(),
            Read);
        try
        {
            var result = await runner.RunScriptAsync(new ForgeCommand
            {
                Tool = "forge_run_script",
                DryRun = true,
                UnsafeAcknowledged = true,
                Args = ForgeJson.ToElement(new { dwgPath = dwg, scriptPath = scr, autoCadYear = 2024 })
            });
            Assert.True(result.Ok);
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(result.Data));
            var wouldRun = document.RootElement.GetProperty("wouldRun").GetString();
            Assert.Equal(exe2024, wouldRun, ignoreCase: true);
            Assert.NotEqual(Path.Combine(root2026, "accoreconsole.exe"), wouldRun);
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
        var empty2024 = Path.Combine(Path.GetTempPath(), "forge-miss-2024-" + Guid.NewGuid().ToString("N"));
        var work = Path.Combine(Path.GetTempPath(), "forge-miss-work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root2026);
        Directory.CreateDirectory(empty2024);
        Directory.CreateDirectory(work);
        var exe2026 = Path.Combine(root2026, "accoreconsole.exe");
        File.WriteAllBytes(exe2026, []);
        var dwg = Path.Combine(work, "a.dwg");
        var scr = Path.Combine(work, "a.scr");
        await File.WriteAllTextAsync(dwg, "x");
        await File.WriteAllTextAsync(scr, "ZOOM *\n");
        string? Read(string name) => name switch
        {
            "AUTOCAD_2026_ROOT" => root2026,
            "AUTOCAD_2024_ROOT" => empty2024,
            _ => null
        };
        var runner = new HeadlessAccoreConsoleRunner(
            new ForgeEnvironment
            {
                BackupDirectory = work,
                AuditDirectory = work,
                AutoCadRoot = root2026,
                AutoCadYear = "2026",
                EnableUnsafeOps = true
            },
            new BackupPlanner(work),
            new SafetyPolicy(),
            Read);
        try
        {
            var result = await runner.RunScriptAsync(new ForgeCommand
            {
                Tool = "forge_run_script",
                DryRun = true,
                UnsafeAcknowledged = true,
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
            Directory.Delete(empty2024, recursive: true);
            Directory.Delete(work, recursive: true);
        }
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
