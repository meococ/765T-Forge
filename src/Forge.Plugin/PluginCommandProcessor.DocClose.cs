using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Forge.Shared;

namespace Forge.Plugin;

// forge_doc_close (fix-smoke-20261029 D8) — close one open drawing, discarding or saving it, then read back
// that the Document is gone from AutoCAD's document list. Runs in APPLICATION context (the pipe server routes
// it through the Application.Idle marshal): a document cannot be closed from inside a command context.
// The target resolves through DocumentMatcher (exact full path or exact file name; ambiguity fails closed).
// After the close the Document object is only compared by reference, never touched.
public sealed partial class PluginCommandProcessor
{
    internal const string DocCloseUnconfirmedCode = "doc_close_unconfirmed";

    private sealed record DocCloseArgs
    {
        public string? Name { get; init; }
        public string? Path { get; init; }
        public bool Save { get; init; }
        public bool DiscardAcknowledged { get; init; }
    }

    /// <summary>Read-back payload; the pipe server keeps polling with <see cref="TargetDocument"/> (by reference).</summary>
    internal sealed record DocCloseReadBack(
        string Target,
        bool Closed,
        bool Saved,
        string? Active,
        object[] Documents)
    {
        public bool? Unsaved { get; init; }
        public string? SavedPath { get; init; }
        public string? BakPath { get; init; }
        public string? Backup { get; init; }

        [System.Text.Json.Serialization.JsonIgnore]
        public Document? TargetDocument { get; init; }
    }

    private ForgeResult DocClose(ForgeCommand command)
    {
        var args = Args<DocCloseArgs>(command);
        if (RejectControlChars(command, ("name", args.Name), ("path", args.Path)) is { } reject)
        {
            return reject;
        }

        var query = !string.IsNullOrWhiteSpace(args.Path) ? args.Path!.Trim() : args.Name?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return ForgeResult.Failure(command.Id, "missing_document", "name or path is required.", "Call forge_doc_list_open to see the open drawings.");
        }

        var docs = Documents.Cast<Document>().ToList();
        var names = docs.Select(d => d.Name).ToList();
        var matches = DocumentMatcher.Match(names, query);
        var active = Documents.MdiActiveDocument;
        var target = matches.Count == 1 ? docs[matches[0]] : null;

        var commandActive = target is not null && (HasCommandInProgress(target) || (active is not null && HasCommandInProgress(active)));
        var unsaved = target is null ? null : ReadUnsaved(target, active);
        var untitled = target is not null && !IsNamed(target);
        var readOnly = target is not null && IsReadOnlyDoc(target);
        var targetName = target?.Name ?? query;
        var isDwg = string.Equals(System.IO.Path.GetExtension(targetName), ".dwg", StringComparison.OrdinalIgnoreCase);

        var refusal = DocClosePolicy.Refusal(matches.Count, docs.Count, commandActive, unsaved, args.Save, args.DiscardAcknowledged, untitled, readOnly, isDwg);
        if (refusal is not null)
        {
            var refusalData = new
            {
                target = targetName,
                unsaved,
                commandActive,
                openDocuments = names,
                candidates = matches.Count > 1 ? matches.Select(i => names[i]).ToArray() : null
            };
            return refusal switch
            {
                DocClosePolicy.NotFound => ForgeResult.Failure(command.Id, refusal, $"Open document not found: {query}", "Call forge_doc_list_open to see the open drawings.", data: refusalData),
                DocClosePolicy.Ambiguous => ForgeResult.Failure(command.Id, refusal, $"Multiple open documents match '{query}'.", "Pass the full drawing path.", data: refusalData),
                DocClosePolicy.LastDocument => ForgeResult.Failure(command.Id, refusal, $"'{targetName}' is the only open drawing; Forge does not close the last one.", "Call forge_doc_new first, then close this drawing.", data: refusalData),
                DocClosePolicy.CommandActive => ForgeResult.Failure(command.Id, refusal, "An AutoCAD command is running in the target or the active drawing.", "Finish or cancel the command in AutoCAD, then retry.", data: refusalData),
                DocClosePolicy.DiscardNotAcknowledged => ForgeResult.Failure(
                    command.Id,
                    refusal,
                    unsaved is null
                        ? $"Could not read whether '{targetName}' has unsaved changes, so they would be lost."
                        : $"'{targetName}' has unsaved changes that would be lost.",
                    "Pass save=true to save it first, or discardAcknowledged=true to close it without saving. A drawing saved by forge_doc_save can still read as modified.",
                    data: refusalData),
                DocClosePolicy.SaveRequiresPath => ForgeResult.Failure(command.Id, refusal, $"'{targetName}' has never been saved, so save=true has no file to write.", "forge_doc_save path=\"...\\name.dwg\" first, or close with discardAcknowledged=true.", data: refusalData),
                DocClosePolicy.ReadOnly => ForgeResult.Failure(command.Id, refusal, $"'{targetName}' is open read-only and cannot be saved in place.", "Close it with discardAcknowledged=true, or forge_doc_save path= to another file first.", data: refusalData),
                _ => ForgeResult.Failure(command.Id, refusal, $"'{targetName}' is not a .dwg file; save=true would write DWG data under another extension.", "forge_doc_save path=\"...\\name.dwg\" first, or close with discardAcknowledged=true.", data: refusalData)
            };
        }

        var doc = target!;
        var closeName = doc.Name;
        if (command.DryRun)
        {
            return DryRun(command, new
            {
                target = closeName,
                save = args.Save,
                unsaved,
                discards = !args.Save && unsaved != false,
                activeBefore = active?.Name,
                openDocuments = names
            });
        }

        if (!Documents.IsApplicationContext)
        {
            return ForgeResult.Failure(command.Id, "doc_close_requires_application_context", "forge_doc_close must run in AutoCAD's application context; nothing was saved or closed.", "Call it through the Forge pipe server, which runs it when AutoCAD is idle.");
        }

        string? savedPath = null;
        string? bakPath = null;
        string? backup = null;
        if (args.Save)
        {
            var path = closeName;
            backup = File.Exists(path) ? _backupPlanner.TryBackup(path) : null;
            var startUtc = DateTime.UtcNow;
            try
            {
                using (doc.LockDocument())
                {
                    var db = doc.Database;
                    db.SaveAs(path, true, DwgVersion.Current, db.SecurityParameters);
                }
            }
            catch (System.Exception ex)
            {
                return ForgeResult.Failure(command.Id, "save_failed", $"Saving '{closeName}' failed, so it was not closed: {ex.Message}", "Check the folder is writable and the file is not open elsewhere.", data: new { target = closeName, backup });
            }

            var info = new FileInfo(path);
            info.Refresh();
            if (!DocumentReadBack.FileWritten(info.Exists, info.Exists ? info.Length : 0, info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue, startUtc))
            {
                return ForgeResult.Failure(command.Id, "save_unconfirmed", $"No new file was found at {path} after the save, so '{closeName}' was not closed.", "Read back with forge_doc_list_open; save with forge_doc_save before closing.", data: new { target = closeName, backup });
            }

            savedPath = path;
            var bak = System.IO.Path.ChangeExtension(path, ".bak");
            bakPath = File.Exists(bak) && File.GetLastWriteTimeUtc(bak) >= startUtc.AddSeconds(-2) ? bak : null;
        }

        try
        {
            doc.CloseAndDiscard();
        }
        catch (System.Exception ex)
        {
            var busy = ex.Message.Contains("busy", StringComparison.OrdinalIgnoreCase);
            return ForgeResult.Failure(
                command.Id,
                busy ? "doc_close_busy" : "doc_close_failed",
                $"AutoCAD did not close '{closeName}': {ex.Message}",
                "Read back with forge_doc_list_open; wait until AutoCAD is idle, then retry.",
                data: new { target = closeName, saved = savedPath is not null, savedPath, backup });
        }

        return ReadBackClosedDocument(command.Id, new DocCloseReadBack(closeName, false, savedPath is not null, null, [])
        {
            Unsaved = unsaved,
            SavedPath = savedPath,
            BakPath = bakPath,
            Backup = backup,
            TargetDocument = doc
        });
    }

    /// <summary>The close is confirmed only when the captured Document object is no longer in AutoCAD's document list.</summary>
    internal static ForgeResult ReadBackClosedDocument(string commandId, DocCloseReadBack pending)
    {
        var docs = Documents.Cast<Document>().ToList();
        var stillOpen = pending.TargetDocument is null || docs.Any(d => ReferenceEquals(d, pending.TargetDocument));
        var active = Documents.MdiActiveDocument;
        var data = pending with
        {
            Closed = !stillOpen,
            Active = active?.Name,
            Documents = docs.Select(d => (object)new { name = d.Name, isActive = ReferenceEquals(d, active) }).ToArray()
        };
        var verification = new ForgeVerification
        {
            Attempted = true,
            Passed = !stillOpen,
            Message = stillOpen ? "The drawing is still in AutoCAD's document list." : "The drawing is no longer in AutoCAD's document list.",
            ReadBack = new { documents = docs.Count, active = active?.Name }
        };

        return stillOpen
            ? ForgeResult.Failure(
                commandId,
                DocCloseUnconfirmedCode,
                $"AutoCAD still lists '{pending.Target}' as open.",
                "Call forge_doc_list_open to read the current state before retrying.",
                data: data,
                verification: verification)
            : ForgeResult.Success(commandId, data, verification: verification);
    }

    private static bool HasCommandInProgress(Document doc)
    {
        try
        {
            return !string.IsNullOrEmpty(doc.CommandInProgress);
        }
        catch
        {
            return true; // unknown counts as busy
        }
    }

    /// <summary>
    /// The target's own COM Saved flag, plus DBMOD when the target is active AND DWGNAME names it (sysvars can
    /// still belong to the previous drawing right after a switch). Unsaved if either says so; null if neither
    /// can be read (DocClosePolicy.Unsaved).
    /// </summary>
    private static bool? ReadUnsaved(Document target, Document? active)
    {
        bool? dbmodSaysModified = null;
        if (ReferenceEquals(target, active))
        {
            try
            {
                var dwgName = Application.GetSystemVariable("DWGNAME") as string;
                if (DocumentReadBack.IsConfirmed(true, target.Name, dwgName))
                {
                    dbmodSaysModified = System.Convert.ToInt32(Application.GetSystemVariable("DBMOD"), CultureInfo.InvariantCulture) != 0;
                }
            }
            catch
            {
                // Not readable: the COM flag decides, or the state stays unknown.
            }
        }

        bool? comSaved = null;
        try
        {
            dynamic com = target.GetAcadDocument();
            comSaved = (bool)com.Saved;
        }
        catch
        {
            // Not readable.
        }

        return DocClosePolicy.Unsaved(dbmodSaysModified, comSaved);
    }

    private static bool IsNamed(Document doc)
    {
        try
        {
            return doc.IsNamedDrawing;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsReadOnlyDoc(Document doc)
    {
        try
        {
            return doc.IsReadOnly;
        }
        catch
        {
            return true;
        }
    }
}
