using System.Diagnostics;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Forge.Shared;

namespace Forge.Plugin;

// forge_qa_annotation_clash — read-only presentation QA so an agent stops reviewing PNGs by eye.
//
// One block-aware walk (WalkBlockSpace) collects world-space text boxes (TEXT, ATTRIB, MTEXT via
// GetBoundingPoints, MLEADER content, dimension text from the dimension block) and tessellated
// linework (lines, polylines with bulges, arcs, circles, ellipses, splines, leader lines,
// dimension lines). Everything after extraction — overlap, crossing, door swing, wall straddle,
// plotted height — is Forge.Shared.AnnotationClash (pure, unit-tested). Layers that are off,
// frozen or non-plotting are skipped, and so is everything inside an INSERT on such a layer.
public sealed partial class PluginCommandProcessor
{
    private const int ClashMaxTexts = 50_000;
    private const int ClashMaxSegments = 600_000;
    private const int ClashGenericCurveSamples = 48;

    private sealed record AnnotationClashArgs
    {
        public string? Layout { get; init; }
        public double[][]? Window { get; init; }
        public string[]? Checks { get; init; }
        public string? TextLayers { get; init; }
        public string? CurveLayers { get; init; }
        public string? ExcludeLayers { get; init; }
        public string? DoorLayers { get; init; }
        public string? DoorBlocks { get; init; }
        public string? WallLayers { get; init; }
        public double? PlotScale { get; init; }
        public double? MinTextHeightMm { get; init; }
        public string? DrawingUnits { get; init; }
        public double? MinOverlapRatio { get; init; }
        public double? Padding { get; init; }
        public bool ReportMaskedCrossings { get; init; }
        public bool IncludeBlocks { get; init; } = true;
        public int? BlockDepth { get; init; }
        public bool IncludeXrefs { get; init; } = true;
        public int? MaxFindings { get; init; }
    }

    private static ForgeResult AnnotationClashQa(ForgeCommand command)
    {
        var args = Args<AnnotationClashArgs>(command);
        if (RejectControlChars(command, ("layout", args.Layout), ("textLayers", args.TextLayers), ("curveLayers", args.CurveLayers), ("excludeLayers", args.ExcludeLayers), ("doorLayers", args.DoorLayers), ("doorBlocks", args.DoorBlocks), ("wallLayers", args.WallLayers)) is { } reject)
        {
            return reject;
        }

        double[]? window = null;
        if (args.Window is { Length: > 0 })
        {
            if (!PlotLayoutMath.TryNormalizeWindow(args.Window, null, out var box, out var windowError))
            {
                return ForgeResult.Failure(command.Id, "invalid_window", windowError ?? "window is invalid.");
            }

            window = box;
        }

        var checks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (args.Checks is { Length: > 0 })
        {
            foreach (var check in args.Checks)
            {
                var name = check?.Trim() ?? "";
                if (!AnnotationClash.AllChecks.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    return ForgeResult.Failure(command.Id, "invalid_check", $"Unknown check '{check}'. Use: {string.Join(", ", AnnotationClash.AllChecks)}.");
                }

                checks.Add(name);
            }
        }
        else
        {
            checks.UnionWith([AnnotationClash.CheckTextOverlap, AnnotationClash.CheckTextCrossing, AnnotationClash.CheckDoorSwing, AnnotationClash.CheckWallStraddle]);
            if (args.PlotScale is not null)
            {
                checks.Add(AnnotationClash.CheckMinHeight);
            }
        }

        if (args.PlotScale is { } ps && !(ps > 0 && double.IsFinite(ps)))
        {
            return ForgeResult.Failure(command.Id, "invalid_plot_scale", "plotScale is the scale denominator (100 for 1:100) and must be > 0.");
        }

        var watch = Stopwatch.StartNew();
        var warnings = new List<string>();
        var db = ActiveDb;
        var spaceName = string.IsNullOrWhiteSpace(args.Layout) ? "Model" : args.Layout!.Trim();
        var isPaper = !string.Equals(spaceName, "Model", StringComparison.OrdinalIgnoreCase);

        double? unitsPerPaperMm = null;
        if (checks.Contains(AnnotationClash.CheckMinHeight))
        {
            var meters = ParseDrawingUnits(args.DrawingUnits) ?? DrawingImport.MetersPerUnit((int)db.Insunits);
            if (meters is null)
            {
                meters = 0.001;
                warnings.Add("Drawing is unitless (INSUNITS=0); min_height assumes millimetres. Pass drawingUnits to override.");
            }

            var denominator = args.PlotScale ?? (isPaper ? 1.0 : 0);
            if (denominator <= 0)
            {
                return ForgeResult.Failure(command.Id, "missing_plot_scale", "min_height on Model needs plotScale (100 for 1:100).");
            }

            // Paper space is plotted 1:1 in paper units (mm, or inches when MEASUREMENT is English).
            var paperMeters = db.Measurement == MeasurementValue.English ? 0.0254 : 0.001;
            unitsPerPaperMm = AnnotationClash.UnitsPerPaperMm(denominator, isPaper && args.DrawingUnits is null ? paperMeters : meters.Value);
        }

        var textFilter = WildcardSet.Parse(args.TextLayers);
        var curveFilter = WildcardSet.Parse(args.CurveLayers);
        var exclude = WildcardSet.Parse(args.ExcludeLayers);
        var doorLayers = WildcardSet.Parse(string.IsNullOrWhiteSpace(args.DoorLayers) ? "*DOOR*" : args.DoorLayers);
        var doorBlocks = WildcardSet.Parse(string.IsNullOrWhiteSpace(args.DoorBlocks) ? "*DOOR*,*DR-*" : args.DoorBlocks);
        var wallLayers = WildcardSet.Parse(string.IsNullOrWhiteSpace(args.WallLayers) ? "*WALL*" : args.WallLayers);

        var texts = new List<ClashText>();
        var curves = new List<ClashCurve>();
        var segments = 0;
        var skippedHidden = 0;
        var skippedDims = 0;
        var truncated = false;
        var stats = new BlockWalkStats();
        var options = new BlockWalkOptions(args.IncludeBlocks, BlockWalk.ClampDepth(args.BlockDepth), window, args.IncludeXrefs);

        using var tr = db.TransactionManager.StartTransaction();
        var space = ResolveSpace(tr, db, spaceName);
        if (space is null)
        {
            return ForgeResult.Failure(command.Id, "layout_not_found", $"Layout not found: {spaceName}", "Call forge_doc_list_layouts to see available layouts.");
        }

        var hiddenLayers = HiddenLayers(tr, db);
        var hiddenInserts = new HashSet<string>(StringComparer.Ordinal);
        var insertSizes = new Dictionary<string, double>(StringComparer.Ordinal);

        bool InHiddenInsert(BlockWalkFrame frame)
        {
            foreach (var step in frame.Path)
            {
                if (hiddenInserts.Contains(BaseHandle(step.InsertHandle)))
                {
                    return true;
                }
            }

            return false;
        }

        (string? Owner, double Size) Owner(BlockWalkFrame frame)
        {
            if (frame.Path.Count == 0)
            {
                return (null, 0);
            }

            var handle = frame.Path[^1].InsertHandle;
            return (handle, insertSizes.TryGetValue(BaseHandle(handle), out var size) ? size : double.MaxValue);
        }

        string? PathLabel(BlockWalkFrame frame) => frame.Path.Count > 0 ? BlockWalk.InsertPathLabel(frame.Path) : null;

        bool LayerVisible(string effectiveLayer) => !hiddenLayers.Contains(effectiveLayer);

        bool TextWanted(string own, string effective) =>
            textFilter.Matches(effective, emptyMatchesAll: true) || textFilter.Matches(own);

        bool CurveWanted(string own, string effective) =>
            !exclude.Matches(effective) && !exclude.Matches(own) &&
            (curveFilter.Matches(effective, emptyMatchesAll: true) || curveFilter.Matches(own));

        bool InWindow(Vec2[] pts)
        {
            if (window is null)
            {
                return true;
            }

            var box = new[] { pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y) };
            return BlockWalk.BoxesIntersect(box, window);
        }

        void AddText(string type, Entity source, string text, Vec2[] localCorners, double localRotation, double localHeight, bool masked, BlockWalkFrame frame, string? ownerOverride = null)
        {
            if (texts.Count >= ClashMaxTexts)
            {
                truncated = true;
                return;
            }

            var effective = BlockWalk.EffectiveLayer(source.Layer, frame.ParentEffectiveLayer);
            if (!LayerVisible(effective) || exclude.Matches(effective) || exclude.Matches(source.Layer) || !TextWanted(source.Layer, effective))
            {
                return;
            }

            var corners = frame.IsIdentity ? localCorners : localCorners.Select(c => frame.Affine.Apply2D(c.X, c.Y)).ToArray();
            if (!InWindow(corners))
            {
                return;
            }

            var height = frame.IsIdentity ? localHeight : TextRead.WorldMetrics(frame.Affine, localRotation, localHeight).Height;
            var (owner, size) = ownerOverride is not null ? (ownerOverride, 0.0) : Owner(frame);
            texts.Add(new ClashText
            {
                Handle = source.Handle.ToString(),
                Type = type,
                Layer = effective,
                Text = text,
                Corners = corners,
                Height = height,
                Masked = masked,
                Owner = owner,
                OwnerSize = size,
                Path = PathLabel(frame)
            });
        }

        void AddCurve(string handle, string type, string ownLayer, Vec2[] worldPoints, bool closed, ClashCurveKind kind, Vec2? apex, BlockWalkFrame frame, string? ownerOverride = null)
        {
            if (worldPoints.Length < 2)
            {
                return;
            }

            if (segments >= ClashMaxSegments)
            {
                truncated = true;
                return;
            }

            var effective = BlockWalk.EffectiveLayer(ownLayer, frame.ParentEffectiveLayer);
            if (!LayerVisible(effective) || !CurveWanted(ownLayer, effective) || !InWindow(worldPoints))
            {
                return;
            }

            var (owner, size) = ownerOverride is not null ? (ownerOverride, 0.0) : Owner(frame);
            segments += closed ? worldPoints.Length : worldPoints.Length - 1;
            curves.Add(new ClashCurve
            {
                Handle = handle,
                Type = type,
                Layer = effective,
                Points = worldPoints,
                Closed = closed,
                Kind = kind,
                SectorApex = apex,
                Owner = owner,
                OwnerSize = size,
                Path = PathLabel(frame)
            });
        }

        WalkBlockSpace(tr, space, options, stats, (entity, id, frame) =>
        {
            if (frame.Depth > 0 && InHiddenInsert(frame))
            {
                return true;
            }

            var effective = BlockWalk.EffectiveLayer(entity.Layer, frame.ParentEffectiveLayer);
            if (!LayerVisible(effective))
            {
                skippedHidden++;
                if (entity is BlockReference hiddenRef)
                {
                    hiddenInserts.Add(hiddenRef.Handle.ToString());
                }

                return true;
            }

            try
            {
                switch (entity)
                {
                    case AttributeDefinition ad:
                        if (frame.Depth > 0 && ad.Constant && !ad.Invisible)
                        {
                            AddText("ATTDEF_CONSTANT", ad, ad.TextString ?? "", LocalTextBox(ad, ad.Rotation, ad.Height), ad.Rotation, ad.Height, false, frame);
                        }

                        break;
                    case DBText dt:
                        AddText("TEXT", dt, CadSpatialMath.StripMTextFormatting(dt.TextString), LocalTextBox(dt, dt.Rotation, dt.Height), dt.Rotation, dt.Height, false, frame);
                        break;
                    case MText mt:
                        AddText("MTEXT", mt, PlainMText(mt), MTextBox(mt), mt.Rotation, mt.TextHeight, mt.BackgroundFill, frame);
                        break;
                    case MLeader ml:
                        CollectMLeader(ml, frame);
                        break;
                    case Dimension dim:
                        if (!CollectDimension(dim, frame))
                        {
                            skippedDims++;
                        }

                        break;
                    case BlockReference br:
                    {
                        if (WorldBox(br, frame) is { Length: 4 } wb)
                        {
                            insertSizes[br.Handle.ToString()] = Math.Max(wb[2] - wb[0], wb[3] - wb[1]);
                        }

                        foreach (ObjectId attId in br.AttributeCollection)
                        {
                            if (attId.IsErased || tr.GetObject(attId, OpenMode.ForRead) is not AttributeReference ar || ar.Invisible)
                            {
                                continue;
                            }

                            var value = ar.IsMTextAttribute && ar.MTextAttribute is { } mta ? PlainAndDispose(mta) : CadSpatialMath.StripMTextFormatting(ar.TextString ?? "");
                            if (string.IsNullOrWhiteSpace(value))
                            {
                                continue;
                            }

                            // Attribute references live in the INSERT's owner space; they belong to this INSERT.
                            var size = insertSizes.TryGetValue(br.Handle.ToString(), out var s) ? s : 0;
                            AddAttribute(ar, value, frame, br.Handle.ToString(), size);
                        }

                        break;
                    }
                    case Curve curve:
                        CollectCurve(curve, id, frame);
                        break;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // An entity without computable geometry (empty text, proxy) is skipped, not fatal.
            }

            return texts.Count < ClashMaxTexts || segments < ClashMaxSegments;
        });

        void AddAttribute(AttributeReference ar, string value, BlockWalkFrame frame, string owner, double ownerSize)
        {
            if (texts.Count >= ClashMaxTexts)
            {
                truncated = true;
                return;
            }

            var effective = BlockWalk.EffectiveLayer(ar.Layer, frame.ParentEffectiveLayer);
            if (!LayerVisible(effective) || exclude.Matches(effective) || !TextWanted(ar.Layer, effective))
            {
                return;
            }

            var local = LocalTextBox(ar, ar.Rotation, ar.Height);
            var corners = frame.IsIdentity ? local : local.Select(c => frame.Affine.Apply2D(c.X, c.Y)).ToArray();
            if (!InWindow(corners))
            {
                return;
            }

            texts.Add(new ClashText
            {
                Handle = ar.Handle.ToString(),
                Type = "ATTRIB",
                Layer = effective,
                Text = value,
                Corners = corners,
                Height = frame.IsIdentity ? ar.Height : TextRead.WorldMetrics(frame.Affine, ar.Rotation, ar.Height).Height,
                Owner = owner,
                OwnerSize = ownerSize,
                Path = PathLabel(frame)
            });
        }

        void CollectCurve(Curve curve, ObjectId id, BlockWalkFrame frame)
        {
            var xf = frame.IsIdentity ? (Matrix3d?)null : frame.Matrix;
            Vec2[] points;
            bool closed;
            Vec2? apex = null;
            switch (curve)
            {
                case Line or Polyline or Polyline2d or Polyline3d or Arc or Circle:
                {
                    if (TryMakeEntity(tr, curve, id, xf) is not { } le)
                    {
                        return;
                    }

                    points = le.Points;

                    // Tessellated circles already repeat their first point, so they stay open rings.
                    closed = le.Kind != LineworkKind.Circle && le.Closed;

                    if (curve is Arc arc)
                    {
                        apex = Xf(arc.Center, xf);
                    }

                    break;
                }
                case Ellipse or Spline:
                {
                    points = SampleGenericCurve(curve, xf);
                    closed = false;
                    break;
                }
                default:
                    return;
            }

            var blockNames = frame.Path.Count > 0 ? frame.Path.Select(p => p.BlockName) : null;
            var effective = BlockWalk.EffectiveLayer(curve.Layer, frame.ParentEffectiveLayer);
            var kind = AnnotationClash.Classify(effective, blockNames, curve is Arc, doorLayers, doorBlocks, wallLayers);
            AddCurve(curve.Handle.ToString(), curve.GetType().Name, curve.Layer, points, closed, kind, kind == ClashCurveKind.DoorSwing ? apex : null, frame);
        }

        void CollectMLeader(MLeader ml, BlockWalkFrame frame)
        {
            var handle = ml.Handle.ToString();
            if (ml.ContentType == ContentType.MTextContent)
            {
                using var mt = ml.MText;
                if (mt is not null && !string.IsNullOrWhiteSpace(mt.Contents))
                {
                    var extents = ml.GetContentGeomExtents();
                    var localBox = AnnotationClash.FromRotatedAabb(extents.MinPoint.X, extents.MinPoint.Y, extents.MaxPoint.X, extents.MaxPoint.Y, mt.Rotation, mt.TextHeight);
                    AddText("MLEADER", ml, PlainMText(mt), localBox, mt.Rotation, mt.TextHeight, mt.BackgroundFill, frame, handle);
                }
            }
            else if (ml.ContentType == ContentType.BlockContent)
            {
                var extents = ml.GetContentGeomExtents();
                var localBox = AnnotationClash.RectCorners(
                    (extents.MinPoint.X + extents.MaxPoint.X) / 2,
                    (extents.MinPoint.Y + extents.MaxPoint.Y) / 2,
                    extents.MaxPoint.X - extents.MinPoint.X,
                    extents.MaxPoint.Y - extents.MinPoint.Y,
                    0);
                var name = tr.GetObject(ml.BlockContentId, OpenMode.ForRead) is BlockTableRecord content ? content.Name : "block";
                AddText("MLEADER_BLOCK", ml, name, localBox, 0, extents.MaxPoint.Y - extents.MinPoint.Y, false, frame, handle);
            }

            var xf = frame.IsIdentity ? (Matrix3d?)null : frame.Matrix;
            foreach (int leaderIndex in ml.GetLeaderIndexes())
            {
                foreach (int lineIndex in ml.GetLeaderLineIndexes(leaderIndex))
                {
                    var count = ml.VerticesCount(lineIndex);
                    var pts = new Vec2[count];
                    for (var i = 0; i < count; i++)
                    {
                        pts[i] = Xf(ml.GetVertex(lineIndex, i), xf);
                    }

                    AddCurve(handle, "MLeaderLine", ml.Layer, pts, false, ClashCurveKind.Leader, null, frame, handle);
                }
            }
        }

        bool CollectDimension(Dimension dim, BlockWalkFrame frame)
        {
            if (dim.DimBlockId.IsNull || tr.GetObject(dim.DimBlockId, OpenMode.ForRead) is not BlockTableRecord block)
            {
                return false;
            }

            var handle = dim.Handle.ToString();
            var masked = dim.Dimtfill > 0;
            var offset = dim.DimBlockPosition - Point3d.Origin;
            var local = offset.IsZeroLength() ? (Matrix3d?)null : Matrix3d.Displacement(offset);
            var xf = frame.IsIdentity ? local : local is { } l ? frame.Matrix * l : frame.Matrix;
            var foundText = false;
            foreach (ObjectId id in block)
            {
                switch (tr.GetObject(id, OpenMode.ForRead))
                {
                    case MText mt:
                    {
                        var box = MTextBox(mt);
                        if (local is { } lm)
                        {
                            box = box.Select(c => Xf(new Point3d(c.X, c.Y, 0), lm)).ToArray();
                        }

                        AddText("DIMENSION", dim, PlainMText(mt), box, mt.Rotation, mt.TextHeight, masked, frame, handle);
                        foundText = true;
                        break;
                    }
                    case Line line:
                        AddCurve(handle, "DimensionLine", dim.Layer, [Xf(line.StartPoint, xf), Xf(line.EndPoint, xf)], false, ClashCurveKind.Generic, null, frame, handle);
                        break;
                    case Arc arc:
                        AddCurve(handle, "DimensionArc", dim.Layer, SampleCurve(arc, arc.StartParam, arc.EndParam, xf), false, ClashCurveKind.Generic, null, frame, handle);
                        break;
                }
            }

            return foundText;
        }

        tr.Commit();

        var report = AnnotationClash.Analyze(texts, curves, new ClashOptions
        {
            TextOverlap = checks.Contains(AnnotationClash.CheckTextOverlap),
            TextCrossing = checks.Contains(AnnotationClash.CheckTextCrossing),
            DoorSwing = checks.Contains(AnnotationClash.CheckDoorSwing),
            WallStraddle = checks.Contains(AnnotationClash.CheckWallStraddle),
            MinHeight = checks.Contains(AnnotationClash.CheckMinHeight),
            MinOverlapRatio = args.MinOverlapRatio is > 0 and < 1 ? args.MinOverlapRatio.Value : 0.02,
            Padding = args.Padding is { } pad && double.IsFinite(pad) ? pad : 0,
            MinPlottedHeightMm = args.MinTextHeightMm ?? 2.0,
            UnitsPerPaperMm = unitsPerPaperMm,
            ReportMaskedCrossings = args.ReportMaskedCrossings,
            MaxFindings = args.MaxFindings ?? 200
        });

        if (truncated)
        {
            warnings.Add($"Extraction stopped at {ClashMaxTexts} texts / {ClashMaxSegments} segments; pass a window or layer filters to check the rest.");
        }

        if (skippedDims > 0)
        {
            warnings.Add($"{skippedDims} dimension(s) had no computed dimension block and were skipped (regen the drawing once).");
        }

        var total = report.Counts.Values.Sum();
        watch.Stop();
        return ForgeResult.Success(command.Id, new
        {
            layout = spaceName,
            passed = total == 0,
            summary = new
            {
                findings = total,
                byCheck = report.Counts,
                texts = report.Texts,
                curves = report.Curves,
                segments = report.Segments,
                maskedCrossingsSkipped = report.MaskedCrossingsSkipped,
                sameGroupPairsSkipped = report.SameGroupPairsSkipped,
                skippedHiddenLayer = skippedHidden
            },
            checks = checks.OrderBy(c => Array.IndexOf(AnnotationClash.AllChecks, c)).ToArray(),
            findings = report.Findings,
            findingsTruncated = report.Truncated,
            unitsPerPaperMm,
            blockTraversal = BlockTraversalReport(options, stats),
            warnings,
            elapsedMs = watch.ElapsedMilliseconds
        });
    }

    /// <summary>Oriented box of a single-line text in its owner space (from extents + rotation).</summary>
    private static Vec2[] LocalTextBox(Entity text, double rotation, double height)
    {
        var e = text.GeometricExtents;
        return AnnotationClash.FromRotatedAabb(e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y, rotation, height);
    }

    /// <summary>MText frame corners (exact, rotation-aware) in owner space; falls back to extents.</summary>
    private static Vec2[] MTextBox(MText mt)
    {
        try
        {
            var pts = mt.GetBoundingPoints();
            if (pts is { Count: 4 })
            {
                // Order is top-left, top-right, bottom-left, bottom-right → walk the perimeter.
                return [new Vec2(pts[0].X, pts[0].Y), new Vec2(pts[1].X, pts[1].Y), new Vec2(pts[3].X, pts[3].Y), new Vec2(pts[2].X, pts[2].Y)];
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception)
        {
            // Fall through to the extents decomposition.
        }

        return LocalTextBox(mt, mt.Rotation, mt.TextHeight);
    }

    private static string PlainMText(MText mt)
    {
        var raw = mt.Contents ?? "";
        return !string.IsNullOrEmpty(mt.Text) ? mt.Text : CadSpatialMath.StripMTextFormatting(raw);
    }

    private static string PlainAndDispose(MText mt)
    {
        using (mt)
        {
            return PlainMText(mt);
        }
    }

    private static Vec2[] SampleGenericCurve(Curve curve, Matrix3d? xf)
    {
        var start = curve.StartParam;
        var end = curve.EndParam;
        var pts = new Vec2[ClashGenericCurveSamples + 1];
        for (var i = 0; i <= ClashGenericCurveSamples; i++)
        {
            pts[i] = Xf(curve.GetPointAtParameter(start + (end - start) * i / ClashGenericCurveSamples), xf);
        }

        return pts;
    }

    /// <summary>Effective layers that do not plot: off, frozen, or non-plottable.</summary>
    private static HashSet<string> HiddenLayers(Transaction tr, Database db)
    {
        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (ObjectId id in table)
        {
            var layer = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (layer.IsOff || layer.IsFrozen || !layer.IsPlottable)
            {
                hidden.Add(layer.Name);
            }
        }

        return hidden;
    }

    private static string BaseHandle(string insertHandle)
    {
        var cut = insertHandle.IndexOf('[', StringComparison.Ordinal);
        return cut > 0 ? insertHandle[..cut] : insertHandle;
    }

    private static double? ParseDrawingUnits(string? units) => units?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "mm" or "millimeter" or "millimeters" or "millimetre" or "millimetres" => 0.001,
        "cm" or "centimeter" or "centimeters" => 0.01,
        "m" or "meter" or "meters" or "metre" or "metres" => 1.0,
        "in" or "inch" or "inches" => 0.0254,
        "ft" or "foot" or "feet" => 0.3048,
        _ => null
    };
}
