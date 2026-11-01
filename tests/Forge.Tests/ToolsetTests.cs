using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Forge.Server;
using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// Forge MCP core v2 item 1: toolsets bound the tools/list context budget. Covers the
/// group→set mapping (registry-derived, cannot drift), session parsing/enablement, and the
/// wire-level budget: default tools/list must fit ≤25 tools / ≤32 KiB (~8k tokens at 4 B/tok),
/// while FORGE_TOOLSETS=all exposes the full catalog plus the always-on meta tools.
/// </summary>
public sealed class ToolsetTests
{
    [Fact]
    public void EveryRegistryGroupMapsToAToolset()
    {
        var unmapped = ForgeToolRegistry.All
            .Select(t => t.Group)
            .Where(g => !string.Equals(g, ForgeToolsets.Meta, StringComparison.OrdinalIgnoreCase))
            .Where(g => ForgeToolsets.ToolsetForGroup(g) is null)
            .Distinct()
            .ToArray();
        Assert.True(unmapped.Length == 0, "Registry groups with no toolset: " + string.Join(", ", unmapped));
    }

    [Fact]
    public void EveryToolBelongsToExactlyOneSetAndMetaIsAlwaysOn()
    {
        foreach (var tool in ForgeToolRegistry.All)
        {
            var set = ForgeToolsets.ToolsetForTool(tool.Name);
            Assert.NotNull(set);
            if (tool.Group == ForgeToolsets.Meta)
            {
                Assert.Equal(ForgeToolsets.Meta, set);
            }
            else
            {
                Assert.Contains(set, ForgeToolsets.Names);
            }
        }

        Assert.Equal(3, ForgeToolsets.ToolsInSet(ForgeToolsets.Meta).Count);
    }

    [Fact]
    public void ParseSelection_Defaults_Core_All_And_Unknowns()
    {
        Assert.Equal(["core"], ForgeToolsetState.ParseSelection(null));
        Assert.Equal(["core"], ForgeToolsetState.ParseSelection(""));
        Assert.Equal(["core"], ForgeToolsetState.ParseSelection("  "));
        Assert.Equal(ForgeToolsets.Names.OrderBy(x => x), ForgeToolsetState.ParseSelection("all").OrderBy(x => x));
        Assert.Equal(["draw"], ForgeToolsetState.ParseSelection("draw"));
        Assert.Equal(["draw", "edit"], ForgeToolsetState.ParseSelection("draw, edit,draw").OrderBy(x => x));
        // Unknown names are dropped, not fatal — a typo must not take the session down.
        Assert.Equal(["draw"], ForgeToolsetState.ParseSelection("draw,bogus"));
        Assert.Empty(ForgeToolsetState.ParseSelection("bogus"));
    }

    [Fact]
    public void MetaToolsStayEnabledUnderEverySelection()
    {
        foreach (var selection in new[] { null, "core", "draw", "bogus", "" })
        {
            var state = new ForgeToolsetState(selection);
            Assert.True(state.IsEnabled("forge_toolset_list"), selection);
            Assert.True(state.IsEnabled("forge_toolset_enable"), selection);
            Assert.True(state.IsEnabled("forge_toolset_search"), selection);
        }
    }

    [Fact]
    public void EnableAddsSetAndRejectsUnknown()
    {
        var state = new ForgeToolsetState(null);
        Assert.True(state.IsEnabled("forge_system_health"));
        Assert.False(state.IsEnabled("forge_draw_line"));

        Assert.False(state.Enable("bogus"));
        Assert.False(state.IsEnabled("forge_draw_line"));

        Assert.True(state.Enable("draw"));
        Assert.True(state.IsEnabled("forge_draw_line"));
        Assert.Contains("draw", state.Enabled);
    }

    /// <summary>
    /// Wire-level budget: spawn the built server, initialize, tools/list. Default must be
    /// ≤25 tools and ≤32,768 bytes; FORGE_TOOLSETS=all must expose the whole registry.
    /// </summary>
    [Fact]
    public async Task DefaultToolsListFitsBudgetAndAllExposesFullCatalog()
    {
        var exe = FindServerExe();
        var (defaultCount, defaultBytes) = await ListToolsAsync(exe, toolsets: null);
        var (allCount, allBytes) = await ListToolsAsync(exe, toolsets: "all");

        Assert.True(defaultCount <= 25, $"default tools/list returned {defaultCount} tools (>25)");
        Assert.True(defaultBytes <= 32_768, $"default tools/list is {defaultBytes:N0} bytes (>32 KiB ≈ 8k tokens)");
        Assert.Equal(ForgeToolRegistry.All.Count, allCount);
        Assert.True(allBytes > defaultBytes, "FORGE_TOOLSETS=all should return a larger list than the default");
    }

    /// <summary>forge_toolset_enable mutates the session and reports toolsListChanged.</summary>
    [Fact]
    public async Task ToolsetEnableTurnsSetOnOverTheWire()
    {
        var exe = FindServerExe();
        await using var server = await McpStdio.SpawnAsync(exe, toolsets: null);
        var before = await server.ListToolsAsync();
        Assert.DoesNotContain("forge_draw_line", before);

        var reply = await server.CallToolAsync("forge_toolset_enable", """{"names":["draw"]}""");
        using var doc = JsonDocument.Parse(reply);
        // Success results carry structuredContent plus the same JSON mirrored as text
        // (core v2 item 2); assert both surfaces stay populated and consistent.
        var result = doc.RootElement.GetProperty("result");
        var structured = result.GetProperty("structuredContent");
        Assert.True(structured.GetProperty("ok").GetBoolean());
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        using var body = JsonDocument.Parse(text);
        Assert.True(body.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(body.RootElement.GetProperty("data").GetProperty("toolsListChanged").GetBoolean());

        var after = await server.ListToolsAsync();
        Assert.Contains("forge_draw_line", after);
    }

    /// <summary>A disabled tool is refused with tool_not_enabled instead of reaching the pipe.</summary>
    [Fact]
    public async Task DisabledToolCallIsRefused()
    {
        var exe = FindServerExe();
        await using var server = await McpStdio.SpawnAsync(exe, toolsets: null);
        var reply = await server.CallToolAsync("forge_draw_line", """{"points":[[0,0],[1,1]]}""");
        using var doc = JsonDocument.Parse(reply);
        var result = doc.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        var structured = result.GetProperty("structuredContent");
        Assert.Equal("tool_not_enabled", structured.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("draw", structured.GetProperty("data").GetProperty("toolset").GetString());
    }

    internal static string FindServerExe()
    {
        var bin = PluginDispatchSyncTests.RepoPath("src", "Forge.Server", "bin");
        foreach (var config in new[] { "Debug", "Release" })
        {
            var candidate = Path.Combine(bin, config, "net8.0", "Forge.Server.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Forge.Server.exe not found under {bin} — build the server project first.");
    }

    private static async Task<(int Count, int Bytes)> ListToolsAsync(string exe, string? toolsets)
    {
        await using var server = await McpStdio.SpawnAsync(exe, toolsets);
        var tools = await server.ListToolsRawAsync();
        var count = tools.GetProperty("tools").GetArrayLength();
        return (count, tools.GetRawText().Length);
    }

    /// <summary>
    /// Minimal newline-delimited JSON-RPC stdio client — one request/response at a time,
    /// bounded reads, server killed on dispose. stdout carries only JSON-RPC; logs go to stderr.
    /// </summary>
    internal sealed class McpStdio : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly StreamWriter _stdin;
        private int _nextId;

        /// <summary>
        /// Answers server→client requests (elicitation/create etc.): returns the JSON-RPC
        /// result object for the request, or null to answer a protocol error. Tests set it.
        /// </summary>
        public Func<int, string, JsonElement, object?>? ServerRequestHandler { get; set; }

        private McpStdio(Process process, StreamWriter stdin)
        {
            _process = process;
            _stdin = stdin;
        }

        public static async Task<McpStdio> SpawnAsync(
            string exe,
            string? toolsets,
            IReadOnlyDictionary<string, string>? env = null,
            object? capabilities = null)
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.Environment["FORGE_DEV_ALLOW_DEFAULT_TOKEN"] = "true";
            if (toolsets is not null)
            {
                psi.Environment["FORGE_TOOLSETS"] = toolsets;
            }

            if (env is not null)
            {
                foreach (var (key, value) in env)
                {
                    psi.Environment[key] = value;
                }
            }

            var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start Forge.Server.");
            _ = DrainAsync(process.StandardError);
            var client = new McpStdio(process, process.StandardInput);
            await client.RequestAsync("initialize", new
            {
                protocolVersion = "2025-11-25",
                capabilities = capabilities ?? new { },
                clientInfo = new { name = "toolset-test", version = "0.0.0" }
            });
            await client.NotifyAsync("notifications/initialized");
            return client;
        }

        public async Task<JsonElement> ListToolsRawAsync()
        {
            var reply = await RequestAsync("tools/list", new { });
            return reply.GetProperty("result").Clone();
        }

        public async Task<string[]> ListToolsAsync()
        {
            var reply = await ListToolsRawAsync();
            return reply.GetProperty("tools").EnumerateArray()
                .Select(t => t.GetProperty("name").GetString()!).ToArray();
        }

        /// <summary>Raw request escape hatch for methods without a typed helper (resources, completions, prompts).</summary>
        public Task<JsonElement> SendAsync(string method, object? paramsValue)
            => RequestAsync(method, paramsValue);

        public async Task<string> CallToolAsync(string name, string argumentsJson)
        {
            using var args = JsonDocument.Parse(argumentsJson);
            var reply = await RequestAsync("tools/call", new { name, arguments = args.RootElement });
            return reply.GetRawText();
        }

        private Task<JsonElement> RequestAsync(string method, object? paramsValue)
        {
            var id = ++_nextId;
            var payload = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = paramsValue ?? new { } });
            _stdin.WriteLine(payload);
            _stdin.Flush();
            return ReadReplyAsync(id);
        }

        private Task NotifyAsync(string method)
        {
            _stdin.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", method }));
            return _stdin.FlushAsync();
        }

        private async Task<JsonElement> ReadReplyAsync(int id)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (true)
            {
                var line = await _process.StandardOutput.ReadLineAsync(cts.Token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Server closed stdout.");
                if (!line.StartsWith("{", StringComparison.Ordinal))
                {
                    continue;
                }

                var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("id", out var replyId) && replyId.GetInt32() == id &&
                    !doc.RootElement.TryGetProperty("method", out _))
                {
                    return doc.RootElement.Clone();
                }

                // Server→client request (e.g. elicitation/create) — hand it to the
                // test's handler and write the JSON-RPC response back.
                if (doc.RootElement.TryGetProperty("method", out var methodEl) &&
                    doc.RootElement.TryGetProperty("id", out var requestId) &&
                    requestId.ValueKind == JsonValueKind.Number)
                {
                    var requestParams = doc.RootElement.TryGetProperty("params", out var prm) ? prm.Clone() : default;
                    var result = ServerRequestHandler?.Invoke(requestId.GetInt32(), methodEl.GetString() ?? "", requestParams);
                    var response = result is not null
                        ? JsonSerializer.Serialize(new { jsonrpc = "2.0", id = requestId.GetInt32(), result })
                        : JsonSerializer.Serialize(new { jsonrpc = "2.0", id = requestId.GetInt32(), error = new { code = -32601, message = "method not found" } });
                    _stdin.WriteLine(response);
                    _stdin.Flush();
                }
            }
        }

        private static async Task DrainAsync(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is not null) { }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
            catch
            {
                // Process may already be gone.
            }

            _process.Dispose();
        }
    }
}
