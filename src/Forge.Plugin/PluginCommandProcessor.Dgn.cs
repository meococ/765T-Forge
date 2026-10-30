using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Forge.Shared;

namespace Forge.Plugin;

// docs/cad-job-tools.md C15 — forge_dgn_import. The import runs in a fresh temporary
// document so neither the source DGN nor the user's active drawing is ever modified;
// RequiresBackup is therefore false in the registry, and an existing target is backed up
// explicitly here before SaveAs (same rule as forge_doc_save).
public sealed partial class PluginCommandProcessor
{
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

        var previous = Documents.MdiActiveDocument;
        var previousFileDia = Application.GetSystemVariable("FILEDIA");
        // D10: -DGNIMPORT converts a foreign file into a temporary document — suppress the
        // proxy dialog the same way the DxfIn/ReadDwgFile paths do (DGN content can carry
        // proxy/embedded objects through the conversion).
        using var proxyNotice = ProxyNoticeScope.Enter();
        Document? tempDoc = null;
        var tempClosed = false;

        void RestoreContext()
        {
            try
            {
                if (previous is not null)
                {
                    Documents.MdiActiveDocument = previous;
                }
            }
            catch
            {
                // Best-effort restore of the previously active document.
            }

            try
            {
                tempDoc?.CloseAndDiscard();
                tempClosed = true;
            }
            catch
            {
                // Best-effort close of the temporary document; reported via tempDocumentClosed.
            }
        }

        try
        {
            Application.SetSystemVariable("FILEDIA", 0);
            tempDoc = Documents.Add("");
            Documents.MdiActiveDocument = tempDoc;

            // -DGNIMPORT prompt order (Autodesk Help, checked 2026-10): filename → design
            // model (Enter accepts the DGN default) → conversion units Master/Sub → mapping
            // setup (Enter accepts the default). Editor.Command maps an empty string to Enter.
            tempDoc.Editor.Command(
                "-DGNIMPORT",
                dgnPath,
                (object?)(modelName ?? ""),
                units,
                "");

            var (entityCount, layerCount) = CountModelSpace(tempDoc.Database);
            if (entityCount == 0)
            {
                RestoreContext();
                return ForgeResult.Failure(
                    command.Id,
                    "dgn_import_empty",
                    $"-DGNIMPORT finished but the fresh document's model space holds 0 entities (dgn: {dgnPath}, model: {modelName ?? "<default>"}).",
                    "The DGN may use a differently named design model (pass modelName; the dialog-based DGNIMPORT lists them with '?') or the file may not be a readable V8 DGN.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            tempDoc.Database.SaveAs(targetPath, DwgVersion.Current);
            var outputBytes = new FileInfo(targetPath).Length;

            RestoreContext();

            return ForgeResult.Success(
                command.Id,
                new
                {
                    dgnPath,
                    targetPath,
                    units,
                    modelName,
                    entityCount,
                    layerCount,
                    sourceBytes,
                    outputBytes,
                    targetBackup,
                    tempDocumentClosed = tempClosed,
                    proxynotice = proxyNotice
                },
                verification: new ForgeVerification
                {
                    Attempted = true,
                    Passed = File.Exists(targetPath) && entityCount > 0,
                    Message = "Model-space entity count read back after import; target file exists after SaveAs.",
                    ReadBack = new { entityCount, layerCount, outputBytes }
                });
        }
        catch (System.Exception ex)
        {
            RestoreContext();
            return ForgeResult.Failure(
                command.Id,
                "dgn_import_failed",
                $"-DGNIMPORT failed: {ex.Message}",
                "Verify the DGN is V8 and not password-protected. A large DGN can also exceed the plugin response timeout — raise FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS and retry with the same args.");
        }
        finally
        {
            try
            {
                Application.SetSystemVariable("FILEDIA", previousFileDia);
            }
            catch
            {
                // Best-effort sysvar restore.
            }
        }
    }

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
