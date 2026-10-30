using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Forge.Shared;

namespace Forge.Plugin;

// Waves C29–C30 — AI spatial perception (drawing summary, text search, OSNAP intelligence)
// and high-level AEC generators (structural axis grid with bubbles/dims, mitered double-line
// walls with automatic door/window openings).
public sealed partial class PluginCommandProcessor
{
    private static ForgeResult DrawingSummary(ForgeCommand command)
    {
        var args = Args<DrawingSummaryArgs>(command);
        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();

        var targetLayoutName = string.IsNullOrWhiteSpace(args.Layout) ? "Model" : args.Layout.Trim();
        BlockTableRecord? targetSpace = null;
        if (string.Equals(targetLayoutName, "Model", StringComparison.OrdinalIgnoreCase))
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            targetSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            targetLayoutName = "Model";
        }
        else
        {
            var layoutDict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in layoutDict)
            {
                if (string.Equals(entry.Key, targetLayoutName, StringComparison.OrdinalIgnoreCase) &&
                    tr.GetObject(entry.Value, OpenMode.ForRead) is Layout layout)
                {
                    targetSpace = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
                    targetLayoutName = layout.LayoutName;
                    break;
                }
            }
        }

        if (targetSpace is null)
        {
            return ForgeResult.Failure(command.Id, "layout_not_found", $"Layout not found: {args.Layout}");
        }

        var byType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byLayer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        var hasExtents = false;
        var totalEntities = 0;

        foreach (ObjectId id in targetSpace)
        {
            if (id.IsNull || id.IsErased)
            {
                continue;
            }

            if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent)
            {
                continue;
            }

            totalEntities++;
            var typeName = ent.GetType().Name;
            byType[typeName] = byType.TryGetValue(typeName, out var tc) ? tc + 1 : 1;
            var layerName = ent.Layer ?? "0";
            byLayer[layerName] = byLayer.TryGetValue(layerName, out var lc) ? lc + 1 : 1;

            try
            {
                var ext = ent.GeometricExtents;
                if (!double.IsNaN(ext.MinPoint.X) && !double.IsInfinity(ext.MinPoint.X) &&
                    !double.IsNaN(ext.MaxPoint.X) && !double.IsInfinity(ext.MaxPoint.X))
                {
                    minX = Math.Min(minX, ext.MinPoint.X);
                    minY = Math.Min(minY, ext.MinPoint.Y);
                    minZ = Math.Min(minZ, ext.MinPoint.Z);
                    maxX = Math.Max(maxX, ext.MaxPoint.X);
                    maxY = Math.Max(maxY, ext.MaxPoint.Y);
                    maxZ = Math.Max(maxZ, ext.MaxPoint.Z);
                    hasExtents = true;
                }
            }
            catch
            {
                // Some empty/uninitialized entities have no geometric extents.
            }
        }

        var layerSummaries = new List<object>();
        var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (ObjectId lid in layerTable)
        {
            if (lid.IsErased || tr.GetObject(lid, OpenMode.ForRead) is not LayerTableRecord ltr)
            {
                continue;
            }

            var ltName = "Continuous";
            if (!ltr.LinetypeObjectId.IsNull && tr.GetObject(ltr.LinetypeObjectId, OpenMode.ForRead) is LinetypeTableRecord lrec)
            {
                ltName = lrec.Name;
            }

            byLayer.TryGetValue(ltr.Name, out var entCount);
            layerSummaries.Add(new
            {
                name = ltr.Name,
                entityCount = entCount,
                colorIndex = (int)ltr.Color.ColorIndex,
                linetype = ltName,
                isOn = !ltr.IsOff,
                isFrozen = ltr.IsFrozen,
                isLocked = ltr.IsLocked,
                isPlottable = ltr.IsPlottable
            });
        }

        var linetypes = new List<string>();
        var ltTable = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        foreach (ObjectId id in ltTable)
        {
            if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is LinetypeTableRecord rec)
            {
                linetypes.Add(rec.Name);
            }
        }

        var textStyles = new List<string>();
        var tsTable = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        foreach (ObjectId id in tsTable)
        {
            if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is TextStyleTableRecord rec)
            {
                textStyles.Add(rec.Name);
            }
        }

        var dimStyles = new List<string>();
        var dsTable = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
        foreach (ObjectId id in dsTable)
        {
            if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is DimStyleTableRecord rec)
            {
                dimStyles.Add(rec.Name);
            }
        }

        var blocks = new List<string>();
        var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId id in blockTable)
        {
            if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is BlockTableRecord btr &&
                !btr.IsLayout && !btr.IsAnonymous && !btr.IsFromExternalReference)
            {
                blocks.Add(btr.Name);
            }
        }

        var layouts = new List<string>();
        var layoutDictionary = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry entry in layoutDictionary)
        {
            layouts.Add(entry.Key);
        }

        var currentLayer = !db.Clayer.IsNull && tr.GetObject(db.Clayer, OpenMode.ForRead) is LayerTableRecord curLayerRec
            ? curLayerRec.Name
            : "0";
        var currentTextStyle = !db.Textstyle.IsNull && tr.GetObject(db.Textstyle, OpenMode.ForRead) is TextStyleTableRecord curTsRec
            ? curTsRec.Name
            : "Standard";
        var currentDimStyle = !db.Dimstyle.IsNull && tr.GetObject(db.Dimstyle, OpenMode.ForRead) is DimStyleTableRecord curDsRec
            ? curDsRec.Name
            : "Standard";

        object? extentsObj = hasExtents
            ? new
            {
                min = new[] { minX, minY, minZ },
                max = new[] { maxX, maxY, maxZ },
                width = maxX - minX,
                height = maxY - minY,
                center = new[] { (minX + maxX) * 0.5, (minY + maxY) * 0.5 }
            }
            : null;

        return ForgeResult.Success(command.Id, new
        {
            documentName = ActiveDoc.Name,
            layout = targetLayoutName,
            totalEntities,
            extents = extentsObj,
            units = db.Insunits.ToString(),
            currentLayer,
            currentTextStyle,
            currentDimStyle,
            ltscale = db.Ltscale,
            dimscale = db.Dimscale,
            entityCountsByType = byType.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
            entityCountsByLayer = byLayer.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
            layers = layerSummaries,
            linetypes = linetypes.OrderBy(x => x).ToArray(),
            textStyles = textStyles.OrderBy(x => x).ToArray(),
            dimStyles = dimStyles.OrderBy(x => x).ToArray(),
            blocks = blocks.OrderBy(x => x).ToArray(),
            layouts = layouts.OrderBy(x => x).ToArray()
        });
    }

    private static ForgeResult EntitySearchText(ForgeCommand command)
    {
        var args = Args<EntitySearchTextArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Query))
        {
            return ForgeResult.Failure(command.Id, "missing_query", "query string is required.");
        }

        if (args.UseRegex)
        {
            try
            {
                _ = new System.Text.RegularExpressions.Regex(args.Query);
            }
            catch (ArgumentException ex)
            {
                return ForgeResult.Failure(command.Id, "invalid_regex", $"Invalid regular expression: {ex.Message}");
            }
        }

        double minWx = double.NegativeInfinity, minWy = double.NegativeInfinity;
        double maxWx = double.PositiveInfinity, maxWy = double.PositiveInfinity;
        var hasWindow = false;
        if (args.Window is not null)
        {
            if (args.Window.Length != 2 || args.Window[0] is not { Length: >= 2 } w0 || args.Window[1] is not { Length: >= 2 } w1)
            {
                return ForgeResult.Failure(command.Id, "invalid_window", "window requires [[x1,y1],[x2,y2]].");
            }

            minWx = Math.Min(w0[0], w1[0]);
            minWy = Math.Min(w0[1], w1[1]);
            maxWx = Math.Max(w0[0], w1[0]);
            maxWy = Math.Max(w0[1], w1[1]);
            hasWindow = true;
        }

        var maxResults = Math.Clamp(args.MaxResults ?? 100, 1, 1000);
        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();

        var targetLayoutName = string.IsNullOrWhiteSpace(args.Layout) ? "Model" : args.Layout.Trim();
        BlockTableRecord? targetSpace = null;
        if (string.Equals(targetLayoutName, "Model", StringComparison.OrdinalIgnoreCase))
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            targetSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        }
        else
        {
            var layoutDict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in layoutDict)
            {
                if (string.Equals(entry.Key, targetLayoutName, StringComparison.OrdinalIgnoreCase) &&
                    tr.GetObject(entry.Value, OpenMode.ForRead) is Layout layout)
                {
                    targetSpace = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
                    break;
                }
            }
        }

        if (targetSpace is null)
        {
            return ForgeResult.Failure(command.Id, "layout_not_found", $"Layout not found: {args.Layout}");
        }

        var matches = new List<object>();
        var truncated = false;

        bool InWindow(Point3d pt) =>
            !hasWindow || (pt.X >= minWx && pt.X <= maxWx && pt.Y >= minWy && pt.Y <= maxWy);

        foreach (ObjectId id in targetSpace)
        {
            if (id.IsNull || id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not Entity ent)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(args.Layer) &&
                !string.Equals(ent.Layer, args.Layer, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            switch (ent)
            {
                case DBText dt:
                {
                    var raw = dt.TextString ?? string.Empty;
                    var plain = CadSpatialMath.StripMTextFormatting(raw);
                    if (InWindow(dt.Position) &&
                        CadSpatialMath.MatchesTextQuery(plain, raw, args.Query, args.CaseSensitive, args.ExactMatch, args.UseRegex))
                    {
                        matches.Add(new
                        {
                            handle = HandleString(dt),
                            type = "DBText",
                            layer = dt.Layer,
                            text = plain,
                            rawText = raw,
                            position = new[] { dt.Position.X, dt.Position.Y, dt.Position.Z },
                            height = dt.Height,
                            rotationDeg = RadiansToDegrees(dt.Rotation)
                        });
                    }
                    break;
                }

                case MText mt:
                {
                    var raw = mt.Contents ?? string.Empty;
                    var plain = !string.IsNullOrEmpty(mt.Text) ? mt.Text : CadSpatialMath.StripMTextFormatting(raw);
                    if (InWindow(mt.Location) &&
                        CadSpatialMath.MatchesTextQuery(plain, raw, args.Query, args.CaseSensitive, args.ExactMatch, args.UseRegex))
                    {
                        matches.Add(new
                        {
                            handle = HandleString(mt),
                            type = "MText",
                            layer = mt.Layer,
                            text = plain,
                            rawText = raw,
                            position = new[] { mt.Location.X, mt.Location.Y, mt.Location.Z },
                            height = mt.TextHeight,
                            rotationDeg = RadiansToDegrees(mt.Rotation)
                        });
                    }
                    break;
                }

                case MLeader ml:
                {
                    var raw = ml.MText?.Contents ?? string.Empty;
                    var plain = CadSpatialMath.StripMTextFormatting(raw);
                    var pos = ml.MText?.Location ?? Point3d.Origin;
                    if (!string.IsNullOrEmpty(raw) && InWindow(pos) &&
                        CadSpatialMath.MatchesTextQuery(plain, raw, args.Query, args.CaseSensitive, args.ExactMatch, args.UseRegex))
                    {
                        matches.Add(new
                        {
                            handle = HandleString(ml),
                            type = "MLeader",
                            layer = ml.Layer,
                            text = plain,
                            rawText = raw,
                            position = new[] { pos.X, pos.Y, pos.Z }
                        });
                    }
                    break;
                }

                case Table tbl:
                {
                    if (!InWindow(tbl.Position))
                    {
                        break;
                    }

                    var rows = Math.Min(tbl.Rows.Count, 200);
                    var cols = Math.Min(tbl.Columns.Count, 50);
                    for (var r = 0; r < rows && matches.Count < maxResults; r++)
                    {
                        for (var c = 0; c < cols && matches.Count < maxResults; c++)
                        {
                            var raw = tbl.Cells[r, c].TextString ?? string.Empty;
                            if (string.IsNullOrEmpty(raw))
                            {
                                continue;
                            }

                            var plain = CadSpatialMath.StripMTextFormatting(raw);
                            if (CadSpatialMath.MatchesTextQuery(plain, raw, args.Query, args.CaseSensitive, args.ExactMatch, args.UseRegex))
                            {
                                matches.Add(new
                                {
                                    handle = HandleString(tbl),
                                    type = "Table",
                                    layer = tbl.Layer,
                                    text = plain,
                                    rawText = raw,
                                    cell = new[] { r, c },
                                    position = new[] { tbl.Position.X, tbl.Position.Y, tbl.Position.Z }
                                });
                            }
                        }
                    }
                    break;
                }

                case BlockReference br:
                {
                    foreach (ObjectId attId in br.AttributeCollection)
                    {
                        if (attId.IsNull || attId.IsErased || tr.GetObject(attId, OpenMode.ForRead) is not AttributeReference attRef)
                        {
                            continue;
                        }

                        var raw = attRef.TextString ?? string.Empty;
                        var plain = CadSpatialMath.StripMTextFormatting(raw);
                        var tagMatch = CadSpatialMath.MatchesTextQuery(attRef.Tag ?? string.Empty, null, args.Query, args.CaseSensitive, args.ExactMatch, args.UseRegex);
                        var valMatch = CadSpatialMath.MatchesTextQuery(plain, raw, args.Query, args.CaseSensitive, args.ExactMatch, args.UseRegex);
                        if (InWindow(attRef.Position) && (tagMatch || valMatch))
                        {
                            matches.Add(new
                            {
                                handle = HandleString(br),
                                attributeHandle = HandleString(attRef),
                                type = "BlockAttribute",
                                blockName = br.Name,
                                attributeTag = attRef.Tag,
                                layer = br.Layer,
                                text = plain,
                                rawText = raw,
                                position = new[] { attRef.Position.X, attRef.Position.Y, attRef.Position.Z }
                            });
                            if (matches.Count >= maxResults)
                            {
                                break;
                            }
                        }
                    }
                    break;
                }
            }

            if (matches.Count >= maxResults)
            {
                truncated = true;
                break;
            }
        }

        return ForgeResult.Success(command.Id, new
        {
            query = args.Query,
            count = matches.Count,
            truncated,
            matches
        });
    }

    private static ForgeResult EntitySnap(ForgeCommand command)
    {
        var args = Args<EntitySnapArgs>(command);
        if (!TryValidateOptionalPoint2D(args.Point, "point", out var queryPt, out var ptErr) || queryPt is null)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", ptErr ?? "point [x, y] is required.");
        }

        if (!TryValidateOptionalPoint2D(args.FromPoint, "fromPoint", out var fromPt, out var fromErr))
        {
            return ForgeResult.Failure(command.Id, "invalid_args", fromErr!);
        }

        var radius = args.Radius ?? 50.0;
        if (radius <= 0 || double.IsNaN(radius) || double.IsInfinity(radius))
        {
            return ForgeResult.Failure(command.Id, "invalid_radius", "radius must be finite and > 0.");
        }

        var enabledKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (args.SnapTypes is { Length: > 0 })
        {
            foreach (var st in args.SnapTypes)
            {
                if (string.IsNullOrWhiteSpace(st) || !CadSpatialMath.SupportedSnapKinds.Contains(st.Trim()))
                {
                    return ForgeResult.Failure(
                        command.Id,
                        "invalid_snap_type",
                        $"Unsupported snapType '{st}'. Supported: {string.Join(", ", CadSpatialMath.SupportedSnapKinds)}.");
                }

                enabledKinds.Add(st.Trim().ToLowerInvariant());
            }
        }
        else
        {
            enabledKinds.UnionWith(CadSpatialMath.SupportedSnapKinds);
        }

        var maxResults = Math.Clamp(args.MaxResults ?? 20, 1, 200);
        var qx = queryPt[0];
        var qy = queryPt[1];
        var minAx = qx - radius;
        var maxAx = qx + radius;
        var minAy = qy - radius;
        var maxAy = qy + radius;

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

        var rawCandidates = new List<CadSpatialMath.SnapCandidate>();
        var nearbyCurves = new List<(Curve Curve, string Handle, string Layer)>();

        foreach (ObjectId id in modelSpace)
        {
            if (id.IsNull || id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not Entity ent)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(args.Layer) &&
                !string.Equals(ent.Layer, args.Layer, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var ext = ent.GeometricExtents;
                if (ext.MaxPoint.X < minAx || ext.MinPoint.X > maxAx ||
                    ext.MaxPoint.Y < minAy || ext.MinPoint.Y > maxAy)
                {
                    continue;
                }
            }
            catch
            {
                // Keep evaluating if GeometricExtents is unavailable
            }

            var handle = HandleString(ent);
            var layer = ent.Layer ?? "0";
            var typeName = ent.GetType().Name;

            if (ent is Curve curve && nearbyCurves.Count < 40)
            {
                nearbyCurves.Add((curve, handle, layer));
            }

            switch (ent)
            {
                case Line line:
                    rawCandidates.AddRange(CadSpatialMath.ComputeLineSegmentSnaps(
                        [line.StartPoint.X, line.StartPoint.Y, line.StartPoint.Z],
                        [line.EndPoint.X, line.EndPoint.Y, line.EndPoint.Z],
                        queryPt,
                        fromPt,
                        handle,
                        typeName,
                        layer,
                        enabledKinds));
                    break;

                case Polyline pl:
                {
                    var vCount = pl.NumberOfVertices;
                    var segCount = pl.Closed ? vCount : vCount - 1;
                    for (var i = 0; i < segCount; i++)
                    {
                        var next = (i + 1) % vCount;
                        var bulge = pl.GetBulgeAt(i);
                        if (Math.Abs(bulge) <= 1e-9)
                        {
                            var p0 = pl.GetPoint3dAt(i);
                            var p1 = pl.GetPoint3dAt(next);
                            rawCandidates.AddRange(CadSpatialMath.ComputeLineSegmentSnaps(
                                [p0.X, p0.Y, p0.Z],
                                [p1.X, p1.Y, p1.Z],
                                queryPt,
                                fromPt,
                                handle,
                                typeName,
                                layer,
                                enabledKinds,
                                includeStartEndpoint: true,
                                includeEndEndpoint: next == vCount - 1 && !pl.Closed));
                        }
                        else
                        {
                            var arc2d = pl.GetArcSegment2dAt(i);
                            var startAng = arc2d.IsClockWise ? -arc2d.EndAngle : arc2d.StartAngle;
                            var endAng = arc2d.IsClockWise ? -arc2d.StartAngle : arc2d.EndAngle;
                            rawCandidates.AddRange(CadSpatialMath.ComputeCircleOrArcSnaps(
                                [arc2d.Center.X, arc2d.Center.Y, pl.Elevation],
                                arc2d.Radius,
                                startAng,
                                endAng,
                                queryPt,
                                handle,
                                typeName,
                                layer,
                                enabledKinds));
                        }
                    }
                    break;
                }

                case Circle circle:
                    rawCandidates.AddRange(CadSpatialMath.ComputeCircleOrArcSnaps(
                        [circle.Center.X, circle.Center.Y, circle.Center.Z],
                        circle.Radius,
                        null,
                        null,
                        queryPt,
                        handle,
                        typeName,
                        layer,
                        enabledKinds));
                    break;

                case Arc arc:
                    rawCandidates.AddRange(CadSpatialMath.ComputeCircleOrArcSnaps(
                        [arc.Center.X, arc.Center.Y, arc.Center.Z],
                        arc.Radius,
                        arc.StartAngle,
                        arc.EndAngle,
                        queryPt,
                        handle,
                        typeName,
                        layer,
                        enabledKinds));
                    break;

                case BlockReference br when enabledKinds.Contains("insertion"):
                {
                    double[] ins = [br.Position.X, br.Position.Y, br.Position.Z];
                    var d = Math.Sqrt(Math.Pow(ins[0] - qx, 2) + Math.Pow(ins[1] - qy, 2));
                    rawCandidates.Add(new CadSpatialMath.SnapCandidate("insertion", ins, d, handle, typeName, layer));
                    break;
                }

                case DBText dt when enabledKinds.Contains("insertion"):
                {
                    double[] ins = [dt.Position.X, dt.Position.Y, dt.Position.Z];
                    var d = Math.Sqrt(Math.Pow(ins[0] - qx, 2) + Math.Pow(ins[1] - qy, 2));
                    rawCandidates.Add(new CadSpatialMath.SnapCandidate("insertion", ins, d, handle, typeName, layer));
                    break;
                }

                case MText mt when enabledKinds.Contains("insertion"):
                {
                    double[] ins = [mt.Location.X, mt.Location.Y, mt.Location.Z];
                    var d = Math.Sqrt(Math.Pow(ins[0] - qx, 2) + Math.Pow(ins[1] - qy, 2));
                    rawCandidates.Add(new CadSpatialMath.SnapCandidate("insertion", ins, d, handle, typeName, layer));
                    break;
                }
            }
        }

        if (enabledKinds.Contains("intersection") && nearbyCurves.Count >= 2)
        {
            for (var i = 0; i < nearbyCurves.Count; i++)
            {
                for (var j = i + 1; j < nearbyCurves.Count; j++)
                {
                    var pts = new Point3dCollection();
                    try
                    {
                        nearbyCurves[i].Curve.IntersectWith(nearbyCurves[j].Curve, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
                        foreach (Point3d pt in pts)
                        {
                            var d = Math.Sqrt(Math.Pow(pt.X - qx, 2) + Math.Pow(pt.Y - qy, 2));
                            if (d <= radius + 1e-9)
                            {
                                rawCandidates.Add(new CadSpatialMath.SnapCandidate(
                                    "intersection",
                                    [pt.X, pt.Y, pt.Z],
                                    d,
                                    nearbyCurves[i].Handle,
                                    nearbyCurves[i].Curve.GetType().Name,
                                    nearbyCurves[i].Layer,
                                    nearbyCurves[j].Handle));
                            }
                        }
                    }
                    catch
                    {
                        // Ignore non-intersectable curve pairs
                    }
                }
            }
        }

        var ranked = CadSpatialMath.RankSnapCandidates(rawCandidates, radius, maxResults);
        var snapsDto = ranked.Select(c => new
        {
            kind = c.Kind,
            point = c.Point,
            distance = c.Distance,
            handle = c.Handle,
            secondaryHandle = c.SecondaryHandle,
            entityType = c.EntityType,
            layer = c.Layer
        }).ToArray();

        return ForgeResult.Success(command.Id, new
        {
            queryPoint = queryPt,
            radius,
            count = snapsDto.Length,
            bestSnap = snapsDto.Length > 0 ? snapsDto[0] : null,
            snaps = snapsDto
        });
    }

    private static ForgeResult DrawGrid(ForgeCommand command)
    {
        var args = Args<DrawGridArgs>(command);
        if (ValidateColorIndex(command, args.ColorIndex) is { } colorErr)
        {
            return colorErr;
        }

        CadSpatialMath.StructuralGridPlan plan;
        try
        {
            plan = CadSpatialMath.BuildStructuralGrid(
                args.Origin,
                args.XPositions,
                args.XSpacings,
                args.XLabels,
                args.YPositions,
                args.YSpacings,
                args.YLabels,
                args.ExtensionLength,
                args.BubbleRadius,
                args.BubbleSides ?? "start",
                args.IncludeDimensions);
        }
        catch (ArgumentException ex)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", ex.Message);
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();

        var layersToCheck = new[] { args.Layer, args.GridLayer, args.BubbleLayer, args.DimLayer };
        foreach (var lyr in layersToCheck)
        {
            if (!string.IsNullOrWhiteSpace(lyr) && !LayerExists(db, tr, lyr!))
            {
                return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {lyr}", "Create it with forge_layer_create first.");
            }
        }

        if (!string.IsNullOrWhiteSpace(args.GridLinetype))
        {
            var ltTable = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (!ltTable.Has(args.GridLinetype!))
            {
                return ForgeResult.Failure(command.Id, "linetype_not_found", $"Linetype not loaded: {args.GridLinetype}", "Load it with forge_linetype_load first.");
            }
        }

        ObjectId dimStyleId = db.Dimstyle;
        if (!string.IsNullOrWhiteSpace(args.DimStyle))
        {
            var dsTable = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            if (!dsTable.Has(args.DimStyle!))
            {
                return ForgeResult.Failure(command.Id, "dimstyle_not_found", $"Dimension style not found: {args.DimStyle}");
            }

            dimStyleId = dsTable[args.DimStyle!];
        }

        if (command.DryRun)
        {
            return DryRun(command, new
            {
                tool = "forge_draw_grid",
                gridLineCount = plan.GridLines.Length,
                bubbleCount = plan.Bubbles.Length,
                dimensionCount = plan.Dimensions.Length,
                intersections = plan.Intersections
            });
        }

        var gridLayer = args.GridLayer ?? args.Layer;
        var bubbleLayer = args.BubbleLayer ?? args.Layer;
        var dimLayer = args.DimLayer ?? args.Layer;

        var lineHandles = new List<string>(plan.GridLines.Length);
        var bubbleHandles = new List<string>(plan.Bubbles.Length * 2);
        var dimHandles = new List<string>(plan.Dimensions.Length);

        foreach (var gl in plan.GridLines)
        {
            var line = new Line(new Point3d(gl.Start[0], gl.Start[1], 0), new Point3d(gl.End[0], gl.End[1], 0));
            try
            {
                line.SetDatabaseDefaults(db);
                ApplyLayerAndColor(line, gridLayer, args.ColorIndex);
                if (!string.IsNullOrWhiteSpace(args.GridLinetype))
                {
                    line.Linetype = args.GridLinetype!;
                }

                AppendExistingToModelSpace(db, tr, line);
                lineHandles.Add(HandleString(line));
            }
            catch
            {
                if (line.ObjectId.IsNull)
                {
                    line.Dispose();
                }

                throw;
            }
        }

        foreach (var bb in plan.Bubbles)
        {
            var centerPt = new Point3d(bb.Center[0], bb.Center[1], 0);
            var circle = new Circle(centerPt, Vector3d.ZAxis, bb.Radius);
            try
            {
                circle.SetDatabaseDefaults(db);
                ApplyLayerAndColor(circle, bubbleLayer, args.ColorIndex);
                AppendExistingToModelSpace(db, tr, circle);
                bubbleHandles.Add(HandleString(circle));
            }
            catch
            {
                if (circle.ObjectId.IsNull)
                {
                    circle.Dispose();
                }

                throw;
            }

            var txt = new DBText();
            try
            {
                txt.SetDatabaseDefaults(db);
                txt.Height = bb.TextHeight;
                txt.TextString = bb.AxisLabel;
                txt.Justify = AttachmentPoint.MiddleCenter;
                txt.Position = centerPt;
                txt.AlignmentPoint = centerPt;
                ApplyLayerAndColor(txt, bubbleLayer, args.ColorIndex);
                AppendExistingToModelSpace(db, tr, txt);
                txt.AdjustAlignment(db);
                bubbleHandles.Add(HandleString(txt));
            }
            catch
            {
                if (txt.ObjectId.IsNull)
                {
                    txt.Dispose();
                }

                throw;
            }
        }

        foreach (var dm in plan.Dimensions)
        {
            var rotRad = string.Equals(dm.Orientation, "vertical", StringComparison.OrdinalIgnoreCase)
                ? Math.PI * 0.5
                : 0.0;
            var dim = new RotatedDimension(
                rotRad,
                new Point3d(dm.P1[0], dm.P1[1], 0),
                new Point3d(dm.P2[0], dm.P2[1], 0),
                new Point3d(dm.DimLinePoint[0], dm.DimLinePoint[1], 0),
                string.Empty,
                dimStyleId);
            try
            {
                dim.SetDatabaseDefaults(db);
                dim.DimensionStyle = dimStyleId;
                ApplyLayerAndColor(dim, dimLayer, args.ColorIndex);
                AppendExistingToModelSpace(db, tr, dim);
                try
                {
                    dim.RecomputeDimensionBlock(true);
                }
                catch
                {
                    // Best effort recompute
                }

                dimHandles.Add(HandleString(dim));
            }
            catch
            {
                if (dim.ObjectId.IsNull)
                {
                    dim.Dispose();
                }

                throw;
            }
        }

        var readBack = new
        {
            gridLineCount = lineHandles.Count,
            bubbleEntityCount = bubbleHandles.Count,
            dimensionCount = dimHandles.Count,
            lineHandles,
            bubbleHandles,
            dimensionHandles = dimHandles,
            intersections = plan.Intersections,
            extents = new { min = plan.MinExtents, max = plan.MaxExtents }
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            readBack,
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ForgeResult DrawWall(ForgeCommand command)
    {
        var args = Args<DrawWallArgs>(command);
        if (ValidateColorIndex(command, args.ColorIndex) is { } colorErr)
        {
            return colorErr;
        }

        var openingInputs = args.Openings?
            .Select(o => new CadSpatialMath.WallOpeningInput(o.SegmentIndex, o.OffsetFromStart, o.Width))
            .ToArray();

        CadSpatialMath.DoubleLineWallPlan plan;
        try
        {
            plan = CadSpatialMath.BuildDoubleLineWall(
                args.Vertices ?? [],
                args.Thickness,
                args.Justification ?? "center",
                args.Closed,
                openingInputs);
        }
        catch (ArgumentException ex)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", ex.Message);
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();

        if (!string.IsNullOrWhiteSpace(args.Layer) && !LayerExists(db, tr, args.Layer!))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}", "Create it with forge_layer_create first.");
        }

        if (!string.IsNullOrWhiteSpace(args.HatchLayer) && !LayerExists(db, tr, args.HatchLayer!))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.HatchLayer}", "Create it with forge_layer_create first.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new
            {
                tool = "forge_draw_wall",
                wallLoopCount = plan.ClosedWallLoops.Length,
                openingCount = plan.Openings.Length,
                args.Thickness,
                args.Justification,
                args.Closed,
                args.HatchPattern
            });
        }

        var wallHandles = new List<string>(plan.ClosedWallLoops.Length);
        var hatchHandles = new List<string>();
        var loopIds = new List<ObjectId>(plan.ClosedWallLoops.Length);
        double totalArea = 0;
        double totalLength = 0;

        foreach (var loop in plan.ClosedWallLoops)
        {
            var pl = new Polyline(loop.Vertices.Length);
            try
            {
                pl.SetDatabaseDefaults(db);
                for (var i = 0; i < loop.Vertices.Length; i++)
                {
                    var v = loop.Vertices[i];
                    var b = i < loop.Bulges.Length ? loop.Bulges[i] : 0.0;
                    pl.AddVertexAt(i, new Point2d(v[0], v[1]), b, 0, 0);
                }

                pl.Closed = loop.Closed;
                ApplyLayerAndColor(pl, args.Layer, args.ColorIndex);
                var id = AppendExistingToModelSpace(db, tr, pl);
                loopIds.Add(id);
                wallHandles.Add(HandleString(pl));
                totalLength += pl.Length;
                try
                {
                    totalArea += pl.Area;
                }
                catch
                {
                    // Ignore area if self-intersecting
                }
            }
            catch
            {
                if (pl.ObjectId.IsNull)
                {
                    pl.Dispose();
                }

                throw;
            }
        }

        if (!string.IsNullOrWhiteSpace(args.HatchPattern))
        {
            var hatchScale = args.HatchScale ?? 1.0;
            if (hatchScale <= 0 || double.IsNaN(hatchScale))
            {
                return ForgeResult.Failure(command.Id, "invalid_scale", "hatchScale must be > 0.");
            }

            // fix-smoke-20261029 D2: same pattern order and readback as forge_draw_hatch. A rejected pattern
            // or a readback mismatch fails the call; the transaction is not committed, so nothing is left.
            var hatchPatternName = args.HatchPattern!.Trim().ToUpperInvariant();
            var hatchSolid = hatchPatternName.Equals("SOLID", StringComparison.Ordinal);
            var hatchAngleRad = DegreesToRadians(args.HatchAngleDeg);
            try
            {
                // If closed wall without openings has 2 loops (outer + inner), hatch between them in one Hatch;
                // otherwise hatch each solid wall piece loop.
                if (args.Closed && plan.Openings.Length == 0 && loopIds.Count == 2)
                {
                    var hatch = new Hatch();
                    try
                    {
                        hatch.SetDatabaseDefaults(db);
                        ApplyLayerAndColor(hatch, args.HatchLayer ?? args.Layer, null);
                        AppendExistingToModelSpace(db, tr, hatch);
                        ApplyHatchPattern(hatch, hatchPatternName, hatchSolid, hatchScale, hatchAngleRad);
                        hatch.Associative = true;
                        hatch.AppendLoop(HatchLoopTypes.External, [loopIds[0]]);
                        hatch.AppendLoop(HatchLoopTypes.Default, [loopIds[1]]);
                        hatch.EvaluateHatch(true);
                        VerifyHatch(db, hatch, hatchPatternName, hatchSolid, hatchScale, hatchAngleRad);
                        hatchHandles.Add(HandleString(hatch));
                    }
                    catch
                    {
                        if (hatch.ObjectId.IsNull)
                        {
                            hatch.Dispose();
                        }

                        throw;
                    }
                }
                else
                {
                    foreach (var loopId in loopIds)
                    {
                        var hatch = new Hatch();
                        try
                        {
                            hatch.SetDatabaseDefaults(db);
                            ApplyLayerAndColor(hatch, args.HatchLayer ?? args.Layer, null);
                            AppendExistingToModelSpace(db, tr, hatch);
                            ApplyHatchPattern(hatch, hatchPatternName, hatchSolid, hatchScale, hatchAngleRad);
                            hatch.Associative = true;
                            hatch.AppendLoop(HatchLoopTypes.Default, [loopId]);
                            hatch.EvaluateHatch(true);
                            VerifyHatch(db, hatch, hatchPatternName, hatchSolid, hatchScale, hatchAngleRad);
                            hatchHandles.Add(HandleString(hatch));
                        }
                        catch
                        {
                            if (hatch.ObjectId.IsNull)
                            {
                                hatch.Dispose();
                            }

                            throw;
                        }
                    }
                }
            }
            catch (DrawStepException ex)
            {
                return ForgeResult.Failure(command.Id, ex.Code, ex.Message, "Check the hatch pattern name (e.g. ANSI31, SOLID). Nothing was drawn: the walls and hatches of this call were rolled back.");
            }
        }

        var openingsDto = plan.Openings.Select(o => new
        {
            segmentIndex = o.SegmentIndex,
            center = o.Center,
            width = o.Width,
            angleDeg = o.AngleDeg,
            jambLeft = o.JambLeft,
            jambRight = o.JambRight
        }).ToArray();

        var readBack = new
        {
            wallHandles,
            hatchHandles,
            wallPieceCount = wallHandles.Count,
            totalLength,
            totalArea,
            openings = openingsDto
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            readBack,
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private sealed record DrawingSummaryArgs
    {
        public string? Layout { get; init; }
    }

    private sealed record EntitySearchTextArgs
    {
        public string? Query { get; init; }
        public string? Layer { get; init; }
        public double[][]? Window { get; init; }
        public string? Layout { get; init; }
        public bool CaseSensitive { get; init; }
        public bool ExactMatch { get; init; }
        public bool UseRegex { get; init; }
        public int? MaxResults { get; init; }
    }

    private sealed record EntitySnapArgs
    {
        public double[]? Point { get; init; }
        public double? Radius { get; init; }
        public double[]? FromPoint { get; init; }
        public string[]? SnapTypes { get; init; }
        public string? Layer { get; init; }
        public int? MaxResults { get; init; }
    }

    private sealed record DrawGridArgs
    {
        public double[]? Origin { get; init; }
        public double[]? XPositions { get; init; }
        public double[]? XSpacings { get; init; }
        public string[]? XLabels { get; init; }
        public double[]? YPositions { get; init; }
        public double[]? YSpacings { get; init; }
        public string[]? YLabels { get; init; }
        public double? ExtensionLength { get; init; }
        public double? BubbleRadius { get; init; }
        public string? BubbleSides { get; init; }
        public bool IncludeDimensions { get; init; } = true;
        public string? Layer { get; init; }
        public string? GridLayer { get; init; }
        public string? BubbleLayer { get; init; }
        public string? DimLayer { get; init; }
        public string? GridLinetype { get; init; }
        public string? DimStyle { get; init; }
        public int? ColorIndex { get; init; }
    }

    private sealed record DrawWallArgs
    {
        public double[][]? Vertices { get; init; }
        public double Thickness { get; init; }
        public string? Justification { get; init; }
        public bool Closed { get; init; }
        public WallOpeningDto[]? Openings { get; init; }
        public string? HatchPattern { get; init; }
        public double? HatchScale { get; init; }
        public double HatchAngleDeg { get; init; }
        public string? Layer { get; init; }
        public string? HatchLayer { get; init; }
        public int? ColorIndex { get; init; }
    }
}
