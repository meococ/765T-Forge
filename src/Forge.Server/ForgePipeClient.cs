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

    public Task<ForgeResult> SendAsync(ForgeCommand command, CancellationToken cancellationToken = default)
        => SendAsync(command, onProgress: null, cancellationToken);

    /// <summary>
    /// onProgress is the deterministic seam for wire tests — production leaves it null and
    /// progress lines fall back to the ambient ForgeProgressScope (MCP notifications/progress).
    /// </summary>
    public async Task<ForgeResult> SendAsync(
        ForgeCommand command,
        Func<ForgeProgressMessage, Task>? onProgress,
        CancellationToken cancellationToken = default)
    {
        var authenticated = command.WithToken(_environment.Token);
        // compat-progress-flag: ask the plugin for forge_progress lines only when a progress
        // sink exists — a progress-less request must get exactly one reply line, as before.
        if (!authenticated.Progress && (onProgress is not null || ForgeProgressScope.Current is not null))
        {
            authenticated = authenticated with { Progress = true };
        }

        await using var pipe = new NamedPipeClientStream(
            ".",
            _environment.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(_environment.PluginConnectTimeoutSeconds));
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

            // The plugin may emit forge_progress lines before its final ForgeResult line
            // (long ops like publish/pack). Skip-and-forward those; the first non-progress
            // JSON line is the reply.
            string? response = null;
            while (true)
            {
                var line = await reader.ReadLineAsync(responseTimeout.Token).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith("{", StringComparison.Ordinal))
                {
                    continue;
                }

                var progressLine = ForgeProgressMessage.TryParse(trimmed);
                if (progressLine is not null)
                {
                    if (string.Equals(progressLine.CommandId, command.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        if (onProgress is not null)
                        {
                            await onProgress(progressLine).ConfigureAwait(false);
                        }
                        else
                        {
                            await ForgeProgressScope.ReportAsync(
                                progressLine.Progress, progressLine.Total, progressLine.Message,
                                responseTimeout.Token).ConfigureAwait(false);
                        }
                    }

                    continue;
                }

                response = line;
                break;
            }

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
                    : $"Timed out connecting to named pipe '{_environment.PipeName}' after {_environment.PluginConnectTimeoutSeconds}s.",
                pipe.IsConnected
                    ? "Do not blindly retry non-idempotent writes. Check AutoCAD, then call a read-back/QA tool to see whether the operation committed."
                    : "Start AutoCAD and NETLOAD Forge.Plugin.dll built for the host year (scripts/build-plugin.ps1 -Year), then call forge_system_health again.");
        }
        catch (OperationCanceledException)
        {
            // Real caller cancellation (notifications/cancelled, tasks/cancel, disconnect):
            // surface it — the catch(Exception) below must not relabel it plugin_unavailable.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Named pipe request failed.");
            return ForgeResult.Failure(
                command.Id,
                "plugin_unavailable",
                $"Could not reach AutoCAD plugin over named pipe '{_environment.PipeName}'.",
                "Open AutoCAD, NETLOAD Forge.Plugin.dll, and verify the MCP_STATUS command.");
        }
    }
}
