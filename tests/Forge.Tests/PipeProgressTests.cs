using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Forge.Server;
using Forge.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Forge.Tests;

/// <summary>
/// Wire-level loopback tests for the forge_progress pipe protocol: a NamedPipeServerStream
/// stands in for the plugin and emits progress lines ahead of the final ForgeResult.
/// </summary>
public sealed class PipeProgressTests
{
    private static ForgePipeClient Client(string pipeName)
        => new(new ForgeEnvironment
        {
            PipeName = pipeName,
            Token = "test-token",
            PluginConnectTimeoutSeconds = 10,
            PluginResponseTimeoutSeconds = 10
        }, NullLogger<ForgePipeClient>.Instance);

    private static async Task ServeAsync(string pipeName, Func<StreamWriter, string, Task> script)
    {
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        await server.WaitForConnectionAsync();

        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        var requestLine = await reader.ReadLineAsync();
        Assert.False(string.IsNullOrWhiteSpace(requestLine));
        await script(writer, requestLine!);
    }

    [Fact]
    public async Task ProgressLinesAreForwardedAndResultIsReturned()
    {
        var pipeName = $"forge-test-{Guid.NewGuid():N}";
        var command = new ForgeCommand { Id = "cmd-progress-1", Tool = "forge_plot_publish" };
        var progress = new List<ForgeProgressMessage>();

        var serverTask = ServeAsync(pipeName, async (writer, _) =>
        {
            // Two matching progress lines, one foreign-id line (must be dropped), then result.
            await writer.WriteLineAsync(JsonSerializer.Serialize(
                ForgeProgressMessage.For("cmd-progress-1", 1, 4, "Publishing sheet 1 of 4"), ForgeJson.Options));
            await writer.WriteLineAsync(JsonSerializer.Serialize(
                ForgeProgressMessage.For("someone-else", 9, 9, "not ours"), ForgeJson.Options));
            await writer.WriteLineAsync(JsonSerializer.Serialize(
                ForgeProgressMessage.For("cmd-progress-1", 4, 4, "Publishing sheet 4 of 4"), ForgeJson.Options));
            await writer.WriteLineAsync(JsonSerializer.Serialize(
                ForgeResult.Success(command.Id, new { outputPath = "set.pdf" }), ForgeJson.Options));
        });

        var result = await Client(pipeName).SendAsync(
            command,
            p => { progress.Add(p); return Task.CompletedTask; },
            CancellationToken.None);

        await serverTask;

        Assert.True(result.Ok);
        Assert.Equal(command.Id, result.Id);
        Assert.Equal(2, progress.Count);
        Assert.Equal(1, progress[0].Progress);
        Assert.Equal(4, progress[1].Progress);
        Assert.Equal("Publishing sheet 4 of 4", progress[1].Message);
    }

    [Fact]
    public async Task NoProgressSinkOmitsWireFlagAndGetsSingleReply()
    {
        // compat-progress-flag: an old-style request (no progress sink anywhere) must put no
        // "progress" flag on the wire — that is what guarantees the new plugin writes exactly
        // one reply line for servers older than core v2.
        var pipeName = $"forge-test-{Guid.NewGuid():N}";
        var command = new ForgeCommand { Id = "cmd-noflag-1", Tool = "forge_plot_to_pdf" };

        var serverTask = ServeAsync(pipeName, async (writer, requestLine) =>
        {
            using var doc = JsonDocument.Parse(requestLine);
            var root = doc.RootElement;
            Assert.False(
                root.TryGetProperty("progress", out var flag) && flag.GetBoolean(),
                "request must not ask for progress without a progress sink");

            // The new plugin emits zero progress lines without the flag: exactly one reply.
            await writer.WriteLineAsync(JsonSerializer.Serialize(
                ForgeResult.Success(command.Id, new { outputPath = "a.pdf" }), ForgeJson.Options));
        });

        var result = await Client(pipeName).SendAsync(command, CancellationToken.None);

        await serverTask;
        Assert.True(result.Ok);
        Assert.Equal(command.Id, result.Id);
    }

    [Fact]
    public async Task ProgressSinkSetsWireFlag()
    {
        var pipeName = $"forge-test-{Guid.NewGuid():N}";
        var command = new ForgeCommand { Id = "cmd-flag-1", Tool = "forge_plot_to_pdf" };

        var serverTask = ServeAsync(pipeName, async (writer, requestLine) =>
        {
            using var doc = JsonDocument.Parse(requestLine);
            Assert.True(doc.RootElement.GetProperty("progress").GetBoolean());
            await writer.WriteLineAsync(JsonSerializer.Serialize(
                ForgeResult.Success(command.Id, new { outputPath = "a.pdf" }), ForgeJson.Options));
        });

        var result = await Client(pipeName).SendAsync(
            command,
            _ => Task.CompletedTask,
            CancellationToken.None);

        await serverTask;
        Assert.True(result.Ok);
    }

    [Fact]
    public async Task AmbientProgressScopeSetsWireFlag()
    {
        // The production path: the call filter pushes ForgeProgressScope when the MCP caller
        // sent _meta.progressToken — the wire request must then carry "progress": true even
        // though no onProgress delegate was supplied.
        var pipeName = $"forge-test-{Guid.NewGuid():N}";
        var command = new ForgeCommand { Id = "cmd-ambient-1", Tool = "forge_plot_publish" };

        var serverTask = ServeAsync(pipeName, async (writer, requestLine) =>
        {
            using var doc = JsonDocument.Parse(requestLine);
            Assert.True(doc.RootElement.GetProperty("progress").GetBoolean());
            await writer.WriteLineAsync(JsonSerializer.Serialize(
                ForgeResult.Success(command.Id, new { outputPath = "set.pdf" }), ForgeJson.Options));
        });

        using var _ = ForgeProgressScope.Push(new ForgeProgressScope.Context(null!, new ModelContextProtocol.Protocol.ProgressToken("smoke")));
        var result = await Client(pipeName).SendAsync(command, CancellationToken.None);

        await serverTask;
        Assert.True(result.Ok);
    }

    [Fact]
    public async Task CallerCancellationAbortsTheWait()
    {
        var pipeName = $"forge-test-{Guid.NewGuid():N}";
        var command = new ForgeCommand { Id = "cmd-cancel-1", Tool = "forge_plot_publish" };

        var serverTask = ServeAsync(pipeName, async (writer, _) =>
        {
            // Emit progress then stall — the client must surface cancellation, not a typed timeout.
            await writer.WriteLineAsync(JsonSerializer.Serialize(
                ForgeProgressMessage.For(command.Id, 1, 4, "Publishing sheet 1 of 4"), ForgeJson.Options));
            await Task.Delay(TimeSpan.FromSeconds(5));
        });

        // Cancel only after the progress callback provably ran — a fixed timer raced the
        // connect+read on slow CI runners and flaked with an empty collection.
        var progressSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new List<ForgeProgressMessage>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var sendTask = Client(pipeName).SendAsync(
            command,
            p => { received.Add(p); progressSeen.TrySetResult(true); return Task.CompletedTask; },
            cts.Token);

        await progressSeen.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sendTask);
        Assert.Single(received);
    }
}
