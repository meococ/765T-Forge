using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Forge.Shared;
using Xunit;

namespace Forge.Tests;

/// <summary>
/// Forge MCP core v2 item 8 — the CI conformance gate: one scripted client sweep over
/// real stdio exercising the methods a conformant client needs. Any protocol drift
/// (missing capability, renamed method, broken envelope) fails this test by name in
/// the ServerOnly CI job.
/// </summary>
public sealed class McpConformanceTests
{
    private static string Exe => ToolsetTests.FindServerExe();

    /// <summary>
    /// The full sweep: initialize capabilities → tools/list (default AND all) →
    /// resources (+template) → prompts → completion/complete → one tools/call over a
    /// fake plugin pipe. Each stage asserts the wire contract a real client relies on.
    /// </summary>
    [Fact]
    public async Task ConformanceSweep()
    {
        var pipeName = $"forge-conformance-{Guid.NewGuid():N}";
        await using var pipe = new FakePipe(pipeName);
        await using var server = await ToolsetTests.McpStdio.SpawnAsync(
            Exe,
            toolsets: null,
            env: new Dictionary<string, string>
            {
                ["FORGE_PIPE_NAME"] = pipeName,
                ["FORGE_PLUGIN_CONNECT_TIMEOUT_SECONDS"] = "5",
                ["FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS"] = "10"
            },
            capabilities: new { });

        // --- tools/list on the default toolset: bounded budget ---
        var defaultTools = await server.ListToolsAsync();
        Assert.True(defaultTools.Length <= 25, $"default tools/list {defaultTools.Length} > 25");
        Assert.Contains("forge_toolset_enable", defaultTools);

        // --- tools/list with FORGE_TOOLSETS=all: full catalog reachable ---
        await using var allServer = await ToolsetTests.McpStdio.SpawnAsync(
            Exe,
            toolsets: "all",
            env: new Dictionary<string, string> { ["FORGE_PIPE_NAME"] = pipeName });
        var allTools = await allServer.ListToolsAsync();
        Assert.True(allTools.Length >= 120, $"FORGE_TOOLSETS=all listed {allTools.Length} tools, expected the full catalog");
        Assert.Contains("forge_draw_batch", allTools);

        // --- resources/list + resources/templates/list ---
        var resources = (await server.SendAsync("resources/list", new { }))
            .GetProperty("result").GetProperty("resources").EnumerateArray().ToArray();
        Assert.True(resources.Length >= 7, $"expected >=7 resources, got {resources.Length}");
        Assert.All(resources, r => Assert.False(string.IsNullOrWhiteSpace(r.GetProperty("title").GetString())));

        var templates = (await server.SendAsync("resources/templates/list", new { }))
            .GetProperty("result").GetProperty("resourceTemplates").EnumerateArray().ToArray();
        Assert.Contains(templates, t => t.GetProperty("uriTemplate").GetString() == "forge://profiles/{name}");

        // --- prompts/list + prompts/get ---
        var prompts = (await server.SendAsync("prompts/list", new { }))
            .GetProperty("result").GetProperty("prompts").EnumerateArray().ToArray();
        Assert.True(prompts.Length >= 6, $"expected >=6 prompts, got {prompts.Length}");
        Assert.All(prompts, p => Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("title").GetString())));

        var promptGet = (await server.SendAsync("prompts/get", new
        {
            name = "safety_first",
            arguments = new { }
        })).GetProperty("result");
        Assert.True(promptGet.GetProperty("messages").GetArrayLength() >= 1);

        // --- completion/complete (capability must be advertised or SDK rejects) ---
        var complete = (await server.SendAsync("completion/complete", new
        {
            @ref = new { type = "ref/resource", uri = "forge://profiles/{name}" },
            argument = new { name = "name", value = "" }
        })).GetProperty("result").GetProperty("completion");
        Assert.Contains("core", complete.GetProperty("values").EnumerateArray().Select(v => v.GetString()));

        // --- one tool call over the fake pipe: typed success + structured mirror ---
        var reply = await server.CallToolAsync("forge_system_health", "{}");
        using var call = JsonDocument.Parse(reply);
        var result = call.RootElement.GetProperty("result");
        // Success omits isError entirely; only business failures set it (MarkBusinessFailure).
        Assert.True(!result.TryGetProperty("isError", out var isErr) || !isErr.GetBoolean(),
            $"forge_system_health reply: {reply}");
        var structured = result.GetProperty("structuredContent");
        Assert.True(structured.GetProperty("ok").GetBoolean());
        // text mirror carries the same JSON for older clients
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        using var mirrored = JsonDocument.Parse(text);
        Assert.True(mirrored.RootElement.GetProperty("ok").GetBoolean());
        Assert.Single(pipe.Requests); // exactly one round-trip to the plugin pipe
    }

    /// <summary>Minimal loopback plugin: reads one command line, answers ok:true.</summary>
    private sealed class FakePipe : IAsyncDisposable
    {
        public List<JsonElement> Requests { get; } = new();
        private readonly Task _loop;
        private readonly CancellationTokenSource _stop = new();

        public FakePipe(string pipeName)
            => _loop = Task.Run(() => ServeAsync(pipeName));

        private async Task ServeAsync(string pipeName)
        {
            await using var stream = new NamedPipeServerStream(
                pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await stream.WaitForConnectionAsync(_stop.Token);
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                using var doc = JsonDocument.Parse(line);
                Requests.Add(doc.RootElement.Clone());
                var id = doc.RootElement.GetProperty("id").GetString()!;
                await writer.WriteLineAsync(JsonSerializer.Serialize(
                    ForgeResult.Success(id, new { pipe = "fake", status = "healthy" }), ForgeJson.Options));
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            try { await _loop; } catch { }
            _stop.Dispose();
        }
    }
}
