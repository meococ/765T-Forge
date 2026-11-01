using System.Text.Json;
using Forge.Shared;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Forge.Server;

/// <summary>
/// Elicitation (2025-11-25 form mode): when the client advertises elicitation.form, an
/// *_overwrite_not_acknowledged / discard / bulk refusal can be asked to the user mid-call,
/// and an "accept" retries the command once with the matching ack flag injected.
/// Clients without the capability keep today's fallback — the tool's own
/// overwriteAcknowledged / discardAcknowledged / acknowledgeBulk argument.
/// Deliberately NOT elicitable: unsafe_not_acknowledged and dotnet_not_acknowledged —
/// unsafe ops stay gated by FORGE_ENABLE_UNSAFE_OPS and explicit flags; a form answer
/// must never substitute for the unsafe policy. SafetyPolicy semantics unchanged.
/// </summary>
public static class ForgeElicitationGate
{
    private static readonly AsyncLocal<McpServer?> CurrentServer = new();

    /// <summary>Error-code → argument flag to inject when the user confirms.</summary>
    public static IReadOnlyDictionary<string, string> AckFlagByCode { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["plot_overwrite_not_acknowledged"] = "overwriteAcknowledged",
            ["publish_overwrite_not_acknowledged"] = "overwriteAcknowledged",
            ["pack_overwrite_not_acknowledged"] = "overwriteAcknowledged",
            ["dgn_target_overwrite_not_acknowledged"] = "overwriteAcknowledged",
            ["saveas_overwrite_not_acknowledged"] = "overwriteAcknowledged",
            ["xref_save_overwrite_not_acknowledged"] = "overwriteAcknowledged",
            ["capture_overwrite_not_acknowledged"] = "overwriteAcknowledged",
            ["export_overwrite_not_acknowledged"] = "overwriteAcknowledged",
            ["discard_not_acknowledged"] = "discardAcknowledged",
            ["bulk_not_acknowledged"] = "acknowledgeBulk"
        };

    /// <summary>Install the server's elicitation channel for this call — only when the client supports form mode.</summary>
    public static IDisposable? Push(McpServer server)
    {
        if (server.ClientCapabilities?.Elicitation?.Form is null)
        {
            return null;
        }

        var previous = CurrentServer.Value;
        CurrentServer.Value = server;
        return new Pop(previous);
    }

    /// <summary>
    /// True when this refusal code is elicitable AND an elicitation channel is live
    /// AND the user confirms. The caller retries with the mapped flag.
    /// </summary>
    public static async Task<bool> ConfirmAsync(string tool, string refusalCode, string refusalMessage, CancellationToken cancellationToken)
    {
        var server = CurrentServer.Value;
        if (server is null || !AckFlagByCode.ContainsKey(refusalCode))
        {
            return false;
        }

        ElicitResult answer;
        try
        {
            answer = await server.ElicitAsync(new ElicitRequestParams
            {
                Message = $"{tool} needs confirmation to proceed.\n\n{refusalMessage}",
                RequestedSchema = new ElicitRequestParams.RequestSchema
                {
                    Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                    {
                        ["confirmed"] = new ElicitRequestParams.BooleanSchema
                        {
                            Title = "Confirm",
                            Description = "Proceed exactly as described above?"
                        }
                    },
                    Required = ["confirmed"]
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Client answered an error, dropped the request, or closed mid-ask —
            // fall back to the original refusal (same as a decline).
            return false;
        }

        return answer.IsAccepted &&
               answer.Content is not null &&
               answer.Content.TryGetValue("confirmed", out var confirmed) &&
               confirmed.ValueKind == JsonValueKind.True;
    }

    /// <summary>Merge {flag:true} into the command's args object for the confirmed retry.</summary>
    public static ForgeCommand WithAck(ForgeCommand command, string flagName)
    {
        var args = new Dictionary<string, JsonElement>();
        if (command.Args.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in command.Args.EnumerateObject())
            {
                args[prop.Name] = prop.Value;
            }
        }

        args[flagName] = JsonDocument.Parse("true").RootElement;
        var merged = JsonDocument.Parse(JsonSerializer.Serialize(args));
        return command with { Args = merged.RootElement };
    }

    private sealed class Pop(McpServer? previous) : IDisposable
    {
        public void Dispose() => CurrentServer.Value = previous;
    }
}
