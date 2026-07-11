using Forge.Shared;

namespace Forge.Tests;

public sealed class EnvironmentAndQaTests
{
    [Fact]
    public void FromProcessRequiresTokenUnlessDevEscapeHatch()
    {
        var previousToken = Environment.GetEnvironmentVariable("FORGE_AUTOCAD_TOKEN");
        var previousMcp = Environment.GetEnvironmentVariable("MCP_AUTOCAD_TOKEN");
        var previousDev = Environment.GetEnvironmentVariable("FORGE_DEV_ALLOW_DEFAULT_TOKEN");

        try
        {
            Environment.SetEnvironmentVariable("FORGE_AUTOCAD_TOKEN", null);
            Environment.SetEnvironmentVariable("MCP_AUTOCAD_TOKEN", null);
            Environment.SetEnvironmentVariable("FORGE_DEV_ALLOW_DEFAULT_TOKEN", null);

            Assert.Throws<InvalidOperationException>(() => ForgeEnvironment.FromProcess());

            Environment.SetEnvironmentVariable("FORGE_DEV_ALLOW_DEFAULT_TOKEN", "true");
            var env = ForgeEnvironment.FromProcess();
            Assert.True(env.UsingDevDefaultToken);
            Assert.Equal(ForgeConstants.DevOnlyInsecureToken, env.Token);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORGE_AUTOCAD_TOKEN", previousToken);
            Environment.SetEnvironmentVariable("MCP_AUTOCAD_TOKEN", previousMcp);
            Environment.SetEnvironmentVariable("FORGE_DEV_ALLOW_DEFAULT_TOKEN", previousDev);
        }
    }

    [Fact]
    public void QaReportFailsOnErrorSeverity()
    {
        var report = QaReport.FromFindings("demo.dwg", new[]
        {
            new QaFinding("xref_unhealthy", "error", "missing"),
            new QaFinding("pstyle_mode", "info", "ok")
        });

        Assert.False(report.Passed);
        Assert.Equal(2, report.Findings.Count);
    }

    [Fact]
    public void ProductVersionIs021AndMoatToolsAreRegistered()
    {
        Assert.Equal("0.2.1", ForgeConstants.ProductVersion);
        var names = ForgeToolRegistry.All.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("forge_qa_preflight", names);
        Assert.Contains("forge_recipe_issue_set", names);
        Assert.Contains("forge_pack_and_go", names);
        Assert.Contains("forge_batch_run", names);
        Assert.Contains("forge_system_capabilities", names);
        Assert.Contains("forge_block_campaign", names);
        Assert.Contains("forge_xref_normalize_relative", names);
        Assert.Contains("forge_registry_load", names);
        Assert.Contains("forge_pack_load", names);
        Assert.Contains("forge_viewport_list", names);
        Assert.Contains("forge_qa_plot_fingerprint", names);
        Assert.Contains("forge_xref_pin_verify", names);
        Assert.Contains("forge_transmittal_seal", names);
        Assert.Contains("forge_cde_gate_evaluate", names);
        Assert.False(ForgeToolRegistry.Get("forge_batch_run").OpenWorld);
    }

    [Fact]
    public void DefaultSecretTokenConstantIsGone()
    {
        var constants = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Forge.Shared", "ForgeConstants.cs"));
        Assert.DoesNotContain("default-secret-token", constants);
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
