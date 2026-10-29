using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// fix-smoke-20261029 D3: verified forge_undo. The pure ledger/verifier logic runs against a fake AutoCAD
/// undo stack, so each rule (pre-checks, unit classes, revert and restore readback) is proven without CAD.
/// The live behaviour is covered by the headless runs in artifacts\fix-smoke-20261029\diag\undo-h-new.txt.
/// </summary>
public sealed class UndoVerifierTests
{
    private const long Seed = 0x2C1;

    /// <summary>One undo unit of the fake stack: events it fires and the state it leaves on U and on redo.</summary>
    private sealed class FakeUnit
    {
        public List<UndoObjectEvent> Events { get; } = new();
        public HashSet<string> Sysvars { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<long, UndoObjectState> OnUndo { get; } = new();
        public Dictionary<long, UndoObjectState> OnRedo { get; } = new();
        public Dictionary<string, string?> SysvarsOnUndo { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string?> SysvarsOnRedo { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool RedoFails { get; init; }
    }

    private sealed class FakeHost : IUndoHost
    {
        private readonly List<FakeUnit> _stack;
        private readonly Stack<FakeUnit> _undone = new();
        private long _now;

        public FakeHost(params FakeUnit[] unitsTopFirst) => _stack = unitsTopFirst.ToList();

        public Dictionary<long, UndoObjectState> State { get; } = new();
        public Dictionary<string, string?> Vars { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<long> Derived { get; } = new();
        public int UCalls { get; private set; }
        public int MredoTotal { get; private set; }
        public long MsPerU { get; set; } = 1;
        public int ThrowOnU { get; set; }
        public bool MredoThrows { get; set; }

        public UndoEventSet RunU()
        {
            UCalls++;
            _now += MsPerU;
            if (UCalls == ThrowOnU)
            {
                throw new InvalidOperationException("U failed");
            }

            var set = new UndoEventSet();
            if (_stack.Count == 0)
            {
                return set;
            }

            var unit = _stack[0];
            _stack.RemoveAt(0);
            _undone.Push(unit);
            foreach (var (h, s) in unit.OnUndo)
            {
                State[h] = s;
            }

            foreach (var (n, v) in unit.SysvarsOnUndo)
            {
                Vars[n] = v;
            }

            set.Objects.AddRange(unit.Events);
            foreach (var n in unit.Sysvars)
            {
                set.Sysvars.Add(n);
            }

            return set;
        }

        public void RunMredo(int count)
        {
            MredoTotal += count;
            if (MredoThrows)
            {
                throw new InvalidOperationException("MREDO failed");
            }

            for (var i = 0; i < count && _undone.Count > 0; i++)
            {
                var unit = _undone.Pop();
                _stack.Insert(0, unit);
                if (unit.RedoFails)
                {
                    continue;
                }

                foreach (var (h, s) in unit.OnRedo)
                {
                    State[h] = s;
                }

                foreach (var (n, v) in unit.SysvarsOnRedo)
                {
                    Vars[n] = v;
                }
            }
        }

        public UndoObjectState GetState(long handle) => State.TryGetValue(handle, out var s) ? s : UndoObjectState.Missing;

        public bool IsDerived(long handle) => Derived.Contains(handle);

        public string? GetSysvar(string name) => Vars.TryGetValue(name, out var v) ? v : null;

        public long NowMs() => _now;
    }

    private static UndoObjectEvent Ev(UndoEventKind kind, long handle, string cls = "AcDbLine", bool container = false, bool tableRecord = false, bool derived = false, bool vtr = false)
        => new(kind, handle, cls, IsContainer: container, IsViewportTableRecord: vtr, IsDerived: derived, IsTableRecord: tableRecord);

    /// <summary>A tracked draw_line: appends one line in the model-space block (container 1F).</summary>
    private static (UndoFootprint Fp, FakeUnit Unit, FakeHost Host) DrawLine(params FakeUnit[] above)
    {
        var fp = new UndoFootprint("forge_draw_line", "cmd1") { HandseedBefore = Seed };
        fp.Add(Ev(UndoEventKind.Appended, Seed));
        fp.Add(Ev(UndoEventKind.Modified, 0x1F, "AcDbBlockTableRecord", container: true));
        fp.FinishHandseed(Seed + 1);
        fp.PostWriteState[Seed] = UndoObjectState.Live;

        var unit = new FakeUnit();
        unit.Events.Add(Ev(UndoEventKind.Modified, 0x1F, "AcDbBlockTableRecord", container: true));
        unit.Events.Add(Ev(UndoEventKind.Unappended, Seed));
        unit.Events.Add(Ev(UndoEventKind.Erased, Seed));
        unit.OnUndo[Seed] = UndoObjectState.Erased;
        unit.OnRedo[Seed] = UndoObjectState.Live;

        var host = new FakeHost(above.Append(unit).ToArray());
        host.State[Seed] = UndoObjectState.Live;
        return (fp, unit, host);
    }

    [Fact]
    public void EmptyUnitsAreSkippedUntilTheTargetIsRevertedAndReadBack()
    {
        var (fp, _, host) = DrawLine(new FakeUnit(), new FakeUnit(), new FakeUnit());

        var outcome = UndoVerifier.Run(fp, host);

        Assert.True(outcome.Undone);
        Assert.Null(outcome.Code);
        Assert.Equal(4, outcome.UCount);
        Assert.Equal(3, outcome.SkippedEmptyUnits);
        Assert.True(outcome.PopEntry);
        Assert.Equal(new[] { "Empty", "Empty", "Empty", "Target" }, outcome.Classes);
        Assert.Equal(UndoObjectState.Erased, host.State[Seed]);
        Assert.Equal(0, host.MredoTotal);
    }

    [Fact]
    public void AForeignUnitAboveTheWriteIsRedoneAndNothingIsUndone()
    {
        var foreign = new FakeUnit();
        foreign.Events.Add(Ev(UndoEventKind.Appended, 0x500));
        foreign.OnUndo[0x500] = UndoObjectState.Erased;
        foreign.OnRedo[0x500] = UndoObjectState.Live;
        var (fp, _, host) = DrawLine(foreign);
        host.State[0x500] = UndoObjectState.Live;

        var outcome = UndoVerifier.Run(fp, host);

        Assert.False(outcome.Undone);
        Assert.Equal(UndoVerifier.TargetNotReverted, outcome.Code);
        Assert.True(outcome.Restored);
        Assert.Equal(1, host.MredoTotal);
        Assert.Equal(UndoObjectState.Live, host.State[0x500]);
        Assert.Equal(UndoObjectState.Live, host.State[Seed]);
        Assert.Contains(outcome.Foreign, f => f.Contains("500", StringComparison.Ordinal));
        Assert.Null(outcome.MarkDirty);
        Assert.False(outcome.PopEntry);
    }

    [Fact]
    public void AnOutsideSysvarChangeInTheWayIsForeign()
    {
        var foreign = new FakeUnit();
        foreign.Sysvars.Add("LTSCALE");
        var (fp, _, host) = DrawLine(foreign);

        var outcome = UndoVerifier.Run(fp, host);

        Assert.Equal(UndoVerifier.TargetNotReverted, outcome.Code);
        Assert.Contains("sysvar LTSCALE", outcome.Foreign);
        Assert.True(outcome.Restored);
    }

    [Fact]
    public void ATargetUnitThatLeavesTheLineLiveIsIncompleteAndRedone()
    {
        var (fp, unit, host) = DrawLine();
        // The U touched the line but left it in the drawing.
        unit.Events.Clear();
        unit.Events.Add(Ev(UndoEventKind.Modified, 0x1F, "AcDbBlockTableRecord", container: true));
        unit.Events.Add(Ev(UndoEventKind.Modified, Seed));
        unit.OnUndo.Clear();

        var outcome = UndoVerifier.Run(fp, host);

        Assert.False(outcome.Undone);
        Assert.Equal(UndoVerifier.Incomplete, outcome.Code);
        Assert.Contains(outcome.NotReverted, p => p.Contains("2C1", StringComparison.Ordinal));
        Assert.Equal(1, host.MredoTotal);
    }

    [Fact]
    public void AFailedRestoreMarksTheEntrySoItIsRefusedFromNowOn()
    {
        var foreign = new FakeUnit { RedoFails = true };
        foreign.Events.Add(Ev(UndoEventKind.Unappended, 0x500));
        foreign.OnUndo[0x500] = UndoObjectState.Erased;
        var (fp, _, host) = DrawLine(foreign);
        host.State[0x500] = UndoObjectState.Live;

        var outcome = UndoVerifier.Run(fp, host);

        Assert.Equal(UndoVerifier.TargetNotReverted, outcome.Code);
        Assert.False(outcome.Restored);
        Assert.Equal("restore_unverified", outcome.MarkDirty);
        Assert.Contains(outcome.NotRestored, p => p.Contains("500", StringComparison.Ordinal));
    }

    [Fact]
    public void TheLoopStopsAfterTheEmptyUnitLimit()
    {
        var empties = Enumerable.Range(0, UndoPolicy.MaxEmptyUnits + 5).Select(_ => new FakeUnit()).ToArray();
        var (fp, _, host) = DrawLine(empties);

        var outcome = UndoVerifier.Run(fp, host);

        Assert.Equal(UndoVerifier.NoEffect, outcome.Code);
        Assert.Equal(UndoPolicy.MaxEmptyUnits + 1, outcome.UCount);
        Assert.Equal(UndoPolicy.MaxEmptyUnits + 1, host.MredoTotal);
        Assert.True(outcome.Restored);
    }

    [Fact]
    public void TheLoopStopsAtTheTimeBudget()
    {
        var empties = Enumerable.Range(0, 50).Select(_ => new FakeUnit()).ToArray();
        var (fp, _, host) = DrawLine(empties);
        host.MsPerU = UndoPolicy.LoopBudgetMs / 5;

        var outcome = UndoVerifier.Run(fp, host);

        Assert.Equal(UndoVerifier.NoEffect, outcome.Code);
        Assert.Equal(6, outcome.UCount); // 6 x 4 s passes the 20 s budget
        Assert.True(outcome.Restored);
    }

    [Fact]
    public void ZoomAndSideEffectUnitsAreSteppedOverWithinTheirLimit()
    {
        var zoom = new FakeUnit();
        zoom.Events.Add(Ev(UndoEventKind.Modified, 0x29, "AcDbViewportTableRecord", vtr: true));
        zoom.Sysvars.Add("VIEWCTR");

        // A plot's layout switch (side-effect record attached to the write) appended nine objects.
        var side = new FakeUnit();
        side.Events.Add(Ev(UndoEventKind.Unappended, 0x700, "AcDbViewport"));
        side.Events.Add(Ev(UndoEventKind.Modified, 0x701, "AcDbLayout"));
        side.Sysvars.Add("TILEMODE");
        var (fp, _, host) = DrawLine(zoom, side);
        var record = new UndoEventSet();
        record.Objects.Add(Ev(UndoEventKind.Appended, 0x700, "AcDbViewport"));
        record.Objects.Add(Ev(UndoEventKind.Modified, 0x701, "AcDbLayout"));
        record.Sysvars.Add("TILEMODE");
        fp.SideEffects.Add(record);

        var outcome = UndoVerifier.Run(fp, host);

        Assert.True(outcome.Undone);
        Assert.Equal(new[] { "ZoomPan", "SideEffect", "Target" }, outcome.Classes);
        Assert.Equal(1, outcome.SkippedViewUnits);
        Assert.Equal(1, outcome.SkippedSideEffectUnits);
    }

    [Fact]
    public void ASideEffectUnitThatTouchesSomethingElseIsForeign()
    {
        var side = new FakeUnit();
        side.Events.Add(Ev(UndoEventKind.Modified, 0x701, "AcDbLayout"));
        side.Events.Add(Ev(UndoEventKind.Modified, 0x999, "AcDbLine"));
        var (fp, _, host) = DrawLine(side);
        var record = new UndoEventSet();
        record.Objects.Add(Ev(UndoEventKind.Modified, 0x701, "AcDbLayout"));
        fp.SideEffects.Add(record);

        var outcome = UndoVerifier.Run(fp, host);

        Assert.Equal(UndoVerifier.TargetNotReverted, outcome.Code);
    }

    [Fact]
    public void ASysvarOnlyUnitNeverMatchesASideEffectRecordEvenWhenTheRecordListsThatSysvar()
    {
        // Plan review r2 minor 1: a side-effect match needs a shared handle, so a user's REGENMODE change
        // above the write is not stepped over just because a plot also touched REGENMODE.
        var regen = new FakeUnit();
        regen.Sysvars.Add("REGENMODE");
        regen.SysvarsOnUndo["REGENMODE"] = "1";
        regen.SysvarsOnRedo["REGENMODE"] = "0";
        var (fp, _, host) = DrawLine(regen);
        host.Vars["REGENMODE"] = "0";
        var record = new UndoEventSet();
        record.Objects.Add(Ev(UndoEventKind.Modified, 0x701, "AcDbLayout"));
        record.Sysvars.Add("REGENMODE");
        fp.SideEffects.Add(record);

        var outcome = UndoVerifier.Run(fp, host);

        Assert.False(outcome.Undone);
        Assert.Equal(UndoVerifier.TargetNotReverted, outcome.Code);
        Assert.Equal(new[] { "Foreign" }, outcome.Classes);
        Assert.Equal(1, host.MredoTotal);
        Assert.True(outcome.Restored);
        Assert.Equal("0", host.Vars["REGENMODE"]);
        Assert.Equal(UndoObjectState.Live, host.State[Seed]);
    }

    [Fact]
    public void AnUndoStepThatThrowsRedoesTheStepsAlreadyRunAndRefusesTheWriteFromNowOn()
    {
        var (fp, _, host) = DrawLine(new FakeUnit());
        host.ThrowOnU = 2;

        var outcome = UndoVerifier.Run(fp, host);

        Assert.False(outcome.Undone);
        Assert.Equal(UndoVerifier.StepFailed, outcome.Code);
        Assert.Equal(1, outcome.UCount);
        Assert.Equal(1, host.MredoTotal);
        Assert.Equal("undo_step_failed", outcome.MarkDirty);
        Assert.False(outcome.PopEntry);
    }

    [Fact]
    public void AMredoThatThrowsLeavesTheRestoreUnverifiedAndTheWriteDirty()
    {
        var foreign = new FakeUnit();
        foreign.Events.Add(Ev(UndoEventKind.Modified, 0x999, "AcDbLine"));
        var (fp, _, host) = DrawLine(foreign);
        host.MredoThrows = true;

        var outcome = UndoVerifier.Run(fp, host);

        Assert.Equal(UndoVerifier.TargetNotReverted, outcome.Code);
        Assert.False(outcome.Restored);
        Assert.Equal("restore_unverified", outcome.MarkDirty);
        Assert.Contains(outcome.NotRestored, p => p.Contains("MREDO 1 failed", StringComparison.Ordinal));
    }

    [Fact]
    public void ContainerAndTableRecordChangesCountAsOwnedOnlyInAUnitThatRevertsTheWrite()
    {
        var fp = new UndoFootprint("forge_draw_dimension", "c") { HandseedBefore = Seed };
        fp.Add(Ev(UndoEventKind.Appended, Seed, "AcDbAlignedDimension"));
        fp.FinishHandseed(Seed + 1);
        var consumed = new HashSet<int>();

        // h13: the U of a dimension also modified the dimension style record, which the write never reported.
        var target = new UndoEventSet();
        target.Objects.Add(Ev(UndoEventKind.Unappended, Seed, "AcDbAlignedDimension"));
        target.Objects.Add(Ev(UndoEventKind.Modified, 0x27, "AcDbDimStyleTableRecord", tableRecord: true));
        target.Objects.Add(Ev(UndoEventKind.Modified, 0x19, "AcDbDictionary", container: true));
        Assert.Equal(UndoUnitClass.Target, UndoVerifier.Classify(target, fp, [], consumed, out _));

        // The same record change without any of the write's content is someone else's change.
        var other = new UndoEventSet();
        other.Objects.Add(Ev(UndoEventKind.Modified, 0x27, "AcDbDimStyleTableRecord", tableRecord: true));
        Assert.Equal(UndoUnitClass.Foreign, UndoVerifier.Classify(other, fp, [], consumed, out _));

        // An erased table record is never assumed to be the write's.
        var erasedRecord = new UndoEventSet();
        erasedRecord.Objects.Add(Ev(UndoEventKind.Unappended, Seed, "AcDbAlignedDimension"));
        erasedRecord.Objects.Add(Ev(UndoEventKind.Erased, 0x27, "AcDbDimStyleTableRecord", tableRecord: true));
        Assert.Equal(UndoUnitClass.Foreign, UndoVerifier.Classify(erasedRecord, fp, [], consumed, out _));
    }

    [Fact]
    public void ASysvarOnlyWriteIsTargetedBySysvarsAndCheckedByValue()
    {
        var fp = new UndoFootprint("forge_system_setvar", "c");
        fp.AddSysvarPre("LTSCALE", "1");
        fp.SetSysvarPost("LTSCALE", "2");
        var unit = new FakeUnit();
        unit.Sysvars.Add("LTSCALE");
        unit.SysvarsOnUndo["LTSCALE"] = "1";
        var host = new FakeHost(unit);
        host.Vars["LTSCALE"] = "2";

        var outcome = UndoVerifier.Run(fp, host);

        Assert.True(outcome.Undone);
        Assert.Equal("1", host.Vars["LTSCALE"]);
    }

    [Fact]
    public void PreChecksRefuseBeforeAnyU()
    {
        var (fp, _, host) = DrawLine();
        fp.Overflow = true;
        Assert.Equal(UndoVerifier.FootprintTooLarge, UndoVerifier.Run(fp, host).Code);

        (fp, _, host) = DrawLine();
        fp.MarkDirty("external_change: modified AcDbLine 2C4");
        Assert.Equal(UndoVerifier.ChangedSinceWrite, UndoVerifier.Run(fp, host).Code);

        (fp, _, host) = DrawLine();
        host.State[Seed] = UndoObjectState.Erased; // erased after the write
        Assert.Equal(UndoVerifier.ChangedSinceWrite, UndoVerifier.Run(fp, host).Code);

        var sv = new UndoFootprint("forge_system_setvar", "c");
        sv.AddSysvarPre("LTSCALE", "1");
        sv.SetSysvarPost("LTSCALE", "2");
        var svHost = new FakeHost();
        svHost.Vars["LTSCALE"] = "3";
        Assert.Equal(UndoVerifier.ChangedSinceWrite, UndoVerifier.Run(sv, svHost).Code);
        Assert.Equal(0, svHost.UCalls);

        Assert.Equal(0, host.UCalls);
    }

    [Fact]
    public void AWriteThatChangedNothingIsPoppedWithoutAnyU()
    {
        var fp = new UndoFootprint("forge_layer_set", "c");
        var host = new FakeHost(new FakeUnit());

        var outcome = UndoVerifier.Run(fp, host);

        Assert.False(outcome.Undone);
        Assert.Equal(UndoVerifier.NothingRecordedCode, outcome.Code);
        Assert.True(outcome.NothingRecorded);
        Assert.True(outcome.PopEntry);
        Assert.Equal(0, host.UCalls);
    }

    [Fact]
    public void ObjectsTheTargetUUnappendedAreGoneEvenIfNotFlaggedErased()
    {
        // h6: undoing an xref attach unappends the block's BlockBegin/BlockEnd without erasing them.
        var fp = new UndoFootprint("forge_doc_import", "c") { HandseedBefore = 0x313 };
        foreach (var h in new long[] { 0x313, 0x314, 0x315, 0x31C })
        {
            fp.Add(Ev(UndoEventKind.Appended, h));
        }

        fp.FinishHandseed(0x31D);
        var u = new UndoEventSet();
        u.Objects.Add(Ev(UndoEventKind.Erased, 0x31C));
        u.Objects.Add(Ev(UndoEventKind.Erased, 0x313, "AcDbBlockTableRecord", container: true));
        u.Objects.Add(Ev(UndoEventKind.Unappended, 0x314, "AcDbBlockBegin"));
        u.Objects.Add(Ev(UndoEventKind.Unappended, 0x315, "AcDbBlockEnd"));
        var host = new FakeHost();
        host.State[0x313] = UndoObjectState.Erased;
        host.State[0x31C] = UndoObjectState.Erased;
        host.State[0x314] = UndoObjectState.Live;
        host.State[0x315] = UndoObjectState.Live;

        Assert.Empty(UndoVerifier.VerifyReverted(fp, u, host));

        u.Objects.RemoveAt(3); // BlockEnd not unappended: still in the drawing
        Assert.Contains(UndoVerifier.VerifyReverted(fp, u, host), p => p.StartsWith("315", StringComparison.Ordinal));
    }

    [Fact]
    public void RegeneratedDimensionGraphicsAreNotCheckedByHandle()
    {
        // h6/h12: AutoCAD recreates *D block contents with new handles, so their old handles prove nothing.
        var fp = new UndoFootprint("forge_draw_dimension", "c") { HandseedBefore = Seed };
        fp.Add(Ev(UndoEventKind.Appended, Seed, "AcDbRotatedDimension"));
        fp.Add(Ev(UndoEventKind.Appended, Seed + 2, derived: true)); // flagged when it was created
        fp.FinishHandseed(Seed + 3);
        var u = new UndoEventSet();
        u.Objects.Add(Ev(UndoEventKind.Unappended, Seed, "AcDbRotatedDimension"));
        var host = new FakeHost();
        host.State[Seed] = UndoObjectState.Erased;
        host.State[Seed + 1] = UndoObjectState.Live; // not flagged by its event; the drawing says it is *D content
        host.State[Seed + 2] = UndoObjectState.Live;

        Assert.Contains(UndoVerifier.VerifyReverted(fp, u, host), p => p.StartsWith("2C2", StringComparison.Ordinal));
        host.Derived.Add(Seed + 1);
        Assert.Empty(UndoVerifier.VerifyReverted(fp, u, host));

        // After MREDO the old graphics stay erased: also not a failed restore.
        fp.PostWriteState[Seed] = UndoObjectState.Live;
        fp.PostWriteState[Seed + 1] = UndoObjectState.Live;
        host.State[Seed] = UndoObjectState.Live;
        host.State[Seed + 1] = UndoObjectState.Erased;
        var uEvents = new List<UndoEventSet> { u };
        Assert.Empty(UndoVerifier.VerifyRestored(fp, uEvents, host));
        host.Derived.Clear();
        Assert.Contains(UndoVerifier.VerifyRestored(fp, uEvents, host), p => p.StartsWith("2C2", StringComparison.Ordinal));
    }

    [Fact]
    public void FootprintSortsEventsByWhatTheyMeanForTheRevert()
    {
        var fp = new UndoFootprint("forge_doc_import", "c") { HandseedBefore = 0x300 };
        Assert.True(fp.Add(Ev(UndoEventKind.Appended, 0x10, "AcDbLayerTableRecord"))); // existed already (xref resolve noise)
        fp.Add(Ev(UndoEventKind.Appended, 0x301));
        fp.Add(Ev(UndoEventKind.Appended, 0x302, derived: true));
        fp.Add(new UndoObjectEvent(UndoEventKind.Modified, 0x303, "AcDbAssocNetwork", IsAssoc: true));
        fp.Add(Ev(UndoEventKind.Erased, 0x50));
        fp.Add(Ev(UndoEventKind.Erased, 0x301)); // created then erased in the same write: stays "appended"

        Assert.Contains(0x10L, fp.Containers);
        Assert.DoesNotContain(0x10L, fp.Appended);
        Assert.Contains(0x301L, fp.Appended);
        Assert.Contains(0x302L, fp.Derived);
        Assert.DoesNotContain(0x303L, fp.Modified);
        Assert.Contains(0x50L, fp.Erased);
        Assert.DoesNotContain(0x301L, fp.Erased);
    }

    [Fact]
    public void FootprintOverflowsAtTheHandleCap()
    {
        var fp = new UndoFootprint("forge_draw_batch", "c") { HandseedBefore = 1 };
        fp.FinishHandseed(2 + UndoPolicy.MaxHandlesPerFootprint);
        Assert.True(fp.Overflow);
    }

    [Fact]
    public void SysvarsBackAtTheirStartValueAreDropped()
    {
        var fp = new UndoFootprint("t", "c");
        fp.AddSysvarPre("CLAYER", "0");
        fp.AddSysvarPre("CLAYER", "A"); // only the first old value counts
        fp.SetSysvarPost("CLAYER", "0");
        fp.AddSysvarPre("BACKGROUNDPLOT", "2"); // volatile: never recorded

        Assert.Empty(fp.SysvarPre);
        Assert.True(fp.IsEmpty);
    }

    [Fact]
    public void LedgerKeepsTheNewestEntriesAndDecidesWhatOtherCallsMean()
    {
        var ledger = new UndoLedgerState();
        for (var i = 0; i < UndoPolicy.MaxLedgerDepth + 3; i++)
        {
            ledger.Push(new UndoFootprint("forge_draw_line", $"c{i}"));
        }

        Assert.Equal(UndoPolicy.MaxLedgerDepth, ledger.Depth);
        Assert.Equal(3, ledger.DroppedOldest);
        Assert.Equal($"c{UndoPolicy.MaxLedgerDepth + 2}", ledger.Top!.CommandId);

        var change = new UndoEventSet();
        change.Objects.Add(Ev(UndoEventKind.Modified, 0x40));
        Assert.Equal("ignored", ledger.ApplyNonTrackedCall("forge_undo", change));
        Assert.Equal("no_change", ledger.ApplyNonTrackedCall("forge_entity_list", new UndoEventSet()));
        Assert.Equal("side_effect", ledger.ApplyNonTrackedCall("forge_plot_to_pdf", change));
        Assert.Single(ledger.Top!.SideEffects);
        Assert.Null(ledger.Top!.DirtyReason);
        Assert.Equal("dirty", ledger.ApplyNonTrackedCall("forge_layer_set", change));
        Assert.All(ledger.Entries, e => Assert.NotNull(e.DirtyReason));
    }

    [Fact]
    public void OutsideEventsMarkTheLedgerUnlessIgnorable()
    {
        var ledger = new UndoLedgerState();
        Assert.False(ledger.ApplyExternalEvent(Ev(UndoEventKind.Modified, 0x40))); // nothing to protect yet

        ledger.Push(new UndoFootprint("forge_draw_line", "c"));
        Assert.False(ledger.ApplyExternalEvent(Ev(UndoEventKind.Modified, 0x29, "AcDbViewportTableRecord", vtr: true)));
        Assert.False(ledger.ApplyExternalEvent(new UndoObjectEvent(UndoEventKind.Modified, 0x41, "AcDbAssocAction", IsAssoc: true)));
        Assert.False(ledger.ApplyExternalEvent(Ev(UndoEventKind.Appended, 0x42, derived: true)));
        Assert.Null(ledger.Top!.DirtyReason);

        Assert.True(ledger.ApplyExternalEvent(Ev(UndoEventKind.Modified, 0x1F, "AcDbBlockTableRecord", container: true)));
        Assert.StartsWith("external_change", ledger.Top!.DirtyReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("*D12", true)]
    [InlineData("*d3", true)]
    [InlineData("*T5", true)]
    [InlineData("*U2", false)]
    [InlineData("*Model_Space", false)]
    [InlineData("DOOR", false)]
    [InlineData("*D", false)]
    [InlineData(null, false)]
    public void DerivedBlocksAreDimensionAndTableBlocks(string? name, bool derived)
        => Assert.Equal(derived, UndoPolicy.IsDerivedBlockName(name));

    [Fact]
    public void PolicyListsMatchThePlan()
    {
        Assert.Contains("BACKGROUNDPLOT", UndoPolicy.VolatileSysvars);
        Assert.Contains("SECURELOAD", UndoPolicy.VolatileSysvars);
        Assert.Contains("EXTMIN", UndoPolicy.ViewSysvars);
        Assert.Contains("forge_plot_to_pdf", UndoPolicy.SideEffectTools);
        Assert.True(UndoPolicy.IsInternalClass("AcDbFontTableRecord"));
        Assert.Equal(256, UndoPolicy.MaxEmptyUnits);
        Assert.Equal(8, UndoPolicy.MaxSkippableUnits);
    }
}
