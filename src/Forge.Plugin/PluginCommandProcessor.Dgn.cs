using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Forge.Shared;

namespace Forge.Plugin;

// docs/cad-job-tools.md C15 — forge_dgn_import. The import runs in a fresh temporary
// document so neither the source DGN nor the user's active drawing is ever modified;
// RequiresBackup is therefore false in the registry, and an existing target is backed up
// explicitly here before SaveAs (same rule as forge_doc_save).
//
// D14 root cause and shape: Editor.Command/Editor.CommandAsync only run inside a document's
// own command context — called from the application context (or from another document's
// command context) they throw eInvalidInput, which is why -DGNIMPORT never ran on either
// build. The handler therefore splits in two halves joined by the run state below:
//   DgnImport   — application lane: guards, backup, sysvar scopes, Documents.Add +
//                 MdiActiveDocument, then queues the import leg through
//                 DocumentCollection.ExecuteInCommandContextAsync and returns the run
//                 as a sentinel Data payload the Idle lane waits on.
//   EndDgnImport — application lane again (later Idle tick): evaluates the leg outcome
//                 and produces the typed ForgeResult the client sees.
// The command leg itself runs in the temp document's command context, so the entity count
// and Database.SaveAs need no document lock.
//
// D14-quiescence (artifacts/d14/fail-evidence-20261030.md): the first build ran the
// destructive half — CloseAndDiscard plus the active-doc/sysvar restores — on the first
// Idle tick after the leg flagged LegFinished, which can fall inside the window where the
// command-context request is still unwinding; AutoCAD reported itself quiescent while the
// session it belonged to was mid-teardown, the close orphaned it, and AutoCAD never went
// quiescent again. The leg therefore runs synchronous Editor.Command (no nested async
// command session exists at all) and queues ONLY the ordering proof plus the sysvar/scope
// restore onto the application context (DocumentCollection.ExecuteInApplicationContext) —
// dispatched strictly after the doc-context request completes. r3 proved the document
// close cannot complete inside that callback (fail-evidence-r3), so the Idle lane — which
// demonstrably CAN close on a tick — waits for ContextRestored, lets one full quiescent
// cycle pass (SettleArmed), then retries TryRestoreDgnDocument on each tick until the
// deadline; on timeout it answers dgn_temp_document_leaked instead of ok:true, the
// leftover document staying open for the Terminate drain. EndDgnImport's own restores
// stay as the idempotent fallback for paths where no restore was queued (context timeout,
// failed marshal).
public sealed partial class PluginCommandProcessor
{
    /// <summary>
    /// Per-call state shared between the two halves of forge_dgn_import. It travels to the
    /// Idle lane as the ForgeResult.Data sentinel (consumed by exact type, never
    /// serialized), and the command-context leg reports through it. On the branch that adds
    /// ProxyNoticeScope, the scope handle lives here as well so suppression spans the whole
    /// import window and is disposed by the queued application-context restore (EndDgnImport
    /// on the paths where none was queued).
    /// </summary>
    internal sealed class DgnImportRun
    {
        public required string DgnPath { get; init; }
        public required string TargetPath { get; init; }
        public required string Units { get; init; }
        public required string? ModelName { get; init; }
        public required long SourceBytes { get; init; }
        public required string? TargetBackup { get; init; }
        public required Document? Previous { get; init; }
        public required object? PreviousFileDia { get; init; }
        public required object? PreviousDgnImportMode { get; init; }

        public Document? TempDoc { get; set; }
        public bool TempClosed { get; set; }
        public string? AuditId { get; set; }

        // D10 + r1 finding 5: the PROXYNOTICE suppression scope, entered on the application
        // lane before the temp document exists and disposed by the queued application-context
        // restore (EndDgnImport on the paths where none was queued) — it spans the whole
        // asynchronous import window, so the proxy dialog can never block mid-leg.
        internal ProxyNoticeScope? ProxyNotice { get; set; }

        // Command-leg reporting. LegStarted tells the lane the context callback was
        // dispatched (a started leg always runs to completion — the watchdog's contract);
        // Abandoned is set when the deadline passes before dispatch so a late context grant
        // can never start the write. LegFinished marks the end of the leg's own work;
        // ContextRestored is set by the queued application-context callback after the
        // sysvar/scope restore ran — the lane's ordering proof that the command session
        // fully unwound (the r4 callback runs NO document operations: r3 showed
        // CloseAndDiscard cannot complete inside that boundary). SettleArmed marks the
        // first ContextRestored-seeing Idle tick — the document close is attempted only
        // on ticks AFTER it (one full quiescent cycle later), retried until the deadline;
        // RestoreInFlight guards a nested Idle raise during a pumping close attempt;
        // SysvarsRestored makes the queued callback skip the sysvar half if the lane
        // already ran it on a timeout path.
        public bool LegStarted { get; set; }
        public bool LegFinished { get; set; }
        public bool ContextRestored { get; set; }
        public bool SettleArmed { get; set; }
        public bool RestoreInFlight { get; set; }
        public bool SysvarsRestored { get; set; }
        public bool Abandoned { get; set; }
        public bool WrongDocument { get; set; }
        public string? ActiveAtDispatch { get; set; }
        public System.Exception? LegError { get; set; }

        // The temp document's name captured at creation (Document.Name throws on a
        // disposed/closed document) — used by the leaked-document reporting fields.
        public string? TempDocName { get; set; }
        public int EntityCount { get; set; }
        public int LayerCount { get; set; }
    }

    private ForgeResult DgnImport(ForgeCommand command)
    {
        var args = Args<DgnImportArgs>(command);
        var guard = DgnImportPolicy.Validate(args);
        if (!guard.Allowed)
        {
            return ForgeResult.Failure(command.Id, guard.Code!, guard.Message!, guard.Suggestion);
        }

        var dgnPath = DgnImportPolicy.CanonicalPath(args.DgnPath!);
        var targetPath = DgnImportPolicy.CanonicalPath(args.TargetDwgPath!);
        var units = DgnImportPolicy.NormalizeUnits(args.Units);
        var modelName = string.IsNullOrWhiteSpace(args.ModelName) ? null : args.ModelName!.Trim();

        if (!File.Exists(dgnPath))
        {
            return ForgeResult.Failure(command.Id, "dgn_not_found", $"DGN file not found: {dgnPath}");
        }

        if (DgnImportPolicy.LooksLikeV7Header(TryReadHeader(dgnPath)))
        {
            return ForgeResult.Failure(
                command.Id,
                "dgn_v7_not_supported",
                $"{dgnPath} carries a MicroStation V7 DGN header; AutoCAD's importer takes V8 only.",
                "Convert the file to DGN V8 first (MicroStation or Bentley View 'Save As V8'), then retry forge_dgn_import.");
        }

        if (FindOpenDocumentByPath(targetPath) is not null)
        {
            return ForgeResult.Failure(
                command.Id,
                "dgn_target_document_open",
                $"Target DWG is currently open in the editor: {targetPath}",
                "Close that document or choose another targetDwgPath; SaveAs over an open document is refused.");
        }

        var targetExists = File.Exists(targetPath);
        var sourceBytes = new FileInfo(dgnPath).Length;

        if (command.DryRun)
        {
            return DryRun(command, new
            {
                dgnPath,
                sourceBytes,
                targetPath,
                targetExists,
                units,
                modelName = modelName ?? "<default design model>",
                mappingSetup = "default",
                args.OverwriteAcknowledged,
                note = "Import runs in a fresh temporary document; the source DGN and the active drawing are never modified."
            });
        }

        if (targetExists && !args.OverwriteAcknowledged)
        {
            return ForgeResult.Failure(
                command.Id,
                "dgn_target_overwrite_not_acknowledged",
                $"Refusing to overwrite existing target DWG: {targetPath}",
                "Pass overwriteAcknowledged=true after confirming the existing target is archived (a backup copy is still taken).");
        }

        string? targetBackup = null;
        if (targetExists)
        {
            targetBackup = _backupPlanner.TryBackup(targetPath);
        }

        var run = new DgnImportRun
        {
            DgnPath = dgnPath,
            TargetPath = targetPath,
            Units = units,
            ModelName = modelName,
            SourceBytes = sourceBytes,
            TargetBackup = targetBackup,
            Previous = Documents.MdiActiveDocument,
            PreviousFileDia = Application.GetSystemVariable("FILEDIA"),
            PreviousDgnImportMode = Application.GetSystemVariable("DGNIMPORTMODE")
        };

        // D10 + r1 finding 5: -DGNIMPORT converts a foreign file into a temporary document —
        // suppress the proxy dialog the same way the DxfIn/ReadDwgFile paths do, and fail
        // closed when it cannot be suppressed (no foreign load without suppression).
        run.ProxyNotice = ProxyNoticeScope.EnterOrFail(command, out var scopeFailure);
        if (scopeFailure is not null)
        {
            run.ProxyNotice?.Dispose();
            run.ProxyNotice = null;
            return scopeFailure;
        }

        try
        {
            Application.SetSystemVariable("FILEDIA", 0);
            // DGNIMPORTMODE is user-profile state: force "import into the current drawing"
            // or -DGNIMPORT would create a second document of its own (mode 0) or prefix
            // every named object with the DGN file name (mode 2). Restored by the queued
            // application-context restore (EndDgnImport where none was queued).
            Application.SetSystemVariable("DGNIMPORTMODE", DgnImportPolicy.ImportIntoCurrentDrawing);
            run.TempDoc = Documents.Add("");
            run.TempDocName = run.TempDoc.Name;
            Documents.MdiActiveDocument = run.TempDoc;

            // -DGNIMPORT prompt order (Autodesk Help, checked 2026-10): filename → design
            // model (Enter accepts the DGN default) → conversion units Master/Sub → mapping
            // setup (Enter accepts the default). Queue the command inside the temporary
            // document's OWN command context — Editor.Command is legal there; calling it
            // on this application lane is exactly the eInvalidInput this fixes.
            // ExecutionResult is a dispatch receipt, not a Task — discarded here exactly as
            // in ExecuteInCommandContextAsync; the outcome arrives through run state. If the
            // marshal never dispatches, LegFinished stays false and the Idle lane's deadline
            // path answers dgn_context_timeout.
            _ = Documents.ExecuteInCommandContextAsync(_ =>
            {
                run.LegStarted = true;
                try
                {
                    if (run.Abandoned)
                    {
                        // The lane gave up before the context was granted — never start the
                        // write after the caller's deadline.
                        return Task.CompletedTask;
                    }

                    if (!ReferenceEquals(Documents.MdiActiveDocument, run.TempDoc))
                    {
                        run.WrongDocument = true;
                        run.ActiveAtDispatch = Documents.MdiActiveDocument?.Name;
                        return Task.CompletedTask;
                    }

                    // Synchronous Editor.Command — D14-quiescence: CommandAsync nested a
                    // second async command session inside this doc-context request, and its
                    // bookkeeping could still be live when the callback completed. The sync
                    // command ends entirely inside the callback: when it returns, only this
                    // request's own unwind remains before the application context resumes.
                    run.TempDoc.Editor.Command(
                        DgnImportPolicy.CommandArguments(run.DgnPath, run.ModelName, run.Units));

                    // Inside the temp document's own command context its database needs no
                    // lock — the earlier app-context SaveAs would have thrown eLockViolation
                    // even if the command had run.
                    (run.EntityCount, run.LayerCount) = CountModelSpace(run.TempDoc.Database);
                    if (run.EntityCount > 0)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(run.TargetPath)!);
                        run.TempDoc.Database.SaveAs(run.TargetPath, DwgVersion.Current);
                    }
                }
                catch (System.Exception ex)
                {
                    run.LegError = ex;
                }
                finally
                {
                    run.LegFinished = true;
                    // D14-quiescence r4 (artifacts/d14/fail-evidence-r3-20261031.md): queue
                    // the ordering proof onto the application context — app-context work is
                    // dispatched only after this request fully completed, so the callback
                    // landing is what proves the unwind. The callback runs ONLY the sysvar
                    // and scope restore plus the ContextRestored flag: r3 showed the
                    // document half (Previous-activate + CloseAndDiscard) cannot complete
                    // inside this boundary, so the temp-document close was moved to a
                    // settled Idle tick — the lane arms on the tick that first sees
                    // ContextRestored and attempts the close on the next tick, retried
                    // until the request deadline. Skipped on the Abandoned return — the
                    // lane's timeout path already ran the cleanup itself, and no restore
                    // of a stale previous may land late.
                    if (!run.Abandoned)
                    {
                        try
                        {
                            Documents.ExecuteInApplicationContext(_ =>
                            {
                                try
                                {
                                    // Review r1 F2: if a timeout path already restored the
                                    // sysvars before this callback dispatched, do not
                                    // stomp anything a user changed after the answer.
                                    if (!run.SysvarsRestored)
                                    {
                                        RestoreDgnSysvars(run);
                                        run.SysvarsRestored = true;
                                    }
                                }
                                finally
                                {
                                    run.ContextRestored = true;
                                }
                            }, null);
                        }
                        catch
                        {
                            // The marshal itself could not be queued — unblock the lane;
                            // EndDgnImport's own sysvar restore is the fallback (pre-fix
                            // behaviour on a path that cannot normally fail).
                            run.ContextRestored = true;
                        }
                    }
                }

                return Task.CompletedTask;
            }, null);

            // Sentinel consumed by the Idle lane via Data's exact type — see
            // ExecuteDocActivateWhenIdleAsync. It must not reach a client, but the code is a
            // readable failure if a routing regression ever lets it.
            return ForgeResult.Failure(
                command.Id,
                "dgn_import_pending",
                "Internal scheduling marker: -DGNIMPORT was queued in the temporary document's command context.",
                data: run);
        }
        catch (System.Exception ex)
        {
            // The leg never queued (marshal threw before the sentinel returned) — no
            // command session exists, so the synchronous restore is still safe here.
            TryRestoreDgnDocument(run);
            RestoreDgnSysvars(run);
            run.SysvarsRestored = true;
            return ForgeResult.Failure(
                command.Id,
                "dgn_import_failed",
                $"-DGNIMPORT failed: {ex.Message}",
                "Verify the DGN is V8 and not password-protected. A large DGN can also exceed the plugin response timeout — raise FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS and retry with the same args.");
        }
    }

    /// <summary>
    /// D14: second half of forge_dgn_import, called by the Idle lane once the command leg
    /// finished, the queued application-context callback landed (ordering proof), one
    /// full quiescent cycle passed (SettleArmed), and the settled document restore either
    /// closed the temp document or the request deadline ran out (or once the deadline
    /// passed without the leg ever starting). Runs on the application context. The lane
    /// — not this method — owns the document close on the restored path: r3 showed
    /// CloseAndDiscard cannot run inside the app-context callback, so the lane attempts
    /// it on settled ticks only. The sysvar/scope restore stays an idempotent fallback
    /// here for the paths where no callback was queued.
    /// </summary>
    internal ForgeResult EndDgnImport(ForgeCommand command, DgnImportRun run, bool contextTimedOut = false, bool restoreTimedOut = false)
    {
        try
        {
            if (restoreTimedOut)
            {
                // D14-quiescence: the queued application-context callback never
                // dispatched, so ContextRestored never landed — nothing owns the
                // document close (it is deliberately NOT queued inside the callback:
                // r3 proved it cannot run there) and running it here could repeat the
                // mid-teardown close that wedged quiescence. The sysvar half is
                // session-independent and still runs in the finally; the temp document
                // stays open and the Terminate drain is the fallback.
                return FinishDgn(run, ForgeResult.Failure(
                    command.Id,
                    "dgn_context_restore_timeout",
                    "-DGNIMPORT finished but AutoCAD did not dispatch the queued application-context callback before the request deadline; the temporary document is still open.",
                    $"The leftover document ({run.TempDocName ?? "DrawingN.dwg"}) is empty and unsaved — close it by hand in AutoCAD, or leave it: the plugin's Terminate drain discards it at program close. Check for a dialog or a running command holding AutoCAD, then retry.",
                    data: new { tempDocumentClosed = run.TempClosed, tempDocumentLeaked = !run.TempClosed, tempDocumentName = run.TempDocName }));
            }

            if (contextTimedOut)
            {
                // No command session ever ran, so no session teardown can be orphaned —
                // the synchronous document restore is still safe on this path.
                TryRestoreDgnDocument(run);
                return FinishDgn(run, ForgeResult.Failure(
                    command.Id,
                    "dgn_context_timeout",
                    "AutoCAD did not grant the temporary document's command context before the request deadline; -DGNIMPORT never ran.",
                    "The temporary document was discarded without importing. Retry; if it repeats, a dialog or a running command is likely blocking the command line.",
                    data: new { tempDocumentClosed = run.TempClosed, tempDocumentLeaked = !run.TempClosed, tempDocumentName = run.TempDocName }));
            }

            if (run.WrongDocument)
            {
                return FinishDgn(run, ForgeResult.Failure(
                    command.Id,
                    "dgn_temp_document_not_active",
                    $"The temporary document was not the active document when the command context opened (active: {run.ActiveAtDispatch ?? "<none>"}); -DGNIMPORT was not run.",
                    "Another drawing grabbed activation between document creation and command dispatch. Close modal dialogs or palettes that switch drawings and retry.",
                    data: new { tempDocumentClosed = run.TempClosed, tempDocumentLeaked = !run.TempClosed, tempDocumentName = run.TempDocName }));
            }

            if (run.LegError is { } legError)
            {
                return FinishDgn(run, ForgeResult.Failure(
                    command.Id,
                    "dgn_import_failed",
                    $"-DGNIMPORT failed: {legError.Message}",
                    "Verify the DGN is V8 and not password-protected. A large DGN can also exceed the plugin response timeout — raise FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS and retry with the same args.",
                    data: new { tempDocumentClosed = run.TempClosed, tempDocumentLeaked = !run.TempClosed, tempDocumentName = run.TempDocName }));
            }

            if (run.EntityCount == 0)
            {
                return FinishDgn(run, ForgeResult.Failure(
                    command.Id,
                    "dgn_import_empty",
                    $"-DGNIMPORT finished but the fresh document's model space holds 0 entities (dgn: {run.DgnPath}, model: {run.ModelName ?? "<default>"}).",
                    "The DGN may use a differently named design model (pass modelName; the dialog-based DGNIMPORT lists them with '?') or the file may not be a readable V8 DGN.",
                    data: new { tempDocumentClosed = run.TempClosed, tempDocumentLeaked = !run.TempClosed, tempDocumentName = run.TempDocName }));
            }

            var outputBytes = new FileInfo(run.TargetPath).Length;
            if (!run.TempClosed)
            {
                // r4 honest reporting: the import itself succeeded but the settled close
                // retries never landed before the deadline — surface a distinct failure,
                // not ok:true with a footnote. The leftover document stays open (the
                // Terminate drain is the fallback) and the field reports it honestly.
                return FinishDgn(run, ForgeResult.Failure(
                    command.Id,
                    "dgn_temp_document_leaked",
                    $"-DGNIMPORT completed ({run.EntityCount} entities) but the temporary document ({run.TempDocName ?? "DrawingN.dwg"}) could not be closed before the request deadline.",
                    "The leftover document is empty and unsaved — close it by hand in AutoCAD, or leave it: the plugin's Terminate drain discards it at program close. A dialog or a running command may have blocked the close; check AutoCAD and retry.",
                    data: new
                    {
                        dgnPath = run.DgnPath,
                        targetPath = run.TargetPath,
                        units = run.Units,
                        modelName = run.ModelName,
                        entityCount = run.EntityCount,
                        layerCount = run.LayerCount,
                        sourceBytes = run.SourceBytes,
                        outputBytes,
                        targetBackup = run.TargetBackup,
                        tempDocumentClosed = run.TempClosed,
                        tempDocumentLeaked = true,
                        tempDocumentName = run.TempDocName,
                        proxynotice = run.ProxyNotice
                    }));
            }

            return FinishDgn(run, ForgeResult.Success(
                command.Id,
                new
                {
                    dgnPath = run.DgnPath,
                    targetPath = run.TargetPath,
                    units = run.Units,
                    modelName = run.ModelName,
                    entityCount = run.EntityCount,
                    layerCount = run.LayerCount,
                    sourceBytes = run.SourceBytes,
                    outputBytes,
                    targetBackup = run.TargetBackup,
                    tempDocumentClosed = run.TempClosed,
                    tempDocumentLeaked = false,
                    proxynotice = run.ProxyNotice
                },
                verification: new ForgeVerification
                {
                    Attempted = true,
                    Passed = File.Exists(run.TargetPath) && run.EntityCount > 0,
                    Message = "Model-space entity count read back after import; target file exists after SaveAs.",
                    ReadBack = new { run.EntityCount, run.LayerCount, outputBytes }
                }));
        }
        finally
        {
            // Session-independent half — also stamps SysvarsRestored so a late-landing
            // queued callback will not rewrite sysvars a user changed after the answer.
            if (!run.SysvarsRestored)
            {
                RestoreDgnSysvars(run);
                run.SysvarsRestored = true;
            }
        }
    }

    /// <summary>
    /// Plugin teardown path for a run still pending at program close — managed plugins
    /// cannot be unloaded mid-session, so Terminate is the only trigger. Closes the temp
    /// document and restores the scoped sysvars, best-effort throughout (at Terminate all
    /// documents are already destroyed, so the document half throws and is swallowed; the
    /// sysvar half is what does real work). Called from NamedPipePluginServer.Dispose on
    /// the same application context the Idle lane uses.
    /// </summary>
    internal void CleanupDgnImport(DgnImportRun run)
    {
        TryRestoreDgnDocument(run);
        RestoreDgnSysvars(run);
        run.SysvarsRestored = true;
    }

    /// <summary>
    /// The document half of the restore: re-activate the document that was active before
    /// the temp doc took over, then CloseAndDiscard the temp document. Both best-effort;
    /// <see cref="DgnImportRun.TempClosed"/> reports the close truth. Called ONLY by the
    /// Idle lane on settled ticks (after ContextRestored plus one full quiescent cycle,
    /// retried until the deadline), by the contextTimedOut path in EndDgnImport (no
    /// command session exists there), and by the Terminate drain — never inside the
    /// queued application-context callback, where r3 proved the close cannot complete.
    /// </summary>
    internal bool TryRestoreDgnDocument(DgnImportRun run)
    {
        // The previous active document first (so the temp close runs on a non-active
        // doc when one exists). If Previous is null (import started at zero documents)
        // or gone — closed meanwhile, the assignment throws — activate any other open
        // document instead of leaving the temp doc active for its close.
        var activated = false;
        if (run.Previous is not null)
        {
            try
            {
                Documents.MdiActiveDocument = run.Previous;
                activated = true;
            }
            catch
            {
                // Best-effort activation of the previously active document.
            }
        }

        if (!activated)
        {
            try
            {
                foreach (Document doc in Documents)
                {
                    if (!ReferenceEquals(doc, run.TempDoc))
                    {
                        Documents.MdiActiveDocument = doc;
                        break;
                    }
                }
            }
            catch
            {
                // Best-effort activation — the close attempt proceeds regardless.
            }
        }

        // Last-document case: nothing else is open, so the temp doc closes while active.
        // Precedent: the pre-fix Idle-lane close completed in exactly this configuration
        // (fail-evidence-20261030) — the wedge came from the mid-teardown boundary, not
        // from closing the last doc. If AutoCAD still refuses, the catch leaves the doc
        // open and the run reports dgn_temp_document_leaked.
        try
        {
            run.TempDoc?.CloseAndDiscard();
            run.TempClosed = true;
        }
        catch
        {
            // Best-effort close of the temporary document; reported via tempDocumentClosed.
        }

        return run.TempClosed;
    }

    private static void RestoreDgnSysvars(DgnImportRun run)
    {
        try
        {
            Application.SetSystemVariable("FILEDIA", run.PreviousFileDia);
        }
        catch
        {
            // Best-effort sysvar restore.
        }

        try
        {
            Application.SetSystemVariable("DGNIMPORTMODE", run.PreviousDgnImportMode);
        }
        catch
        {
            // Best-effort sysvar restore.
        }

        try
        {
            run.ProxyNotice?.Dispose();
        }
        catch
        {
            // Best-effort scope dispose — its inner sysvar restore is itself best-effort.
        }
    }

    private static ForgeResult FinishDgn(DgnImportRun run, ForgeResult result)
        => result with { AuditId = run.AuditId };

    private static Document? FindOpenDocumentByPath(string fullPath)
    {
        foreach (Document doc in Documents)
        {
            if (PathsEqual(doc.Name, fullPath))
            {
                return doc;
            }
        }

        return null;
    }

    private static (int Entities, int Layers) CountModelSpace(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var model = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        var entities = 0;
        foreach (ObjectId _ in model)
        {
            entities++;
        }

        var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        var layers = 0;
        foreach (ObjectId _ in layerTable)
        {
            layers++;
        }

        tr.Commit();
        return (entities, layers);
    }

    private static byte[]? TryReadHeader(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[2];
            var read = stream.Read(buffer, 0, buffer.Length);
            return read == buffer.Length ? buffer : buffer[..read];
        }
        catch
        {
            return null;
        }
    }
}
