namespace Forge.Shared;

/// <summary>fix-smoke-20261029-r2 D13: where a tool call is dispatched for a given document state.</summary>
public enum DocDispatchDecision
{
    /// <summary>Document lifecycle tool: always the application-context Idle marshal.</summary>
    ApplicationLane,

    /// <summary>Document-free tool, zero open documents: the zero-document answer (synthesized where possible).</summary>
    ZeroDocumentLane,

    /// <summary>Document-free tool, documents open but none active: the Idle marshal (no command context needed).</summary>
    IdleLane,

    /// <summary>Zero open documents: a command context cannot exist — fail fast with no_active_document.</summary>
    FailNoActiveDocument,

    /// <summary>Documents exist but none is active: a command context cannot exist — fail fast with no_command_context.</summary>
    FailNoCommandContext,

    /// <summary>An active document exists: run in its command context.</summary>
    CommandContext
}

/// <summary>
/// fix-smoke-20261029 D1: tools that create, open, switch or close documents must run in AutoCAD's
/// application context (the plugin's Application.Idle lane). Inside a document's command context AutoCAD
/// defers the document switch until that command ends, so the new drawing never became active.
/// </summary>
public static class DocumentContextPolicy
{
    public static readonly IReadOnlySet<string> ApplicationContextTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "forge_doc_activate",
        "forge_doc_close",
        "forge_doc_new",
        "forge_doc_open"
    };

    /// <summary>
    /// smoke-r2 D13: tools that answer while AutoCAD sits on the Start tab. With zero documents
    /// they take the zero-document lane (health/doc_list_open get a synthesized answer, version the
    /// idle marshal); with documents open but none active they ride the Idle marshal.
    /// </summary>
    public static readonly IReadOnlySet<string> ZeroDocumentTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "forge_system_health",
        "forge_system_version",
        "forge_doc_list_open"
    };

    /// <summary>Tools that create, open or close a drawing: they may need longer than a plain switch to settle.</summary>
    public static readonly IReadOnlySet<string> LifecycleTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "forge_doc_close",
        "forge_doc_new",
        "forge_doc_open"
    };

    /// <summary>Failure codes that mean "done, but AutoCAD has not yet reported the drawing as active": keep polling.</summary>
    public static readonly IReadOnlySet<string> UnconfirmedCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "doc_activate_unconfirmed",
        "doc_close_unconfirmed",
        "doc_new_unconfirmed",
        "doc_open_unconfirmed"
    };

    public const int ActivatePollMs = 2000;

    /// <summary>Safety margin kept before the request deadline when a lifecycle tool polls.</summary>
    public const int LifecycleDeadlineMarginMs = 5000;

    public static bool RunsInApplicationContext(string tool) => ApplicationContextTools.Contains(tool);

    /// <summary>
    /// smoke-r2 D13 decision table: tool x open-document count x active document -> dispatch lane or
    /// fail-fast code. Zero documents means a command context can never exist, so document tools fail
    /// immediately (no_active_document) instead of waiting out the request deadline; documents open
    /// but none active keep the no_command_context refusal for the same reason; the lifecycle tools
    /// always take the application lane and the Start-tab diagnostics take the document-free lanes.
    /// </summary>
    public static DocDispatchDecision Route(string tool, int documentCount, bool hasActiveDocument)
    {
        if (RunsInApplicationContext(tool))
        {
            return DocDispatchDecision.ApplicationLane;
        }

        if (!hasActiveDocument)
        {
            if (ZeroDocumentTools.Contains(tool))
            {
                return documentCount == 0 ? DocDispatchDecision.ZeroDocumentLane : DocDispatchDecision.IdleLane;
            }

            return documentCount == 0 ? DocDispatchDecision.FailNoActiveDocument : DocDispatchDecision.FailNoCommandContext;
        }

        return DocDispatchDecision.CommandContext;
    }

    /// <summary>
    /// The zero-document lane answer for a Start-tab tool, or null when the tool needs the Idle
    /// marshal instead (forge_system_version reports host build data only AutoCAD can give).
    /// </summary>
    public static ForgeResult? ZeroDocumentAnswer(string commandId, string tool)
        => tool.ToLowerInvariant() switch
        {
            "forge_system_health" => ForgeResult.Success(commandId, new
            {
                status = "ok",
                server = ForgeConstants.ServerName,
                documents = 0,
                activeDocument = (string?)null,
                hint = "AutoCAD is on the Start tab with no drawing open. Call forge_doc_new or forge_doc_open before using document tools; forge_system_health, forge_system_version and forge_doc_list_open still answer."
            }),
            "forge_doc_list_open" => ForgeResult.Success(commandId, new { documents = Array.Empty<object>() }),
            _ => null
        };

    /// <summary>How long (ms since the request started) the Idle lane may keep re-reading the active document.</summary>
    public static long PollWindowMs(string tool, long requestDeadlineMs)
        => LifecycleTools.Contains(tool)
            ? Math.Max(ActivatePollMs, requestDeadlineMs - LifecycleDeadlineMarginMs)
            : ActivatePollMs;
}

/// <summary>Pure readback decisions for document activation (D1) and save-as (D7).</summary>
public static class DocumentReadBack
{
    /// <summary>
    /// The target drawing is confirmed active only when AutoCAD's active document is the same object
    /// AND DWGNAME names its file (DWGNAME lagged behind in r1).
    /// </summary>
    public static bool IsConfirmed(bool activeIsTarget, string? targetName, string? dwgName)
        => activeIsTarget
           && !string.IsNullOrWhiteSpace(targetName)
           && string.Equals(FileName(targetName), dwgName?.Trim(), StringComparison.OrdinalIgnoreCase);

    public const string SaveConfirmed = "confirmed";
    public const string SaveNotRenamed = "not_renamed";
    public const string SaveNotWritten = "not_written";

    /// <summary>
    /// forge_doc_save readback: the file must exist, be non-empty and be written during this call, and the
    /// open document must now carry the target path (document name, database file name and DWGNAME).
    /// </summary>
    public static string SaveDecision(
        string targetPath,
        string? documentName,
        string? databaseFilename,
        string? dwgName,
        bool fileExists,
        long fileLength,
        DateTime fileWriteUtc,
        DateTime callStartUtc)
    {
        if (!FileWritten(fileExists, fileLength, fileWriteUtc, callStartUtc))
        {
            return SaveNotWritten;
        }

        return SamePath(documentName, targetPath)
               && SamePath(databaseFilename, targetPath)
               && string.Equals(FileName(targetPath), dwgName?.Trim(), StringComparison.OrdinalIgnoreCase)
            ? SaveConfirmed
            : SaveNotRenamed;
    }

    /// <summary>The file exists, is non-empty and was written during this call.</summary>
    public static bool FileWritten(bool fileExists, long fileLength, DateTime fileWriteUtc, DateTime callStartUtc)
        // File times on NTFS are precise, but allow for coarse file systems.
        => fileExists && fileLength > 0 && fileWriteUtc >= callStartUtc.AddSeconds(-2);

    public static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(a.Trim()), Path.GetFullPath(b.Trim()), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string FileName(string path)
    {
        var trimmed = path.Trim();
        var cut = trimmed.LastIndexOfAny(['\\', '/']);
        return cut >= 0 ? trimmed[(cut + 1)..] : trimmed;
    }
}

/// <summary>
/// fix-smoke-20261029 D8: the refusals of forge_doc_close, decided before anything is saved or closed.
/// Unsaved state that could not be read counts as unsaved.
/// </summary>
public static class DocClosePolicy
{
    public const string NotFound = "document_not_found";
    public const string Ambiguous = "ambiguous_document";
    public const string LastDocument = "last_document";
    public const string CommandActive = "command_active";
    public const string DiscardNotAcknowledged = "discard_not_acknowledged";
    public const string SaveRequiresPath = "save_requires_path";
    public const string ReadOnly = "document_read_only";
    public const string NotDwg = "invalid_path";

    /// <summary>
    /// Unsaved state of the drawing to close, from two sources: DBMOD (only when DWGNAME read on the same
    /// tick names the target, because sysvars can still belong to the previous drawing right after a
    /// switch) and the target's own COM Saved flag. Unsaved if either says so; null if neither could be read.
    /// </summary>
    public static bool? Unsaved(bool? dbmodSaysModified, bool? comSaved)
    {
        if (dbmodSaysModified == true || comSaved == false)
        {
            return true;
        }

        return dbmodSaysModified == false || comSaved == true ? false : null;
    }

    /// <returns>The refusal code, or null when the close may go ahead.</returns>
    public static string? Refusal(
        int matchCount,
        int openCount,
        bool commandActive,
        bool? unsaved,
        bool save,
        bool discardAcknowledged,
        bool untitled,
        bool readOnly,
        bool isDwg)
    {
        if (matchCount == 0)
        {
            return NotFound;
        }

        if (matchCount > 1)
        {
            return Ambiguous;
        }

        if (openCount <= 1)
        {
            return LastDocument;
        }

        if (commandActive)
        {
            return CommandActive;
        }

        if (save)
        {
            if (untitled)
            {
                return SaveRequiresPath;
            }

            if (readOnly)
            {
                return ReadOnly;
            }

            return isDwg ? null : NotDwg;
        }

        return unsaved != false && !discardAcknowledged ? DiscardNotAcknowledged : null;
    }
}
