using System.Globalization;

namespace Forge.Shared;

// forge_undo — verified undo (fix-smoke-20261029 D3). AutoCAD leaves one undo unit per command-context
// call, even for reads and dry runs, so a single "_.U" may pop an empty unit, a zoom, a plot's layout
// switch, or a user's edit instead of the Forge write. This file holds the pure decision logic: what one
// tracked write changed (its footprint), the per-drawing stack of footprints (the ledger), and the undo
// loop, which runs "_.U" through an IUndoHost, classifies what each U touched, verifies the revert from
// real state, and redoes every U it ran when anything is not proven.

/// <summary>Live/erased state of an object, read back from the drawing.</summary>
public enum UndoObjectState
{
    Live,
    Erased,
    Missing
}

/// <summary>Kinds of database object events Forge records.</summary>
public enum UndoEventKind
{
    Appended,
    Modified,
    Erased,
    Unerased,
    Unappended,
    Reappended
}

/// <summary>
/// One database object event. Flags are set by the plugin from the object's type. IsDerived marks
/// graphics AutoCAD regenerates on its own (the contents of anonymous dimension and table blocks,
/// *D and *T): they get new handles on every redo (headless h6), so they are tracked through their
/// owning block and dimension/table entity, never by handle.
/// </summary>
public sealed record UndoObjectEvent(
    UndoEventKind Kind,
    long Handle,
    string ClassName,
    bool IsContainer = false,
    bool IsAssoc = false,
    bool IsViewportTableRecord = false,
    bool IsDerived = false,
    bool IsTableRecord = false)
{
    public string Describe() => $"{Kind.ToString().ToLowerInvariant()} {ClassName} {Handle.ToString("X", CultureInfo.InvariantCulture)}";
}

/// <summary>Events captured during one U, or during one Forge call.</summary>
public sealed class UndoEventSet
{
    public List<UndoObjectEvent> Objects { get; } = new();

    /// <summary>Changed system variables (upper case).</summary>
    public HashSet<string> Sysvars { get; } = new(StringComparer.OrdinalIgnoreCase);

    public UndoEventSet Filtered()
    {
        var copy = new UndoEventSet();
        copy.Objects.AddRange(Objects.Where(e => !e.IsAssoc && !e.IsDerived));
        foreach (var name in Sysvars.Where(n => !UndoPolicy.IsVolatileSysvar(n)))
        {
            copy.Sysvars.Add(name);
        }

        return copy;
    }

    public bool IsEmpty => Objects.Count == 0 && Sysvars.Count == 0;

    public HashSet<long> Handles() => Objects.Select(o => o.Handle).ToHashSet();
}

/// <summary>Constants and small rules shared by the recorder, the ledger and the verifier.</summary>
public static class UndoPolicy
{
    public const int MaxEmptyUnits = 256;
    public const int MaxSkippableUnits = 8;
    public const int MaxLedgerDepth = 32;
    public const int MaxHandlesPerFootprint = 200_000;
    public const long MaxHandlesPerLedger = 400_000;
    public const long LoopBudgetMs = 20_000;

    /// <summary>
    /// Sysvars that change as a side effect of running any command or of Forge's own command
    /// plumbing. They are ignored in footprints and in every U classification.
    /// </summary>
    public static readonly IReadOnlySet<string> VolatileSysvars = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "CMDNAMES", "CMDACTIVE", "CMDECHO", "LASTPROMPT", "DBMOD", "UNDOCTL", "UNDOMARKS", "LASTPOINT",
        "ERRNO", "NOMUTT", "EXPERT", "FILEDIA", "CMDDIA", "DIASTAT", "BACKGROUNDPLOT", "SECURELOAD"
    };

    /// <summary>Sysvars a zoom or pan changes. A U that touches only these and the active viewport record is a view change.</summary>
    public static readonly IReadOnlySet<string> ViewSysvars = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "VIEWCTR", "VIEWSIZE", "VIEWDIR", "VIEWTWIST", "VIEWMODE", "TARGET", "LENSLENGTH", "FRONTZ", "BACKZ",
        "VSMIN", "VSMAX", "VIEWBACKSTATUS", "EXTMIN", "EXTMAX"
    };

    /// <summary>Tools whose non-tracked call may leave an undo unit that forge_undo can step over (§D3.2).</summary>
    public static readonly IReadOnlySet<string> SideEffectTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "forge_plot_to_pdf",
        "forge_view_capture"
    };

    public static bool IsVolatileSysvar(string name) => VolatileSysvars.Contains(name);

    /// <summary>
    /// Events that never mark a drawing changed: AutoCAD's idle Assoc updates, zoom/pan on the viewport
    /// table, and regenerated dimension/table graphics (a real change to those always also modifies the
    /// dimension or table entity itself).
    /// </summary>
    public static bool IsIgnorableChange(UndoObjectEvent e)
        => e.IsAssoc || e.IsDerived || (e.Kind == UndoEventKind.Modified && e.IsViewportTableRecord);

    /// <summary>
    /// Internal objects AutoCAD creates on demand and never undoes (headless h6: the text font cache
    /// records a DXF merge added stayed live after its U). Treated like regenerated graphics.
    /// </summary>
    public static readonly IReadOnlySet<string> InternalClasses = new HashSet<string>(StringComparer.Ordinal)
    {
        "AcDbFontTableRecord"
    };

    public static bool IsInternalClass(string? rxClassName) => rxClassName is not null && InternalClasses.Contains(rxClassName);

    /// <summary>Anonymous blocks whose contents AutoCAD regenerates: dimensions (*D) and tables (*T).</summary>
    public static bool IsDerivedBlockName(string? blockName)
        => blockName is { Length: > 2 } && blockName[0] == '*' && (blockName[1] is 'D' or 'd' or 'T' or 't');

    /// <summary>Same comparison for every sysvar value check: exact text of the invariant-formatted value.</summary>
    public static bool SameValue(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal);
}

/// <summary>What one tracked write changed. Built while the write runs; read when forge_undo targets it.</summary>
public sealed class UndoFootprint
{
    public UndoFootprint(string tool, string commandId)
    {
        Tool = tool;
        CommandId = commandId;
    }

    public string Tool { get; }
    public string CommandId { get; }
    public HashSet<long> Appended { get; } = new();
    public HashSet<long> Erased { get; } = new();
    public HashSet<long> Unerased { get; } = new();
    public HashSet<long> Modified { get; } = new();

    /// <summary>Objects that may be touched by the revert but need not be (block table records, tables, dictionaries, viewport table records).</summary>
    public HashSet<long> Containers { get; } = new();

    /// <summary>Regenerated dimension/table graphics the write touched (see <see cref="UndoObjectEvent"/>); never checked by handle.</summary>
    public HashSet<long> Derived { get; } = new();

    public long HandseedBefore { get; set; }
    public long HandseedAfter { get; set; }
    public Dictionary<string, string?> SysvarPre { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string?> SysvarPost { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>State of each appended/erased/unerased handle read back when the write ended.</summary>
    public Dictionary<long, UndoObjectState> PostWriteState { get; } = new();

    public bool Overflow { get; set; }
    public string? DirtyReason { get; private set; }
    public List<UndoEventSet> SideEffects { get; } = new();

    public void MarkDirty(string reason) => DirtyReason ??= reason;

    public long HandleCount => (long)Appended.Count + Erased.Count + Unerased.Count + Modified.Count + Containers.Count;

    public bool InHandseedRange(long handle) => handle >= HandseedBefore && handle < HandseedAfter && HandseedAfter > HandseedBefore;

    /// <summary>Handles the write changed itself (not containers).</summary>
    public bool OwnsContent(long handle)
        => Appended.Contains(handle) || Erased.Contains(handle) || Unerased.Contains(handle) || Modified.Contains(handle) || InHandseedRange(handle);

    public bool Owns(long handle) => OwnsContent(handle) || Containers.Contains(handle);

    public bool HasContentHandles => Appended.Count + Erased.Count + Unerased.Count + Modified.Count > 0 || HandseedAfter > HandseedBefore;

    public bool IsEmpty => !HasContentHandles && SysvarPre.Count == 0;

    /// <summary>Adds one recorded event of the write. Returns false when the handle cap is reached.</summary>
    public bool Add(UndoObjectEvent e)
    {
        if (e.IsAssoc)
        {
            return true;
        }

        if (e.IsDerived)
        {
            Derived.Add(e.Handle);
            return true;
        }

        if (HandleCount >= UndoPolicy.MaxHandlesPerFootprint)
        {
            Overflow = true;
            return false;
        }

        switch (e.Kind)
        {
            case UndoEventKind.Appended:
            case UndoEventKind.Reappended:
                if (HandseedBefore > 0 && e.Handle < HandseedBefore)
                {
                    // An "append" of an object that already existed before the write. Headless h6 (xref
                    // attach): AutoCAD fires ObjectAppended for ~35 existing scales, regapps, layers and
                    // materials while resolving the xref; U leaves them alone. They are owned (so a U that
                    // touches them is not foreign) but are not something the write created.
                    Containers.Add(e.Handle);
                }
                else
                {
                    Appended.Add(e.Handle);
                }

                break;
            case UndoEventKind.Erased:
            case UndoEventKind.Unappended:
                if (!Appended.Contains(e.Handle))
                {
                    Erased.Add(e.Handle);
                }

                break;
            case UndoEventKind.Unerased:
                if (!Appended.Contains(e.Handle))
                {
                    Unerased.Add(e.Handle);
                }

                break;
            default:
                if (e.IsContainer || e.IsViewportTableRecord)
                {
                    Containers.Add(e.Handle);
                }
                else
                {
                    Modified.Add(e.Handle);
                }

                break;
        }

        return true;
    }

    /// <summary>Records a sysvar's value before its first change in the write.</summary>
    public void AddSysvarPre(string name, string? oldValue)
    {
        if (!UndoPolicy.IsVolatileSysvar(name) && !SysvarPre.ContainsKey(name))
        {
            SysvarPre[name] = oldValue;
        }
    }

    /// <summary>Called when the write ends: post values read back; a sysvar back at its pre value is dropped.</summary>
    public void SetSysvarPost(string name, string? value)
    {
        if (!SysvarPre.TryGetValue(name, out var pre))
        {
            return;
        }

        if (UndoPolicy.SameValue(pre, value))
        {
            SysvarPre.Remove(name);
            SysvarPost.Remove(name);
            return;
        }

        SysvarPost[name] = value;
    }

    /// <summary>Handles whose live/erased state is read back when the write ends.</summary>
    public IEnumerable<long> StateTrackedHandles() => Appended.Concat(Erased).Concat(Unerased).Distinct();

    public void FinishHandseed(long handseedAfter)
    {
        HandseedAfter = handseedAfter;
        if (HandseedAfter - HandseedBefore > UndoPolicy.MaxHandlesPerFootprint)
        {
            Overflow = true;
        }
    }
}

/// <summary>Per-drawing stack of tracked-write footprints.</summary>
public sealed class UndoLedgerState
{
    private readonly List<UndoFootprint> _stack = new();

    public int Depth => _stack.Count;
    public UndoFootprint? Top => _stack.Count == 0 ? null : _stack[^1];
    public int DroppedOldest { get; private set; }

    public IReadOnlyList<UndoFootprint> Entries => _stack;

    public void Push(UndoFootprint footprint)
    {
        _stack.Add(footprint);
        while (_stack.Count > UndoPolicy.MaxLedgerDepth || (_stack.Count > 1 && _stack.Sum(f => f.HandleCount) > UndoPolicy.MaxHandlesPerLedger))
        {
            _stack.RemoveAt(0);
            DroppedOldest++;
        }
    }

    public void Pop()
    {
        if (_stack.Count > 0)
        {
            _stack.RemoveAt(_stack.Count - 1);
        }
    }

    public void MarkAllDirty(string reason)
    {
        foreach (var entry in _stack)
        {
            entry.MarkDirty(reason);
        }
    }

    public void AttachSideEffect(UndoEventSet set)
    {
        Top?.SideEffects.Add(set);
    }

    /// <summary>
    /// Decides what a finished non-tracked Forge call means for the ledger: nothing, a side-effect record
    /// on the top entry, or "changed since the write" for every entry. Only object events count; sysvars
    /// are carried in side-effect records and checked by value at undo time.
    /// </summary>
    public string ApplyNonTrackedCall(string tool, UndoEventSet events)
    {
        if (string.Equals(tool, "forge_undo", StringComparison.OrdinalIgnoreCase) || Depth == 0)
        {
            return "ignored";
        }

        var changes = events.Objects.Where(e => !UndoPolicy.IsIgnorableChange(e)).ToList();
        if (changes.Count == 0)
        {
            return "no_change";
        }

        if (UndoPolicy.SideEffectTools.Contains(tool))
        {
            AttachSideEffect(events.Filtered());
            return "side_effect";
        }

        MarkAllDirty($"untracked_forge_change: {tool} ({changes[0].Describe()})");
        return "dirty";
    }

    /// <summary>An object event outside any Forge call.</summary>
    public bool ApplyExternalEvent(UndoObjectEvent e)
    {
        if (Depth == 0 || UndoPolicy.IsIgnorableChange(e))
        {
            return false;
        }

        MarkAllDirty($"external_change: {e.Describe()}");
        return true;
    }
}

/// <summary>The plugin side of the undo loop: AutoCAD commands and readbacks.</summary>
public interface IUndoHost
{
    /// <summary>Runs one "_.U" and returns the object and sysvar events it fired.</summary>
    UndoEventSet RunU();

    /// <summary>Runs "MREDO n".</summary>
    void RunMredo(int count);

    UndoObjectState GetState(long handle);

    /// <summary>
    /// True for regenerated dimension/table graphics (an entity owned by a *D or *T block) and internal
    /// cache objects. Read from the drawing when a handle-level check disagrees; AutoCAD recreates these
    /// with new handles on redo, so their own handles prove nothing either way.
    /// </summary>
    bool IsDerived(long handle);

    string? GetSysvar(string name);

    long NowMs();
}

public enum UndoUnitClass
{
    Empty,
    ZoomPan,
    SideEffect,
    Target,
    Foreign
}

public sealed record UndoOutcome
{
    public bool Undone { get; init; }
    public string? Code { get; init; }
    public string Message { get; init; } = "";
    public bool? Restored { get; init; }
    public int UCount { get; init; }
    public int RedoCount { get; init; }
    public int SkippedEmptyUnits { get; init; }
    public int SkippedViewUnits { get; init; }
    public int SkippedSideEffectUnits { get; init; }
    public bool NothingRecorded { get; init; }

    /// <summary>True when the ledger entry is finished (reverted, or nothing to revert).</summary>
    public bool PopEntry { get; init; }

    /// <summary>Set when the entry must be refused from now on (restore not proven).</summary>
    public string? MarkDirty { get; init; }

    public string? Reason { get; init; }
    public IReadOnlyList<string> Foreign { get; init; } = [];
    public IReadOnlyList<string> NotReverted { get; init; } = [];
    public IReadOnlyList<string> NotRestored { get; init; } = [];
    public IReadOnlyList<string> Classes { get; init; } = [];
}

public static class UndoVerifier
{
    public const string ChangedSinceWrite = "undo_target_changed_since_write";
    public const string FootprintTooLarge = "undo_footprint_too_large";
    public const string NothingRecordedCode = "undo_nothing_recorded";
    public const string TargetNotReverted = "undo_target_not_reverted";
    public const string Incomplete = "undo_incomplete";
    public const string NoEffect = "undo_no_effect";
    public const string StepFailed = "undo_step_failed";

    private static bool NotLive(UndoObjectState s) => s != UndoObjectState.Live;

    private static bool SameLiveness(UndoObjectState a, UndoObjectState b) => NotLive(a) == NotLive(b);

    /// <summary>Checks before any U: refuses when the write was changed or covered after it ran.</summary>
    public static UndoOutcome? PreCheck(UndoFootprint fp, IUndoHost host)
    {
        if (fp.Overflow)
        {
            return new UndoOutcome
            {
                Code = FootprintTooLarge,
                Message = $"The Forge write '{fp.Tool}' changed more than {UndoPolicy.MaxHandlesPerFootprint} objects; forge_undo cannot verify a revert that large, so nothing was undone.",
                Reason = "overflow"
            };
        }

        if (fp.DirtyReason is { } dirty)
        {
            return new UndoOutcome
            {
                Code = ChangedSinceWrite,
                Message = $"The drawing changed after the Forge write '{fp.Tool}' ({dirty}); undoing it now could undo that later change, so nothing was undone.",
                Reason = dirty
            };
        }

        if (fp.IsEmpty)
        {
            return new UndoOutcome
            {
                Code = NothingRecordedCode,
                Message = $"The Forge write '{fp.Tool}' recorded no change to the drawing; there is nothing to revert, and no U was run.",
                NothingRecorded = true,
                PopEntry = true
            };
        }

        foreach (var (name, post) in fp.SysvarPost)
        {
            var now = host.GetSysvar(name);
            if (!UndoPolicy.SameValue(now, post))
            {
                return new UndoOutcome
                {
                    Code = ChangedSinceWrite,
                    Message = $"{name} is '{now}' but the Forge write '{fp.Tool}' left it at '{post}'; it was changed afterwards, so nothing was undone.",
                    Reason = $"sysvar {name} changed since the write"
                };
            }
        }

        foreach (var (handle, post) in fp.PostWriteState)
        {
            var now = host.GetState(handle);
            if (!SameLiveness(now, post) && !host.IsDerived(handle))
            {
                return new UndoOutcome
                {
                    Code = ChangedSinceWrite,
                    Message = $"Object {handle:X} is {now} but the Forge write '{fp.Tool}' left it {post}; it was changed afterwards, so nothing was undone.",
                    Reason = $"object {handle:X} {post}->{now} since the write"
                };
            }
        }

        return null;
    }

    /// <summary>Classifies what one U touched (checks in the §D3.3 order).</summary>
    public static UndoUnitClass Classify(UndoEventSet raw, UndoFootprint fp, IReadOnlyList<UndoEventSet> sideEffects, ISet<int> consumed, out int sideEffectIndex)
    {
        sideEffectIndex = -1;
        var u = raw.Filtered();
        if (u.IsEmpty)
        {
            return UndoUnitClass.Empty;
        }

        if (u.Objects.All(o => o.Kind == UndoEventKind.Modified && o.IsViewportTableRecord) && u.Sysvars.All(UndoPolicy.ViewSysvars.Contains))
        {
            return UndoUnitClass.ZoomPan;
        }

        var handles = u.Handles();
        var touchesFootprintContent = handles.Any(fp.OwnsContent);
        if (!touchesFootprintContent && handles.Count > 0)
        {
            for (var i = sideEffects.Count - 1; i >= 0; i--)
            {
                if (consumed.Contains(i))
                {
                    continue;
                }

                var record = sideEffects[i];
                var recordHandles = record.Handles();
                if (handles.Overlaps(recordHandles) && handles.IsSubsetOf(recordHandles) && u.Sysvars.All(record.Sysvars.Contains))
                {
                    sideEffectIndex = i;
                    return UndoUnitClass.SideEffect;
                }
            }
        }

        // Within a unit that reverts footprint content, a modified container (block table record, symbol
        // table, dictionary) or symbol table record is part of the same undo group: AutoCAD changed it during
        // the write without an event (headless h6: a DXF merge's dictionary; h13: draw_dimension's dimension
        // style record). Elsewhere these modifications stay foreign.
        var allOwned = u.Objects.All(o => fp.Owns(o.Handle)
                                          || (touchesFootprintContent && o.Kind == UndoEventKind.Modified && (o.IsContainer || o.IsTableRecord)))
                       && u.Sysvars.All(fp.SysvarPre.ContainsKey);
        var touchesFootprint = fp.HasContentHandles
            ? touchesFootprintContent
            : u.Sysvars.Any(fp.SysvarPre.ContainsKey);
        return allOwned && touchesFootprint ? UndoUnitClass.Target : UndoUnitClass.Foreign;
    }

    /// <summary>After the target U: is the drawing back to its state before the write?</summary>
    public static List<string> VerifyReverted(UndoFootprint fp, UndoEventSet targetU, IUndoHost host)
    {
        var problems = new List<string>();
        var touched = targetU.Handles();

        // What the target U itself did to each handle, in event order. An object the U unappended is out of
        // the drawing even though its id is not flagged erased (h6: the xref block's BlockBegin/BlockEnd).
        var removedByU = new Dictionary<long, bool>();
        foreach (var o in targetU.Objects)
        {
            switch (o.Kind)
            {
                case UndoEventKind.Erased:
                case UndoEventKind.Unappended:
                    removedByU[o.Handle] = true;
                    break;
                case UndoEventKind.Unerased:
                case UndoEventKind.Reappended:
                case UndoEventKind.Appended:
                    removedByU[o.Handle] = false;
                    break;
            }
        }

        bool Gone(long handle) => NotLive(host.GetState(handle)) || (removedByU.TryGetValue(handle, out var removed) && removed);

        foreach (var handle in fp.Appended.Concat(fp.Unerased))
        {
            if (!Gone(handle) && !host.IsDerived(handle))
            {
                problems.Add($"{handle:X} still live");
            }
        }

        foreach (var handle in fp.Erased)
        {
            if (fp.InHandseedRange(handle))
            {
                continue; // created and erased inside the write: it did not exist before
            }

            if (NotLive(host.GetState(handle)) && !host.IsDerived(handle))
            {
                problems.Add($"{handle:X} still erased");
            }
        }

        if (fp.HandseedAfter > fp.HandseedBefore)
        {
            for (var handle = fp.HandseedBefore; handle < fp.HandseedAfter; handle++)
            {
                if (fp.Derived.Contains(handle))
                {
                    continue; // regenerated graphics: gone with their owning block, checked there
                }

                if (!Gone(handle) && !host.IsDerived(handle))
                {
                    problems.Add($"{handle:X} (created by the write) still live");
                }
            }
        }

        foreach (var handle in fp.Modified)
        {
            if (!touched.Contains(handle) && !fp.InHandseedRange(handle) && !host.IsDerived(handle))
            {
                problems.Add($"{handle:X} not reverted");
            }
        }

        foreach (var (name, pre) in fp.SysvarPre)
        {
            var now = host.GetSysvar(name);
            if (!UndoPolicy.SameValue(now, pre))
            {
                problems.Add($"{name}='{now}' (expected '{pre}')");
            }
        }

        return problems.Distinct().Take(50).ToList();
    }

    /// <summary>After MREDO: is the drawing back to the state it had before this call's U's?</summary>
    public static List<string> VerifyRestored(UndoFootprint fp, IReadOnlyList<UndoEventSet> uEvents, IUndoHost host)
    {
        var problems = new List<string>();
        foreach (var (handle, post) in fp.PostWriteState)
        {
            var now = host.GetState(handle);
            if (!SameLiveness(now, post) && !host.IsDerived(handle))
            {
                problems.Add($"{handle:X} is {now}, expected {post}");
            }
        }

        foreach (var (name, post) in fp.SysvarPost)
        {
            var now = host.GetSysvar(name);
            if (!UndoPolicy.SameValue(now, post))
            {
                problems.Add($"{name}='{now}' (expected '{post}')");
            }
        }

        // State each touched object had before the first U that touched it, from the event kind.
        var before = new Dictionary<long, UndoObjectState>();
        foreach (var set in uEvents)
        {
            foreach (var e in set.Objects)
            {
                if (before.ContainsKey(e.Handle))
                {
                    continue;
                }

                switch (e.Kind)
                {
                    case UndoEventKind.Unappended:
                    case UndoEventKind.Erased:
                        before[e.Handle] = UndoObjectState.Live;
                        break;
                    case UndoEventKind.Reappended:
                    case UndoEventKind.Unerased:
                        before[e.Handle] = UndoObjectState.Erased;
                        break;
                }
            }
        }

        foreach (var (handle, expected) in before)
        {
            var now = host.GetState(handle);
            if (!SameLiveness(now, expected) && !host.IsDerived(handle))
            {
                problems.Add($"{handle:X} is {now}, expected {expected}");
            }
        }

        return problems.Distinct().Take(50).ToList();
    }

    /// <summary>The whole forge_undo decision for the top ledger entry.</summary>
    public static UndoOutcome Run(UndoFootprint fp, IUndoHost host)
    {
        if (PreCheck(fp, host) is { } refused)
        {
            return refused;
        }

        var start = host.NowMs();
        var uEvents = new List<UndoEventSet>();
        var classes = new List<string>();
        var consumed = new HashSet<int>();
        int empty = 0, view = 0, side = 0;

        UndoOutcome Fail(string code, string message, IReadOnlyList<string>? foreign = null, IReadOnlyList<string>? notReverted = null, string? dirty = null)
        {
            var count = uEvents.Count;
            List<string> restoreProblems;
            try
            {
                host.RunMredo(count);
                restoreProblems = VerifyRestored(fp, uEvents, host);
            }
            catch (Exception ex)
            {
                restoreProblems = [$"MREDO {count} failed: {ex.Message}"];
            }

            var restored = restoreProblems.Count == 0;

            // What the restore check reads back: objects created/erased by the write or by the U's (live or
            // erased) and the write's sysvars. Objects the U's only modified are not compared after MREDO.
            return new UndoOutcome
            {
                Code = code,
                Message = message + (restored
                    ? $" All {count} U step(s) were redone (MREDO {count}); the created/erased objects and the write's sysvars read back as before the U's (modified-only objects are not compared)."
                    : $" MREDO {count} ran but the restore could not be confirmed; forge_undo will refuse this write from now on."),
                Restored = restored,
                UCount = count,
                RedoCount = count,
                SkippedEmptyUnits = empty,
                SkippedViewUnits = view,
                SkippedSideEffectUnits = side,
                MarkDirty = dirty ?? (restored ? null : "restore_unverified"),
                Foreign = foreign ?? [],
                NotReverted = notReverted ?? [],
                NotRestored = restoreProblems,
                Classes = classes
            };
        }

        while (true)
        {
            if (host.NowMs() - start > UndoPolicy.LoopBudgetMs)
            {
                return Fail(NoEffect, $"The Forge write '{fp.Tool}' was not reached within {UndoPolicy.LoopBudgetMs / 1000} s of undo steps.");
            }

            UndoEventSet u;
            try
            {
                u = host.RunU();
            }
            catch (Exception ex)
            {
                // The failed step may have changed the drawing without reporting it: redo the U's that did
                // report, read back, and refuse this write from now on whatever the readback says.
                return Fail(StepFailed, $"An undo step failed while undoing the Forge write '{fp.Tool}': {ex.Message}.", dirty: "undo_step_failed");
            }

            uEvents.Add(u);
            var cls = Classify(u, fp, fp.SideEffects, consumed, out var sideIndex);
            classes.Add(cls.ToString());
            switch (cls)
            {
                case UndoUnitClass.Empty:
                    if (++empty > UndoPolicy.MaxEmptyUnits)
                    {
                        return Fail(NoEffect, $"More than {UndoPolicy.MaxEmptyUnits} empty undo steps without reaching the Forge write '{fp.Tool}'.");
                    }

                    continue;
                case UndoUnitClass.ZoomPan:
                    if (++view + side > UndoPolicy.MaxSkippableUnits)
                    {
                        return Fail(NoEffect, $"More than {UndoPolicy.MaxSkippableUnits} view/side-effect undo steps without reaching the Forge write '{fp.Tool}'.");
                    }

                    continue;
                case UndoUnitClass.SideEffect:
                    consumed.Add(sideIndex);
                    if (view + ++side > UndoPolicy.MaxSkippableUnits)
                    {
                        return Fail(NoEffect, $"More than {UndoPolicy.MaxSkippableUnits} view/side-effect undo steps without reaching the Forge write '{fp.Tool}'.");
                    }

                    continue;
                case UndoUnitClass.Target:
                {
                    var problems = VerifyReverted(fp, u, host);
                    if (problems.Count > 0)
                    {
                        return Fail(Incomplete, $"The undo step that touched the Forge write '{fp.Tool}' did not fully revert it.", notReverted: problems);
                    }

                    return new UndoOutcome
                    {
                        Undone = true,
                        Message = $"The Forge write '{fp.Tool}' was undone and read back as reverted.",
                        UCount = uEvents.Count,
                        SkippedEmptyUnits = empty,
                        SkippedViewUnits = view,
                        SkippedSideEffectUnits = side,
                        PopEntry = true,
                        Classes = classes
                    };
                }

                default:
                {
                    var filtered = u.Filtered();
                    var foreign = filtered.Objects.Where(o => !fp.Owns(o.Handle)).Select(o => o.Describe())
                        .Concat(filtered.Sysvars.Where(n => !fp.SysvarPre.ContainsKey(n)).Select(n => "sysvar " + n))
                        .Distinct().Take(20).ToList();
                    return Fail(TargetNotReverted, $"An undo step above the Forge write '{fp.Tool}' changed something that write did not touch (a change made outside Forge).", foreign: foreign);
                }
            }
        }
    }
}
