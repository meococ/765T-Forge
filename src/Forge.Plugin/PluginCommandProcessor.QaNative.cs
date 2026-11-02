// forge_qa_native_objects — detect geometry left behind when real objects were exploded:
// dimension clusters (numeric text + linework + arrowhead solids, no live DIMENSION),
// exploded hatch line arrays, and repeated exploded-block motifs. Wired into
// forge_qa_preflight and forge_recipe_issue_set so the gate flags them automatically.
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Forge.Shared;

namespace Forge.Plugin;

public sealed partial class PluginCommandProcessor
{
    private static ForgeResult QaNativeObjects(ForgeCommand command)
    {
        var args = Args<NativeObjectsArgs>(command);
        if (args.Checks is { Length: > 0 })
        {
            foreach (var check in args.Checks)
            {
                if (!NativeObjectScan.AllChecks.Contains(check, StringComparer.OrdinalIgnoreCase))
                {
                    return ForgeResult.Failure(command.Id, "invalid_check", $"Unknown check '{check}'. Use: {string.Join(", ", NativeObjectScan.AllChecks)}.");
                }
            }
        }

        var window = args.Window is > 0 ? args.Window.Value : 1500.0;
        var report = RunNativeObjectScan(window, args.Checks, args.MaxFindings);
        return ForgeResult.Success(command.Id, report, verification: new ForgeVerification
        {
            Attempted = true,
            Passed = report.Passed,
            Message = report.Passed ? "No exploded native-object geometry detected." : "Exploded native-object geometry detected.",
            ReadBack = report
        });
    }

    private static NativeObjectScanReport RunNativeObjectScan(double window, string[]? checks, int? maxFindings)
    {
        var snapshots = CollectLayoutSnapshots();
        var scan = NativeObjectScan.Scan(snapshots, window, checks, maxFindings ?? 200);
        return new NativeObjectScanReport(
            scan.Findings.Count == 0,
            scan.EntitiesScanned,
            scan.Window,
            scan.Summary,
            scan.Findings,
            scan.FindingsTruncated);
    }

    /// <summary>Warning findings so preflight/issue-set validation inherit the same scan.</summary>
    internal static IEnumerable<QaFinding> NativeObjectFindings()
    {
        try
        {
            return RunNativeObjectScan(1500.0, null, 50).Findings
                .Select(f => new QaFinding(
                    f.Code,
                    f.Severity,
                    $"{f.Message} Handles: {string.Join(", ", f.Handles.Take(6))}{(f.Handles.Length > 6 ? "…" : "")}.",
                    "Inspect and rebuild via forge_qa_native_objects.",
                    "forge_qa_native_objects"))
                .ToArray();
        }
        catch
        {
            return Array.Empty<QaFinding>();
        }
    }

    /// <summary>Entity snapshots from every layout space (model + paper), block defs excluded.</summary>
    private static NativeEntitySnapshot[] CollectLayoutSnapshots()
    {
        var db = ActiveDb;
        var list = new List<NativeEntitySnapshot>();
        using var tr = db.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId btrId in blockTable)
        {
            var space = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (!space.IsLayout)
            {
                continue;
            }
            foreach (ObjectId entId in space)
            {
                if (tr.GetObject(entId, OpenMode.ForRead) is not Entity ent)
                {
                    continue;
                }
                Extents3d ext;
                try
                {
                    ext = ent.GeometricExtents;
                }
                catch
                {
                    continue;
                }
                string? content = null;
                double? height = null;
                double? length = null;
                double? angle = null;
                switch (ent)
                {
                    case DBText t:
                        content = t.TextString;
                        height = t.Height;
                        break;
                    case MText m:
                        content = m.Contents;
                        height = m.TextHeight;
                        break;
                    case Line l:
                        var dx = l.EndPoint.X - l.StartPoint.X;
                        var dy = l.EndPoint.Y - l.StartPoint.Y;
                        length = Math.Sqrt(dx * dx + dy * dy);
                        angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                        break;
                    case Curve c:
                        try
                        {
                            length = c.GetDistanceAtParameter(c.EndParam) - c.GetDistanceAtParameter(c.StartParam);
                        }
                        catch
                        {
                            // Closed/degenerate curve — extents still carry the fingerprint.
                        }
                        break;
                }
                list.Add(new NativeEntitySnapshot(
                    HandleString(ent),
                    ent.GetType().Name,
                    ent.Layer,
                    space.Name,
                    ext.MinPoint.X, ext.MinPoint.Y, ext.MaxPoint.X, ext.MaxPoint.Y,
                    content, height, length, angle));
            }
        }
        return list.ToArray();
    }

    private sealed record NativeObjectsArgs
    {
        public double? Window { get; init; }
        public string[]? Checks { get; init; }
        public int? MaxFindings { get; init; }
    }
}

internal sealed record NativeObjectScanReport(
    bool Passed,
    int EntitiesScanned,
    double Window,
    IReadOnlyDictionary<string, int> Summary,
    IReadOnlyList<NativeObjectFinding> Findings,
    bool FindingsTruncated);
