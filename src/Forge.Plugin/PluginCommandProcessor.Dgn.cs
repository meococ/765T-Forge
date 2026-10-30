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
//   EndDgnImport — application lane again (later Idle tick): evaluates the leg outcome,
//                 closes the temp document, restores the previous active document and the
//                 scoped sysvars, and produces the typed ForgeResult the client sees.
// The command leg itself runs in the temp document's command context, so the entity count
// and Database.SaveAs need no document lock.
public sealed partial class PluginCommandProcessor
{
    /// <summary>
    /// Per-call state shared between the two halves of forge_dgn_import. It travels to the
    /// Idle lane as the ForgeResult.Data sentinel (consumed by exact type, never
    /// serialized), and the command-context leg reports through it. On the branch that adds
    /// ProxyNoticeScope, the scope handle lives here as well so suppression spans the whole
    /// import window and is disposed in EndDgnImport.
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
        // lane before the temp document exists and disposed in EndDgnImport — it spans the
        // whole asynchronous import window, so the proxy dialog can never block mid-leg.
        internal ProxyNoticeScope? ProxyNotice { get; set; }

        // Command-leg reporting. LegStarted tells the lane the context callback was
        // dispatched (a started leg always runs to completion — the watchdog's contract);
        // Abandoned is set when the deadline passes before dispatch so a late context grant
        // can never start the write.
        public bool LegStarted { get; set; }
        public bool LegFinished { get; set; }
        public bool Abandoned { get; set; }
        public bool WrongDocument { get; set; }
        public string? ActiveAtDispatch { get; set; }
        public System.Exception? LegError { get; set; }
        public int EntityCount { get; set; }
        public int LayerCount { get; set; }
        public bool Saved { get; set; }
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
            // every named object with the DGN file name (mode 2). Restored in EndDgnImport.
            Application.SetSystemVariable("DGNIMPORTMODE", DgnImportPolicy.ImportIntoCurrentDrawing);
            run.TempDoc = Documents.Add("");
            Documents.MdiActiveDocument = run.TempDoc;

            // -DGNIMPORT prompt order (Autodesk Help, checked 2026-10): filename → design
            // model (Enter accepts the DGN default) → conversion units Master/Sub → mapping
            // setup (Enter accepts the default). Queue the command inside the temporary
            // document's OWN command context — Editor.CommandAsync is legal there; calling
            // it on this application lane is exactly the eInvalidInput this fixes.
            // ExecutionResult is a dispatch receipt, not a Task — discarded here exactly as
            // in ExecuteInCommandContextAsync; the outcome arrives through run state. If the
            // marshal never dispatches, LegFinished stays false and the Idle lane's deadline
            // path answers dgn_context_timeout.
            _ = Documents.ExecuteInCommandContextAsync(async _ =>
            {
                run.LegStarted = true;
                try
                {
                    if (run.Abandoned)
                    {
                        // The lane gave up before the context was granted — never start the
                        // write after the caller's deadline.
                        return;
                    }

                    if (!ReferenceEquals(Documents.MdiActiveDocument, run.TempDoc))
                    {
                        run.WrongDocument = true;
                        run.ActiveAtDispatch = Documents.MdiActiveDocument?.Name;
                        return;
                    }

                    await run.TempDoc.Editor.CommandAsync(
                        DgnImportPolicy.CommandArguments(run.DgnPath, run.ModelName, run.Units));

                    // Inside the temp document's own command context its database needs no
                    // lock — the earlier app-context SaveAs would have thrown eLockViolation
                    // even if the command had run.
                    (run.EntityCount, run.LayerCount) = CountModelSpace(run.TempDoc.Database);
                    if (run.EntityCount > 0)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(run.TargetPath)!);
                        run.TempDoc.Database.SaveAs(run.TargetPath, DwgVersion.Current);
                        run.Saved = true;
                    }
                }
                catch (System.Exception ex)
                {
                    run.LegError = ex;
                }
                finally
                {
                    run.LegFinished = true;
                }
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
            RestoreDgnContext(run);
            RestoreDgnSysvars(run);
            return ForgeResult.Failure(
                command.Id,
                "dgn_import_failed",
                $"-DGNIMPORT failed: {ex.Message}",
                "Verify the DGN is V8 and not password-protected. A large DGN can also exceed the plugin response timeout — raise FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS and retry with the same args.");
        }
    }

    /// <summary>
    /// D14: second half of forge_dgn_import, called by the Idle lane once the command leg
    /// finished (or once the deadline passed without the leg ever starting). Runs on the
    /// application context: closes the temp document and restores the previous active
    /// document on every path, restores the scoped sysvars, and maps the leg outcome to the
    /// same typed results the synchronous version returned.
    /// </summary>
    internal ForgeResult EndDgnImport(ForgeCommand command, DgnImportRun run, bool contextTimedOut = false)
    {
        try
        {
            RestoreDgnContext(run);

            if (contextTimedOut)
            {
                return FinishDgn(run, ForgeResult.Failure(
                    command.Id,
                    "dgn_context_timeout",
                    "AutoCAD did not grant the temporary document's command context before the request deadline; -DGNIMPORT never ran.",
                    "The temporary document was discarded without importing. Retry; if it repeats, a dialog or a running command is likely blocking the command line.",
                    data: new { run.TempClosed }));
            }

            if (run.WrongDocument)
            {
                return FinishDgn(run, ForgeResult.Failure(
                    command.Id,
                    "dgn_temp_document_not_active",
                    $"The temporary document was not the active document when the command context opened (active: {run.ActiveAtDispatch ?? "<none>"}); -DGNIMPORT was not run.",
                    "Another drawing grabbed activation between document creation and command dispatch. Close modal dialogs or palettes that switch drawings and retry.",
                    data: new { run.TempClosed }));
            }

            if (run.LegError is { } legError)
            {
                return FinishDgn(run, ForgeResult.Failure(
                    command.Id,
                    "dgn_import_failed",
                    $"-DGNIMPORT failed: {legError.Message}",
                    "Verify the DGN is V8 and not password-protected. A large DGN can also exceed the plugin response timeout — raise FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS and retry with the same args.",
                    data: new { run.TempClosed }));
            }

            if (run.EntityCount == 0)
            {
                return FinishDgn(run, ForgeResult.Failure(
                    command.Id,
                    "dgn_import_empty",
                    $"-DGNIMPORT finished but the fresh document's model space holds 0 entities (dgn: {run.DgnPath}, model: {run.ModelName ?? "<default>"}).",
                    "The DGN may use a differently named design model (pass modelName; the dialog-based DGNIMPORT lists them with '?') or the file may not be a readable V8 DGN.",
                    data: new { run.TempClosed }));
            }

            var outputBytes = new FileInfo(run.TargetPath).Length;
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
            RestoreDgnSysvars(run);
        }
    }

    // The previous active document first (so the temp close runs on a non-active doc), then
    // the temp document — both best-effort; tempDocumentClosed reports the truth.
    private static void RestoreDgnContext(DgnImportRun run)
    {
        try
        {
            if (run.Previous is not null)
            {
                Documents.MdiActiveDocument = run.Previous;
            }
        }
        catch
        {
            // Best-effort restore of the previously active document.
        }

        try
        {
            run.TempDoc?.CloseAndDiscard();
            run.TempClosed = true;
        }
        catch
        {
            // Best-effort close of the temporary document; reported via tempDocumentClosed.
        }
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
