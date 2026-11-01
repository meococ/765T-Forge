using System.Text.Json.Nodes;
using Forge.Shared;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Forge.Server;

/// <summary>
/// Ambient progress channel for one tools/call. The call-tool filter pushes the request's
/// _meta.progressToken + McpServer here for the duration of the call; anything deeper in the
/// stack (named-pipe progress lines, the headless runner) forwards to it as
/// notifications/progress. AsyncLocal flows through awaits, so no signatures change.
/// </summary>
public static class ForgeProgressScope
{
    public sealed record Context(McpServer Server, ProgressToken Token);

    private static readonly AsyncLocal<Context?> CurrentLocal = new();

    public static Context? Current => CurrentLocal.Value;

    public static IDisposable Push(Context context)
    {
        var previous = CurrentLocal.Value;
        CurrentLocal.Value = context;
        return new Pop(previous);
    }

    /// <summary>Extract a progress context from tools/call params (_meta.progressToken).</summary>
    public static Context? FromRequest(McpServer server, CallToolRequestParams? parameters)
    {
        var meta = parameters?.Meta;
        if (meta is null || !meta.TryGetPropertyValue("progressToken", out var raw) || raw is null)
        {
            return null;
        }

        ProgressToken token;
        if (raw is JsonValue value && value.TryGetValue<long>(out var numeric))
        {
            token = new ProgressToken(numeric);
        }
        else if (raw is JsonValue stringValue && stringValue.TryGetValue<string>(out var text) && text is not null)
        {
            token = new ProgressToken(text);
        }
        else
        {
            return null;
        }

        return new Context(server, token);
    }

    /// <summary>Forward one progress event; never throws — progress is best-effort.</summary>
    public static async Task ReportAsync(double progress, double? total = null, string? message = null, CancellationToken cancellationToken = default)
    {
        var context = Current;
        if (context is null)
        {
            return;
        }

        try
        {
            await context.Server.SendNotificationAsync(
                NotificationMethods.ProgressNotification,
                new ProgressNotificationParams
                {
                    ProgressToken = context.Token,
                    Progress = new ProgressNotificationValue
                    {
                        Progress = (float)progress,
                        Total = (float?)total,
                        Message = message
                    }
                },
                serializerOptions: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Client left, token dropped, transport busy — never let reporting break the call.
        }
    }

    private sealed class Pop(Context? previous) : IDisposable
    {
        public void Dispose() => CurrentLocal.Value = previous;
    }
}
