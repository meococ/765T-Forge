using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Forge.Shared;

namespace Forge.Plugin;

// docs/cad-authoring-tools.md C21 — forge_entity_get/list/move/copy/rotate/scale/mirror/
// offset/erase/set_props plus forge_undo. Entities are referenced by explicit handle strings
// only (uppercase hex); there is no selection and no "all" path. Clones and offset results are
// appended with AppendExistingToModelSpace — they already carry their properties and
// SetDatabaseDefaults would reset them. Unknown handles are reported per handle and skipped so
// one bad id never fails the whole batch.
public sealed partial class PluginCommandProcessor
{
    private static ForgeResult EntityGet(ForgeCommand command)
    {
        var args = Args<EntityGetArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Handle))
        {
            return ForgeResult.Failure(command.Id, "missing_handle", "handle is required.");
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var entity = TryOpenEntityForRead(db, tr, args.Handle!);
        if (entity is null)
        {
            return ForgeResult.Failure(command.Id, "handle_not_found", $"No entity for handle: {args.Handle}");
        }

        var summary = EntitySummary(entity);

        // Kind read-back is best effort: geometry properties can throw for degenerate entities.
        object? kind = entity switch
        {
            Line line => new
            {
                start = new[] { line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z },
                end = new[] { line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z }
            },
            Circle circle => new
            {
                center = new[] { circle.Center.X, circle.Center.Y, circle.Center.Z },
                radius = circle.Radius
            },
            Polyline pl => new { vertexCount = pl.NumberOfVertices, closed = pl.Closed, length = pl.Length },
            DBText text => new { textString = text.TextString },
            MText mtext => new { contents = mtext.Contents },
            BlockReference blockRef => new { name = blockRef.Name },
            Dimension dimension => new { measurement = dimension.Measurement },
            _ => null
        };

        return kind is null
            ? ForgeResult.Success(command.Id, summary)
            : ForgeResult.Success(command.Id, new { entity = summary, kind });
    }

    private static ForgeResult EntityList(ForgeCommand command)
    {
        var args = Args<EntityListArgs>(command);
        var hasWindow = args.Window is { Length: > 0 };
        var hasLayer = !string.IsNullOrWhiteSpace(args.Layer);
        if (hasWindow == hasLayer)
        {
            // Exactly one query axis is required — there is deliberately no unbounded "all".
            return ForgeResult.Failure(
                command.Id,
                "missing_query",
                hasWindow
                    ? "Provide window [[x1,y1],[x2,y2]] or layer — exactly one, not both."
                    : "Provide window [[x1,y1],[x2,y2]] or layer.");
        }

        double minX = 0, minY = 0, maxX = 0, maxY = 0;
        if (hasWindow)
        {
            if (args.Window!.Length != 2)
            {
                return ForgeResult.Failure(command.Id, "invalid_args", "Window must be [[x1,y1],[x2,y2]].");
            }

            try
            {
                var p1 = ToPoint3d(args.Window[0]);
                var p2 = ToPoint3d(args.Window[1]);
                minX = Math.Min(p1.X, p2.X);
                minY = Math.Min(p1.Y, p2.Y);
                maxX = Math.Max(p1.X, p2.X);
                maxY = Math.Max(p1.Y, p2.Y);
            }
            catch (ArgumentException ex)
            {
                return ForgeResult.Failure(command.Id, "invalid_args", ex.Message);
            }
        }

        var maxEntities = Math.Clamp(args.MaxEntities ?? 1000, 1, 10000);

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);

        var entities = new List<object>();
        foreach (ObjectId id in modelSpace)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity entity)
            {
                continue;
            }

            // Cheap table filters first; the extents-based window test is the expensive one.
            if (hasLayer && !entity.Layer.Equals(args.Layer, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Type is the exact simple class name (Polyline, Line, Circle, Arc, DBText, MText,
            // BlockReference, Hatch, ...), compared case-insensitively.
            if (!string.IsNullOrWhiteSpace(args.Type) && !entity.GetType().Name.Equals(args.Type, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (hasWindow)
            {
                var extents = TryExtents(entity);
                if (extents is null ||
                    extents[0] > maxX || extents[3] < minX ||
                    extents[1] > maxY || extents[4] < minY)
                {
                    // No bounding box, or it does not overlap the window — excluded in window mode.
                    continue;
                }
            }

            entities.Add(new { handle = HandleString(entity), type = entity.GetType().Name, layer = entity.Layer });
            if (entities.Count >= maxEntities)
            {
                break;
            }
        }

        return ForgeResult.Success(command.Id, new { count = entities.Count, truncated = entities.Count == maxEntities, entities });
    }

    private static ForgeResult EntityMove(ForgeCommand command)
    {
        var args = Args<EntityMoveArgs>(command);
        if (CheckHandles(command, args.Handles, "missing_handles") is { } denied)
        {
            return denied;
        }

        Vector3d delta;
        try
        {
            var point = ToPoint3d(args.Delta);
            delta = new Vector3d(point.X, point.Y, point.Z);
        }
        catch (ArgumentException ex)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", ex.Message);
        }

        if (command.DryRun)
        {
            return DryRun(command, new { handles = args.Handles, delta = new[] { delta.X, delta.Y, delta.Z } });
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var unknown = new List<string>();
        var opened = OpenHandlesForWrite(db, tr, args.Handles!, unknown);
        var displacement = Matrix3d.Displacement(delta);
        foreach (var entity in opened)
        {
            entity.TransformBy(displacement);
        }

        var readBack = opened.Select(EntitySummary).ToArray();
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { moved = opened.Count, unknownHandles = unknown, handles = args.Handles },
            verification: new ForgeVerification { Attempted = true, Passed = opened.Count > 0, ReadBack = readBack });
    }

    private static ForgeResult EntityCopy(ForgeCommand command)
    {
        var args = Args<EntityCopyArgs>(command);
        if (CheckHandles(command, args.Handles, "missing_handles") is { } denied)
        {
            return denied;
        }

        var copies = Math.Clamp(args.Copies, 1, 100);
        Vector3d delta;
        try
        {
            var point = ToPoint3d(args.Delta);
            delta = new Vector3d(point.X, point.Y, point.Z);
        }
        catch (ArgumentException ex)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", ex.Message);
        }

        if (command.DryRun)
        {
            return DryRun(command, new { handles = args.Handles, delta = new[] { delta.X, delta.Y, delta.Z }, copies });
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var unknown = new List<string>();
        var created = new List<Entity>();
        foreach (var handle in args.Handles!)
        {
            var source = TryOpenEntityForRead(db, tr, handle);
            if (source is null)
            {
                unknown.Add(handle);
                continue;
            }

            for (var c = 0; c < copies; c++)
            {
                // One matrix per copy distance: copy c+1 lands (c+1)×delta from the source.
                var clone = (Entity)source.Clone();
                clone.TransformBy(Matrix3d.Displacement(new Vector3d(delta.X * (c + 1), delta.Y * (c + 1), delta.Z * (c + 1))));
                // The clone carries the source's properties — AppendExistingToModelSpace
                // (no SetDatabaseDefaults) keeps them.
                AppendExistingToModelSpace(db, tr, clone);
                created.Add(clone);
            }
        }

        var newHandles = created.Select(HandleString).ToArray();
        var readBack = created.Select(EntitySummary).ToArray();
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { created = created.Count, newHandles, unknownHandles = unknown },
            verification: new ForgeVerification { Attempted = true, Passed = created.Count > 0, ReadBack = readBack });
    }

    private static ForgeResult EntityRotate(ForgeCommand command)
    {
        var args = Args<EntityRotateArgs>(command);
        if (CheckHandles(command, args.Handles, "missing_handles") is { } denied)
        {
            return denied;
        }

        Point3d basePoint;
        try
        {
            basePoint = ToPoint3d(args.BasePoint);
        }
        catch (ArgumentException ex)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", ex.Message);
        }

        if (command.DryRun)
        {
            return DryRun(command, new
            {
                handles = args.Handles,
                basePoint = new[] { basePoint.X, basePoint.Y, basePoint.Z },
                angleDeg = args.AngleDeg
            });
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var unknown = new List<string>();
        var opened = OpenHandlesForWrite(db, tr, args.Handles!, unknown);
        // Degrees at the boundary, radians inside; CCW positive about the WCS Z axis.
        var rotation = Matrix3d.Rotation(DegreesToRadians(args.AngleDeg), Vector3d.ZAxis, basePoint);
        foreach (var entity in opened)
        {
            entity.TransformBy(rotation);
        }

        var readBack = opened.Select(EntitySummary).ToArray();
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { rotated = opened.Count, unknownHandles = unknown, handles = args.Handles },
            verification: new ForgeVerification { Attempted = true, Passed = opened.Count > 0, ReadBack = readBack });
    }

    private static ForgeResult EntityScale(ForgeCommand command)
    {
        var args = Args<EntityScaleArgs>(command);
        if (CheckHandles(command, args.Handles, "missing_handles") is { } denied)
        {
            return denied;
        }

        if (args.Factor is not > 0)
        {
            return ForgeResult.Failure(command.Id, "invalid_factor", "Factor must be > 0.");
        }

        Point3d basePoint;
        try
        {
            basePoint = ToPoint3d(args.BasePoint);
        }
        catch (ArgumentException ex)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", ex.Message);
        }

        if (command.DryRun)
        {
            return DryRun(command, new
            {
                handles = args.Handles,
                basePoint = new[] { basePoint.X, basePoint.Y, basePoint.Z },
                args.Factor
            });
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var unknown = new List<string>();
        var opened = OpenHandlesForWrite(db, tr, args.Handles!, unknown);
        var scaling = Matrix3d.Scaling(args.Factor, basePoint);
        foreach (var entity in opened)
        {
            entity.TransformBy(scaling);
        }

        var readBack = opened.Select(EntitySummary).ToArray();
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { scaled = opened.Count, unknownHandles = unknown, handles = args.Handles },
            verification: new ForgeVerification { Attempted = true, Passed = opened.Count > 0, ReadBack = readBack });
    }

    private static ForgeResult EntityMirror(ForgeCommand command)
    {
        var args = Args<EntityMirrorArgs>(command);
        if (CheckHandles(command, args.Handles, "missing_handles") is { } denied)
        {
            return denied;
        }

        Point3d p1, p2;
        try
        {
            p1 = ToPoint3d(args.P1);
            p2 = ToPoint3d(args.P2);
        }
        catch (ArgumentException ex)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", ex.Message);
        }

        if (command.DryRun)
        {
            return DryRun(command, new
            {
                handles = args.Handles,
                p1 = new[] { p1.X, p1.Y, p1.Z },
                p2 = new[] { p2.X, p2.Y, p2.Z },
                args.EraseSource
            });
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var unknown = new List<string>();
        var opened = OpenHandlesForWrite(db, tr, args.Handles!, unknown);
        // Matrix3d has no two-point Mirroring overload — mirror about the Line3d through
        // p1–p2 (reflection through the axis; exact 2D mirror for flat drawing geometry).
        var mirroring = Matrix3d.Mirroring(new Line3d(p1, p2));
        var mirrored = 0;
        var erased = 0;
        var newHandles = new List<string>();
        var readBack = new List<object>();
        foreach (var entity in opened)
        {
            if (args.EraseSource)
            {
                // Mirror+erase lands the entity on the mirrored position under a new handle:
                // clone-transform-append first, erase the source last.
                var clone = (Entity)entity.Clone();
                clone.TransformBy(mirroring);
                AppendExistingToModelSpace(db, tr, clone);
                newHandles.Add(HandleString(clone));
                readBack.Add(EntitySummary(clone));
                entity.Erase();
                erased++;
            }
            else
            {
                entity.TransformBy(mirroring);
                readBack.Add(EntitySummary(entity));
            }

            mirrored++;
        }

        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { mirrored, erased, newHandles, unknownHandles = unknown },
            verification: new ForgeVerification { Attempted = true, Passed = mirrored > 0, ReadBack = readBack });
    }

    private static ForgeResult EntityOffset(ForgeCommand command)
    {
        var args = Args<EntityOffsetArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Handle))
        {
            return ForgeResult.Failure(command.Id, "missing_handle", "handle is required.");
        }

        if (args.Distance == 0 || double.IsNaN(args.Distance))
        {
            return ForgeResult.Failure(command.Id, "invalid_distance", "Distance must be non-zero (negative offsets toward the curve's closed side).");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { args.Handle, args.Distance, args.Layer });
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var entity = TryOpenEntityForRead(db, tr, args.Handle!);
        if (entity is null)
        {
            return ForgeResult.Failure(command.Id, "handle_not_found", $"No entity for handle: {args.Handle}");
        }

        if (entity is not Curve curve)
        {
            return ForgeResult.Failure(
                command.Id,
                "not_a_curve",
                $"Handle {args.Handle} is a {entity.GetType().Name}; only curves can be offset.",
                "Offset applies to lines, polylines, arcs, circles and ellipses.");
        }

        if (!string.IsNullOrWhiteSpace(args.Layer) && !LayerExists(db, tr, args.Layer!))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}", "Create it with forge_layer_create first.");
        }

        // Plane-normal offset keeps the side deterministic; bad geometry throws and bubbles to
        // the dispatcher as plugin_exception.
        var offsetCurves = curve.GetOffsetCurvesGivenPlaneNormal(Vector3d.ZAxis, args.Distance);
        var created = new List<Entity>();
        foreach (Entity offsetEntity in offsetCurves)
        {
            // Offset results already carry their geometry (and source styling) — append without
            // SetDatabaseDefaults, then move onto the requested layer once DB-resident.
            AppendExistingToModelSpace(db, tr, offsetEntity);
            if (!string.IsNullOrWhiteSpace(args.Layer))
            {
                offsetEntity.Layer = args.Layer!;
            }

            created.Add(offsetEntity);
        }

        var newHandles = created.Select(HandleString).ToArray();
        var readBack = created.Select(EntitySummary).ToArray();
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { created = created.Count, newHandles, sourceHandle = args.Handle },
            verification: new ForgeVerification { Attempted = true, Passed = created.Count > 0, ReadBack = readBack });
    }

    private static ForgeResult EntityErase(ForgeCommand command)
    {
        var args = Args<EntityEraseArgs>(command);
        if (args.Handles is null || args.Handles.Length == 0)
        {
            return ForgeResult.Failure(command.Id, "too_many_handles", "handles[] is required for erase.", "List the explicit handles to erase — there is no select-all path.");
        }

        if (args.Handles.Length > AuthoringMaxEntities)
        {
            return ForgeResult.Failure(command.Id, "too_many_handles", $"Erase accepts at most {AuthoringMaxEntities} handles per call.", "Batch the erase across calls.");
        }

        if (args.Handles.Length > AuthoringBulkAckThreshold && !args.AcknowledgeBulk)
        {
            return ForgeResult.Failure(command.Id, "bulk_not_acknowledged", $"Erasing {args.Handles.Length} entities requires bulk acknowledgement.", "Pass acknowledgeBulk=true after confirming the handle list.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { wouldErase = args.Handles.Length, handles = args.Handles });
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var unknown = new List<string>();
        var opened = OpenHandlesForWrite(db, tr, args.Handles, unknown);
        foreach (var entity in opened)
        {
            entity.Erase();
        }

        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { erased = opened.Count, unknownHandles = unknown, handles = args.Handles },
            verification: new ForgeVerification { Attempted = true, Passed = opened.Count > 0, ReadBack = new { erased = opened.Count, unknownHandles = unknown } });
    }

    private static ForgeResult EntitySetProps(ForgeCommand command)
    {
        var args = Args<EntitySetPropsArgs>(command);
        if (CheckHandles(command, args.Handles, "missing_handles") is { } denied)
        {
            return denied;
        }

        var setLayer = !string.IsNullOrWhiteSpace(args.Layer);
        var setLinetype = !string.IsNullOrWhiteSpace(args.Linetype);
        if (!setLayer && args.ColorIndex is null && !setLinetype && args.Lineweight is null)
        {
            return ForgeResult.Failure(command.Id, "missing_props", "At least one of layer, colorIndex, linetype or lineweight is required.");
        }

        if (args.Lineweight is { } lineweight && lineweight is < -3 or > 211)
        {
            return ForgeResult.Failure(command.Id, "invalid_lineweight", "lineweight must be -3 (default), -2 (ByBlock), -1 (ByLayer) or 0–211 (hundredths of a millimeter).");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { handles = args.Handles, args.Layer, args.ColorIndex, args.Linetype, args.Lineweight });
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (setLayer && !LayerExists(db, tr, args.Layer!))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}", "Create it with forge_layer_create first.");
        }

        if (setLinetype && !LinetypeExists(db, tr, args.Linetype!))
        {
            return ForgeResult.Failure(command.Id, "linetype_not_found", $"Linetype not found: {args.Linetype}");
        }

        var unknown = new List<string>();
        var opened = OpenHandlesForWrite(db, tr, args.Handles!, unknown);
        var updated = 0;
        var readBack = new List<object>();
        foreach (var entity in opened)
        {
            if (setLayer)
            {
                entity.Layer = args.Layer!;
            }

            ApplyColor(entity, args.ColorIndex);
            if (setLinetype)
            {
                entity.Linetype = args.Linetype!;
            }

            if (args.Lineweight is { } weight)
            {
                entity.LineWeight = (LineWeight)weight;
            }

            updated++;
            // EntitySummary has no linetype/lineweight fields, so the read-back repeats the
            // full property set for these props.
            readBack.Add(new
            {
                handle = HandleString(entity),
                type = entity.GetType().Name,
                layer = entity.Layer,
                color = (int)entity.Color.ColorIndex,
                linetype = entity.Linetype,
                lineweight = (short)entity.LineWeight
            });
        }

        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { updated, unknownHandles = unknown },
            verification: new ForgeVerification { Attempted = true, Passed = updated > 0, ReadBack = readBack });
    }

    // forge_undo is the one handler allowed to call Editor.Command: AutoCAD's undo engine owns
    // the group boundaries (the dispatcher wraps each typed write in one UNDO mark), so a single
    // "_.U" pops exactly one Forge write group. No transaction, no dry-run.
    private static ForgeResult Undo(ForgeCommand command)
    {
        var args = Args<UndoArgs>(command); // empty record; kept for the Args<T> convention
        _ = args;
        try
        {
            ActiveEditor.Command("_.U");
            return ForgeResult.Success(command.Id, new { undone = true, note = "AutoCAD executed one undo step." });
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(command.Id, "undo_failed", ex.Message, "There may be nothing left to undo.");
        }
    }

    /// <summary>
    /// Batch entity tools require an explicit non-empty handle list within AuthoringMaxEntities;
    /// returns null when the list is acceptable. overCapCode lets erase report too_many_handles.
    /// </summary>
    private static ForgeResult? CheckHandles(ForgeCommand command, string[]? handles, string overCapCode)
    {
        if (handles is null || handles.Length == 0)
        {
            return ForgeResult.Failure(command.Id, "missing_handles", "handles[] is required and must not be empty.");
        }

        if (handles.Length > AuthoringMaxEntities)
        {
            return ForgeResult.Failure(command.Id, overCapCode, $"A single call accepts at most {AuthoringMaxEntities} handles.", "Split the batch across calls.");
        }

        return null;
    }

    private sealed record EntityGetArgs { public string? Handle { get; init; } }
    private sealed record EntityListArgs { public double[][]? Window { get; init; } public string? Layer { get; init; } public string? Type { get; init; } public int? MaxEntities { get; init; } }
    private sealed record EntityMoveArgs { public string[]? Handles { get; init; } public double[]? Delta { get; init; } }
    private sealed record EntityCopyArgs { public string[]? Handles { get; init; } public double[]? Delta { get; init; } public int Copies { get; init; } = 1; }
    private sealed record EntityRotateArgs { public string[]? Handles { get; init; } public double[]? BasePoint { get; init; } public double AngleDeg { get; init; } }
    private sealed record EntityScaleArgs { public string[]? Handles { get; init; } public double[]? BasePoint { get; init; } public double Factor { get; init; } }
    private sealed record EntityMirrorArgs { public string[]? Handles { get; init; } public double[]? P1 { get; init; } public double[]? P2 { get; init; } public bool EraseSource { get; init; } }
    private sealed record EntityOffsetArgs { public string? Handle { get; init; } public double Distance { get; init; } public string? Layer { get; init; } }
    private sealed record EntityEraseArgs { public string[]? Handles { get; init; } public bool AcknowledgeBulk { get; init; } }
    private sealed record EntitySetPropsArgs { public string[]? Handles { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } public string? Linetype { get; init; } public int? Lineweight { get; init; } }
    private sealed record UndoArgs { }
}
