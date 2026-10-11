using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Forge.Shared;

namespace Forge.Plugin;

public sealed class NamedPipePluginServer : IDisposable
{
    // Tools that answer without any open document (AutoCAD Start tab). Everything else needs a
    // command context, which only exists with an active document: ExecuteInCommandContextAsync
    // on a document-free host never dispatches its callback, so without this split the first
    // call on the Start tab wedged the single pipe instance until AutoCAD restarted.
    private static readonly HashSet<string> DocumentFreeTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "forge_system_health",
        "forge_system_version",
        "forge_doc_list_open",
    };

    private readonly ForgeEnvironment _environment;
    private readonly PluginCommandProcessor _processor;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _loop;

    public NamedPipePluginServer(ForgeEnvironment environment, PluginCommandProcessor processor)
    {
        _environment = environment;
        _processor = processor;
    }

    /// <summary>
    /// Per-request watchdog deadline, kept five seconds ahead of the MCP client's response
    /// timeout so the plugin, not the client, names the failure. Work AutoCAD has already
    /// started still runs to completion (the client's read-back-after-timeout protocol still
    /// applies); only callbacks that never got dispatched are refused as stale, so an
    /// abandoned write cannot execute long after its caller gave up.
    /// </summary>
    private TimeSpan RequestDeadline => TimeSpan.FromSeconds(
        Math.Max(5, _environment.PluginResponseTimeoutSeconds - 5));

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _loop = Task.Run(() => AcceptLoopAsync(_shutdown.Token));
        _ = _loop.ContinueWith(
            t => TryWriteEditorMessage($"\n[765T-Forge] Pipe loop fault: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public void Dispose()
    {
        // Cancel but do not dispose the CTS: the accept loop may be parked in Task.Delay on
        // this token, and an ObjectDisposedException here would kill the loop silently
        // during teardown.
        _shutdown.Cancel();
    }

    private static void TryWriteEditorMessage(string message)
    {
        try
        {
            Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(message);
        }
        catch
        {
            // The editor can be unavailable (Start tab, shutdown) — logging must never be
            // what kills the loop.
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await HandleRequestAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                TryWriteEditorMessage($"\n[765T-Forge] Pipe error: {ex.Message}");
                // A persistent failure (e.g. another process holding the pipe name) must not
                // turn into a tight retry spin.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    private NamedPipeServerStream CreatePipe()
    {
        var pipeSecurity = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Could not resolve current Windows user SID.");
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            user,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            system,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            _environment.PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity);
    }

    private async Task HandleRequestAsync(Stream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true
        };

        // A client that connects and never writes must not hold the single pipe instance
        // forever — bound the request line like the work itself.
        using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readDeadline.CancelAfter(RequestDeadline);
        var line = await reader.ReadLineAsync(readDeadline.Token).ConfigureAwait(false);
        ForgeCommand? command = null;
        ForgeResult? earlyFailure = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            earlyFailure = ForgeResult.Failure("", "empty_request", "No command was received over the named pipe.");
        }
        else
        {
            try
            {
                command = JsonSerializer.Deserialize<ForgeCommand>(line, ForgeJson.Options);
                if (command is null)
                {
                    earlyFailure = ForgeResult.Failure("", "invalid_request", "Command JSON could not be parsed.");
                }
            }
            catch (Exception ex)
            {
                earlyFailure = ForgeResult.Failure("", "request_failed", ex.Message);
            }
        }

        ForgeResult result;
        if (command is null)
        {
            result = earlyFailure ?? ForgeResult.Failure("", "invalid_request", "Command could not be processed.");
        }
        else
        {
            Task<ForgeResult> work;
            try
            {
                work = ExecuteOnMainThreadAsync(command);
            }
            catch (Exception ex)
            {
                work = Task.FromResult(ForgeResult.Failure(command.Id, "plugin_exception", ex.Message));
            }

            result = await AwaitWithWatchdogAsync(work, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var json = JsonSerializer.Serialize(result, ForgeJson.Options);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // The client may have timed out and dropped its end; the accept loop disposes the
            // pipe and keeps serving the next connection.
        }
    }

    private async Task<ForgeResult> AwaitWithWatchdogAsync(Task<ForgeResult> work, CancellationToken cancellationToken)
    {
        var deadline = RequestDeadline;
        var completed = await Task.WhenAny(work, Task.Delay(deadline, cancellationToken)).ConfigureAwait(false);
        if (completed == work)
        {
            return await work.ConfigureAwait(false);
        }

        // Propagates OperationCanceledException on shutdown; an elapsed deadline falls through
        // to the typed timeout below.
        await completed.ConfigureAwait(false);
        return ForgeResult.Failure(
            "",
            "plugin_request_timeout",
            $"The plugin abandoned the request after {deadline.TotalSeconds:0}s because AutoCAD did not return a result.",
            "Do not blindly retry non-idempotent writes. Call forge_qa_readback_after_timeout to check whether the operation committed, or raise FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS for long-running tools.");
    }

    private Task<ForgeResult> ExecuteOnMainThreadAsync(ForgeCommand command)
    {
        if (Application.DocumentManager.MdiActiveDocument is null)
        {
            if (DocumentFreeTools.Contains(command.Tool))
            {
                // Diagnostics answer on the Start tab by riding the main thread's idle pump —
                // there is no command context to marshal into without a document.
                return ExecuteWhenIdleAsync(command);
            }

            if (Application.DocumentManager.Count == 0)
            {
                return Task.FromResult(ForgeResult.Failure(
                    command.Id,
                    "no_command_context",
                    "AutoCAD has no open document (Start tab): there is no command context to execute this tool in.",
                    "Open a drawing in AutoCAD first (forge_doc_open also needs a document), then retry; forge_system_health, forge_system_version and forge_doc_list_open still answer."));
            }

            // Documents are open but none is active (Start tab focused): still try the command
            // context — if the host wedges, the watchdog answers and the pipe keeps serving.
        }

        return ExecuteInCommandContextAsync(command);
    }

    private Task<ForgeResult> ExecuteInCommandContextAsync(ForgeCommand command)
    {
        var tcs = new TaskCompletionSource<ForgeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedAt = Environment.TickCount64;
        var staleAfterMs = (long)RequestDeadline.TotalMilliseconds;

        // The dispatcher's ExecutionResult is deliberately discarded (it is not a Task and the
        // tcs carries the outcome). If the dispatch itself faults before the callback runs, the
        // tcs stays incomplete and the watchdog answers the client with plugin_request_timeout.
        Application.DocumentManager.ExecuteInCommandContextAsync(_ =>
        {
            try
            {
                if (Environment.TickCount64 - startedAt >= staleAfterMs)
                {
                    // The watchdog has already answered the client; starting now could commit
                    // a write long after its caller gave up.
                    tcs.TrySetResult(ForgeResult.Failure(
                        command.Id,
                        "plugin_request_stale",
                        "The request was abandoned by the plugin watchdog before AutoCAD started it; nothing was executed."));
                    return Task.CompletedTask;
                }

                var current = Application.DocumentManager.MdiActiveDocument;
                Document? doc;

                if (!string.IsNullOrWhiteSpace(command.Document))
                {
                    var matches = ResolveDocuments(command.Document);
                    if (matches.Count == 0)
                    {
                        tcs.TrySetResult(ForgeResult.Failure(command.Id, "document_not_found", $"Open document not found: {command.Document}"));
                        return Task.CompletedTask;
                    }

                    if (matches.Count > 1)
                    {
                        tcs.TrySetResult(ForgeResult.Failure(
                            command.Id,
                            "ambiguous_document",
                            $"Multiple open documents match '{command.Document}'.",
                            "Pass the full drawing path; use forge_doc_list_open to see exact names."));
                        return Task.CompletedTask;
                    }

                    doc = matches[0];
                }
                else
                {
                    doc = current;
                }

                if (doc is null && !command.Tool.Equals("forge_doc_open", StringComparison.OrdinalIgnoreCase))
                {
                    tcs.TrySetResult(ForgeResult.Failure(command.Id, "no_active_document", "No active AutoCAD document is available."));
                    return Task.CompletedTask;
                }

                if (doc is not null)
                {
                    // Only touch the active document when the command genuinely targets a
                    // different drawing. Assigning MdiActiveDocument from a command context is
                    // risky, so avoid it entirely on the common same-document path.
                    var switched = !ReferenceEquals(doc, current);
                    if (switched)
                    {
                        Application.DocumentManager.MdiActiveDocument = doc;
                    }

                    try
                    {
                        using (doc.LockDocument())
                        {
                            tcs.TrySetResult(_processor.Process(command));
                        }
                    }
                    finally
                    {
                        if (switched && current is not null)
                        {
                            try
                            {
                                Application.DocumentManager.MdiActiveDocument = current;
                            }
                            catch
                            {
                                // Best-effort restore of the previously active document.
                            }
                        }
                    }
                }
                else
                {
                    tcs.TrySetResult(_processor.Process(command));
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetResult(ForgeResult.Failure(command.Id, "plugin_exception", ex.Message));
            }

            return Task.CompletedTask;
        }, null);

        return tcs.Task;
    }

    private Task<ForgeResult> ExecuteWhenIdleAsync(ForgeCommand command)
    {
        var tcs = new TaskCompletionSource<ForgeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedAt = Environment.TickCount64;
        var staleAfterMs = (long)RequestDeadline.TotalMilliseconds;

        void OnIdle(object? sender, EventArgs e)
        {
            Application.Idle -= OnIdle;
            try
            {
                if (Environment.TickCount64 - startedAt >= staleAfterMs)
                {
                    tcs.TrySetResult(ForgeResult.Failure(
                        command.Id,
                        "plugin_request_stale",
                        "The request was abandoned by the plugin watchdog before AutoCAD went idle; nothing was executed."));
                    return;
                }

                tcs.TrySetResult(_processor.Process(command));
            }
            catch (Exception ex)
            {
                tcs.TrySetResult(ForgeResult.Failure(command.Id, "plugin_exception", ex.Message));
            }
        }

        Application.Idle += OnIdle;
        return tcs.Task;
    }

    private static IReadOnlyList<Document> ResolveDocuments(string document)
    {
        // Match by full path when the caller gave a rooted path, otherwise by exact file
        // name. A loose suffix match (EndsWith) could silently select the wrong drawing
        // (e.g. "1.dwg" matching "plan1.dwg"), so we never do that. If more than one open
        // document matches, the caller reports ambiguity instead of guessing.
        var matches = new List<Document>();
        var rooted = Path.IsPathRooted(document);
        var targetFull = rooted ? TryGetFullPath(document) : null;
        var targetLeaf = Path.GetFileName(document);

        foreach (Document doc in Application.DocumentManager)
        {
            var name = doc.Name;
            var match = rooted
                ? string.Equals(TryGetFullPath(name) ?? name, targetFull, StringComparison.OrdinalIgnoreCase)
                : !string.IsNullOrEmpty(targetLeaf) &&
                  string.Equals(Path.GetFileName(name), targetLeaf, StringComparison.OrdinalIgnoreCase);

            if (match || string.Equals(name, document, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(doc);
            }
        }

        return matches;
    }

    private static string? TryGetFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }
}
