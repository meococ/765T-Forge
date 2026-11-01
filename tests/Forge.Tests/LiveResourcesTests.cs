using System.Text.Json;
using Xunit;

namespace Forge.Tests;

/// <summary>
/// Forge MCP core v2 item 6: live drawing resources, workflow prompts, and argument
/// completions. Wire-level over real stdio: resources/list advertises the four live
/// drawing resources; resources/read answers through ForgeToolRunner and degrades to a
/// typed failure body (never a transport error) when no plugin pipe is listening; the
/// advertised completions capability routes completion/complete to the live filter.
/// </summary>
public sealed class LiveResourcesTests
{
    private static string Exe => ToolsetTests.FindServerExe();

    private static async Task<ToolsetTests.McpStdio> SpawnDeadPipeAsync()
    {
        // Unique pipe name + short connect timeout — deterministic "no plugin" path
        // even on seats where a real AutoCAD pipe is listening.
        var env = new Dictionary<string, string>
        {
            ["FORGE_PIPE_NAME"] = $"forge-dead-{Guid.NewGuid():N}",
            ["FORGE_PLUGIN_CONNECT_TIMEOUT_SECONDS"] = "1"
        };
        return await ToolsetTests.McpStdio.SpawnAsync(Exe, toolsets: "all", env: env);
    }

    [Fact]
    public async Task ResourcesList_AdvertisesLiveDrawingResources()
    {
        await using var server = await SpawnDeadPipeAsync();
        var reply = await server.SendAsync("resources/list", new { });
        var resources = reply.GetProperty("result").GetProperty("resources").EnumerateArray().ToArray();

        foreach (var uri in new[]
        {
            "forge://drawing/summary", "forge://drawing/layers",
            "forge://drawing/layouts", "forge://drawing/xrefs"
        })
        {
            var resource = resources.SingleOrDefault(r => r.GetProperty("uri").GetString() == uri);
            Assert.True(resource.ValueKind != JsonValueKind.Undefined, $"missing resource {uri}");
            Assert.False(string.IsNullOrWhiteSpace(resource.GetProperty("title").GetString()));
        }
    }

    [Fact]
    public async Task ResourceRead_DeadPipeReturnsTypedFailureJson()
    {
        await using var server = await SpawnDeadPipeAsync();
        var reply = await server.SendAsync("resources/read", new { uri = "forge://drawing/layouts" });
        var contents = reply.GetProperty("result").GetProperty("contents");
        var body = contents[0].GetProperty("text").GetString()!;

        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        var code = doc.RootElement.TryGetProperty("error", out var err)
            ? (err.TryGetProperty("code", out var c) ? c.GetString() : null)
            : (doc.RootElement.TryGetProperty("code", out var c2) ? c2.GetString() : null);
        Assert.False(string.IsNullOrWhiteSpace(code), $"typed failure body missing a code: {body}");
    }

    [Fact]
    public async Task Completions_ProfileNamesAnswerWithoutPipe()
    {
        await using var server = await SpawnDeadPipeAsync();
        var reply = await server.SendAsync("completion/complete", new
        {
            @ref = new { type = "ref/resource", uri = "forge://profiles/{name}" },
            argument = new { name = "name", value = "" }
        });

        // Method routed (capability advertised) and the static profile list answered.
        var completion = reply.GetProperty("result").GetProperty("completion");
        var values = completion.GetProperty("values").EnumerateArray().Select(v => v.GetString()).ToArray();
        Assert.Contains("core", values);
    }

    [Fact]
    public async Task Completions_PromptArgDeadPipeFallsBackToEmptyResult()
    {
        await using var server = await SpawnDeadPipeAsync();
        var reply = await server.SendAsync("completion/complete", new
        {
            @ref = new { type = "ref/prompt", name = "qa_review" },
            argument = new { name = "layout", value = "" }
        });

        // Live source unreachable → filter yields to the SDK, which still answers a
        // well-formed (possibly empty) completion rather than an error.
        var result = reply.GetProperty("result");
        Assert.True(result.TryGetProperty("completion", out _), $"no completion result: {reply.GetRawText()}");
    }

    [Fact]
    public async Task PromptsList_WorkflowPromptsPresentWithTitles()
    {
        await using var server = await SpawnDeadPipeAsync();
        var reply = await server.SendAsync("prompts/list", new { });
        var prompts = reply.GetProperty("result").GetProperty("prompts").EnumerateArray().ToArray();

        foreach (var name in new[] { "dgn_convert", "publish_set", "drawing_cleanup", "qa_review", "issue_set_runbook", "safety_first" })
        {
            var prompt = prompts.SingleOrDefault(p => p.GetProperty("name").GetString() == name);
            Assert.True(prompt.ValueKind != JsonValueKind.Undefined, $"missing prompt {name}");
            Assert.False(string.IsNullOrWhiteSpace(prompt.GetProperty("title").GetString()));
        }
    }

    [Fact]
    public async Task PromptsGet_QaReviewRendersRealToolNames()
    {
        await using var server = await SpawnDeadPipeAsync();
        var reply = await server.SendAsync("prompts/get", new
        {
            name = "qa_review",
            arguments = new { layout = "C-301" }
        });
        var messages = reply.GetProperty("result").GetProperty("messages");
        var text = messages[0].GetProperty("content").GetProperty("text").GetString()!;

        Assert.Contains("forge_qa_annotation_clash", text);
        Assert.Contains("forge_qa_preflight", text);
        Assert.DoesNotContain("forge_qa_clash", text);
    }
}
