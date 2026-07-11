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
    private readonly ForgeEnvironment _environment;
    private readonly PluginCommandProcessor _processor;
    private readonly CancellationTokenSource _shutdown = new();
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
                await HandleRequestAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[765T-Forge] Pipe error: {ex.Message}");
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

        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        ForgeResult result;
        if (string.IsNullOrWhiteSpace(line))
        {
            result = ForgeResult.Failure("", "empty_request", "No command was received over the named pipe.");
        }
        else
        {
            try
            {
                var command = JsonSerializer.Deserialize<ForgeCommand>(line, ForgeJson.Options);
                result = command is null
                    ? ForgeResult.Failure("", "invalid_request", "Command JSON could not be parsed.")
                    : await ExecuteOnMainThreadAsync(command).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = ForgeResult.Failure("", "request_failed", ex.Message);
            }
        }

        var json = JsonSerializer.Serialize(result, ForgeJson.Options);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private Task<ForgeResult> ExecuteOnMainThreadAsync(ForgeCommand command)
    {
        var tcs = new TaskCompletionSource<ForgeResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        Application.DocumentManager.ExecuteInCommandContextAsync(_ =>
        {
            try
            {
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
