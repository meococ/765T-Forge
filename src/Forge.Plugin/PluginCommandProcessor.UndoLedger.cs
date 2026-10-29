using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Forge.Shared;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Forge.Plugin;

// fix-smoke-20261029 D3 — the per-drawing undo ledger (FIX-PLAN §D3.1–D3.2). Records what each tracked
// write changed, and watches the drawing between that write and forge_undo: any object change made
// outside a Forge call marks the ledger "changed since the write", so forge_undo refuses instead of
// undoing someone else's work. The decisions live in Forge.Shared (UndoVerifier/UndoLedgerState).
public sealed partial class PluginCommandProcessor
{
    private sealed class DocLedger
    {
        public DocLedger(Document doc)
        {
            Doc = doc;
            Db = doc.Database;
        }

        public Document Doc { get; }
        public Database Db { get; }
        public UndoLedgerState State { get; } = new();
        public bool Subscribed { get; set; }
        public ObjectEventHandler? OnAppended { get; set; }
        public ObjectEventHandler? OnModified { get; set; }
        public ObjectErasedEventHandler? OnErased { get; set; }
        public ObjectEventHandler? OnUnappended { get; set; }
        public ObjectEventHandler? OnReappended { get; set; }
    }

    /// <summary>One Forge call, from the top of Process to the next idle tick (or the next call).</summary>
    private sealed class UndoCallScope
    {
        public UndoCallScope(string tool, Document? doc)
        {
            Tool = tool;
            Doc = doc;
        }

        public string Tool { get; }
        public Document? Doc { get; }
        public bool Tracked { get; set; }
        public bool TrackedSucceeded { get; set; }
        public UndoFootprint? Footprint { get; set; }
        public Database? FootprintDb { get; set; }
        public bool FootprintOpen { get; set; }
        public Dictionary<Database, UndoEventSet> Events { get; } = new(DbComparer.Instance);
        public HashSet<string> Sysvars { get; } = new(StringComparer.OrdinalIgnoreCase);
        public UndoEventSet? Capture { get; set; }
        public Database? CaptureDb { get; set; }

        public UndoEventSet EventsFor(Database db)
        {
            if (!Events.TryGetValue(db, out var set))
            {
                set = new UndoEventSet();
                Events[db] = set;
            }

            return set;
        }
    }

    /// <summary>
    /// Two managed Database wrappers may stand for the same drawing (headless r2 diag: the U events were
    /// lost when doc.Database and the ledger's wrapper were compared by reference). Compare the native object.
    /// </summary>
    private sealed class DbComparer : IEqualityComparer<Database>
    {
        public static readonly DbComparer Instance = new();

        public bool Equals(Database? x, Database? y) => SameDb(x, y);

        public int GetHashCode(Database obj) => NativeOf(obj).GetHashCode();
    }

    private static IntPtr NativeOf(Database? db)
    {
        try
        {
            return db is null || db.IsDisposed ? IntPtr.Zero : db.UnmanagedObject;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static bool SameDb(Database? a, Database? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        var na = NativeOf(a);
        return na != IntPtr.Zero && na == NativeOf(b);
    }

    private static readonly Dictionary<Document, DocLedger> Ledgers = new(ReferenceEqualityComparer.Instance);
    private static UndoCallScope? _undoScope;
    private static bool _documentHooks;
    private static bool _idleHooked;

    private static DocLedger LedgerFor(Document doc)
    {
        if (!_documentHooks)
        {
            _documentHooks = true;
            Documents.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
        }

        if (!Ledgers.TryGetValue(doc, out var ledger))
        {
            ledger = new DocLedger(doc);
            Ledgers[doc] = ledger;
        }

        return ledger;
    }

    private static DocLedger? ExistingLedger(Document? doc)
        => doc is not null && Ledgers.TryGetValue(doc, out var ledger) ? ledger : null;

    private static void OnDocumentToBeDestroyed(object? sender, DocumentCollectionEventArgs e)
    {
        try
        {
            if (e.Document is { } doc && Ledgers.TryGetValue(doc, out var ledger))
            {
                Unsubscribe(ledger);
                Ledgers.Remove(doc);
            }
        }
        catch
        {
            // Never let a cleanup handler reach AutoCAD.
        }
    }

    private static void Subscribe(DocLedger ledger)
    {
        if (ledger.Subscribed)
        {
            return;
        }

        ledger.OnAppended = (_, e) => OnLedgerEvent(ledger, UndoEventKind.Appended, e.DBObject);
        ledger.OnModified = (_, e) => OnLedgerEvent(ledger, UndoEventKind.Modified, e.DBObject);
        ledger.OnErased = (_, e) => OnLedgerEvent(ledger, e.Erased ? UndoEventKind.Erased : UndoEventKind.Unerased, e.DBObject);
        ledger.OnUnappended = (_, e) => OnLedgerEvent(ledger, UndoEventKind.Unappended, e.DBObject);
        ledger.OnReappended = (_, e) => OnLedgerEvent(ledger, UndoEventKind.Reappended, e.DBObject);
        ledger.Db.ObjectAppended += ledger.OnAppended;
        ledger.Db.ObjectModified += ledger.OnModified;
        ledger.Db.ObjectErased += ledger.OnErased;
        ledger.Db.ObjectUnappended += ledger.OnUnappended;
        ledger.Db.ObjectReappended += ledger.OnReappended;
        ledger.Subscribed = true;
    }

    private static void Unsubscribe(DocLedger ledger)
    {
        if (!ledger.Subscribed)
        {
            return;
        }

        try
        {
            ledger.Db.ObjectAppended -= ledger.OnAppended;
            ledger.Db.ObjectModified -= ledger.OnModified;
            ledger.Db.ObjectErased -= ledger.OnErased;
            ledger.Db.ObjectUnappended -= ledger.OnUnappended;
            ledger.Db.ObjectReappended -= ledger.OnReappended;
        }
        catch
        {
            // The database may already be going away.
        }

        ledger.Subscribed = false;
    }

    private static UndoObjectEvent DescribeEvent(UndoEventKind kind, DBObject obj)
    {
        var rx = obj.GetRXClass()?.Name ?? obj.GetType().Name;
        return new UndoObjectEvent(
            kind,
            obj.Handle.Value,
            rx,
            IsContainer: obj is BlockTableRecord or SymbolTable or DBDictionary,
            IsAssoc: rx.StartsWith("AcDbAssoc", StringComparison.Ordinal),
            IsViewportTableRecord: obj is ViewportTableRecord,
            IsDerived: IsDerivedGraphics(obj),
            IsTableRecord: obj is SymbolTableRecord);
    }

    private static bool IsDerivedGraphics(DBObject obj)
    {
        try
        {
            return UndoPolicy.IsInternalClass(obj.GetRXClass()?.Name)
                   || (obj is Entity entity && UndoPolicy.IsDerivedBlockName(entity.BlockName));
        }
        catch
        {
            return false;
        }
    }

    private static void OnLedgerEvent(DocLedger ledger, UndoEventKind kind, DBObject obj)
    {
        try
        {
            var e = DescribeEvent(kind, obj);
            if (_undoScope is { } scope)
            {
                if (scope.Capture is { } capture && SameDb(scope.CaptureDb, ledger.Db))
                {
                    capture.Objects.Add(e);
                }

                if (scope.FootprintOpen && scope.Footprint is { } fp && SameDb(scope.FootprintDb, ledger.Db))
                {
                    fp.Add(e);
                }
                else
                {
                    scope.EventsFor(ledger.Db).Objects.Add(e);
                }

                return;
            }

            ledger.State.ApplyExternalEvent(e);
        }
        catch
        {
            // An event handler must never throw into AutoCAD.
        }
    }

    private static void OnUndoSysvarChanging(object? sender, Autodesk.AutoCAD.ApplicationServices.SystemVariableChangingEventArgs e)
    {
        try
        {
            if (_undoScope is { FootprintOpen: true, Footprint: { } fp } && !UndoPolicy.IsVolatileSysvar(e.Name))
            {
                fp.AddSysvarPre(e.Name.ToUpperInvariant(), FormatSysvar(Application.GetSystemVariable(e.Name)));
            }
        }
        catch
        {
            // Best effort: a missing pre value makes the later verification fail honestly.
        }
    }

    private static void OnUndoSysvarChanged(object? sender, Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventArgs e)
    {
        try
        {
            if (_undoScope is not { } scope)
            {
                return;
            }

            var name = e.Name.ToUpperInvariant();
            scope.Capture?.Sysvars.Add(name);
            if (scope.FootprintOpen && scope.Footprint is { } fp)
            {
                fp.AddSysvarPre(name, null); // no-op when Changing already recorded the old value
            }
            else
            {
                scope.Sysvars.Add(name);
            }
        }
        catch
        {
            // Never throw into AutoCAD.
        }
    }

    /// <summary>Invariant text of a sysvar value, so values compare exactly across reads.</summary>
    internal static string? FormatSysvar(object? value) => value switch
    {
        null => null,
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        Point3d p => string.Create(CultureInfo.InvariantCulture, $"({p.X:R},{p.Y:R},{p.Z:R})"),
        Point2d p => string.Create(CultureInfo.InvariantCulture, $"({p.X:R},{p.Y:R})"),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private static UndoObjectState ReadState(Database db, long handle)
    {
        try
        {
            if (!db.TryGetObjectId(new Handle(handle), out var id) || id.IsNull)
            {
                return UndoObjectState.Missing;
            }

            return id.IsErased ? UndoObjectState.Erased : UndoObjectState.Live;
        }
        catch
        {
            return UndoObjectState.Missing;
        }
    }

    /// <summary>Opens the call scope at the top of Process. A scope still open from the previous call is closed first.</summary>
    private static void BeginUndoCallScope(string tool)
    {
        CloseUndoCallScope();
        _undoScope = new UndoCallScope(tool, Documents.MdiActiveDocument);
        Application.SystemVariableChanging += OnUndoSysvarChanging;
        Application.SystemVariableChanged += OnUndoSysvarChanged;
        if (!_idleHooked)
        {
            _idleHooked = true;
            Application.Idle += OnIdleCloseUndoScope;
        }
    }

    private static void OnIdleCloseUndoScope(object? sender, EventArgs e)
    {
        Application.Idle -= OnIdleCloseUndoScope;
        _idleHooked = false;
        try
        {
            CloseUndoCallScope();
        }
        catch
        {
            // Never throw into AutoCAD.
        }
    }

    /// <summary>
    /// Ends the current call scope: tail events of a tracked write join its footprint; events of other
    /// calls become a side-effect record or mark the ledger changed. The headless harness calls this
    /// between steps because it has no idle tick.
    /// </summary>
    internal static void CloseUndoCallScope()
    {
        var scope = _undoScope;
        if (scope is null)
        {
            return;
        }

        _undoScope = null;
        Application.SystemVariableChanging -= OnUndoSysvarChanging;
        Application.SystemVariableChanged -= OnUndoSysvarChanged;

        foreach (var ledger in Ledgers.Values.ToList())
        {
            if (!scope.Events.TryGetValue(ledger.Db, out var events))
            {
                if (ledger.State.Depth == 0)
                {
                    Unsubscribe(ledger);
                }

                continue;
            }

            if (scope.Tracked && SameDb(scope.FootprintDb, ledger.Db))
            {
                if (scope.TrackedSucceeded && scope.Footprint is { } fp)
                {
                    // Events after UNDO _E belong to the same call; they may be reverted with it.
                    foreach (var e in events.Objects.Where(o => !UndoPolicy.IsIgnorableChange(o)))
                    {
                        fp.Containers.Add(e.Handle);
                    }
                }
            }
            else
            {
                if (SameDb(scope.Doc?.Database, ledger.Db))
                {
                    foreach (var name in scope.Sysvars)
                    {
                        events.Sysvars.Add(name);
                    }
                }

                ledger.State.ApplyNonTrackedCall(scope.Tool, events);
            }

            if (ledger.State.Depth == 0)
            {
                Unsubscribe(ledger);
            }
        }
    }

    /// <summary>WithUndoMark: starts recording the footprint just before UNDO _BE.</summary>
    private static UndoFootprint? OpenFootprint(string tool, string commandId)
    {
        if (_undoScope is not { } scope || Documents.MdiActiveDocument is not { } doc)
        {
            return null;
        }

        var ledger = LedgerFor(doc);
        Subscribe(ledger);
        var fp = new UndoFootprint(tool, commandId) { HandseedBefore = ledger.Db.Handseed.Value };
        scope.Tracked = true;
        scope.Footprint = fp;
        scope.FootprintDb = ledger.Db;
        scope.FootprintOpen = true;
        return fp;
    }

    /// <summary>WithUndoMark: ends the footprint just after UNDO _E, reads back post-write state, pushes or marks.</summary>
    private static void CloseFootprint(UndoFootprint? fp, bool succeeded)
    {
        if (fp is null || _undoScope is not { } scope || scope.FootprintDb is not { } db)
        {
            return;
        }

        scope.FootprintOpen = false;
        var ledger = Ledgers.Values.FirstOrDefault(l => SameDb(l.Db, db));
        if (ledger is null)
        {
            return;
        }

        try
        {
            fp.FinishHandseed(db.Handseed.Value);
            foreach (var name in fp.SysvarPre.Keys.ToList())
            {
                object? value = null;
                try
                {
                    value = Application.GetSystemVariable(name);
                }
                catch
                {
                    // Unreadable now: compare as null.
                }

                fp.SetSysvarPost(name, FormatSysvar(value));
            }

            foreach (var handle in fp.StateTrackedHandles())
            {
                fp.PostWriteState[handle] = ReadState(db, handle);
            }
        }
        catch
        {
            fp.Overflow = true; // could not read back: never undo this write by guesswork
        }

        if (succeeded)
        {
            scope.TrackedSucceeded = true;
            ledger.State.Push(fp);
        }
        else if (!fp.IsEmpty)
        {
            ledger.State.MarkAllDirty($"failed_write_changed_drawing: {fp.Tool}");
        }
    }

    /// <summary>forge_undo's view of AutoCAD: U/MREDO with event capture, and state readback.</summary>
    private sealed class AcadUndoHost : IUndoHost
    {
        private readonly Database _db;
        private readonly Editor _editor;
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

        public AcadUndoHost(Database db, Editor editor)
        {
            _db = db;
            _editor = editor;
        }

        public UndoEventSet RunU()
        {
            var capture = new UndoEventSet();
            if (_undoScope is { } scope)
            {
                scope.Capture = capture;
                scope.CaptureDb = _db;
            }

            try
            {
                _editor.Command("_.U");
            }
            finally
            {
                if (_undoScope is { } s)
                {
                    s.Capture = null;
                    s.CaptureDb = null;
                }
            }

            return capture;
        }

        public void RunMredo(int count)
        {
            if (count > 0)
            {
                _editor.Command("_.MREDO", count.ToString(CultureInfo.InvariantCulture));
            }
        }

        public UndoObjectState GetState(long handle) => ReadState(_db, handle);

        public bool IsDerived(long handle)
        {
            try
            {
                if (!_db.TryGetObjectId(new Handle(handle), out var id) || id.IsNull)
                {
                    return false;
                }

                using var tr = _db.TransactionManager.StartOpenCloseTransaction();
                var obj = tr.GetObject(id, OpenMode.ForRead, true);
                if (UndoPolicy.IsInternalClass(obj.GetRXClass()?.Name))
                {
                    return true;
                }

                return obj is Entity
                       && !obj.OwnerId.IsNull
                       && tr.GetObject(obj.OwnerId, OpenMode.ForRead, true) is BlockTableRecord owner
                       && UndoPolicy.IsDerivedBlockName(owner.Name);
            }
            catch
            {
                return false;
            }
        }

        public string? GetSysvar(string name)
        {
            try
            {
                return FormatSysvar(Application.GetSystemVariable(name));
            }
            catch
            {
                return null;
            }
        }

        public long NowMs() => _clock.ElapsedMilliseconds;
    }
}
