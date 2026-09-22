using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Forge.Shared;
using Microsoft.Extensions.Logging;

namespace Forge.Server;

public sealed class ForgePipeClient
{
    private readonly ForgeEnvironment _environment;
    private readonly ILogger<ForgePipeClient> _logger;

    public ForgePipeClient(ForgeEnvironment environment, ILogger<ForgePipeClient> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task<ForgeResult> SendAsync(ForgeCommand command, CancellationToken cancellationToken = default)
    {
        var authenticated = command.WithToken(_environment.Token);
        await using var pipe = new NamedPipeClientStream(
            ".",
            _environment.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            await pipe.ConnectAsync(connectTimeout.Token).ConfigureAwait(false);

            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);

            using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            responseTimeout.CancelAfter(TimeSpan.FromSeconds(_environment.PluginResponseTimeoutSeconds));

            var json = JsonSerializer.Serialize(authenticated, ForgeJson.Options);
            await writer.WriteLineAsync(json.AsMemory(), responseTimeout.Token).ConfigureAwait(false);

            var response = await reader.ReadLineAsync(responseTimeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(response))
            {
                return ForgeResult.Failure(command.Id, "pipe_empty_response", "AutoCAD plugin returned an empty response.");
            }

            return JsonSerializer.Deserialize<ForgeResult>(response, ForgeJson.Options)
                ?? ForgeResult.Failure(command.Id, "pipe_invalid_response", "AutoCAD plugin returned invalid JSON.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ForgeResult.Failure(
                command.Id,
                pipe.IsConnected ? "plugin_response_timeout" : "plugin_connect_timeout",
                pipe.IsConnected
                    ? $"Timed out waiting for AutoCAD plugin response after {_environment.PluginResponseTimeoutSeconds}s."
                    : $"Timed out connecting to named pipe '{_environment.PipeName}'.",
                pipe.IsConnected
                    ? "Do not blindly retry non-idempotent writes. Check AutoCAD, then call a read-back/QA tool to see whether the operation committed."
                    : "NETLOAD the Forge.Plugin.dll built for the same year as the running AutoCAD, then call forge_system_health again.");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Named pipe request failed.");
            return ForgeResult.Failure(
                command.Id,
                "plugin_unavailable",
                $"Could not reach AutoCAD plugin over named pipe '{_environment.PipeName}'.",
                "Open the AutoCAD release that matches the Forge.Plugin.dll build, NETLOAD that DLL, and verify the MCP_STATUS command.");
        }
    }
}
