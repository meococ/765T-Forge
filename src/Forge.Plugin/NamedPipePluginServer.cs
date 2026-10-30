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
    // Document-free dispatch is decided by Forge.Shared.DocumentContextPolicy.Route (D13): the
    // document-free tool set and both fail-fast codes live there. Every lane authenticates via
    // PluginCommandProcessor.VerifyToken before routing (review-wp2-r1 finding 1).
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
        // forever — bound the request line like the work itself, and cap length at 16 MB.
        using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readDeadline.CancelAfter(RequestDeadline);
        string? line = null;
        ForgeCommand? command = null;
        ForgeResult? earlyFailure = null;
        try
        {
            line = await ReadBoundedLineAsync(reader, 16 * 1024 * 1024, readDeadline.Token).ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            earlyFailure = ForgeResult.Failure("", "request_too_large", ex.Message);
        }

        if (earlyFailure is null && string.IsNullOrWhiteSpace(line))
        {
            earlyFailure = ForgeResult.Failure("", "empty_request", "No command was received over the named pipe.");
        }
        else if (earlyFailure is null)
        {
            try
            {
                command = JsonSerializer.Deserialize<ForgeCommand>(line!, ForgeJson.Options);
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
        // review-wp2-r1 finding 1 (D13): authenticate BEFORE any routing — the zero-document lane
        // and the fail-fast refusals used to answer unauthenticated (an ok health answer and no
        // audit record). A bad token now gets `unauthorized` on every lane; Process re-checks it
        // for the lanes that run through it.
        if (_processor.VerifyToken(command) is { } unauthorized)
        {
            return Task.FromResult(unauthorized);
        }

        // smoke-r2 D13: the Shared decision table decides BEFORE any marshal. Zero documents means a
        // command context can never exist, so document tools fail fast instead of wedging the pipe
        // for the whole timeout; document-free tools ride the Idle marshal into the real handlers
        // (token check, audit record and every Process gate apply there); a missing active document
        // with open documents keeps the no_command_context refusal. The dispatch itself must never
        // depend on AutoCAD going idle.
        var documentCount = Application.DocumentManager.Count;
        var hasActiveDocument = Application.DocumentManager.MdiActiveDocument is not null;
        switch (DocumentContextPolicy.Route(command.Tool, documentCount, hasActiveDocument))
        {
            case DocDispatchDecision.ApplicationLane:
                // Creating, opening or switching the active drawing must happen in application
                // context, never inside a document command context (fix-smoke-20261029 D1).
                return ExecuteDocActivateWhenIdleAsync(command);

            case DocDispatchDecision.ZeroDocumentLane:
            case DocDispatchDecision.IdleLane:
                // Every handler in DocumentContextPolicy.ZeroDocumentTools is document-free, so at
                // zero documents the real Health()/ListOpenDocuments()/Version()/store/sysvar
                // handlers answer through Process — with the audit record written.
                return ExecuteWhenIdleAsync(command);

            case DocDispatchDecision.FailNoActiveDocument:
                // review-wp2-r2 Q2: authenticated refusals write the same audit record Process
                // would — the call was verified, so it must exist in the audit trail.
                return Task.FromResult(ForgeResult.Failure(
                    command.Id,
                    "no_active_document",
                    "AutoCAD has no open document (Start tab): there is no document to run this tool on.",
                    "Create or open a drawing first (forge_doc_new, forge_doc_open), then retry; forge_system_health, forge_system_version and forge_doc_list_open still answer.",
                    _processor.AuditRefusal(command, "no_active_document")));

            case DocDispatchDecision.FailNoCommandContext:
                return Task.FromResult(ForgeResult.Failure(
                    command.Id,
                    "no_command_context",
                    "Documents are open but none is active (the Start tab has focus): there is no command context to execute this tool in.",
                    "Activate a drawing first (forge_doc_activate) or click its window in AutoCAD, then retry.",
                    _processor.AuditRefusal(command, "no_command_context")));

            default:
                return ExecuteInCommandContextAsync(command);
        }
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

    /// <summary>
    /// forge_doc_activate / forge_doc_new / forge_doc_open / forge_doc_close: run on the Idle marshal (application
    /// context). The first idle tick runs the processor (safety, audit, the action and a first
    /// read-back); if AutoCAD has not reported the switch yet, later idle ticks re-read the active
    /// document — matched by reference to the target Document — for up to two seconds (activate) or
    /// until five seconds before the request deadline (new/open), so the reply states what is really
    /// active.
    /// </summary>
    private Task<ForgeResult> ExecuteDocActivateWhenIdleAsync(ForgeCommand command)
    {
        var tcs = new TaskCompletionSource<ForgeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedAt = Environment.TickCount64;
        var staleAfterMs = (long)RequestDeadline.TotalMilliseconds;
        ForgeResult? first = null;
        var claimed = false;
        long pollUntil = 0;

        void Finish(ForgeResult result)
        {
            Application.Idle -= OnIdle;
            tcs.TrySetResult(result);
        }

        void OnIdle(object? sender, EventArgs e)
        {
            try
            {
                if (first is null)
                {
                    // review-wp2-r2 Q1: claim the command BEFORE Process runs. first stays null
                    // for the whole synchronous Process call, so a nested Application.Idle raise
                    // during a message-pumping call inside it (Documents.Add / Editor.Command in
                    // forge_dgn_import) would otherwise re-enter and run the same command twice —
                    // a second temp document + -DGNIMPORT. Already-started work runs to
                    // completion, so a nested raise returns regardless of the deadline.
                    if (claimed)
                    {
                        return;
                    }

                    if (Environment.TickCount64 - startedAt >= staleAfterMs)
                    {
                        Finish(ForgeResult.Failure(
                            command.Id,
                            "plugin_request_stale",
                            "The request was abandoned by the plugin watchdog before AutoCAD went idle; nothing was executed."));
                        return;
                    }

                    claimed = true;
                    first = _processor.Process(command);
                    if (first.Ok || first.Error?.Code is not { } code || !DocumentContextPolicy.UnconfirmedCodes.Contains(code) ||
                        first.Data is not (PluginCommandProcessor.DocActivateReadBack or PluginCommandProcessor.DocCloseReadBack))
                    {
                        Finish(first);
                        return;
                    }

                    pollUntil = Math.Max(Environment.TickCount64 + DocumentContextPolicy.ActivatePollMs, startedAt + DocumentContextPolicy.PollWindowMs(command.Tool, staleAfterMs));
                    return;
                }

                var reread = (first.Data is PluginCommandProcessor.DocCloseReadBack closing
                    ? PluginCommandProcessor.ReadBackClosedDocument(command.Id, closing)
                    : PluginCommandProcessor.ReadBackActiveDocument(command.Id, (PluginCommandProcessor.DocActivateReadBack)first.Data!)) with { AuditId = first.AuditId };
                if (reread.Ok || Environment.TickCount64 >= pollUntil)
                {
                    Finish(reread);
                }
            }
            catch (Exception ex)
            {
                Finish(ForgeResult.Failure(command.Id, "plugin_exception", ex.Message));
            }
        }

        Application.Idle += OnIdle;
        return tcs.Task;
    }

    private static IReadOnlyList<Document> ResolveDocuments(string document)
    {
        // Match by full path when the caller gave a rooted path, otherwise by exact file
        // name (DocumentMatcher, shared with forge_doc_activate). A loose suffix match could
        // silently select the wrong drawing (e.g. "1.dwg" matching "plan1.dwg"), so we never do
        // that. If more than one open document matches, the caller reports ambiguity instead of
        // guessing.
        var docs = Application.DocumentManager.Cast<Document>().ToList();
        var indices = DocumentMatcher.Match(docs.Select(d => d.Name).ToList(), document);
        return indices.Select(i => docs[i]).ToList();
    }

    private static async Task<string?> ReadBoundedLineAsync(StreamReader reader, int maxChars, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var buf = new char[4096];
        while (true)
        {
            var n = await reader.ReadAsync(buf.AsMemory(0, buf.Length), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                return sb.Length == 0 ? null : sb.ToString();
            }

            for (var i = 0; i < n; i++)
            {
                var ch = buf[i];
                if (ch == '\n')
                {
                    if (sb.Length > 0 && sb[^1] == '\r')
                    {
                        sb.Length--;
                    }

                    return sb.ToString();
                }

                sb.Append(ch);
                if (sb.Length > maxChars)
                {
                    throw new InvalidDataException($"Pipe request line exceeded maximum length of {maxChars} characters.");
                }
            }
        }
    }
}
