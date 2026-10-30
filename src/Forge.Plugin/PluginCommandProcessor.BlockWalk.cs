using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Forge.Shared;

namespace Forge.Plugin;

// Block-aware traversal shared by forge_linework_dump (includeBlocks), forge_entity_list
// (includeBlocks) and forge_text_list. A drawing whose plan lives inside blocks (or blocks inside
// xrefs) is otherwise invisible to an agent: the model-space walk only sees the INSERT.
//
// Rules (policy lives in Forge.Shared.BlockWalk so it is unit-tested):
//  • world = parent * INSERT.BlockTransform, composed down the chain (MINSERT arrays expanded);
//  • depth limit (default 8, hard max 32) and a cycle guard on block-definition ids;
//  • an INSERT whose world extents miss the bbox filter is not descended into;
//  • entities hidden by a dynamic-block visibility state (Visible=false) are skipped;
//  • layer "0" inside a block displays on the INSERT's layer (effectiveLayer);
//  • xrefs are walked through their INSERTs (every instance) when xrefs are included.
public sealed partial class PluginCommandProcessor
{
    private const int MaxMInsertInstances = 1000;

    private sealed record BlockWalkOptions(bool IncludeBlocks, int MaxDepth, double[]? FilterBox, bool IncludeXrefs);

    private sealed class BlockWalkFrame
    {
        public static readonly BlockWalkFrame Root = new(Matrix3d.Identity, 0, [], null, []);

        public BlockWalkFrame(Matrix3d matrix, int depth, IReadOnlyList<BlockPathStep> path, string? parentEffectiveLayer, IReadOnlyCollection<string> ancestors)
        {
            Matrix = matrix;
            Depth = depth;
            Path = path;
            ParentEffectiveLayer = parentEffectiveLayer;
            Ancestors = ancestors;
            Affine = ToAffine(matrix);
            IsIdentity = depth == 0 || Affine.IsIdentity;
        }

        /// <summary>Owner-space → world transform for entities that live in this frame.</summary>
        public Matrix3d Matrix { get; }
        public AffineXf Affine { get; }
        public bool IsIdentity { get; }
        public int Depth { get; }
        public IReadOnlyList<BlockPathStep> Path { get; }

        /// <summary>Effective layer of the INSERT that owns this frame (null at top level).</summary>
        public string? ParentEffectiveLayer { get; }
        public IReadOnlyCollection<string> Ancestors { get; }
    }

    private static AffineXf ToAffine(Matrix3d m) => new(
        m[0, 0], m[0, 1], m[0, 2], m[0, 3],
        m[1, 0], m[1, 1], m[1, 2], m[1, 3],
        m[2, 0], m[2, 1], m[2, 2], m[2, 3]);

    /// <summary>World [minX,minY,maxX,maxY] of an entity living in <paramref name="frame"/>, or null without extents.</summary>
    private static double[]? WorldBox(Entity entity, BlockWalkFrame frame)
    {
        if (TryExtents(entity) is not { } e)
        {
            return null;
        }

        return frame.IsIdentity
            ? [e[0], e[1], e[3], e[4]]
            : frame.Affine.TransformBox(e[0], e[1], e[2], e[3], e[4], e[5]);
    }

    private static Point3d ToWorld(Point3d p, BlockWalkFrame frame) => frame.IsIdentity ? p : p.TransformBy(frame.Matrix);

    /// <summary>
    /// Visits every entity of <paramref name="space"/> (and, with IncludeBlocks, of the blocks it
    /// inserts). <paramref name="visit"/> sees each entity with the frame it lives in — including
    /// the BlockReference itself, before its contents — and returns false to stop the walk.
    /// </summary>
    private static void WalkBlockSpace(
        Transaction tr,
        BlockTableRecord space,
        BlockWalkOptions options,
        BlockWalkStats stats,
        Func<Entity, ObjectId, BlockWalkFrame, bool> visit)
    {
        var stop = false;
        Walk(space, BlockWalkFrame.Root);

        void Walk(BlockTableRecord btr, BlockWalkFrame frame)
        {
            foreach (ObjectId id in btr)
            {
                if (stop)
                {
                    return;
                }

                if (id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not Entity entity)
                {
                    continue;
                }

                if (frame.Depth > 0 && !entity.Visible)
                {
                    stats.SkippedInvisible++;
                    continue;
                }

                if (!visit(entity, id, frame))
                {
                    stop = true;
                    stats.Truncated = true;
                    return;
                }

                if (options.IncludeBlocks && entity is BlockReference br)
                {
                    Descend(br, frame);
                }
            }
        }

        void Descend(BlockReference br, BlockWalkFrame frame)
        {
            if (tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) is not BlockTableRecord child)
            {
                return;
            }

            var isXref = child.IsFromExternalReference || child.IsFromOverlayReference;
            if (isXref && !child.IsResolved)
            {
                stats.SkippedXref++;
                return;
            }

            var childKey = child.Handle.ToString();
            var decision = BlockWalk.ShouldDescend(
                frame.Depth,
                options.MaxDepth,
                WorldBox(br, frame),
                options.FilterBox,
                frame.Ancestors,
                childKey,
                isXref,
                options.IncludeXrefs);
            if (decision != BlockDescendDecision.Descend)
            {
                stats.Count(decision);
                return;
            }

            var blockName = child.Name;
            if (br.IsDynamicBlock && !br.DynamicBlockTableRecord.IsNull &&
                tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead) is BlockTableRecord dynamicDef)
            {
                blockName = dynamicDef.Name;
            }

            var ancestors = new HashSet<string>(frame.Ancestors, StringComparer.Ordinal) { childKey };
            var effectiveLayer = BlockWalk.EffectiveLayer(br.Layer, frame.ParentEffectiveLayer);
            var handle = br.Handle.ToString();

            foreach (var (instanceXf, suffix) in InsertInstances(br))
            {
                if (stop)
                {
                    return;
                }

                stats.InsertsVisited++;
                stats.MaxDepthReached = Math.Max(stats.MaxDepthReached, frame.Depth + 1);
                var path = new List<BlockPathStep>(frame.Path) { new(handle + suffix, blockName) };
                Walk(child, new BlockWalkFrame(frame.Matrix * instanceXf, frame.Depth + 1, path, effectiveLayer, ancestors));
            }
        }
    }

    /// <summary>Block transforms of an INSERT — one, or rows x columns for an MINSERT array.</summary>
    private static IEnumerable<(Matrix3d Transform, string Suffix)> InsertInstances(BlockReference br)
    {
        var baseXf = br.BlockTransform;
        if (br is not MInsertBlock mi || (mi.Rows <= 1 && mi.Columns <= 1))
        {
            yield return (baseXf, "");
            yield break;
        }

        var emitted = 0;
        for (var r = 0; r < mi.Rows; r++)
        {
            for (var c = 0; c < mi.Columns; c++)
            {
                if (++emitted > MaxMInsertInstances)
                {
                    yield break;
                }

                // Row/column spacing is measured along the INSERT's own (rotated) axes, in owner units.
                var offset = Matrix3d.Displacement(new Vector3d(c * mi.ColumnSpacing, r * mi.RowSpacing, 0)
                    .TransformBy(Matrix3d.Rotation(mi.Rotation, mi.Normal, Point3d.Origin)));
                yield return (offset * baseXf, $"[{r},{c}]");
            }
        }
    }

    private static double[]? NormalizeFilterBox(double[]? bbox)
    {
        if (bbox is not { Length: 4 })
        {
            return null;
        }

        return [Math.Min(bbox[0], bbox[2]), Math.Min(bbox[1], bbox[3]), Math.Max(bbox[0], bbox[2]), Math.Max(bbox[1], bbox[3])];
    }

    private static object BlockTraversalReport(BlockWalkOptions options, BlockWalkStats stats) => new
    {
        includeBlocks = options.IncludeBlocks,
        maxDepth = options.MaxDepth,
        includeXrefs = options.IncludeXrefs,
        filterBox = options.FilterBox,
        insertsVisited = stats.InsertsVisited,
        maxDepthReached = stats.MaxDepthReached,
        skippedDepthLimit = stats.SkippedDepthLimit,
        skippedOutsideBbox = stats.SkippedOutsideBbox,
        skippedCycle = stats.SkippedCycle,
        skippedXref = stats.SkippedXref,
        skippedInvisible = stats.SkippedInvisible,
        truncated = stats.Truncated
    };

    // ===================== forge_text_list =====================

    private sealed record TextListArgs
    {
        public double[][]? Window { get; init; }
        public string? Layer { get; init; }
        public string? Contains { get; init; }
        public string? Regex { get; init; }
        public string? Layout { get; init; }
        public string? Types { get; init; }
        public bool IncludeBlocks { get; init; } = true;
        public int? BlockDepth { get; init; }
        public bool IncludeXrefs { get; init; } = true;
        public int? MaxResults { get; init; }
    }

    private static ForgeResult TextList(ForgeCommand command)
    {
        var args = Args<TextListArgs>(command);
        if (RejectControlChars(command, ("layer", args.Layer), ("layout", args.Layout), ("types", args.Types)) is { } reject)
        {
            return reject;
        }

        if (!TextRead.TryParseKinds(args.Types, out var kinds, out var typeError))
        {
            return ForgeResult.Failure(command.Id, "invalid_types", typeError!);
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

        if (!string.IsNullOrEmpty(args.Regex))
        {
            try
            {
                _ = new System.Text.RegularExpressions.Regex(args.Regex, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException ex)
            {
                return ForgeResult.Failure(command.Id, "invalid_regex", $"Invalid regular expression: {ex.Message}");
            }
        }

        var maxResults = TextRead.ClampMaxResults(args.MaxResults);
        var options = new BlockWalkOptions(args.IncludeBlocks, BlockWalk.ClampDepth(args.BlockDepth), window, args.IncludeXrefs);
        var stats = new BlockWalkStats();
        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var spaceName = string.IsNullOrWhiteSpace(args.Layout) ? "Model" : args.Layout!.Trim();
        var space = ResolveSpace(tr, db, spaceName);
        if (space is null)
        {
            return ForgeResult.Failure(command.Id, "layout_not_found", $"Layout not found: {spaceName}", "Call forge_doc_list_layouts to see available layouts.");
        }

        var results = new List<object>();
        var countsByType = new Dictionary<string, int>(StringComparer.Ordinal);

        bool Emit(string type, Entity source, string text, string? raw, string? tag, Point3d localPosition, Point3d? localAlignment, double localRotation, double localHeight, string style, bool invisible, BlockWalkFrame frame, double[]? worldBox, string? effectiveLayerOverride = null)
        {
            var effectiveLayer = effectiveLayerOverride ?? BlockWalk.EffectiveLayer(source.Layer, frame.ParentEffectiveLayer);
            if (!TextRead.LayerMatches(args.Layer, source.Layer, effectiveLayer))
            {
                return true;
            }

            var position = ToWorld(localPosition, frame);
            if (window is not null)
            {
                var inside = worldBox is { Length: 4 }
                    ? BlockWalk.BoxesIntersect(worldBox, window)
                    : BlockWalk.PointInBox(position.X, position.Y, window);
                if (!inside)
                {
                    return true;
                }
            }

            if (!string.IsNullOrEmpty(args.Contains) &&
                !CadSpatialMath.MatchesTextQuery(text, raw, args.Contains!, caseSensitive: false, exactMatch: false, useRegex: false) &&
                !(tag is not null && tag.Contains(args.Contains!, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(args.Regex) &&
                !CadSpatialMath.MatchesTextQuery(text, raw, args.Regex!, caseSensitive: false, exactMatch: false, useRegex: true))
            {
                return true;
            }

            var (rotationDeg, height, mirrored) = TextRead.WorldMetrics(frame.Affine, localRotation, localHeight);
            double[]? alignment = null;
            if (localAlignment is { } a)
            {
                var wa = ToWorld(a, frame);
                alignment = [wa.X, wa.Y, wa.Z];
            }

            results.Add(new
            {
                text,
                rawText = raw is not null && raw != text ? raw : null,
                type,
                tag,
                handle = source.Handle.ToString(),
                layer = source.Layer,
                effectiveLayer,
                style,
                position = new[] { position.X, position.Y, position.Z },
                alignmentPoint = alignment,
                center = worldBox is { Length: 4 } wb ? new[] { (wb[0] + wb[2]) / 2, (wb[1] + wb[3]) / 2 } : null,
                bbox = worldBox,
                height = Math.Round(height, 9),
                rotationDeg,
                mirrored = mirrored ? true : (bool?)null,
                invisible = invisible ? true : (bool?)null,
                depth = frame.Depth,
                insertPath = frame.Path.Count > 0 ? BlockWalk.InsertPathLabel(frame.Path) : null,
                blockPath = frame.Path.Count > 0 ? frame.Path.Select(p => p.BlockName).ToArray() : null
            });
            countsByType[type] = countsByType.TryGetValue(type, out var n) ? n + 1 : 1;
            return results.Count < maxResults;
        }

        WalkBlockSpace(tr, space, options, stats, (entity, _, frame) =>
        {
            switch (entity)
            {
                case AttributeDefinition ad when (kinds & TextKinds.AttDef) != 0:
                    // Inside a block only constant attributes display their own text; non-constant
                    // definitions are templates whose values appear as ATTRIB on each INSERT.
                    if (frame.Depth > 0 && !ad.Constant)
                    {
                        return true;
                    }

                    return Emit(ad.Constant ? "ATTDEF_CONSTANT" : "ATTDEF", ad, ad.TextString ?? "", null, ad.Tag, ad.Position, AlignmentOrNull(ad), ad.Rotation, ad.Height, ad.TextStyleName, ad.Invisible, frame, WorldBox(ad, frame));
                case AttributeDefinition:
                    return true;
                case DBText dt when (kinds & TextKinds.Text) != 0:
                    return Emit("TEXT", dt, CadSpatialMath.StripMTextFormatting(dt.TextString), dt.TextString, null, dt.Position, AlignmentOrNull(dt), dt.Rotation, dt.Height, dt.TextStyleName, false, frame, WorldBox(dt, frame));
                case MText mt when (kinds & TextKinds.MText) != 0:
                {
                    var raw = mt.Contents ?? "";
                    var plain = !string.IsNullOrEmpty(mt.Text) ? mt.Text : CadSpatialMath.StripMTextFormatting(raw);
                    return Emit("MTEXT", mt, plain, raw, null, mt.Location, null, mt.Rotation, mt.TextHeight, mt.TextStyleName, false, frame, WorldBox(mt, frame));
                }
                case MLeader ml when (kinds & TextKinds.MLeader) != 0:
                {
                    if (ml.ContentType != ContentType.MTextContent)
                    {
                        return true;
                    }

                    using var mt = ml.MText;
                    if (mt is null || string.IsNullOrEmpty(mt.Contents))
                    {
                        return true;
                    }

                    var plain = !string.IsNullOrEmpty(mt.Text) ? mt.Text : CadSpatialMath.StripMTextFormatting(mt.Contents);
                    return Emit("MLEADER", ml, plain, mt.Contents, null, mt.Location, null, mt.Rotation, mt.TextHeight, mt.TextStyleName, false, frame, WorldBox(ml, frame));
                }
                case BlockReference br when (kinds & TextKinds.Attrib) != 0:
                {
                    foreach (ObjectId attId in br.AttributeCollection)
                    {
                        if (attId.IsErased || tr.GetObject(attId, OpenMode.ForRead) is not AttributeReference ar)
                        {
                            continue;
                        }

                        string text, raw;
                        if (ar.IsMTextAttribute && ar.MTextAttribute is { } mta)
                        {
                            using (mta)
                            {
                                raw = mta.Contents ?? "";
                                text = !string.IsNullOrEmpty(mta.Text) ? mta.Text : CadSpatialMath.StripMTextFormatting(raw);
                            }
                        }
                        else
                        {
                            raw = ar.TextString ?? "";
                            text = CadSpatialMath.StripMTextFormatting(raw);
                        }

                        // Attribute references live in the INSERT's owner space: same frame as the INSERT. Their
                        // layer-0 fallback is the INSERT's effective layer, not the frame's (D6).
                        var attribLayer = BlockWalk.AttributeEffectiveLayer(ar.Layer, br.Layer, frame.ParentEffectiveLayer);
                        if (!Emit("ATTRIB", ar, text, raw, ar.Tag, ar.Position, AlignmentOrNull(ar), ar.Rotation, ar.Height, ar.TextStyleName, ar.Invisible, frame, WorldBox(ar, frame), attribLayer))
                        {
                            return false;
                        }
                    }

                    return true;
                }
                default:
                    return true;
            }
        });

        tr.Commit();
        return ForgeResult.Success(command.Id, new
        {
            layout = spaceName,
            count = results.Count,
            truncated = stats.Truncated,
            maxResults,
            countsByType,
            blockTraversal = BlockTraversalReport(options, stats),
            texts = results
        });
    }

    private static Point3d? AlignmentOrNull(DBText text)
        => text.Justify == AttachmentPoint.BaseLeft ? null : text.AlignmentPoint;

    private static BlockTableRecord? ResolveSpace(Transaction tr, Database db, string layoutName)
    {
        if (string.Equals(layoutName, "Model", StringComparison.OrdinalIgnoreCase))
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            return (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        }

        var layoutDict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry entry in layoutDict)
        {
            if (string.Equals(entry.Key, layoutName, StringComparison.OrdinalIgnoreCase) &&
                tr.GetObject(entry.Value, OpenMode.ForRead) is Layout layout)
            {
                return (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
            }
        }

        return null;
    }
}
