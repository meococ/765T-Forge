using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Forge.Shared;
using Xunit;

namespace Forge.Tests;

/// <summary>
/// End-to-end elicitation: real Forge.Server over stdio → fake plugin named pipe. The pipe
/// refuses the first call with pack_overwrite_not_acknowledged; with elicitation.form the
/// server must send elicitation/create, retry once with overwriteAcknowledged injected, and
/// return the retried result. Without the capability or on decline the original refusal stands.
/// </summary>
public sealed class ElicitationTests
{
    private static string Exe => ToolsetTests.FindServerExe();

    private sealed class FakePluginPipe : IAsyncDisposable
    {
        private readonly string _pipeName;
        private readonly List<JsonElement> _requests = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        public FakePluginPipe(string pipeName)
        {
            _pipeName = pipeName;
            _loop = Task.Run(ServeLoopAsync);
        }

        public IReadOnlyList<JsonElement> Requests => _requests;

        private async Task ServeLoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        _pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(_stop.Token);

                    using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                    await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

                    var line = await reader.ReadLineAsync(_stop.Token);
                    if (line is null)
                    {
                        continue;
                    }

                    var command = JsonSerializer.Deserialize<JsonElement>(line);
                    _requests.Add(command.Clone());

                    var hasAck = command.TryGetProperty("args", out var args) &&
                                 args.ValueKind == JsonValueKind.Object &&
                                 args.TryGetProperty("overwriteAcknowledged", out var ack) &&
                                 ack.ValueKind == JsonValueKind.True;

                    var result = hasAck
                        ? ForgeResult.Success(command.GetProperty("id").GetString()!, new { outputDir = "packed" })
                        : ForgeResult.Failure(
                            command.GetProperty("id").GetString()!,
                            "pack_overwrite_not_acknowledged",
                            "Refusing to overwrite existing pack file: C:\\out\\host.dwg",
                            "Pass overwriteAcknowledged=true after confirming the destination folder is safe.");

                    await writer.WriteLineAsync(JsonSerializer.Serialize(result, ForgeJson.Options));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // Pipe dropped mid-serve — keep accepting; the test decides pass/fail.
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            try { await _loop; } catch { }
            _stop.Dispose();
        }
    }

    [Fact]
    public async Task OverwriteRefusal_ElicitsConfirmAndRetries()
    {
        var pipeName = $"forge-elicit-{Guid.NewGuid():N}";
        await using var pipe = new FakePluginPipe(pipeName);
        await using var server = await ToolsetTests.McpStdio.SpawnAsync(
            Exe,
            toolsets: "all",
            env: new Dictionary<string, string> { ["FORGE_PIPE_NAME"] = pipeName },
            capabilities: new { elicitation = new { form = new { } } });

        var elicitations = 0;
        server.ServerRequestHandler = (id, method, _) =>
        {
            Assert.Equal("elicitation/create", method);
            elicitations++;
            return new { action = "accept", content = new { confirmed = true } };
        };

        var reply = await server.CallToolAsync("forge_pack_and_go", """{"outputDirectory":"C:\\out"}""");
        using var doc = JsonDocument.Parse(reply);
        var result = doc.RootElement.GetProperty("result");

        Assert.Equal(1, elicitations);
        Assert.Equal(2, pipe.Requests.Count);

        // The retry carries the injected flag — proof the elicitation drove it.
        var retryArgs = pipe.Requests[1].GetProperty("args");
        Assert.True(retryArgs.GetProperty("overwriteAcknowledged").GetBoolean());

        // Final answer is the retried success, not the refusal.
        var structured = result.GetProperty("structuredContent");
        Assert.True(structured.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task OverwriteRefusal_DeclineReturnsOriginalRefusal()
    {
        var pipeName = $"forge-elicit-{Guid.NewGuid():N}";
        await using var pipe = new FakePluginPipe(pipeName);
        await using var server = await ToolsetTests.McpStdio.SpawnAsync(
            Exe,
            toolsets: "all",
            env: new Dictionary<string, string> { ["FORGE_PIPE_NAME"] = pipeName },
            capabilities: new { elicitation = new { form = new { } } });

        server.ServerRequestHandler = (id, method, _) => new { action = "decline" };

        var reply = await server.CallToolAsync("forge_pack_and_go", """{"outputDirectory":"C:\\out"}""");
        using var doc = JsonDocument.Parse(reply);
        var result = doc.RootElement.GetProperty("result");

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains("pack_overwrite_not_acknowledged", result.GetProperty("structuredContent").GetRawText());
        Assert.Single(pipe.Requests); // no retry on decline
    }

    [Fact]
    public async Task OverwriteRefusal_NoCapability_NoElicitation()
    {
        var pipeName = $"forge-elicit-{Guid.NewGuid():N}";
        await using var pipe = new FakePluginPipe(pipeName);
        await using var server = await ToolsetTests.McpStdio.SpawnAsync(
            Exe,
            toolsets: "all",
            env: new Dictionary<string, string> { ["FORGE_PIPE_NAME"] = pipeName });

        server.ServerRequestHandler = (id, method, _) =>
            throw new InvalidOperationException($"no server requests expected, got {method}");

        var reply = await server.CallToolAsync("forge_pack_and_go", """{"outputDirectory":"C:\\out"}""");
        using var doc = JsonDocument.Parse(reply);
        var result = doc.RootElement.GetProperty("result");

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains("pack_overwrite_not_acknowledged", result.GetProperty("structuredContent").GetRawText());
        Assert.Single(pipe.Requests);
    }
}
