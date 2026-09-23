using System.Text.Json;
using Forge.Server;
using Forge.Shared;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Forge.Tests;

public sealed class P0ContractTests
{
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public void BusinessFailureBecomesCallToolErrorWithoutDroppingForgeResult()
    {
        var forge = ForgeResult.Failure("id", "plot_probe_failed", "Plot wrote a file but the PDF probe failed.", "Inspect the probe.");
        var structured = JsonSerializer.SerializeToElement(forge, CamelCase);
        var call = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "raw json" }],
            StructuredContent = structured
        };

        var marked = ForgeCallToolResults.MarkBusinessFailure(call);

        Assert.True(marked.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(marked.Content)).Text;
        Assert.Contains("plot_probe_failed", text);
        Assert.Contains("Plot wrote a file but the PDF probe failed.", text);
        Assert.Contains("Inspect the probe.", text);
        Assert.Equal(structured.GetRawText(), marked.StructuredContent!.Value.GetRawText());
    }

    [Fact]
    public void SuccessfulForgeResultStaysASuccessfulCall()
    {
        var forge = ForgeResult.Success("id", new { dryRun = true });
        var structured = JsonSerializer.SerializeToElement(forge, CamelCase);
        var call = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "ok" }],
            StructuredContent = structured
        };

        var marked = ForgeCallToolResults.MarkBusinessFailure(call);

        Assert.True(marked.IsError is not true);
        Assert.Equal("ok", Assert.IsType<TextContentBlock>(Assert.Single(marked.Content)).Text);
    }

    [Fact]
    public void BusinessFailureInTextJsonBecomesCallToolErrorAndKeepsForgeResult()
    {
        var forge = ForgeResult.Failure("id", "qa_failed", "Titleblock verification failed.", "Fix the tags.", data: new { passed = false });
        var json = JsonSerializer.Serialize(forge, CamelCase);
        var call = new CallToolResult
        {
            Content = [new TextContentBlock { Text = json }]
        };

        var marked = ForgeCallToolResults.MarkBusinessFailure(call);

        Assert.True(marked.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(marked.Content)).Text;
        Assert.Contains("qa_failed", text);
        Assert.Contains("Titleblock verification failed.", text);
        Assert.Contains("Fix the tags.", text);
        Assert.True(marked.StructuredContent?.TryGetProperty("ok", out var ok) is true && ok.ValueKind == JsonValueKind.False);
        Assert.True(marked.StructuredContent?.TryGetProperty("data", out var data) is true && data.TryGetProperty("passed", out var passed) && passed.ValueKind == JsonValueKind.False);
    }

    [Fact]
    public void PlainTextSuccessIsLeftAlone()
    {
        var call = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "not json" }]
        };

        var marked = ForgeCallToolResults.MarkBusinessFailure(call);

        Assert.True(marked.IsError is not true);
        Assert.Equal("not json", Assert.IsType<TextContentBlock>(Assert.Single(marked.Content)).Text);
        Assert.True(marked.StructuredContent is null);
    }

    [Fact]
    public void PascalCaseForgeResultIsAlsoMarked()
    {
        var call = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "{\"Id\":\"x\",\"Ok\":false,\"Error\":{\"Code\":\"deny_sysvar\",\"Message\":\"blocked\"}}" }]
        };

        var marked = ForgeCallToolResults.MarkBusinessFailure(call);

        Assert.True(marked.IsError);
        Assert.Contains("deny_sysvar", Assert.IsType<TextContentBlock>(Assert.Single(marked.Content)).Text);
    }

    [Fact]
    public void ServerAnnouncesNameAndInstructions()
    {
        var options = new McpServerOptions();
        ForgeMcpHost.ApplyServerOptions(options);

        Assert.Equal("765T-Forge", options.ServerInfo!.Name);
        Assert.Equal(ForgeConstants.ProductVersion, options.ServerInfo.Version);
        Assert.Contains("forge_system_health", options.ServerInstructions);
        Assert.Contains("forge_registry_lookup", options.ServerInstructions);
        Assert.Contains("handle", options.ServerInstructions);
        Assert.Contains("dryRun", options.ServerInstructions);
        Assert.Contains("force", options.ServerInstructions);
        Assert.Contains("FORGE_ALLOW_FORCE_PUBLISH", options.ServerInstructions);
        Assert.Contains("read back", options.ServerInstructions);
        Assert.DoesNotContain("denylist", options.ServerInstructions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blocklist", options.ServerInstructions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProgramWiresTheCallToolResultFilter()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Forge.Server", "Program.cs"));
        Assert.Contains("AddMcpServer(ForgeMcpHost.ApplyServerOptions)", source, StringComparison.Ordinal);
        Assert.Contains("AddCallToolFilter", source, StringComparison.Ordinal);
        Assert.Contains("ForgeCallToolResults.MarkBusinessFailure", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PipeSuggestionsDoNotHardcodeAutoCad2026()
    {
        var connect = "NETLOAD the Forge.Plugin.dll built for the same year as the running AutoCAD, then call forge_system_health again.";
        var down = "Open the AutoCAD release that matches the Forge.Plugin.dll build, NETLOAD that DLL, and verify the MCP_STATUS command.";
        Assert.DoesNotContain("2026", connect);
        Assert.DoesNotContain("2026", down);

        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Forge.Server", "ForgePipeClient.cs"));
        Assert.Contains(connect, source, StringComparison.Ordinal);
        Assert.Contains(down, source, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoCAD 2026", source, StringComparison.Ordinal);
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
