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
    /// <summary>
    /// More than one server instance so a second concurrent MCP call can connect while the first
    /// request is executing. Execution itself is still strictly serialized by
    /// <see cref="_executionGate"/>; the extra instances only remove the misleading
    /// "plugin_unavailable" transport error for a plugin that is alive and busy.
    /// </summary>
    private const int MaxServerInstances = 4;

    private readonly ForgeEnvironment _environment;
    private readonly PluginCommandProcessor _processor;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly List<Task> _inflight = new();
    private readonly object _inflightGate = new();
    private Task? _loop;

    public NamedPipePluginServer(ForgeEnvironment environment, PluginCommandProcessor processor)
    {
        _environment = environment;
        _processor = processor;
    }

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _loop = Task.Run(() => AcceptLoopAsync(_shutdown.Token));
    }

    public void Dispose()
    {
        _shutdown.Cancel();

        Task[] pending;
        lock (_inflightGate)
        {
            pending = _inflight.ToArray();
        }

        // Drain in-flight request handlers so their pipes and writers are released before the
        // server reports itself stopped. Faults were already observed by Track().
        try
        {
            Task.WaitAll(pending, TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Individual faults are reported by the tracked continuation.
        }

        _shutdown.Dispose();
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

                // Hand the connected pipe to its own tracked handler and immediately wait for the
                // next connection so a busy plugin still accepts concurrent callers.
                var accepted = pipe;
                pipe = null;
                Track(HandleRequestAsync(accepted, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                pipe?.Dispose();
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[765T-Forge] Pipe error: {ex.Message}");
                try
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private void Track(Task task)
    {
        lock (_inflightGate)
        {
            _inflight.Add(task);
        }

        _ = task.ContinueWith(
            completed =>
            {
                lock (_inflightGate)
                {
                    _inflight.Remove(completed);
                }

                if (completed.IsFaulted && completed.Exception is { } exception)
                {
                    Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                        $"\n[765T-Forge] Pipe request fault: {exception.GetBaseException().Message}");
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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

        // ACL is identical on both TFMs: the same PipeSecurity instance (current user + LocalSystem
        // only, no grant to anyone else) is applied by NamedPipeServerStreamAcl.Create on net8 and by
        // the NamedPipeServerStream constructor on net462.
#if NETFRAMEWORK
        return new NamedPipeServerStream(
            _environment.PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: MaxServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity);
#else
        return NamedPipeServerStreamAcl.Create(
            _environment.PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: MaxServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity);
#endif
    }

    private async Task HandleRequestAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using (pipe)
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
#if NETFRAMEWORK
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };
#else
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };
#endif

            ForgeResult result;
            try
            {
                var line = await PipeProtocol.ReadLineAsync(
                    reader,
                    _environment.PluginResponseTimeoutSeconds,
                    _environment.PipeName,
                    cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(line))
                {
                    result = ForgeResult.Failure("", "empty_request", "No command was received over the named pipe.");
                }
                else if (ForgePipeFrames.ExceedsLimit(line))
                {
                    // Exact frame cap: a token-holding peer must not be able to exhaust memory
                    // inside AutoCAD with an unbounded line.
                    result = ForgeResult.Failure(
                        "",
                        "frame_too_large",
                        $"Request frame exceeded {ForgePipeFrames.MaxFrameCharacters} characters and was rejected.");
                }
                else
                {
                    try
                    {
                        var command = JsonSerializer.Deserialize<ForgeCommand>(line, ForgeJson.Options);
                        result = command is null
                            ? ForgeResult.Failure("", "invalid_request", "Command JSON could not be parsed.")
                            : await ExecuteSerializedAsync(command, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        result = ForgeResult.Failure("", "request_failed", ex.Message);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                result = ForgeResult.Failure("", "request_failed", ex.Message);
            }

            await WriteResponseAsync(writer, result, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteResponseAsync(StreamWriter writer, ForgeResult result, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(result, ForgeJson.Options);
        if (ForgePipeFrames.ExceedsLimit(json))
        {
            json = JsonSerializer.Serialize(
                ForgeResult.Failure(
                    result.Id,
                    "response_too_large",
                    $"Serialized response exceeded {ForgePipeFrames.MaxFrameCharacters} characters and was not sent."),
                ForgeJson.Options);
        }

        await PipeProtocol.WriteLineAsync(
            writer,
            json,
            _environment.PluginResponseTimeoutSeconds,
            _environment.PipeName,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Serializes actual execution (arrival order, no interleaving) while letting connections be
    /// accepted concurrently. A caller that cannot acquire the gate within the configured timeout
    /// receives a deterministic <c>plugin_busy</c> result instead of a transport timeout.
    /// </summary>
    private async Task<ForgeResult> ExecuteSerializedAsync(ForgeCommand command, CancellationToken cancellationToken)
    {
        var gateTimeout = TimeSpan.FromSeconds(_environment.PluginResponseTimeoutSeconds);
        if (!await _executionGate.WaitAsync(gateTimeout, cancellationToken).ConfigureAwait(false))
        {
            return ForgeResult.Failure(
                command.Id,
                "plugin_busy",
                "Another request is in flight; retry after it completes.");
        }

        try
        {
            return await ExecuteOnMainThreadAsync(command, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _executionGate.Release();
        }
    }

    private async Task<ForgeResult> ExecuteOnMainThreadAsync(ForgeCommand command, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<ForgeResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            _ = Application.DocumentManager.ExecuteInCommandContextAsync(
                _ =>
                {
                    // The task handed back to AutoCAD is the same task observed here, so a fault
                    // inside the command context can never be swallowed as unobserved.
                    var callbackTask = RunInCommandContextAsync(command, tcs, cancellationToken);
                    _ = ObserveCallbackAsync(callbackTask, tcs, command.Id);
                    return callbackTask;
                },
                null);
        }
        catch (Exception ex)
        {
            return ForgeResult.Failure(command.Id, "plugin_dispatch_failed", ex.Message);
        }

        // Main-thread dispatch timeout: if AutoCAD is showing a modal dialog the callback never
        // runs, so the accept loop must not be blocked forever by an unbounded await.
        var timeout = TimeSpan.FromSeconds(_environment.PluginResponseTimeoutSeconds);
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
        if (!ReferenceEquals(completed, tcs.Task))
        {
            return ForgeResult.Failure(
                command.Id,
                "plugin_main_thread_timeout",
                $"AutoCAD did not run the request on the main thread within {_environment.PluginResponseTimeoutSeconds} seconds (a modal dialog may be open).");
        }

        return await tcs.Task.ConfigureAwait(false);
    }

    private async Task RunInCommandContextAsync(
        ForgeCommand command,
        TaskCompletionSource<ForgeResult> tcs,
        CancellationToken cancellationToken)
    {
        var current = Application.DocumentManager.MdiActiveDocument;
        Document? doc;

        if (!string.IsNullOrWhiteSpace(command.Document))
        {
            var matches = ResolveDocuments(command.Document);
            if (matches.Count == 0)
            {
                tcs.TrySetResult(ForgeResult.Failure(command.Id, "document_not_found", $"Open document not found: {command.Document}"));
                return;
            }

            if (matches.Count > 1)
            {
                tcs.TrySetResult(ForgeResult.Failure(
                    command.Id,
                    "ambiguous_document",
                    $"Multiple open documents match '{command.Document}'.",
                    "Pass the full drawing path; use forge_doc_list_open to see exact names."));
                return;
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
            return;
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
                    tcs.TrySetResult(await DispatchCommandAsync(command, cancellationToken));
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
                    catch (Exception ex)
                    {
                        // A failed restore leaves the wrong drawing active; report it instead of
                        // silently continuing.
                        Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                            $"\n[765T-Forge] Could not restore the previously active document: {ex.Message}");
                    }
                }
            }
        }
        else
        {
            tcs.TrySetResult(await DispatchCommandAsync(command, cancellationToken));
        }
    }

    /// <summary>
    /// Exact dispatch gate: only the Roslyn executor needs the genuinely awaited path; every
    /// other tool completes synchronously inside the AutoCAD command context.
    /// </summary>
    private Task<ForgeResult> DispatchCommandAsync(ForgeCommand command, CancellationToken cancellationToken)
        => ForgeAsyncDispatch.RequiresAwaitedDispatch(command.Tool)
            ? _processor.ProcessAsync(command, cancellationToken)
            : Task.FromResult(_processor.Process(command));

    private static async Task ObserveCallbackAsync(
        Task callbackTask,
        TaskCompletionSource<ForgeResult> tcs,
        string commandId)
    {
        try
        {
            await callbackTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            tcs.TrySetResult(ForgeResult.Failure(commandId, "plugin_exception", ex.Message));
        }
    }

    private static List<Document> ResolveDocuments(string document)
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
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException or System.Security.SecurityException)
        {
            // Callers fall back to comparing the raw document name, so null is handled explicitly.
            return null;
        }
    }
}
