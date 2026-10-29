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
        var entity = TryOpenEntity(db, tr, args.Handle!, forWrite: false, out var openStatus);
        if (entity is null)
        {
            return ForgeResult.Failure(command.Id, OpenStatusCode(openStatus), $"Could not read entity for handle '{args.Handle}' ({openStatus}).");
        }

        var summary = new
        {
            handle = HandleString(entity),
            type = entity.GetType().Name,
            layer = entity.Layer,
            color = entity.Color.ColorValue.Name ?? entity.Color.ColorIndex.ToString(),
            linetype = entity.Linetype,
            lineweight = (int)entity.LineWeight,
            bbox = TryExtents(entity)
        };

        // Kind read-back is best effort: geometry properties can throw for degenerate entities.
        object? kind = null;
        string? kindError = null;
        try
        {
            kind = entity switch
            {
                Line line => new
                {
                    start = new[] { line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z },
                    end = new[] { line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z },
                    length = line.Length,
                    angleDeg = line.Angle * 180.0 / Math.PI
                },
                Circle circle => new
                {
                    center = new[] { circle.Center.X, circle.Center.Y, circle.Center.Z },
                    radius = circle.Radius,
                    area = circle.Area,
                    circumference = circle.Circumference
                },
                Arc arc => new
                {
                    center = new[] { arc.Center.X, arc.Center.Y, arc.Center.Z },
                    radius = arc.Radius,
                    startAngleDeg = arc.StartAngle * 180.0 / Math.PI,
                    endAngleDeg = arc.EndAngle * 180.0 / Math.PI,
                    startPoint = new[] { arc.StartPoint.X, arc.StartPoint.Y, arc.StartPoint.Z },
                    endPoint = new[] { arc.EndPoint.X, arc.EndPoint.Y, arc.EndPoint.Z },
                    length = arc.Length
                },
                Ellipse ellipse => new
                {
                    center = new[] { ellipse.Center.X, ellipse.Center.Y, ellipse.Center.Z },
                    majorAxis = new[] { ellipse.MajorAxis.X, ellipse.MajorAxis.Y, ellipse.MajorAxis.Z },
                    majorRadius = ellipse.MajorRadius,
                    minorRadius = ellipse.MinorRadius,
                    radiusRatio = ellipse.RadiusRatio,
                    closed = ellipse.Closed
                },
                Spline spline => new
                {
                    degree = spline.Degree,
                    closed = spline.Closed,
                    fitPointCount = spline.NumFitPoints,
                    controlPointCount = spline.NumControlPoints
                },
                Polyline pl => BuildPolylineKindReadBack(pl),
                Polyline2d pl2d => BuildPolyline2dKindReadBack(tr, pl2d),
                Polyline3d pl3d => BuildPolyline3dKindReadBack(tr, pl3d),
                DBText text => new
                {
                    textString = text.TextString,
                    position = new[] { text.Position.X, text.Position.Y, text.Position.Z },
                    height = text.Height,
                    rotationDeg = text.Rotation * 180.0 / Math.PI
                },
                MText mtext => new
                {
                    contents = mtext.Contents,
                    text = mtext.Text,
                    location = new[] { mtext.Location.X, mtext.Location.Y, mtext.Location.Z },
                    textHeight = mtext.TextHeight,
                    width = mtext.Width,
                    rotationDeg = mtext.Rotation * 180.0 / Math.PI
                },
                Table table => BuildTableKindReadBack(table),
                BlockReference blockRef => BuildBlockRefKindReadBack(tr, blockRef),
                Dimension dimension => new
                {
                    measurement = dimension.Measurement,
                    dimensionText = dimension.DimensionText,
                    dimStyleName = dimension.DimensionStyleName
                },
                Hatch hatch => BuildHatchKindReadBack(hatch),
                MLeader mleader => new
                {
                    contentType = mleader.ContentType.ToString(),
                    text = mleader.ContentType == ContentType.MTextContent ? mleader.MText?.Contents : null,
                    leaderCount = mleader.LeaderCount
                },
                Region region => new
                {
                    area = TryRegionArea(region),
                    perimeter = region.Perimeter
                },
                Xline xline => new
                {
                    basePoint = new[] { xline.BasePoint.X, xline.BasePoint.Y, xline.BasePoint.Z },
                    unitDir = new[] { xline.UnitDir.X, xline.UnitDir.Y, xline.UnitDir.Z }
                },
                Ray ray => new
                {
                    basePoint = new[] { ray.BasePoint.X, ray.BasePoint.Y, ray.BasePoint.Z },
                    unitDir = new[] { ray.UnitDir.X, ray.UnitDir.Y, ray.UnitDir.Z }
                },
                _ => null
            };
        }
        catch (System.Exception ex)
        {
            kindError = ex.Message;
        }

        return ForgeResult.Success(command.Id, new { entity = summary, kind, kindError });
    }

    private static object BuildPolylineKindReadBack(Polyline pl)
    {
        const int cap = 1000;
        var count = pl.NumberOfVertices;
        var maxSample = Math.Min(count, cap);
        var vertices = new double[maxSample][];
        var bulges = new double[maxSample];
        var startWidths = new double[maxSample];
        var endWidths = new double[maxSample];
        for (var i = 0; i < maxSample; i++)
        {
            var pt = pl.GetPoint2dAt(i);
            vertices[i] = [pt.X, pt.Y];
            bulges[i] = pl.GetBulgeAt(i);
            startWidths[i] = pl.GetStartWidthAt(i);
            endWidths[i] = pl.GetEndWidthAt(i);
        }

        double? area = null;
        if (pl.Closed)
        {
            try
            {
                area = pl.Area;
            }
            catch
            {
                area = null;
            }
        }

        double? constantWidth = null;
        try
        {
            constantWidth = pl.ConstantWidth;
        }
        catch
        {
            // ConstantWidth throws eInvalidInput when vertex widths vary.
            constantWidth = null;
        }

        return new
        {
            vertexCount = count,
            truncated = count > maxSample,
            closed = pl.Closed,
            elevation = pl.Elevation,
            length = pl.Length,
            area,
            constantWidth,
            vertices,
            bulges,
            startWidths,
            endWidths
        };
    }

    private static object BuildPolyline2dKindReadBack(Transaction tr, Polyline2d pl2d)
    {
        const int cap = 1000;
        var vertices = new List<double[]>();
        var total = 0;
        foreach (ObjectId vId in pl2d)
        {
            if (tr.GetObject(vId, OpenMode.ForRead) is Vertex2d v)
            {
                total++;
                if (vertices.Count < cap)
                {
                    vertices.Add([v.Position.X, v.Position.Y, v.Position.Z]);
                }
            }
        }

        return new { vertexCount = total, truncated = total > cap, closed = pl2d.Closed, length = pl2d.Length, vertices };
    }

    private static object BuildPolyline3dKindReadBack(Transaction tr, Polyline3d pl3d)
    {
        const int cap = 1000;
        var vertices = new List<double[]>();
        var total = 0;
        foreach (ObjectId vId in pl3d)
        {
            if (tr.GetObject(vId, OpenMode.ForRead) is PolylineVertex3d v)
            {
                total++;
                if (vertices.Count < cap)
                {
                    vertices.Add([v.Position.X, v.Position.Y, v.Position.Z]);
                }
            }
        }

        return new { vertexCount = total, truncated = total > cap, closed = pl3d.Closed, length = pl3d.Length, vertices };
    }

    private static object BuildBlockRefKindReadBack(Transaction tr, BlockReference blockRef)
    {
        var effectiveName = blockRef.Name;
        try
        {
            if (blockRef.IsDynamicBlock && !blockRef.DynamicBlockTableRecord.IsNull &&
                tr.GetObject(blockRef.DynamicBlockTableRecord, OpenMode.ForRead) is BlockTableRecord dynBtr)
            {
                effectiveName = dynBtr.Name;
            }
        }
        catch
        {
            // Keep blockRef.Name if dynamic record lookup fails.
        }

        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId attId in blockRef.AttributeCollection)
        {
            if (!attId.IsErased && tr.GetObject(attId, OpenMode.ForRead) is AttributeReference attRef)
            {
                attributes[attRef.Tag] = attRef.TextString;
            }
        }

        return new
        {
            name = effectiveName,
            rawName = blockRef.Name,
            isDynamic = blockRef.IsDynamicBlock,
            position = new[] { blockRef.Position.X, blockRef.Position.Y, blockRef.Position.Z },
            scale = new[] { blockRef.ScaleFactors.X, blockRef.ScaleFactors.Y, blockRef.ScaleFactors.Z },
            rotationDeg = blockRef.Rotation * 180.0 / Math.PI,
            attributes
        };
    }

    private static object BuildHatchKindReadBack(Hatch hatch)
    {
        double? area = null;
        try
        {
            area = hatch.Area;
        }
        catch
        {
            area = null;
        }

        return new
        {
            patternName = hatch.PatternName,
            patternScale = hatch.PatternScale,
            patternAngleDeg = hatch.PatternAngle * 180.0 / Math.PI,
            numberOfLoops = hatch.NumberOfLoops,
            area
        };
    }

    private static object BuildTableKindReadBack(Table table)
    {
        var rows = table.Rows.Count;
        var cols = table.Columns.Count;
        var sampleRows = Math.Min(rows, 50);
        var sampleCols = Math.Min(cols, 20);
        var cells = new string[sampleRows][];
        for (var r = 0; r < sampleRows; r++)
        {
            cells[r] = new string[sampleCols];
            for (var c = 0; c < sampleCols; c++)
            {
                try
                {
                    cells[r][c] = table.Cells[r, c].TextString ?? "";
                }
                catch
                {
                    cells[r][c] = "";
                }
            }
        }

        return new
        {
            rows,
            columns = cols,
            truncated = rows > sampleRows || cols > sampleCols,
            position = new[] { table.Position.X, table.Position.Y, table.Position.Z },
            cells
        };
    }

    private static double? TryRegionArea(Region region)
    {
        try
        {
            return region.Area;
        }
        catch
        {
            return null;
        }
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

        if (args.IncludeBlocks)
        {
            return EntityListWithBlocks(command, args, tr, modelSpace, hasWindow ? [minX, minY, maxX, maxY] : null, maxEntities);
        }

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

    /// <summary>
    /// forge_entity_list with includeBlocks=true: the INSERTs themselves plus everything nested inside
    /// them (world bbox, effective layer, depth, insert/block path). The window prunes INSERTs whose
    /// world extents miss it; the layer filter matches the entity's own or effective layer.
    /// </summary>
    private static ForgeResult EntityListWithBlocks(ForgeCommand command, EntityListArgs args, Transaction tr, BlockTableRecord modelSpace, double[]? window, int maxEntities)
    {
        var options = new BlockWalkOptions(true, BlockWalk.ClampDepth(args.BlockDepth), window, IncludeXrefs: true);
        var stats = new BlockWalkStats();
        var entities = new List<object>();
        WalkBlockSpace(tr, modelSpace, options, stats, (entity, _, frame) =>
        {
            var effectiveLayer = BlockWalk.EffectiveLayer(entity.Layer, frame.ParentEffectiveLayer);
            if (!string.IsNullOrWhiteSpace(args.Layer) &&
                !entity.Layer.Equals(args.Layer, StringComparison.OrdinalIgnoreCase) &&
                !effectiveLayer.Equals(args.Layer, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(args.Type) && !entity.GetType().Name.Equals(args.Type, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var bbox = WorldBox(entity, frame);
            if (window is not null && (bbox is null || !BlockWalk.BoxesIntersect(bbox, window)))
            {
                return true;
            }

            entities.Add(new
            {
                handle = HandleString(entity),
                type = entity.GetType().Name,
                layer = entity.Layer,
                effectiveLayer,
                depth = frame.Depth,
                insertPath = frame.Path.Count > 0 ? BlockWalk.InsertPathLabel(frame.Path) : null,
                blockPath = frame.Path.Count > 0 ? frame.Path.Select(p => p.BlockName).ToArray() : null,
                bbox
            });
            return entities.Count < maxEntities;
        });

        tr.Commit();
        return ForgeResult.Success(command.Id, new
        {
            count = entities.Count,
            truncated = stats.Truncated,
            blockTraversal = BlockTraversalReport(options, stats),
            entities
        });
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
        var locked = new List<string>();
        var opened = OpenHandlesForWrite(db, tr, args.Handles!, unknown, locked);
        if (opened.Count == 0)
        {
            return ForgeResult.Failure(
                command.Id,
                locked.Count > 0 ? "layer_locked" : "handle_not_found",
                locked.Count > 0 ? "All target entities are on locked layers." : "No valid layout entities found for the supplied handles.",
                data: new { unknownHandles = unknown, lockedHandles = locked });
        }

        var displacement = Matrix3d.Displacement(delta);
        foreach (var entity in opened)
        {
            entity.TransformBy(displacement);
        }

        var readBack = opened.Select(EntitySummary).ToArray();
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { moved = opened.Count, unknownHandles = unknown, lockedHandles = locked, handles = args.Handles },
            verification: new ForgeVerification { Attempted = true, Passed = opened.Count > 0 && unknown.Count == 0 && locked.Count == 0, ReadBack = readBack });
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
        var seen = new HashSet<ObjectId>();
        foreach (var handle in args.Handles!)
        {
            var source = TryOpenEntityForRead(db, tr, handle);
            if (source is null)
            {
                unknown.Add(handle);
                continue;
            }

            if (!seen.Add(source.ObjectId))
            {
                continue;
            }

            for (var c = 0; c < copies; c++)
            {
                // Deep-clone into the source's owner space (Model or Paper space) so BlockReference
                // attributes and Polyline2d vertices are preserved, then displace copy c+1 by (c+1)×delta.
                var clone = CloneEntityToOwner(db, tr, source, source.OwnerId);
                clone.TransformBy(Matrix3d.Displacement(new Vector3d(delta.X * (c + 1), delta.Y * (c + 1), delta.Z * (c + 1))));
                created.Add(clone);
            }
        }

        if (created.Count == 0)
        {
            return ForgeResult.Failure(
                command.Id,
                "handle_not_found",
                "No valid layout entities found for the supplied handles.",
                data: new { unknownHandles = unknown });
        }

        var newHandles = created.Select(HandleString).ToArray();
        var readBack = created.Select(EntitySummary).ToArray();
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { created = created.Count, newHandles, unknownHandles = unknown },
            verification: new ForgeVerification { Attempted = true, Passed = created.Count > 0 && unknown.Count == 0, ReadBack = readBack });
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
        var locked = new List<string>();
        var opened = OpenHandlesForWrite(db, tr, args.Handles!, unknown, locked);
        if (opened.Count == 0)
        {
            return ForgeResult.Failure(
                command.Id,
                locked.Count > 0 ? "layer_locked" : "handle_not_found",
                locked.Count > 0 ? "All target entities are on locked layers." : "No valid layout entities found for the supplied handles.",
                data: new { unknownHandles = unknown, lockedHandles = locked });
        }

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
            new { rotated = opened.Count, unknownHandles = unknown, lockedHandles = locked, handles = args.Handles },
            verification: new ForgeVerification { Attempted = true, Passed = opened.Count > 0 && unknown.Count == 0 && locked.Count == 0, ReadBack = readBack });
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
        var locked = new List<string>();
        var opened = OpenHandlesForWrite(db, tr, args.Handles!, unknown, locked);
        if (opened.Count == 0)
        {
            return ForgeResult.Failure(
                command.Id,
                locked.Count > 0 ? "layer_locked" : "handle_not_found",
                locked.Count > 0 ? "All target entities are on locked layers." : "No valid layout entities found for the supplied handles.",
                data: new { unknownHandles = unknown, lockedHandles = locked });
        }

        var scaling = Matrix3d.Scaling(args.Factor, basePoint);
        foreach (var entity in opened)
        {
            entity.TransformBy(scaling);
        }

        var readBack = opened.Select(EntitySummary).ToArray();
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { scaled = opened.Count, unknownHandles = unknown, lockedHandles = locked, handles = args.Handles },
            verification: new ForgeVerification { Attempted = true, Passed = opened.Count > 0 && unknown.Count == 0 && locked.Count == 0, ReadBack = readBack });
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

        if (p1.DistanceTo(p2) <= 1e-9)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", "Mirror axis points p1 and p2 must be distinct.");
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
        var locked = new List<string>();
        var opened = OpenHandlesForWrite(db, tr, args.Handles!, unknown, locked);
        if (opened.Count == 0)
        {
            return ForgeResult.Failure(
                command.Id,
                locked.Count > 0 ? "layer_locked" : "handle_not_found",
                locked.Count > 0 ? "All target entities are on locked layers." : "No valid layout entities found for the supplied handles.",
                data: new { unknownHandles = unknown, lockedHandles = locked });
        }

        // Reflect across the vertical plane containing p1–p2 (det = -1). Unlike Matrix3d.Mirroring(Line3d)
        // (which is a 180° 3D rotation with det = +1 that flips Normal to -Z and bypasses MIRRTEXT),
        // a Plane reflection preserves Z and Normal = +Z and triggers ObjectARX's native MIRRTEXT
        // handling for DBText, MText, and BlockReference AttributeReferences.
        var axis = p2 - p1;
        var planeNormal = axis.CrossProduct(Vector3d.ZAxis);
        var mirroring = planeNormal.Length > 1e-9
            ? Matrix3d.Mirroring(new Plane(p1, planeNormal.GetNormal()))
            : Matrix3d.Mirroring(new Line3d(p1, p2));
        var mirrored = 0;
        var erased = 0;
        var newHandles = new List<string>();
        var readBack = new List<object>();
        foreach (var entity in opened)
        {
            // Deep-clone into the source entity's owner block so BlockReference attributes and
            // Polyline2d vertices are preserved and eraseSource=false keeps both original and mirror.
            var clone = CloneEntityToOwner(db, tr, entity, entity.OwnerId);
            clone.TransformBy(mirroring);
            newHandles.Add(HandleString(clone));
            readBack.Add(EntitySummary(clone));
            if (args.EraseSource)
            {
                entity.Erase();
                erased++;
            }

            mirrored++;
        }

        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { mirrored, erased, newHandles, unknownHandles = unknown, lockedHandles = locked },
            verification: new ForgeVerification { Attempted = true, Passed = mirrored > 0 && unknown.Count == 0 && locked.Count == 0, ReadBack = readBack });
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
            // Offset results already carry their geometry (and source styling) — append to the
            // source entity's owner block, then move onto the requested layer once DB-resident.
            AppendToOwner(db, tr, entity.OwnerId, offsetEntity);
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
        var locked = new List<string>();
        var opened = OpenHandlesForWrite(db, tr, args.Handles, unknown, locked);
        if (opened.Count == 0)
        {
            return ForgeResult.Failure(
                command.Id,
                locked.Count > 0 ? "layer_locked" : "handle_not_found",
                locked.Count > 0 ? "All target entities are on locked layers." : "No valid layout entities found for the supplied handles.",
                data: new { unknownHandles = unknown, lockedHandles = locked });
        }

        foreach (var entity in opened)
        {
            entity.Erase();
        }

        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { erased = opened.Count, unknownHandles = unknown, lockedHandles = locked, handles = args.Handles },
            verification: new ForgeVerification { Attempted = true, Passed = opened.Count > 0 && unknown.Count == 0 && locked.Count == 0, ReadBack = new { erased = opened.Count, unknownHandles = unknown, lockedHandles = locked } });
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
        var locked = new List<string>();
        var opened = OpenHandlesForWrite(db, tr, args.Handles!, unknown, locked);
        if (opened.Count == 0)
        {
            return ForgeResult.Failure(
                command.Id,
                locked.Count > 0 ? "layer_locked" : "handle_not_found",
                locked.Count > 0 ? "All target entities are on locked layers." : "No valid layout entities found for the supplied handles.",
                data: new { unknownHandles = unknown, lockedHandles = locked });
        }

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
            new { updated, unknownHandles = unknown, lockedHandles = locked },
            verification: new ForgeVerification { Attempted = true, Passed = updated > 0 && unknown.Count == 0 && locked.Count == 0, ReadBack = readBack });
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
            var undoCtl = System.Convert.ToInt32(Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("UNDOCTL"));
            if ((undoCtl & 1) == 0)
            {
                return ForgeResult.Failure(command.Id, "undo_failed", "AutoCAD UNDO is disabled (UNDOCTL=0).", "Enable UNDO in AutoCAD before calling forge_undo.");
            }

            var db = ActiveDb;
            var trackedBefore = GetTrackedUndoDepth(db);
            var dbmodBefore = System.Convert.ToInt32(Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("DBMOD"));
            var handseedBefore = db.Handseed.Value;
            if (trackedBefore <= 0 && dbmodBefore == 0)
            {
                return ForgeResult.Failure(
                    command.Id,
                    "undo_failed",
                    "Nothing to undo in the active document.",
                    "No Forge write groups or unsaved drawing modifications are present on the undo stack.");
            }

            ActiveEditor.Command("_.U");
            var hadTrackedGroup = TryDecrementTrackedUndoDepth(db, out var remainingForgeUndoSteps);
            var dbmodAfter = System.Convert.ToInt32(Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("DBMOD"));
            var handseedAfter = db.Handseed.Value;

            if (!hadTrackedGroup && dbmodBefore == dbmodAfter && handseedBefore == handseedAfter)
            {
                return ForgeResult.Failure(
                    command.Id,
                    "undo_failed",
                    "Nothing was undone (undo stack is already at the beginning of the session).",
                    "Perform a drawing modification before calling forge_undo.");
            }

            return ForgeResult.Success(
                command.Id,
                new
                {
                    undone = true,
                    undoCtl,
                    remainingForgeUndoSteps,
                    note = "AutoCAD executed one undo step."
                });
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(command.Id, "undo_failed", ex.Message, "There may be nothing left to undo.");
        }
    }

    // ===================== C26 / C28 — precision editing & geometric intelligence =====================

    private static ForgeResult EntityFillet(ForgeCommand command)
    {
        var args = Args<EntityFilletArgs>(command);
        var h1 = args.Handle1 ?? args.Handle;
        if (string.IsNullOrWhiteSpace(h1))
        {
            return ForgeResult.Failure(command.Id, "missing_handle", "handle1 (or handle) is required.");
        }

        if (args.Radius < 0 || double.IsNaN(args.Radius) || double.IsInfinity(args.Radius))
        {
            return ForgeResult.Failure(command.Id, "invalid_radius", "radius must be finite and >= 0.");
        }

        if (!string.IsNullOrWhiteSpace(args.Handle2) &&
            string.Equals(h1.Trim(), args.Handle2.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return ForgeResult.Failure(command.Id, "same_handle", "handle1 and handle2 must reference two different Line entities.");
        }

        if (!TryValidateOptionalPoint2D(args.PickPoint1, "pickPoint1", out var pick1, out var pickErr1))
        {
            return ForgeResult.Failure(command.Id, "invalid_args", pickErr1!);
        }

        if (!TryValidateOptionalPoint2D(args.PickPoint2, "pickPoint2", out var pick2, out var pickErr2))
        {
            return ForgeResult.Failure(command.Id, "invalid_args", pickErr2!);
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (!string.IsNullOrWhiteSpace(args.Layer) && !LayerExists(db, tr, args.Layer!))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}", "Create it with forge_layer_create first.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { tool = "forge_entity_fillet", handle1 = h1, args.Handle2, args.Radius, args.PickPoint1, args.PickPoint2, args.Layer });
        }

        var e1 = TryOpenEntityForWrite(db, tr, h1!, out var s1);
        if (e1 is null)
        {
            return ForgeResult.Failure(command.Id, OpenStatusCode(s1), $"Cannot open entity {h1} for write ({s1}).");
        }

        // Case 1: Single Polyline — fillet all straight interior/closed corners
        if (string.IsNullOrWhiteSpace(args.Handle2))
        {
            if (e1 is not Polyline pl)
            {
                return ForgeResult.Failure(command.Id, "invalid_entity_type", "Single-handle fillet requires a Polyline; provide handle2 to fillet two Lines.");
            }

            if (args.Radius <= 0)
            {
                return ForgeResult.Failure(command.Id, "invalid_radius", "Single-polyline fillet requires radius > 0.");
            }

            var vCount = pl.NumberOfVertices;
            var verts = new CadGeometryMath.PolylineVertex2D[vCount];
            var startWidths = new double[vCount];
            var endWidths = new double[vCount];
            for (var i = 0; i < vCount; i++)
            {
                var pt = pl.GetPoint2dAt(i);
                verts[i] = new CadGeometryMath.PolylineVertex2D(pt.X, pt.Y, pl.GetBulgeAt(i));
                startWidths[i] = pl.GetStartWidthAt(i);
                endWidths[i] = pl.GetEndWidthAt(i);
            }

            var detailed = CadGeometryMath.FilletPolylineCornersDetailed(verts, pl.Closed, args.Radius);
            var skippedDto = detailed.SkippedCorners.Select(s => new { index = s.Index, reason = s.Reason }).ToArray();
            if (detailed.FilletedCorners.Length == 0)
            {
                return ForgeResult.Failure(
                    command.Id,
                    "nothing_filleted",
                    "No corners could be filleted with the requested radius.",
                    "Reduce radius or check skippedCorners for arc/collinear/short segments.",
                    data: new { handle = HandleString(pl), filletedCorners = detailed.FilletedCorners, skippedCorners = skippedDto });
            }

            // Append new vertices first, then remove old vertices so the polyline never drops to 0 vertices.
            for (var i = 0; i < detailed.Vertices.Length; i++)
            {
                var v = detailed.Vertices[i];
                var src = detailed.SourceIndex[i];
                var role = detailed.Roles[i];
                double sw;
                double ew;
                if (role == CadGeometryMath.PolylineFilletVertexRole.TangentIn)
                {
                    var prevIdx = (src - 1 + vCount) % vCount;
                    sw = endWidths[prevIdx];
                    ew = startWidths[src];
                }
                else
                {
                    sw = startWidths[src];
                    ew = endWidths[src];
                }

                pl.AddVertexAt(vCount + i, new Point2d(v.X, v.Y), v.Bulge, sw, ew);
            }

            for (var i = vCount - 1; i >= 0; i--)
            {
                pl.RemoveVertexAt(i);
            }

            if (!string.IsNullOrWhiteSpace(args.Layer))
            {
                pl.Layer = args.Layer!;
            }

            var plReadBack = new
            {
                mode = "polyline",
                handle = HandleString(pl),
                vertexCount = pl.NumberOfVertices,
                closed = pl.Closed,
                length = pl.Length,
                filletedCorners = detailed.FilletedCorners,
                skippedCorners = skippedDto
            };
            tr.Commit();
            return ForgeResult.Success(
                command.Id,
                plReadBack,
                verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = plReadBack });
        }

        // Case 2: Two Lines — trim/extend to tangent arc (or sharp corner if radius == 0)
        var e2 = TryOpenEntityForWrite(db, tr, args.Handle2!, out var s2);
        if (e2 is null)
        {
            return ForgeResult.Failure(command.Id, OpenStatusCode(s2), $"Cannot open entity {args.Handle2} for write ({s2}).");
        }

        if (e1 is not Line line1 || e2 is not Line line2)
        {
            return ForgeResult.Failure(command.Id, "invalid_entity_type", "Two-entity fillet supports Line entities.");
        }

        var z = line1.StartPoint.Z;
        if (Math.Abs(line1.EndPoint.Z - z) > 1e-6 ||
            Math.Abs(line2.StartPoint.Z - z) > 1e-6 ||
            Math.Abs(line2.EndPoint.Z - z) > 1e-6)
        {
            return ForgeResult.Failure(command.Id, "not_planar", "Both lines must lie in the same horizontal Z plane.");
        }

        CadGeometryMath.LineFilletResult result;
        try
        {
            result = CadGeometryMath.ComputeLineFillet(
                [line1.StartPoint.X, line1.StartPoint.Y, z],
                [line1.EndPoint.X, line1.EndPoint.Y, z],
                [line2.StartPoint.X, line2.StartPoint.Y, z],
                [line2.EndPoint.X, line2.EndPoint.Y, z],
                args.Radius,
                pick1,
                pick2);
        }
        catch (CadAmbiguousSideException ex)
        {
            return ForgeResult.Failure(
                command.Id,
                "ambiguous_side",
                ex.Message,
                $"Pass pickPoint{ex.LineNumber} [x,y] on the side of line {ex.LineNumber} that should be kept.");
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(command.Id, "fillet_failed", ex.Message);
        }

        line1.StartPoint = new Point3d(result.Line1Start[0], result.Line1Start[1], z);
        line1.EndPoint = new Point3d(result.Line1End[0], result.Line1End[1], z);
        line2.StartPoint = new Point3d(result.Line2Start[0], result.Line2Start[1], z);
        line2.EndPoint = new Point3d(result.Line2End[0], result.Line2End[1], z);

        string? arcHandle = null;
        if (result.HasArc && result.ArcCenter is not null)
        {
            var arc = new Arc(
                new Point3d(result.ArcCenter[0], result.ArcCenter[1], z),
                result.ArcRadius,
                DegreesToRadians(result.ArcStartAngleDeg),
                DegreesToRadians(result.ArcEndAngleDeg));
            try
            {
                arc.SetDatabaseDefaults(db);
                arc.SetPropertiesFrom(line1);
                if (!string.IsNullOrWhiteSpace(args.Layer))
                {
                    arc.Layer = args.Layer!;
                }

                AppendToOwner(db, tr, line1.OwnerId, arc);
                arcHandle = HandleString(arc);
            }
            catch
            {
                if (arc.ObjectId.IsNull)
                {
                    arc.Dispose();
                }

                throw;
            }
        }

        var readBack = new
        {
            mode = "lines",
            handle1 = HandleString(line1),
            handle2 = HandleString(line2),
            arcHandle,
            radius = args.Radius,
            intersection = result.IntersectionPoint
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            readBack,
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult EntityChamfer(ForgeCommand command)
    {
        var args = Args<EntityChamferArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Handle1) || string.IsNullOrWhiteSpace(args.Handle2))
        {
            return ForgeResult.Failure(command.Id, "missing_handle", "handle1 and handle2 are required.");
        }

        if (string.Equals(args.Handle1.Trim(), args.Handle2.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return ForgeResult.Failure(command.Id, "same_handle", "handle1 and handle2 must reference two different Line entities.");
        }

        var d1 = args.Distance1;
        var d2 = args.Distance2 ?? d1;
        if (d1 <= 0 || d2 <= 0 || double.IsNaN(d1) || double.IsNaN(d2) || double.IsInfinity(d1) || double.IsInfinity(d2))
        {
            return ForgeResult.Failure(command.Id, "invalid_distance", "distance1 and distance2 must be finite and > 0.");
        }

        if (!TryValidateOptionalPoint2D(args.PickPoint1, "pickPoint1", out var pick1, out var pickErr1))
        {
            return ForgeResult.Failure(command.Id, "invalid_args", pickErr1!);
        }

        if (!TryValidateOptionalPoint2D(args.PickPoint2, "pickPoint2", out var pick2, out var pickErr2))
        {
            return ForgeResult.Failure(command.Id, "invalid_args", pickErr2!);
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (!string.IsNullOrWhiteSpace(args.Layer) && !LayerExists(db, tr, args.Layer!))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}", "Create it with forge_layer_create first.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { tool = "forge_entity_chamfer", args.Handle1, args.Handle2, distance1 = d1, distance2 = d2, args.PickPoint1, args.PickPoint2, args.Layer });
        }

        var e1 = TryOpenEntityForWrite(db, tr, args.Handle1!, out var s1);
        if (e1 is null)
        {
            return ForgeResult.Failure(command.Id, OpenStatusCode(s1), $"Cannot open entity {args.Handle1} for write ({s1}).");
        }

        var e2 = TryOpenEntityForWrite(db, tr, args.Handle2!, out var s2);
        if (e2 is null)
        {
            return ForgeResult.Failure(command.Id, OpenStatusCode(s2), $"Cannot open entity {args.Handle2} for write ({s2}).");
        }

        if (e1 is not Line line1 || e2 is not Line line2)
        {
            return ForgeResult.Failure(command.Id, "invalid_entity_type", "forge_entity_chamfer supports Line entities.");
        }

        var z = line1.StartPoint.Z;
        if (Math.Abs(line1.EndPoint.Z - z) > 1e-6 ||
            Math.Abs(line2.StartPoint.Z - z) > 1e-6 ||
            Math.Abs(line2.EndPoint.Z - z) > 1e-6)
        {
            return ForgeResult.Failure(command.Id, "not_planar", "Both lines must lie in the same horizontal Z plane.");
        }

        CadGeometryMath.LineChamferResult result;
        try
        {
            result = CadGeometryMath.ComputeLineChamfer(
                [line1.StartPoint.X, line1.StartPoint.Y, z],
                [line1.EndPoint.X, line1.EndPoint.Y, z],
                [line2.StartPoint.X, line2.StartPoint.Y, z],
                [line2.EndPoint.X, line2.EndPoint.Y, z],
                d1,
                d2,
                pick1,
                pick2);
        }
        catch (CadAmbiguousSideException ex)
        {
            return ForgeResult.Failure(
                command.Id,
                "ambiguous_side",
                ex.Message,
                $"Pass pickPoint{ex.LineNumber} [x,y] on the side of line {ex.LineNumber} that should be kept.");
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(command.Id, "chamfer_failed", ex.Message);
        }

        line1.StartPoint = new Point3d(result.Line1Start[0], result.Line1Start[1], z);
        line1.EndPoint = new Point3d(result.Line1End[0], result.Line1End[1], z);
        line2.StartPoint = new Point3d(result.Line2Start[0], result.Line2Start[1], z);
        line2.EndPoint = new Point3d(result.Line2End[0], result.Line2End[1], z);

        string? chamferHandle = null;
        if (result.HasChamferSegment)
        {
            var bevel = new Line(
                new Point3d(result.ChamferStart[0], result.ChamferStart[1], z),
                new Point3d(result.ChamferEnd[0], result.ChamferEnd[1], z));
            try
            {
                bevel.SetDatabaseDefaults(db);
                bevel.SetPropertiesFrom(line1);
                if (!string.IsNullOrWhiteSpace(args.Layer))
                {
                    bevel.Layer = args.Layer!;
                }

                AppendToOwner(db, tr, line1.OwnerId, bevel);
                chamferHandle = HandleString(bevel);
            }
            catch
            {
                if (bevel.ObjectId.IsNull)
                {
                    bevel.Dispose();
                }

                throw;
            }
        }

        var readBack = new
        {
            handle1 = HandleString(line1),
            handle2 = HandleString(line2),
            chamferHandle,
            distance1 = d1,
            distance2 = d2,
            intersection = result.IntersectionPoint
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            readBack,
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult EntityTrim(ForgeCommand command)
    {
        var args = Args<EntityTrimArgs>(command);
        if (string.IsNullOrWhiteSpace(args.TargetHandle))
        {
            return ForgeResult.Failure(command.Id, "missing_handle", "targetHandle is required.");
        }

        if (args.CuttingHandles is null || args.CuttingHandles.Length == 0)
        {
            return ForgeResult.Failure(command.Id, "missing_handles", "cuttingHandles[] is required.");
        }

        if (args.PickPoint is null && args.KeepPoint is null)
        {
            return ForgeResult.Failure(command.Id, "missing_pick_point", "pickPoint [x,y] is required to identify which segment to trim away.");
        }

        Point3d? keepPt = null;
        Point3d? pickPt = null;
        try
        {
            if (args.KeepPoint is not null)
            {
                keepPt = ToPoint3d(args.KeepPoint);
            }

            if (args.PickPoint is not null)
            {
                pickPt = ToPoint3d(args.PickPoint);
            }
        }
        catch (ArgumentException ex)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", ex.Message);
        }

        if (command.DryRun)
        {
            return DryRun(command, new { tool = "forge_entity_trim", args.TargetHandle, args.CuttingHandles, args.PickPoint, args.KeepPoint });
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var target = TryOpenEntityForWrite(db, tr, args.TargetHandle!, out var targetStatus);
        if (target is null)
        {
            return ForgeResult.Failure(command.Id, OpenStatusCode(targetStatus), $"Cannot open targetHandle {args.TargetHandle} for write ({targetStatus}).");
        }

        if (target is not Curve targetCurve)
        {
            return ForgeResult.Failure(command.Id, "not_a_curve", $"Target {args.TargetHandle} is a {target.GetType().Name}; only curves can be trimmed.");
        }

        var intersections = new List<Point3d>();
        foreach (var ch in args.CuttingHandles)
        {
            if (string.IsNullOrWhiteSpace(ch))
            {
                return ForgeResult.Failure(command.Id, "cutter_not_found", "cuttingHandles[] contains an empty handle.");
            }

            if (string.Equals(ch.Trim(), args.TargetHandle.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return ForgeResult.Failure(command.Id, "cutter_is_target", "A curve cannot be trimmed against itself.");
            }

            var cutter = TryOpenEntity(db, tr, ch, forWrite: false, out _);
            if (cutter is null)
            {
                return ForgeResult.Failure(command.Id, "cutter_not_found", $"No cutting entity found for handle: {ch}");
            }

            var pts = new Point3dCollection();
            targetCurve.IntersectWith(cutter, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
            foreach (Point3d pt in pts)
            {
                intersections.Add(pt);
            }
        }

        if (intersections.Count == 0)
        {
            return ForgeResult.Failure(command.Id, "no_intersection", "Target curve does not intersect any of the cutting entities.");
        }

        var startDist = targetCurve.GetDistanceAtParameter(targetCurve.StartParam);
        var endDist = targetCurve.GetDistanceAtParameter(targetCurve.EndParam);
        var curveLen = Math.Abs(endDist - startDist);
        var isClosed = IsClosedCurve(targetCurve);

        var rawDistances = new List<double>(intersections.Count);
        foreach (var pt in intersections)
        {
            var onCurve = targetCurve.GetClosestPointTo(pt, false);
            var param = targetCurve.GetParameterAtPoint(onCurve);
            var dist = Math.Abs(targetCurve.GetDistanceAtParameter(param) - startDist);
            rawDistances.Add(dist);
        }

        var cutDistances = CadEditMath.NormalizeCutDistances(rawDistances, curveLen, isClosed);
        if (cutDistances.Length < CadEditMath.MinimumCuts(isClosed))
        {
            return isClosed
                ? ForgeResult.Failure(command.Id, "insufficient_cuts", "Closed curves require at least 2 distinct intersection points to trim.")
                : ForgeResult.Failure(command.Id, "no_interior_intersection", "Intersection lies at the curve's endpoint; nothing to trim.");
        }

        var splitParams = cutDistances
            .Select(d => d <= 0 ? targetCurve.StartParam : targetCurve.GetParameterAtDistance(startDist + d))
            .OrderBy(p => p)
            .ToArray();

        var pieces = targetCurve.GetSplitCurves(new DoubleCollection(splitParams));
        var curvePieces = new List<Curve>(pieces.Count);
        try
        {
            foreach (DBObject obj in pieces)
            {
                if (obj is Curve c)
                {
                    curvePieces.Add(c);
                }
                else
                {
                    obj.Dispose();
                }
            }

            if (curvePieces.Count < 2)
            {
                return ForgeResult.Failure(command.Id, "trim_failed", "GetSplitCurves produced fewer than 2 segments.");
            }

            // Decide which piece(s) to remove:
            // - With pickPoint: remove the single split piece closest to pickPoint (standard CAD TRIM).
            // - Legacy keepPoint fallback: keep the single piece closest to keepPoint.
            var removeFlags = new bool[curvePieces.Count];
            if (pickPt.HasValue)
            {
                var dists = curvePieces
                    .Select(p => p.GetClosestPointTo(pickPt.Value, false).DistanceTo(pickPt.Value))
                    .ToArray();
                var removeIdx = CadEditMath.IndexOfClosest(dists);
                if (removeIdx < 0)
                {
                    return ForgeResult.Failure(command.Id, "trim_failed", "Could not identify segment closest to pickPoint.");
                }

                removeFlags[removeIdx] = true;
            }
            else if (keepPt.HasValue)
            {
                var dists = curvePieces
                    .Select(p => p.GetClosestPointTo(keepPt.Value, false).DistanceTo(keepPt.Value))
                    .ToArray();
                var keepIdx = CadEditMath.IndexOfClosest(dists);
                for (var i = 0; i < removeFlags.Length; i++)
                {
                    removeFlags[i] = i != keepIdx;
                }
            }

            double removedLength = 0;
            var surviving = new List<Curve>();
            for (var i = 0; i < curvePieces.Count; i++)
            {
                var piece = curvePieces[i];
                var pieceLen = Math.Abs(piece.GetDistanceAtParameter(piece.EndParam) - piece.GetDistanceAtParameter(piece.StartParam));
                if (removeFlags[i])
                {
                    removedLength += pieceLen;
                    piece.Dispose();
                }
                else
                {
                    surviving.Add(piece);
                }
            }

            // Clear curvePieces ownership list so finally block only disposes un-handled pieces on error.
            curvePieces.Clear();

            if (surviving.Count == 0)
            {
                return ForgeResult.Failure(command.Id, "trim_failed", "No curve segments remained after trim.");
            }

            // When an open Line or Arc has exactly one surviving piece, update the entity in place to preserve its handle.
            if (!isClosed && surviving.Count == 1 && (targetCurve is Line && surviving[0] is Line || targetCurve is Arc && surviving[0] is Arc))
            {
                if (targetCurve is Line targetLine && surviving[0] is Line survivingLine)
                {
                    targetLine.StartPoint = survivingLine.StartPoint;
                    targetLine.EndPoint = survivingLine.EndPoint;
                }
                else if (targetCurve is Arc targetArc && surviving[0] is Arc survivingArc)
                {
                    targetArc.StartAngle = survivingArc.StartAngle;
                    targetArc.EndAngle = survivingArc.EndAngle;
                }

                surviving[0].Dispose();
                var preservedHandle = HandleString(targetCurve);
                var inPlaceReadBack = new
                {
                    trimmedFrom = args.TargetHandle,
                    handle = preservedHandle,
                    handles = new[] { preservedHandle },
                    handlePreserved = true,
                    removedLength,
                    intersectionCount = cutDistances.Length,
                    entity = EntitySummary(targetCurve)
                };
                tr.Commit();
                return ForgeResult.Success(
                    command.Id,
                    inPlaceReadBack,
                    verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = inPlaceReadBack });
            }

            var ownerId = target.OwnerId;
            var newHandles = new List<string>(surviving.Count);
            var summaries = new List<object>(surviving.Count);
            try
            {
                foreach (var piece in surviving)
                {
                    AppendToOwner(db, tr, ownerId, piece);
                    newHandles.Add(HandleString(piece));
                    summaries.Add(EntitySummary(piece));
                }
            }
            catch
            {
                foreach (var piece in surviving)
                {
                    if (piece.ObjectId.IsNull)
                    {
                        piece.Dispose();
                    }
                }

                throw;
            }

            target.Erase();
            var readBack = new
            {
                trimmedFrom = args.TargetHandle,
                handle = newHandles[0],
                handles = newHandles.ToArray(),
                handlePreserved = false,
                removedLength,
                intersectionCount = cutDistances.Length,
                entities = summaries
            };
            tr.Commit();
            return ForgeResult.Success(
                command.Id,
                readBack,
                verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
        }
        finally
        {
            foreach (var c in curvePieces)
            {
                if (c.ObjectId.IsNull)
                {
                    c.Dispose();
                }
            }
        }
    }

    private static ForgeResult EntityExtend(ForgeCommand command)
    {
        var args = Args<EntityExtendArgs>(command);
        if (string.IsNullOrWhiteSpace(args.TargetHandle))
        {
            return ForgeResult.Failure(command.Id, "missing_handle", "targetHandle is required.");
        }

        if (args.BoundaryHandles is null || args.BoundaryHandles.Length == 0)
        {
            return ForgeResult.Failure(command.Id, "missing_handles", "boundaryHandles[] is required.");
        }

        var endMode = CadEditMath.NormalizeExtendMode(args.EndToExtend ?? args.ExtendEnd);
        if (endMode is null)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", "endToExtend must be one of: 'end', 'start', 'both', 'auto', 'near'.");
        }

        Point3d? nearPt = null;
        if (args.NearPoint is not null)
        {
            try
            {
                nearPt = ToPoint3d(args.NearPoint);
            }
            catch (ArgumentException ex)
            {
                return ForgeResult.Failure(command.Id, "invalid_args", ex.Message);
            }
        }
        else if (endMode == "near")
        {
            return ForgeResult.Failure(command.Id, "invalid_args", "nearPoint [x,y] is required when endToExtend='near'.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { tool = "forge_entity_extend", args.TargetHandle, args.BoundaryHandles, endToExtend = endMode, args.NearPoint });
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var target = TryOpenEntityForWrite(db, tr, args.TargetHandle!, out var targetStatus);
        if (target is null)
        {
            return ForgeResult.Failure(command.Id, OpenStatusCode(targetStatus), $"Cannot open targetHandle {args.TargetHandle} for write ({targetStatus}).");
        }

        if (target is not Curve targetCurve)
        {
            return ForgeResult.Failure(command.Id, "not_a_curve", $"Target {args.TargetHandle} is a {target.GetType().Name}; only curves can be extended.");
        }

        if (IsClosedCurve(targetCurve))
        {
            return ForgeResult.Failure(command.Id, "unsupported_entity", "Closed curves cannot be extended.");
        }

        var boundaries = new List<Entity>(args.BoundaryHandles.Length);
        foreach (var bh in args.BoundaryHandles)
        {
            if (string.IsNullOrWhiteSpace(bh))
            {
                return ForgeResult.Failure(command.Id, "boundary_not_found", "boundaryHandles[] contains an empty handle.");
            }

            if (string.Equals(bh.Trim(), args.TargetHandle.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return ForgeResult.Failure(command.Id, "boundary_is_target", "A curve cannot be extended to itself.");
            }

            var boundary = TryOpenEntity(db, tr, bh, forWrite: false, out _);
            if (boundary is null)
            {
                return ForgeResult.Failure(command.Id, "boundary_not_found", $"No boundary entity found for handle: {bh}");
            }

            boundaries.Add(boundary);
        }

        // Resolve 'near' mode to 'start' or 'end' based on distance from nearPoint to the curve's endpoints.
        var effectiveMode = endMode;
        if (effectiveMode == "near" && nearPt.HasValue)
        {
            var dStart = nearPt.Value.DistanceTo(targetCurve.StartPoint);
            var dEnd = nearPt.Value.DistanceTo(targetCurve.EndPoint);
            effectiveMode = dStart <= dEnd ? "start" : "end";
        }

        var extendedEnds = new List<string>();
        Point3d? newStart = null;
        Point3d? newEnd = null;

        if (targetCurve is Line line)
        {
            var candidates = CollectExtendIntersections(line, boundaries);
            var sArr = new[] { line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z };
            var eArr = new[] { line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z };
            var candArr = candidates.Select(p => new[] { p.X, p.Y, p.Z }).ToArray();

            var startIdx = CadEditMath.SelectLineExtension(sArr, eArr, candArr, atEnd: false, out var startExtLen);
            var endIdx = CadEditMath.SelectLineExtension(sArr, eArr, candArr, atEnd: true, out var endExtLen);

            var doStart = effectiveMode == "start" || effectiveMode == "both" ||
                          (effectiveMode == "auto" && startIdx >= 0 && (endIdx < 0 || startExtLen <= endExtLen));
            var doEnd = effectiveMode == "end" || effectiveMode == "both" ||
                        (effectiveMode == "auto" && endIdx >= 0 && (startIdx < 0 || endExtLen < startExtLen));

            if (doStart && startIdx >= 0)
            {
                line.StartPoint = candidates[startIdx];
                newStart = line.StartPoint;
                extendedEnds.Add("start");
            }

            if (doEnd && endIdx >= 0)
            {
                line.EndPoint = candidates[endIdx];
                newEnd = line.EndPoint;
                extendedEnds.Add("end");
            }
        }
        else if (targetCurve is Arc arc)
        {
            var candidates = CollectExtendIntersections(arc, boundaries);
            var plane = new Plane(arc.Center, arc.Normal);
            var candAngles = candidates.Select(pt =>
            {
                var uv = plane.ParameterOf(pt);
                return CadEditMath.NormalizeRadians(Math.Atan2(uv.Y, uv.X));
            }).ToArray();

            var startIdx = CadEditMath.SelectArcExtension(arc.StartAngle, arc.EndAngle, candAngles, atEnd: false, out var startSweep);
            var endIdx = CadEditMath.SelectArcExtension(arc.StartAngle, arc.EndAngle, candAngles, atEnd: true, out var endSweep);

            var doStart = effectiveMode == "start" || effectiveMode == "both" ||
                          (effectiveMode == "auto" && startIdx >= 0 && (endIdx < 0 || startSweep <= endSweep));
            var doEnd = effectiveMode == "end" || effectiveMode == "both" ||
                        (effectiveMode == "auto" && endIdx >= 0 && (startIdx < 0 || endSweep < startSweep));

            if (doStart && startIdx >= 0)
            {
                arc.StartAngle = candAngles[startIdx];
                newStart = arc.StartPoint;
                extendedEnds.Add("start");
            }

            if (doEnd && endIdx >= 0)
            {
                arc.EndAngle = candAngles[endIdx];
                newEnd = arc.EndPoint;
                extendedEnds.Add("end");
            }
        }
        else if (targetCurve is Polyline pl)
        {
            if (pl.NumberOfVertices < 2)
            {
                return ForgeResult.Failure(command.Id, "unsupported_entity", "Polyline must have at least 2 vertices to extend.");
            }

            var lastSegIdx = pl.NumberOfVertices - 2;
            var startIsArc = Math.Abs(pl.GetBulgeAt(0)) > 1e-9;
            var endIsArc = Math.Abs(pl.GetBulgeAt(lastSegIdx)) > 1e-9;
            if ((effectiveMode == "start" && startIsArc) || (effectiveMode == "end" && endIsArc) || (startIsArc && endIsArc))
            {
                return ForgeResult.Failure(command.Id, "unsupported_segment", "Extending an arc segment of a Polyline is not supported; explode or convert to straight end segment.");
            }

            int startIdx = -1;
            double startExtLen = double.PositiveInfinity;
            List<Point3d>? startCandidates = null;
            if (!startIsArc)
            {
                var p0 = pl.GetPoint3dAt(0);
                var p1 = pl.GetPoint3dAt(1);
                using var segLine = new Line(p0, p1);
                startCandidates = CollectExtendIntersections(segLine, boundaries);
                startIdx = CadEditMath.SelectLineExtension(
                    [p0.X, p0.Y, p0.Z],
                    [p1.X, p1.Y, p1.Z],
                    startCandidates.Select(p => new[] { p.X, p.Y, p.Z }).ToArray(),
                    atEnd: false,
                    out startExtLen);
            }

            int endIdx = -1;
            double endExtLen = double.PositiveInfinity;
            List<Point3d>? endCandidates = null;
            if (!endIsArc)
            {
                var pPrev = pl.GetPoint3dAt(lastSegIdx);
                var pLast = pl.GetPoint3dAt(lastSegIdx + 1);
                using var segLine = new Line(pPrev, pLast);
                endCandidates = CollectExtendIntersections(segLine, boundaries);
                endIdx = CadEditMath.SelectLineExtension(
                    [pPrev.X, pPrev.Y, pPrev.Z],
                    [pLast.X, pLast.Y, pLast.Z],
                    endCandidates.Select(p => new[] { p.X, p.Y, p.Z }).ToArray(),
                    atEnd: true,
                    out endExtLen);
            }

            var doStart = effectiveMode == "start" || effectiveMode == "both" ||
                          (effectiveMode == "auto" && startIdx >= 0 && (endIdx < 0 || startExtLen <= endExtLen));
            var doEnd = effectiveMode == "end" || effectiveMode == "both" ||
                        (effectiveMode == "auto" && endIdx >= 0 && (startIdx < 0 || endExtLen < startExtLen));

            if (doStart && startIdx >= 0 && startCandidates is not null)
            {
                var pt = startCandidates[startIdx];
                pl.SetPointAt(0, new Point2d(pt.X, pt.Y));
                newStart = pl.GetPoint3dAt(0);
                extendedEnds.Add("start");
            }

            if (doEnd && endIdx >= 0 && endCandidates is not null)
            {
                var pt = endCandidates[endIdx];
                pl.SetPointAt(lastSegIdx + 1, new Point2d(pt.X, pt.Y));
                newEnd = pl.GetPoint3dAt(lastSegIdx + 1);
                extendedEnds.Add("end");
            }
        }
        else
        {
            return ForgeResult.Failure(command.Id, "unsupported_entity", $"Extend supports Line, Arc, and open Polyline entities (got {targetCurve.GetType().Name}).");
        }

        if (extendedEnds.Count == 0)
        {
            return ForgeResult.Failure(command.Id, "no_boundary_intersection", "No boundary intersection found strictly beyond the requested curve endpoint(s).");
        }

        var primaryEndpoint = newEnd ?? newStart!.Value;
        var readBack = new
        {
            handle = HandleString(targetCurve),
            extendedEnd = extendedEnds.Count == 2 ? "both" : extendedEnds[0],
            extendedEnds = extendedEnds.ToArray(),
            newEndpoint = new[] { primaryEndpoint.X, primaryEndpoint.Y, primaryEndpoint.Z },
            newStartPoint = newStart is { } ns ? new[] { ns.X, ns.Y, ns.Z } : null,
            newEndPoint = newEnd is { } ne ? new[] { ne.X, ne.Y, ne.Z } : null,
            entity = EntitySummary(targetCurve)
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            readBack,
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static List<Point3d> CollectExtendIntersections(Curve curve, IReadOnlyList<Entity> boundaries)
    {
        var candidates = new List<Point3d>();
        foreach (var boundary in boundaries)
        {
            var pts = new Point3dCollection();
            curve.IntersectWith(boundary, Intersect.ExtendThis, pts, IntPtr.Zero, IntPtr.Zero);
            foreach (Point3d pt in pts)
            {
                candidates.Add(pt);
            }
        }

        return candidates;
    }

    private static bool IsClosedCurve(Curve curve) =>
        curve is Circle ||
        curve.Closed ||
        (curve is Ellipse el && Math.Abs(el.EndAngle - el.StartAngle - 2 * Math.PI) <= 1e-6);

    private static ForgeResult EntityJoin(ForgeCommand command)
    {
        var args = Args<EntityJoinArgs>(command);
        if (args.Handles is null || args.Handles.Length < 2)
        {
            return ForgeResult.Failure(command.Id, "missing_handles", "handles[] requires at least 2 curve handles to join.");
        }

        if (args.Handles.Length > AuthoringMaxEntities)
        {
            return ForgeResult.Failure(command.Id, "too_many_handles", $"Join accepts at most {AuthoringMaxEntities} handles.");
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (!string.IsNullOrWhiteSpace(args.Layer) && !LayerExists(db, tr, args.Layer!))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}", "Create it with forge_layer_create first.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { tool = "forge_entity_join", handles = args.Handles, args.Layer });
        }

        var unknown = new List<string>();
        var locked = new List<string>();
        var opened = OpenHandlesForWrite(db, tr, args.Handles, unknown, locked);
        if (locked.Count > 0)
        {
            return ForgeResult.Failure(command.Id, "layer_locked", $"Cannot join entities on a locked layer: {string.Join(", ", locked)}");
        }

        var curves = opened.OfType<Curve>().ToList();
        if (curves.Count < 2)
        {
            return ForgeResult.Failure(command.Id, "not_enough_curves", "At least 2 valid curves are required to join.");
        }

        // Convert first curve to a Polyline if it is a Line or Arc, preserving its properties and owner block.
        Polyline basePl;
        var createdBase = false;
        if (curves[0] is Polyline existingPl)
        {
            basePl = existingPl;
        }
        else if (curves[0] is Line firstLine)
        {
            if (Math.Abs(firstLine.StartPoint.Z - firstLine.EndPoint.Z) > 1e-6)
            {
                return ForgeResult.Failure(command.Id, "not_planar", "First Line must lie in a horizontal Z plane to join into a 2D Polyline.");
            }

            basePl = new Polyline();
            try
            {
                basePl.SetDatabaseDefaults(db);
                basePl.SetPropertiesFrom(firstLine);
                basePl.Elevation = firstLine.StartPoint.Z;
                basePl.AddVertexAt(0, new Point2d(firstLine.StartPoint.X, firstLine.StartPoint.Y), 0, 0, 0);
                basePl.AddVertexAt(1, new Point2d(firstLine.EndPoint.X, firstLine.EndPoint.Y), 0, 0, 0);
                AppendToOwner(db, tr, firstLine.OwnerId, basePl);
                createdBase = true;
            }
            catch
            {
                if (basePl.ObjectId.IsNull)
                {
                    basePl.Dispose();
                }

                throw;
            }
        }
        else if (curves[0] is Arc firstArc)
        {
            if (Math.Abs(firstArc.Normal.X) > 1e-6 || Math.Abs(firstArc.Normal.Y) > 1e-6 || Math.Abs(Math.Abs(firstArc.Normal.Z) - 1.0) > 1e-6)
            {
                return ForgeResult.Failure(command.Id, "not_planar", "First Arc normal must be parallel to WCS Z to join into a 2D Polyline.");
            }

            basePl = new Polyline();
            try
            {
                basePl.SetDatabaseDefaults(db);
                basePl.SetPropertiesFrom(firstArc);
                basePl.Elevation = firstArc.Center.Z;
                var sweep = firstArc.EndAngle - firstArc.StartAngle;
                if (sweep < 0)
                {
                    sweep += 2 * Math.PI;
                }

                var bulge = Math.Tan(sweep / 4.0) * (firstArc.Normal.Z < 0 ? -1.0 : 1.0);
                basePl.AddVertexAt(0, new Point2d(firstArc.StartPoint.X, firstArc.StartPoint.Y), bulge, 0, 0);
                basePl.AddVertexAt(1, new Point2d(firstArc.EndPoint.X, firstArc.EndPoint.Y), 0, 0, 0);
                AppendToOwner(db, tr, firstArc.OwnerId, basePl);
                createdBase = true;
            }
            catch
            {
                if (basePl.ObjectId.IsNull)
                {
                    basePl.Dispose();
                }

                throw;
            }
        }
        else
        {
            return ForgeResult.Failure(command.Id, "unsupported_curve", $"First entity must be a Polyline, Line, or Arc (got {curves[0].GetType().Name}).");
        }

        if (!string.IsNullOrWhiteSpace(args.Layer))
        {
            basePl.Layer = args.Layer!;
        }

        var others = curves.Skip(1).Select(c => (Entity)c).ToArray();
        var joinedOtherIndices = new HashSet<int>();
        try
        {
            var joinedIndices = basePl.JoinEntities(others);
            foreach (var idx in joinedIndices)
            {
                if (idx >= 0 && idx < others.Length)
                {
                    joinedOtherIndices.Add(idx);
                }
            }
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(command.Id, "join_failed", $"JoinEntities failed: {ex.Message}");
        }

        if (joinedOtherIndices.Count == 0)
        {
            return ForgeResult.Failure(
                command.Id,
                "nothing_joined",
                "None of the secondary curves were contiguous and coplanar with the first curve.",
                "Ensure endpoints touch within AutoCAD's fuzz distance and lie in the same plane.");
        }

        if (createdBase)
        {
            curves[0].Erase();
        }

        var unjoinedHandles = new List<string>();
        for (var i = 0; i < others.Length; i++)
        {
            if (joinedOtherIndices.Contains(i))
            {
                others[i].Erase();
            }
            else
            {
                unjoinedHandles.Add(HandleString(others[i]));
            }
        }

        if (basePl.NumberOfVertices >= 3 && basePl.GetPoint2dAt(0).GetDistanceTo(basePl.GetPoint2dAt(basePl.NumberOfVertices - 1)) < 1e-4)
        {
            basePl.RemoveVertexAt(basePl.NumberOfVertices - 1);
            basePl.Closed = true;
        }

        var joinedCount = joinedOtherIndices.Count + 1;
        var readBack = new
        {
            handle = HandleString(basePl),
            joinedCount,
            vertexCount = basePl.NumberOfVertices,
            closed = basePl.Closed,
            length = basePl.Length,
            unjoinedHandles,
            unknownHandles = unknown
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            readBack,
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult EntityExplode(ForgeCommand command)
    {
        var args = Args<EntityExplodeArgs>(command);
        if (CheckHandles(command, args.Handles, "too_many_handles") is { } denied)
        {
            return denied;
        }

        if (command.DryRun)
        {
            return DryRun(command, new { tool = "forge_entity_explode", handles = args.Handles, args.EraseSource });
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var createdHandles = new List<string>();
        var unknown = new List<string>();
        var failed = new List<object>();
        var explodedCount = 0;

        foreach (var rawHandle in args.Handles!)
        {
            var entity = TryOpenEntityForWrite(db, tr, rawHandle, out var openStatus);
            if (entity is null)
            {
                if (openStatus is EntityOpenStatus.NotFound or EntityOpenStatus.Erased)
                {
                    unknown.Add(rawHandle);
                }

                failed.Add(new { handle = rawHandle, reason = OpenStatusCode(openStatus) });
                continue;
            }

            var fragments = new DBObjectCollection();
            try
            {
                entity.Explode(fragments);
            }
            catch (System.Exception ex)
            {
                fragments.Dispose();
                failed.Add(new { handle = rawHandle, reason = ex.Message });
                continue;
            }

            var ownerId = entity.OwnerId;

            // BURST-style attribute preservation for BlockReference: convert visible non-constant AttributeReferences
            // into standalone DBText/MText entities in the same owner space before skipping AttributeDefinitions.
            if (entity is BlockReference br)
            {
                foreach (ObjectId attId in br.AttributeCollection)
                {
                    if (attId.IsNull || attId.IsErased)
                    {
                        continue;
                    }

                    if (tr.GetObject(attId, OpenMode.ForRead) is not AttributeReference attRef || attRef.Invisible || attRef.IsConstant)
                    {
                        continue;
                    }

                    Entity? burstText = null;
                    try
                    {
                        if (attRef.IsMTextAttribute && attRef.MTextAttribute is { } mtClone)
                        {
                            burstText = mtClone;
                            burstText.SetPropertiesFrom(attRef);
                        }
                        else
                        {
                            var dbText = new DBText();
                            dbText.SetDatabaseDefaults(db);
                            dbText.SetPropertiesFrom(attRef);
                            dbText.TextStyleId = attRef.TextStyleId;
                            dbText.Height = attRef.Height;
                            dbText.Rotation = attRef.Rotation;
                            dbText.WidthFactor = attRef.WidthFactor;
                            dbText.Oblique = attRef.Oblique;
                            dbText.HorizontalMode = attRef.HorizontalMode;
                            dbText.VerticalMode = attRef.VerticalMode;
                            dbText.Justify = attRef.Justify;
                            dbText.Position = attRef.Position;
                            if (attRef.Justify != AttachmentPoint.BaseLeft)
                            {
                                dbText.AlignmentPoint = attRef.AlignmentPoint;
                            }

                            dbText.TextString = attRef.TextString;
                            dbText.AdjustAlignment(db);
                            burstText = dbText;
                        }

                        AppendToOwner(db, tr, ownerId, burstText);
                        createdHandles.Add(HandleString(burstText));
                    }
                    catch
                    {
                        if (burstText is not null && burstText.ObjectId.IsNull)
                        {
                            burstText.Dispose();
                        }
                    }
                }
            }

            try
            {
                foreach (DBObject obj in fragments)
                {
                    if (obj is Entity fragEntity && obj is not AttributeDefinition)
                    {
                        AppendToOwner(db, tr, ownerId, fragEntity);
                        createdHandles.Add(HandleString(fragEntity));
                    }
                    else
                    {
                        obj.Dispose();
                    }
                }
            }
            catch
            {
                foreach (DBObject obj in fragments)
                {
                    if (obj.ObjectId.IsNull)
                    {
                        obj.Dispose();
                    }
                }

                throw;
            }

            if (args.EraseSource)
            {
                entity.Erase();
            }

            explodedCount++;
        }

        if (explodedCount == 0)
        {
            return ForgeResult.Failure(
                command.Id,
                "explode_failed",
                "No entities could be exploded.",
                data: new { exploded = 0, failed, unknownHandles = unknown });
        }

        var readBack = new
        {
            exploded = explodedCount,
            createdCount = createdHandles.Count,
            newHandles = createdHandles,
            failed,
            unknownHandles = unknown
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            readBack,
            verification: new ForgeVerification { Attempted = true, Passed = failed.Count == 0 && unknown.Count == 0 && explodedCount > 0, ReadBack = readBack });
    }

    private static ForgeResult EntityIntersect(ForgeCommand command)
    {
        var args = Args<EntityIntersectArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Handle1) || string.IsNullOrWhiteSpace(args.Handle2))
        {
            return ForgeResult.Failure(command.Id, "missing_handle", "handle1 and handle2 are required.");
        }

        var rawMode = string.IsNullOrWhiteSpace(args.ExtendMode) ? "none" : args.ExtendMode.Trim();
        Intersect intersectType;
        string canonicalMode;
        switch (rawMode.ToLowerInvariant())
        {
            case "none":
                intersectType = Intersect.OnBothOperands;
                canonicalMode = "none";
                break;
            case "first":
            case "extend_first":
            case "extendfirst":
                intersectType = Intersect.ExtendThis;
                canonicalMode = "extendFirst";
                break;
            case "second":
            case "extend_second":
            case "extendsecond":
                intersectType = Intersect.ExtendArgument;
                canonicalMode = "extendSecond";
                break;
            case "both":
            case "extend_both":
            case "extendboth":
                intersectType = Intersect.ExtendBoth;
                canonicalMode = "extendBoth";
                break;
            default:
                return ForgeResult.Failure(
                    command.Id,
                    "invalid_extend_mode",
                    $"Unknown extendMode '{args.ExtendMode}'. Expected one of: none, extendFirst, extendSecond, extendBoth.");
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var e1 = TryOpenEntity(db, tr, args.Handle1!, forWrite: false, out _);
        var e2 = TryOpenEntity(db, tr, args.Handle2!, forWrite: false, out _);
        if (e1 is null || e2 is null)
        {
            return ForgeResult.Failure(command.Id, "handle_not_found", $"No entity for handle: {(e1 is null ? args.Handle1 : args.Handle2)}");
        }

        var pts = new Point3dCollection();
        e1.IntersectWith(e2, intersectType, pts, IntPtr.Zero, IntPtr.Zero);
        var points = new List<double[]>(pts.Count);
        foreach (Point3d pt in pts)
        {
            points.Add([pt.X, pt.Y, pt.Z]);
        }

        return ForgeResult.Success(command.Id, new
        {
            handle1 = args.Handle1,
            handle2 = args.Handle2,
            extendMode = canonicalMode,
            count = points.Count,
            points
        });
    }

    private static bool TryValidateOptionalPoint2D(double[]? pt, string name, out double[]? normalized, out string? error)
    {
        if (pt is null)
        {
            normalized = null;
            error = null;
            return true;
        }

        if (pt.Length < 2 || double.IsNaN(pt[0]) || double.IsNaN(pt[1]) || double.IsInfinity(pt[0]) || double.IsInfinity(pt[1]))
        {
            normalized = null;
            error = $"{name} requires finite [x, y] coordinates.";
            return false;
        }

        normalized = pt;
        error = null;
        return true;
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
    private sealed record EntityListArgs { public double[][]? Window { get; init; } public string? Layer { get; init; } public string? Type { get; init; } public int? MaxEntities { get; init; } public bool IncludeBlocks { get; init; } public int? BlockDepth { get; init; } }
    private sealed record EntityMoveArgs { public string[]? Handles { get; init; } public double[]? Delta { get; init; } }
    private sealed record EntityCopyArgs { public string[]? Handles { get; init; } public double[]? Delta { get; init; } public int Copies { get; init; } = 1; }
    private sealed record EntityRotateArgs { public string[]? Handles { get; init; } public double[]? BasePoint { get; init; } public double AngleDeg { get; init; } }
    private sealed record EntityScaleArgs { public string[]? Handles { get; init; } public double[]? BasePoint { get; init; } public double Factor { get; init; } }
    private sealed record EntityMirrorArgs { public string[]? Handles { get; init; } public double[]? P1 { get; init; } public double[]? P2 { get; init; } public bool EraseSource { get; init; } }
    private sealed record EntityOffsetArgs { public string? Handle { get; init; } public double Distance { get; init; } public string? Layer { get; init; } }
    private sealed record EntityEraseArgs { public string[]? Handles { get; init; } public bool AcknowledgeBulk { get; init; } }
    private sealed record EntitySetPropsArgs { public string[]? Handles { get; init; } public string? Layer { get; init; } public int? ColorIndex { get; init; } public string? Linetype { get; init; } public int? Lineweight { get; init; } }
    private sealed record EntityFilletArgs { public string? Handle { get; init; } public string? Handle1 { get; init; } public string? Handle2 { get; init; } public double Radius { get; init; } public double[]? PickPoint1 { get; init; } public double[]? PickPoint2 { get; init; } public string? Layer { get; init; } }
    private sealed record EntityChamferArgs { public string? Handle1 { get; init; } public string? Handle2 { get; init; } public double Distance1 { get; init; } public double? Distance2 { get; init; } public double[]? PickPoint1 { get; init; } public double[]? PickPoint2 { get; init; } public string? Layer { get; init; } }
    private sealed record EntityTrimArgs { public string? TargetHandle { get; init; } public string[]? CuttingHandles { get; init; } public double[]? PickPoint { get; init; } public double[]? KeepPoint { get; init; } }
    private sealed record EntityExtendArgs { public string? TargetHandle { get; init; } public string[]? BoundaryHandles { get; init; } public string? EndToExtend { get; init; } public string? ExtendEnd { get; init; } public double[]? NearPoint { get; init; } }
    private sealed record EntityJoinArgs { public string[]? Handles { get; init; } public string? Layer { get; init; } }
    private sealed record EntityExplodeArgs { public string[]? Handles { get; init; } public bool EraseSource { get; init; } = true; }
    private sealed record EntityIntersectArgs { public string? Handle1 { get; init; } public string? Handle2 { get; init; } public string? ExtendMode { get; init; } }
    private sealed record UndoArgs { }
}
