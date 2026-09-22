#pragma warning disable MCPEXP001
using System.Reflection;
using System.Text.Json;
using Forge.Server;
using Forge.Shared;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Forge.Tests;

public sealed class P0ContractTests
{
    [Fact]
    public void BackupPathStaysBesidePayloadFields()
    {
        var merged = ForgeResultPayload.MergeBackup(new { completed = true, queued = false }, @"D:\backups\A101.dwg");
        var json = JsonSerializer.Serialize(merged, ForgeJson.Options);

        Assert.Contains("\"completed\":true", json);
        Assert.Contains("\"queued\":false", json);
        Assert.Contains("\"backupPath\":", json);
        Assert.DoesNotContain("\"data\":", json);
    }

    [Fact]
    public void GateFailureKeepsReportAndClearsOk()
    {
        var report = new { passed = false, diffs = new[] { "REV" } };
        var result = ForgeResult.Gate("id", false, "qa_failed", "Titleblock verification failed.", "Fix the tags.", report);

        Assert.False(result.Ok);
        Assert.Equal("qa_failed", result.Error!.Code);
        Assert.Same(report, result.Data);
        Assert.False(result.Verification!.Passed);
        Assert.DoesNotContain("force", result.Error.Suggestion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BusinessFailureBecomesCallToolErrorWithoutDroppingForgeResult()
    {
        var forge = ForgeResult.Failure("id", "deny_erase_all", "Blocked destructive ERASE ALL command.", "Use handles.");
        var structured = JsonSerializer.SerializeToElement(forge, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var call = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "raw json" }],
            StructuredContent = structured
        };

        var marked = ForgeCallToolResults.MarkBusinessFailure(call);

        Assert.True(marked.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(marked.Content)).Text;
        Assert.Contains("deny_erase_all", text);
        Assert.Contains("Blocked destructive ERASE ALL command.", text);
        Assert.Contains("Use handles.", text);
        Assert.Equal(structured.GetRawText(), marked.StructuredContent!.Value.GetRawText());
    }

    [Fact]
    public void SuccessfulForgeResultStaysASuccessfulCall()
    {
        var forge = ForgeResult.Success("id", new { dryRun = true });
        var structured = JsonSerializer.SerializeToElement(forge, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
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
        var json = JsonSerializer.Serialize(forge, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
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
        Assert.Contains("read back", options.ServerInstructions);
    }

    [Fact]
    public void EveryToolForbidsTaskSupport()
    {
        var methods = typeof(ForgeMcpTools)
            .GetMethods()
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name is not null);

        foreach (var method in methods)
        {
            var tool = McpServerTool.Create(method);
            Assert.Equal(ToolTaskSupport.Forbidden, tool.ProtocolTool.Execution?.TaskSupport);
        }
    }

    [Fact]
    public void PipeSuggestionsDoNotHardcodeAutoCad2026()
    {
        var connect = "NETLOAD the Forge.Plugin.dll built for the same year as the running AutoCAD, then call forge_system_health again.";
        var down = "Open the AutoCAD release that matches the Forge.Plugin.dll build, NETLOAD that DLL, and verify the MCP_STATUS command.";
        Assert.DoesNotContain("2026", connect);
        Assert.DoesNotContain("2026", down);

        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Forge.Server", "ForgePipeClient.cs"));
        Assert.Contains(connect, source);
        Assert.Contains(down, source);
        Assert.DoesNotContain("AutoCAD 2026", source);
    }

    [Fact]
    public async Task BatchDryRunScansScriptBody()
    {
        var root = Path.Combine(Path.GetTempPath(), "forge-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var badScript = Path.Combine(root, "bad.scr");
        var okScript = Path.Combine(root, "ok.scr");
        await File.WriteAllTextAsync(badScript, "ERASE\nALL\n");
        await File.WriteAllTextAsync(okScript, "ZOOM *\n");
        var runner = new HeadlessAccoreConsoleRunner(
            new ForgeEnvironment { BackupDirectory = root, AuditDirectory = root, AutoCadRoot = root },
            new BackupPlanner(root),
            new SafetyPolicy());

        try
        {
            var denied = await runner.RunBatchAsync(new ForgeCommand
            {
                Tool = "forge_batch_run",
                DryRun = true,
                Args = ForgeJson.ToElement(new { jobs = new[] { new { dwgPath = "missing.dwg", scriptPath = badScript } } })
            });
            Assert.False(denied.Ok);
            Assert.StartsWith("deny_", denied.Error!.Code);

            var allowed = await runner.RunBatchAsync(new ForgeCommand
            {
                Tool = "forge_batch_run",
                DryRun = true,
                Args = ForgeJson.ToElement(new { jobs = new[] { new { dwgPath = "missing.dwg", scriptPath = okScript } } })
            });
            Assert.True(allowed.Ok);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
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
