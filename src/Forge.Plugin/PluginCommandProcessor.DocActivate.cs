using Autodesk.AutoCAD.ApplicationServices;
using Forge.Shared;

namespace Forge.Plugin;

// forge_doc_activate — make an open drawing the active one, then read back which document is active.
// Runs in APPLICATION context (the pipe server routes it through the Application.Idle marshal, also
// when no document is active): assigning MdiActiveDocument from a document command context is not
// safe. The target resolves through DocumentMatcher (exact full path or exact file name; more than
// one match fails closed as ambiguous_document — never a loose suffix match).
public sealed partial class PluginCommandProcessor
{
    internal const string DocActivateTool = "forge_doc_activate";
    internal const string DocActivateUnconfirmedCode = "doc_activate_unconfirmed";

    private sealed record DocActivateArgs
    {
        public string? Name { get; init; }
        public string? Path { get; init; }
    }

    /// <summary>
    /// Read-back payload; the pipe server keeps polling with <see cref="TargetDocument"/> (matched by
    /// reference, never by name) until AutoCAD reports the switch. Shared by forge_doc_activate,
    /// forge_doc_new and forge_doc_open (fix-smoke-20261029 D1).
    /// </summary>
    internal sealed record DocActivateReadBack(
        string Target,
        string? ActiveBefore,
        string? Active,
        bool Confirmed,
        bool Switched,
        object[] Documents)
    {
        public string? DwgName { get; init; }
        public string? Template { get; init; }
        public bool? AlreadyOpen { get; init; }

        [System.Text.Json.Serialization.JsonIgnore]
        public Document? TargetDocument { get; init; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string UnconfirmedCode { get; init; } = DocActivateUnconfirmedCode;
    }

    private static ForgeResult DocActivate(ForgeCommand command)
    {
        var args = Args<DocActivateArgs>(command);
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
        if (matches.Count == 0)
        {
            return ForgeResult.Failure(
                command.Id,
                "document_not_found",
                $"Open document not found: {query}",
                "forge_doc_activate only switches between drawings that are already open; use forge_doc_open to open one.",
                data: new { openDocuments = names });
        }

        if (matches.Count > 1)
        {
            return ForgeResult.Failure(
                command.Id,
                "ambiguous_document",
                $"Multiple open documents match '{query}'.",
                "Pass the full drawing path.",
                data: new { candidates = matches.Select(i => names[i]).ToArray() });
        }

        var target = docs[matches[0]];
        var before = Documents.MdiActiveDocument?.Name;
        if (command.DryRun)
        {
            return DryRun(command, new { target = target.Name, activeBefore = before, alreadyActive = ReferenceEquals(Documents.MdiActiveDocument, target) });
        }

        if (!ReferenceEquals(Documents.MdiActiveDocument, target))
        {
            Documents.MdiActiveDocument = target;
        }

        return ReadBackActiveDocument(command.Id, new DocActivateReadBack(target.Name, before, null, false, false, []) { TargetDocument = target });
    }

    /// <summary>
    /// Builds the activation result from what AutoCAD reports as active right now: the active document
    /// must be the target object (by reference) and DWGNAME must name its file.
    /// </summary>
    internal static ForgeResult ReadBackActiveDocument(string commandId, DocActivateReadBack pending)
    {
        var active = Documents.MdiActiveDocument;
        string? dwgName = null;
        try
        {
            dwgName = Application.GetSystemVariable("DWGNAME") as string;
        }
        catch
        {
            // No document: DWGNAME is unreadable.
        }

        var targetName = pending.TargetDocument is { } targetDoc ? SafeName(targetDoc) ?? pending.Target : pending.Target;
        var isTarget = pending.TargetDocument is { } t
            ? ReferenceEquals(active, t)
            : active is not null && string.Equals(active.Name, targetName, StringComparison.OrdinalIgnoreCase);
        var confirmed = DocumentReadBack.IsConfirmed(isTarget, targetName, dwgName);
        var data = pending with
        {
            Target = targetName,
            Active = active?.Name,
            Confirmed = confirmed,
            Switched = !string.Equals(pending.ActiveBefore, targetName, StringComparison.OrdinalIgnoreCase),
            Documents = Documents.Cast<Document>().Select(d => (object)new { name = d.Name, isActive = ReferenceEquals(d, active) }).ToArray(),
            DwgName = dwgName
        };
        var verification = new ForgeVerification
        {
            Attempted = true,
            Passed = confirmed,
            Message = confirmed ? "Active document and DWGNAME read back as the target drawing." : "AutoCAD has not reported the target drawing as active.",
            ReadBack = new { active = active?.Name, dwgName }
        };

        return confirmed
            ? ForgeResult.Success(commandId, data, verification: verification)
            : ForgeResult.Failure(
                commandId,
                pending.UnconfirmedCode,
                $"'{targetName}' is open but AutoCAD still reports '{active?.Name ?? "(none)"}' as active (DWGNAME '{dwgName ?? "?"}').",
                "Call forge_doc_list_open to read the current state, then forge_doc_activate the drawing you want before working on it.",
                data: data,
                verification: verification);
    }

    private static string? SafeName(Document doc)
    {
        try
        {
            return doc.Name;
        }
        catch
        {
            return null;
        }
    }
}
