using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Forge.Shared;

namespace Forge.Plugin;

/// <summary>
/// CAD linework intelligence: entity dump, point tracing, topology graph, and
/// CAD-vs-model pipe coverage. All read-only; the geometry/analysis pipeline lives in
/// Forge.Shared (LineworkLocal) so it is unit-testable and the MCP server can resolve
/// source=dumpFile calls without AutoCAD. This partial only extracts entities from the
/// live drawing (model space + xref contents with BlockTransform applied).
/// </summary>
public sealed partial class PluginCommandProcessor
{
    private static ForgeResult LineworkDump(ForgeCommand command)
        => RunLinework<LineworkQueryArgs>(command);

    private static ForgeResult LineworkTrace(ForgeCommand command)
        => RunLinework<LineworkTraceArgs>(command);

    private static ForgeResult LineworkTopology(ForgeCommand command)
        => RunLinework<LineworkTopologyArgs>(command);

    private static ForgeResult LineworkCoverage(ForgeCommand command)
        => RunLinework<LineworkCoverageArgs>(command);

    private static ForgeResult LineworkSegments(ForgeCommand command)
        => RunLinework<LineworkSegmentsArgs>(command);

    private static ForgeResult LineworkCompare(ForgeCommand command)
        => RunLinework<LineworkCompareArgs>(command);

    private static ForgeResult RunLinework<TArgs>(ForgeCommand command) where TArgs : LineworkQueryArgs, new()
    {
        var args = Args<TArgs>(command);
        var loaded = LoadLinework(command, args);
        if (loaded.Result is not null)
        {
            return loaded.Result;
        }

        return LineworkLocal.ExecuteOnEntities(command, args, loaded.Entities, loaded.Source);
    }

    // ===================== source loading =====================

    private sealed record LineworkLoadResult(
        ForgeResult? Result,
        List<LineworkEntity> Entities,
        LineworkLocal.SourceInfo Source);

    private static LineworkLoadResult LoadLinework(ForgeCommand command, LineworkQueryArgs args)
    {
        var useDump = string.Equals(args.Source, "dumpFile", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(args.Source, "file", StringComparison.OrdinalIgnoreCase) ||
                      (string.IsNullOrWhiteSpace(args.Source) && !string.IsNullOrWhiteSpace(args.DumpPath));

        if (useDump)
        {
            if (string.IsNullOrWhiteSpace(args.DumpPath))
            {
                return new LineworkLoadResult(
                    ForgeResult.Failure(command.Id, "missing_dump_path", "dumpPath is required when source=dumpFile."),
                    [], new LineworkLocal.SourceInfo("dumpFile", null, args.DumpPath, 0, 1000));
            }

            if (RejectControlChars(command, ("dumpPath", args.DumpPath)) is { } reject)
            {
                return new LineworkLoadResult(reject, [], new LineworkLocal.SourceInfo("dumpFile", null, args.DumpPath, 0, 1000));
            }

            if (!File.Exists(args.DumpPath))
            {
                return new LineworkLoadResult(
                    ForgeResult.Failure(command.Id, "dump_not_found", $"Linework dump not found: {args.DumpPath}"),
                    [], new LineworkLocal.SourceInfo("dumpFile", null, args.DumpPath, 0, 1000));
            }

            List<LineworkEntity> entities;
            try
            {
                entities = LineworkDumpParser.ParseFile(args.DumpPath);
            }
            catch (Exception ex)
            {
                return new LineworkLoadResult(
                    ForgeResult.Failure(command.Id, "dump_invalid", $"Could not parse linework dump: {ex.Message}"),
                    [], new LineworkLocal.SourceInfo("dumpFile", null, args.DumpPath, 0, 1000));
            }

            var upm = args.UnitsPerMeter > 0 ? args.UnitsPerMeter : 1000.0;
            return new LineworkLoadResult(null, entities, new LineworkLocal.SourceInfo("dumpFile", null, args.DumpPath, 0, upm));
        }

        var insunits = 0;
        try
        {
            insunits = Convert.ToInt32(Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("INSUNITS"), CultureInfo.InvariantCulture);
        }
        catch
        {
            // Best effort — fall through to default.
        }

        var skipped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        List<LineworkEntity> extracted;
        object? blockTraversal = null;
        using (var tr = ActiveDb.TransactionManager.StartTransaction())
        {
            if (args.IncludeBlocks)
            {
                var options = new BlockWalkOptions(true, BlockWalk.ClampDepth(args.BlockDepth), NormalizeFilterBox(args.Bbox), args.IncludeXrefContents);
                var stats = new BlockWalkStats();
                extracted = ExtractWithBlocks(tr, ActiveDb, options, stats, skipped);
                blockTraversal = BlockTraversalReport(options, stats);
            }
            else
            {
                extracted = ExtractFromDrawing(tr, ActiveDb, args, skipped);
            }

            tr.Commit();
        }

        var unitsPerMeter = args.UnitsPerMeter > 0 ? args.UnitsPerMeter : InsUnitsToUnitsPerMeter(insunits);
        return new LineworkLoadResult(null, extracted,
            new LineworkLocal.SourceInfo("drawing", ActiveDocumentPath(), null, insunits, unitsPerMeter, blockTraversal));
    }

    /// <summary>Hard ceiling on entities gathered by a block-aware walk before filtering (memory guard).</summary>
    private const int MaxBlockWalkLinework = 1_000_000;

    /// <summary>
    /// includeBlocks=true: model space plus everything reachable through INSERTs (xrefs included via
    /// their INSERTs when includeXrefContents), each entity transformed to world coordinates.
    /// </summary>
    private static List<LineworkEntity> ExtractWithBlocks(Transaction tr, Database db, BlockWalkOptions options, BlockWalkStats stats, Dictionary<string, int> skipped)
    {
        var list = new List<LineworkEntity>();
        var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        WalkBlockSpace(tr, modelSpace, options, stats, (ent, id, frame) =>
        {
            if (ent is BlockReference)
            {
                return true;
            }

            var le = TryMakeEntity(tr, ent, id, frame.IsIdentity ? null : frame.Matrix);
            if (le is null)
            {
                Bump(skipped, frame.Depth > 0 ? $"block:{ent.GetType().Name}" : ent.GetType().Name);
                return true;
            }

            var effectiveLayer = BlockWalk.EffectiveLayer(ent.Layer, frame.ParentEffectiveLayer);
            list.Add(le with
            {
                Id = list.Count,
                Layer = effectiveLayer,
                OwnLayer = string.Equals(effectiveLayer, ent.Layer, StringComparison.Ordinal) ? null : ent.Layer,
                Container = BlockWalk.ContainerLabel(frame.Path),
                InsertPath = frame.Path.Count > 0 ? BlockWalk.InsertPathLabel(frame.Path) : null
            });
            return list.Count < MaxBlockWalkLinework;
        });

        return list;
    }

    /// <summary>INSUNITS → drawing units per meter. Unknown/unitless defaults to mm (1000) per dump convention.</summary>
    private static double InsUnitsToUnitsPerMeter(int insunits) => insunits switch
    {
        1 => 39.3701,   // inches
        2 => 3.28084,   // feet
        4 => 1000,      // millimeters
        5 => 100,       // centimeters
        6 => 1,         // meters
        14 => 10,       // decimeters
        _ => 1000
    };

    // ===================== drawing extraction =====================

    private static List<LineworkEntity> ExtractFromDrawing(Transaction tr, Database db, LineworkQueryArgs args, Dictionary<string, int> skipped)
    {
        var list = new List<LineworkEntity>();
        var nextId = 0;
        var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent)
            {
                continue;
            }

            var le = TryMakeEntity(tr, ent, id, null);
            if (le is null)
            {
                Bump(skipped, ent.GetType().Name);
                continue;
            }

            le = le with { Id = nextId++ };
            list.Add(le);
        }

        if (!args.IncludeXrefContents)
        {
            return list;
        }

        // Entities inside xref block records — same reach as the LISP (entnext) dump.
        // Points are transformed into host space via the xref's BlockReference transform.
        foreach (ObjectId btrId in blockTable)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (!btr.IsFromExternalReference && !btr.IsFromOverlayReference)
            {
                continue;
            }

            var xf = Matrix3d.Identity;
            var transformFound = false;
            foreach (ObjectId eid in modelSpace)
            {
                if (tr.GetObject(eid, OpenMode.ForRead) is BlockReference br && br.BlockTableRecord == btrId)
                {
                    xf = br.BlockTransform;
                    transformFound = true;
                    break;
                }
            }

            var container = transformFound ? btr.Name : $"{btr.Name} (no insertion transform)";
            foreach (ObjectId eid in btr)
            {
                if (tr.GetObject(eid, OpenMode.ForRead) is not Entity ent)
                {
                    continue;
                }

                var le = TryMakeEntity(tr, ent, eid, xf);
                if (le is null)
                {
                    Bump(skipped, $"xref:{ent.GetType().Name}");
                    continue;
                }

                le = le with { Id = nextId++, Container = container };
                list.Add(le);
            }
        }

        return list;
    }

    /// <summary>
    /// One linework entity in world coordinates. Curves are tessellated in their own space first and
    /// the chord points are then transformed, so arcs/bulges stay correct under any block transform
    /// (mirror, non-uniform scale, rotation) and under a -Z extrusion normal.
    /// </summary>
    private static LineworkEntity? TryMakeEntity(Transaction tr, Entity ent, ObjectId id, Matrix3d? xf)
    {
        switch (ent)
        {
            case Line line:
            {
                return MakeEntity(id, LineworkKind.Line, ent.Layer, [Xf(line.StartPoint, xf), Xf(line.EndPoint, xf)], closed: false);
            }
            case Polyline pl:
            {
                var nv = pl.NumberOfVertices;
                var verts = new Vec2[nv];
                var bulges = new double[nv];
                var normalUp = pl.Normal.Z > 0;
                for (var i = 0; i < nv; i++)
                {
                    if (normalUp)
                    {
                        var p2d = pl.GetPoint2dAt(i);
                        verts[i] = new Vec2(p2d.X, p2d.Y);
                        bulges[i] = pl.GetBulgeAt(i);
                    }
                    else
                    {
                        // OCS with a -Z normal: world XY comes from the 3D point and arcs turn the other way.
                        var p3d = pl.GetPoint3dAt(i);
                        verts[i] = new Vec2(p3d.X, p3d.Y);
                        bulges[i] = -pl.GetBulgeAt(i);
                    }
                }

                return MakeEntity(id, LineworkKind.Polyline, ent.Layer, XfAll(ExpandBulges(verts, bulges, pl.Closed), xf), pl.Closed);
            }
            case Polyline2d p2:
            {
                var verts = new List<Vec2>();
                var bulges = new List<double>();
                foreach (ObjectId vid in p2)
                {
                    if (tr.GetObject(vid, OpenMode.ForRead) is Vertex2d v && v.VertexType == Vertex2dType.SimpleVertex)
                    {
                        verts.Add(new Vec2(v.Position.X, v.Position.Y));
                        bulges.Add(v.Bulge);
                    }
                }

                return verts.Count < 2
                    ? null
                    : MakeEntity(id, LineworkKind.Polyline, ent.Layer, XfAll(ExpandBulges(verts.ToArray(), bulges.ToArray(), p2.Closed), xf), p2.Closed);
            }
            case Polyline3d p3:
            {
                var verts = new List<Vec2>();
                foreach (ObjectId vid in p3)
                {
                    if (tr.GetObject(vid, OpenMode.ForRead) is PolylineVertex3d v && v.VertexType == Vertex3dType.SimpleVertex)
                    {
                        verts.Add(Xf(v.Position, xf));
                    }
                }

                return verts.Count < 2
                    ? null
                    : MakeEntity(id, LineworkKind.Polyline, ent.Layer, verts.ToArray(), p3.Closed);
            }
            case Arc arc:
            {
                if (xf is null && arc.Normal.Z > 0)
                {
                    var pts = LineworkGeometry.TessellateArc(arc.Center.X, arc.Center.Y, arc.Radius, arc.StartAngle, arc.EndAngle);
                    return MakeEntity(id, LineworkKind.Arc, ent.Layer, pts, closed: false, radius: arc.Radius);
                }

                var sampled = SampleCurve(arc, arc.StartParam, arc.EndParam, xf);
                return MakeEntity(id, LineworkKind.Arc, ent.Layer, sampled, closed: false, radius: arc.Radius * RadiusScale(xf));
            }
            case Circle circle:
            {
                if (xf is null && circle.Normal.Z > 0)
                {
                    var pts = LineworkGeometry.TessellateArc(circle.Center.X, circle.Center.Y, circle.Radius, 0, Math.PI * 2);
                    return MakeEntity(id, LineworkKind.Circle, ent.Layer, pts, closed: true, radius: circle.Radius);
                }

                var sampled = SampleCurve(circle, circle.StartParam, circle.EndParam, xf);
                return MakeEntity(id, LineworkKind.Circle, ent.Layer, sampled, closed: true, radius: circle.Radius * RadiusScale(xf));
            }
            default:
                return null;
        }
    }

    /// <summary>Chord points of a circular curve between two parameters (radians), in world space.</summary>
    private static Vec2[] SampleCurve(Curve curve, double startParam, double endParam, Matrix3d? xf)
    {
        var sweep = endParam - startParam;
        if (sweep <= 0)
        {
            sweep += Math.PI * 2;
        }

        var n = BlockWalk.ArcSampleCount(sweep);
        var pts = new Vec2[n + 1];
        for (var i = 0; i <= n; i++)
        {
            var t = i == n && endParam > startParam ? endParam : startParam + sweep * i / n;
            pts[i] = Xf(curve.GetPointAtParameter(t), xf);
        }

        return pts;
    }

    /// <summary>Radius scale of a transform (sqrt of the XY area factor; exact for uniform scale).</summary>
    private static double RadiusScale(Matrix3d? xf)
    {
        if (xf is null)
        {
            return 1.0;
        }

        var m = xf.Value;
        return Math.Sqrt(Math.Abs(m[0, 0] * m[1, 1] - m[0, 1] * m[1, 0]));
    }

    private static Vec2[] XfAll(Vec2[] pts, Matrix3d? xf)
    {
        if (xf is null)
        {
            return pts;
        }

        var result = new Vec2[pts.Length];
        for (var i = 0; i < pts.Length; i++)
        {
            result[i] = Xf(new Point3d(pts[i].X, pts[i].Y, 0), xf);
        }

        return result;
    }

    private static Vec2 Xf(Point3d p, Matrix3d? xf)
    {
        if (xf.HasValue)
        {
            p = p.TransformBy(xf.Value);
        }

        return new Vec2(p.X, p.Y);
    }

    private static Vec2[] ExpandBulges(Vec2[] verts, double[] bulges, bool closed)
    {
        var nv = verts.Length;
        if (nv < 2)
        {
            return verts;
        }

        var nseg = closed ? nv : nv - 1;
        var any = false;
        for (var i = 0; i < nseg; i++)
        {
            if (Math.Abs(bulges[i]) > 1e-9)
            {
                any = true;
                break;
            }
        }

        if (!any)
        {
            return verts;
        }

        var expanded = new List<Vec2>();
        for (var i = 0; i < nseg; i++)
        {
            var a = verts[i];
            var b = verts[(i + 1) % nv];
            var chords = LineworkGeometry.TessellateBulge(a, b, bulges[i]);
            for (var k = 0; k < chords.Length - 1; k++)
            {
                expanded.Add(chords[k]);
            }
        }

        if (!closed)
        {
            expanded.Add(verts[nv - 1]);
        }

        return expanded.ToArray();
    }

    private static LineworkEntity MakeEntity(ObjectId id, LineworkKind kind, string layer, Vec2[] pts, bool closed, double radius = 0)
    {
        var length = 0.0;
        var n = closed ? pts.Length : pts.Length - 1;
        for (var i = 0; i < n && pts.Length > 0; i++)
        {
            length += pts[i].DistanceTo(pts[(i + 1) % pts.Length]);
        }

        return new LineworkEntity
        {
            Id = 0,
            Handle = id.Handle.Value.ToString("X", CultureInfo.InvariantCulture),
            Kind = kind,
            Layer = layer,
            Container = "modelspace",
            Points = pts,
            Closed = closed,
            Radius = radius,
            Length = length,
            SegCount = Math.Max(0, closed ? pts.Length : pts.Length - 1)
        };
    }

    private static void Bump(Dictionary<string, int> map, string key)
    {
        map[key] = map.TryGetValue(key, out var v) ? v + 1 : 1;
    }
}
