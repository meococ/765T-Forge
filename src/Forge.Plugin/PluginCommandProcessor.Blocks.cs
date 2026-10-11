using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Forge.Shared;

namespace Forge.Plugin;

// docs/cad-authoring-tools.md C22–C23 — forge_block_create / forge_block_insert / forge_block_list,
// forge_layer_create and forge_layout_create. Two orderings are load-bearing here: block
// definitions consume their source geometry as clone → transform by −basePoint → erase the
// originals last (entities cannot be re-parented, so the sources must outlive every clone), and
// attribute values are applied only after SetAttributesFromBlock, which resets them.
public sealed partial class PluginCommandProcessor
{
    // ===================== C22 — blocks =====================

    private static ForgeResult BlockCreate(ForgeCommand command)
    {
        var args = Args<BlockCreateArgs>(command);
        if (!IsValidBlockName(args.Name))
        {
            return ForgeResult.Failure(
                command.Id,
                "invalid_block_name",
                "Block name is required and must not contain < > / \\ \" : ; ? * | = ` (max 255 characters).");
        }

        Point3d basePoint;
        try
        {
            basePoint = ToPoint3d(args.BasePoint);
        }
        catch (ArgumentException ex)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", $"basePoint: {ex.Message}");
        }

        if (args.Handles is null || args.Handles.Length is 0 or > AuthoringMaxEntities)
        {
            return ForgeResult.Failure(
                command.Id,
                "missing_handles",
                $"handles must list between 1 and {AuthoringMaxEntities} entity handles to consume into the definition.");
        }

        var attributeTags = new List<string>();
        var attributeDefs = new List<(string Tag, string? Prompt, double Height, Point3d Position, bool Invisible)>();
        foreach (var def in args.AttributeDefs ?? [])
        {
            var tag = def.Tag?.Trim().ToUpperInvariant() ?? "";
            if (tag.Length == 0)
            {
                return ForgeResult.Failure(command.Id, "invalid_attr_tag", "Each attribute definition requires a non-empty tag.");
            }

            if (def.Height <= 0)
            {
                return ForgeResult.Failure(command.Id, "invalid_height", $"Attribute '{tag}' requires height > 0.");
            }

            Point3d position;
            try
            {
                position = ToPoint3d(def.Position);
            }
            catch (ArgumentException ex)
            {
                return ForgeResult.Failure(command.Id, "invalid_args", $"Attribute '{tag}' position: {ex.Message}");
            }

            attributeTags.Add(tag);
            attributeDefs.Add((tag, def.Prompt, def.Height, position, def.Invisible));
        }

        if (command.DryRun)
        {
            return DryRun(command, new
            {
                name = args.Name,
                args.BasePoint,
                entityCount = args.Handles.Length,
                attributeTags = attributeTags.ToArray()
            });
        }

        var db = ActiveDb;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (blockTable.Has(args.Name))
            {
                return ForgeResult.Failure(
                    command.Id,
                    "block_exists",
                    $"Block definition already exists: {args.Name}",
                    "Pick another name; forge_block_list shows the definitions already in the drawing.");
            }

            blockTable.UpgradeOpen();
            var btr = new BlockTableRecord { Name = args.Name, Origin = basePoint };
            blockTable.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            // Entities cannot be re-parented between owners: clone into the definition, shift the
            // geometry so the base point lands on the block origin, and erase the originals only
            // after every clone is in place — all inside this one transaction.
            var unknownHandles = new List<string>();
            var sources = OpenHandlesForWrite(db, tr, args.Handles, unknownHandles)
                .DistinctBy(source => source.Id) // a duplicated handle must not clone or erase twice
                .ToList();
            foreach (var source in sources)
            {
                var clone = (Entity)source.Clone();
                clone.TransformBy(Matrix3d.Displacement(Point3d.Origin - basePoint));
                btr.AppendEntity(clone);
                tr.AddNewlyCreatedDBObject(clone, true);
            }

            foreach (var source in sources)
            {
                source.Erase();
            }

            foreach (var def in attributeDefs)
            {
                var attribute = new AttributeDefinition
                {
                    Position = def.Position,
                    Height = def.Height,
                    Tag = def.Tag,
                    Prompt = string.IsNullOrWhiteSpace(def.Prompt) ? def.Tag : def.Prompt,
                    Invisible = def.Invisible,
                    TextString = ""
                };
                btr.AppendEntity(attribute);
                tr.AddNewlyCreatedDBObject(attribute, true);
            }

            var data = new
            {
                name = args.Name,
                blockHandle = HandleString(btr),
                consumed = sources.Count,
                unknownHandles = unknownHandles.ToArray(),
                attributeTags = attributeTags.ToArray()
            };
            var verification = new ForgeVerification
            {
                Attempted = true,
                Passed = true,
                Message = "Block definition created; source entities consumed in the same transaction.",
                ReadBack = new { data.blockHandle, data.consumed, data.attributeTags }
            };
            tr.Commit();
            return ForgeResult.Success(command.Id, data, verification: verification);
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(command.Id, "block_create_failed", $"forge_block_create failed: {ex.Message}");
        }
    }

    private static ForgeResult BlockInsert(ForgeCommand command)
    {
        var args = Args<BlockInsertArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Name))
        {
            return ForgeResult.Failure(command.Id, "missing_block_name", "Block name is required.");
        }

        Point3d position;
        try
        {
            position = ToPoint3d(args.Position);
        }
        catch (ArgumentException ex)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", $"position: {ex.Message}");
        }

        if (args.Scale <= 0)
        {
            return ForgeResult.Failure(command.Id, "invalid_scale", "scale must be > 0.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new
            {
                args.Name,
                args.Position,
                scale = args.Scale,
                rotationDeg = args.RotationDeg,
                args.Layer,
                args.Attributes
            });
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        if (!blockTable.Has(args.Name))
        {
            return ForgeResult.Failure(
                command.Id,
                "block_not_found",
                $"Block definition not found: {args.Name}",
                "Call forge_block_list for the definitions in this drawing, or create one with forge_block_create.");
        }

        var btrId = blockTable[args.Name];
        var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
        var bref = new BlockReference(position, btrId);
        AppendToModelSpace(db, tr, bref);

        // Scale and rotation apply after the reference is resident; attribute values only after
        // SetAttributesFromBlock — that call creates the references and resets their values.
        bref.ScaleFactors = new Scale3d(args.Scale);
        bref.Rotation = DegreesToRadians(args.RotationDeg);

        if (!string.IsNullOrWhiteSpace(args.Layer))
        {
            if (!LayerExists(db, tr, args.Layer))
            {
                return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}", "Create it with forge_layer_create first.");
            }

            bref.Layer = args.Layer;
        }

        var definitions = new List<AttributeDefinition>();
        foreach (ObjectId id in btr)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition ad && !ad.Constant)
            {
                definitions.Add(ad);
            }
        }

        var applied = 0;
        var attributeEcho = new List<object>();
        var unmatchedTags = new List<string>();
        var supplied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (args.Attributes is { Count: > 0 })
        {
            foreach (var pair in args.Attributes)
            {
                supplied[pair.Key] = pair.Value;
            }
        }

        foreach (var definition in definitions)
        {
            // One AttributeReference per non-constant definition, bound to the block transform;
            // supplied values are set before the reference becomes DB-owned (AppendAttribute +
            // AddNewlyCreatedDBObject) — after that the database owns it and must not be disposed.
            var reference = new AttributeReference();
            reference.SetAttributeFromBlock(definition, bref.BlockTransform);
            if (supplied.TryGetValue(definition.Tag, out var value))
            {
                reference.TextString = value;
                applied++;
                attributeEcho.Add(new { tag = definition.Tag, value = reference.TextString });
                supplied.Remove(definition.Tag);
            }

            bref.AttributeCollection.AppendAttribute(reference);
            tr.AddNewlyCreatedDBObject(reference, true);
        }

        // Whatever no AttributeReference claimed has no matching non-constant definition.
        unmatchedTags.AddRange(supplied.Keys);

        var data = new
        {
            handle = HandleString(bref),
            name = args.Name,
            appliedAttributes = applied,
            unmatchedTags = unmatchedTags.ToArray(),
            readBack = attributeEcho.ToArray()
        };
        var verification = new ForgeVerification
        {
            Attempted = true,
            Passed = true,
            Message = "Block reference read back after insert.",
            ReadBack = EntityReadBackWithAttributes(EntitySummary(bref), attributeEcho.ToArray())
        };
        tr.Commit();
        return ForgeResult.Success(command.Id, data, verification: verification);
    }

    private static ForgeResult BlockList(ForgeCommand command)
    {
        // Read-only listing; the tool takes no parameters.
        var blocks = new List<object>();
        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId id in blockTable)
        {
            var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (btr.IsLayout || btr.IsAnonymous || btr.IsFromExternalReference || btr.IsFromOverlayReference)
            {
                continue;
            }

            var attributeTags = new List<string>();
            foreach (ObjectId entityId in btr)
            {
                if (tr.GetObject(entityId, OpenMode.ForRead) is AttributeDefinition ad && !ad.Constant)
                {
                    attributeTags.Add(ad.Tag);
                }
            }

            blocks.Add(new
            {
                name = btr.Name,
                handle = HandleString(btr),
                origin = btr.Origin,
                attributeTags = attributeTags.ToArray()
            });
        }

        tr.Commit();
        return ForgeResult.Success(command.Id, new { count = blocks.Count, blocks = blocks.ToArray() });
    }

    // ===================== C23 — layers & layouts =====================

    private static ForgeResult LayerCreate(ForgeCommand command)
    {
        var args = Args<LayerCreateArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Name))
        {
            return ForgeResult.Failure(command.Id, "missing_layer_name", "Layer name is required.");
        }

        // Layer names follow the same character rules as block names; the shared validator's
        // 255-character cap is accepted as part of that rule.
        if (!IsValidBlockName(args.Name))
        {
            return ForgeResult.Failure(
                command.Id,
                "invalid_layer_name",
                $"Layer name is invalid: {args.Name}",
                "Names must not contain < > / \\ \" : ; ? * | = ` and must be at most 255 characters.");
        }

        if (args.ColorIndex is < 0 or > 256)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", "colorIndex must be 0 (ByBlock), 1–255, or 256 (ByLayer).");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { args.Name, colorIndex = args.ColorIndex ?? 7, args.Linetype, args.Plottable });
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        if (!string.IsNullOrWhiteSpace(args.Linetype) && !LinetypeExists(db, tr, args.Linetype))
        {
            return ForgeResult.Failure(
                command.Id,
                "linetype_not_found",
                $"Linetype not found: {args.Linetype}",
                "Only linetypes already present in the drawing (or loaded from a LIN file) can be assigned.");
        }

        var layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (layerTable.Has(args.Name))
        {
            // Idempotent metadata: an existing layer is a no-op success, no mutation, no backup.
            return ForgeResult.Success(command.Id, new { name = args.Name, alreadyExists = true });
        }

        layerTable.UpgradeOpen();
        var colorIndex = args.ColorIndex ?? 7;
        var ltr = new LayerTableRecord { Name = args.Name, IsPlottable = args.Plottable };
        // LayerTableRecord is a SymbolTableRecord, not an Entity — the shared ApplyColor helper
        // does not apply, the color index is assigned directly.
        ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)colorIndex);
        layerTable.Add(ltr);
        tr.AddNewlyCreatedDBObject(ltr, true);
        if (!string.IsNullOrWhiteSpace(args.Linetype))
        {
            // LayerTableRecord stores the linetype as an ObjectId — resolve the name through
            // the LinetypeTable (its existence was checked above).
            var linetypeTable = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            ltr.LinetypeObjectId = linetypeTable[args.Linetype];
        }

        var data = new { name = ltr.Name, handle = HandleString(ltr), alreadyExists = false };
        var verification = new ForgeVerification
        {
            Attempted = true,
            Passed = true,
            ReadBack = new { name = ltr.Name, colorIndex = ltr.Color.ColorIndex, linetype = args.Linetype, plottable = ltr.IsPlottable }
        };
        tr.Commit();
        return ForgeResult.Success(command.Id, data, verification: verification);
    }

    private static ForgeResult LayoutCreate(ForgeCommand command)
    {
        var args = Args<LayoutCreateArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Name))
        {
            return ForgeResult.Failure(command.Id, "missing_layout_name", "Layout name is required.");
        }

        if (RejectControlChars(command, ("name", args.Name)) is { } reject)
        {
            return reject;
        }

        if (command.DryRun)
        {
            return DryRun(command, new { args.Name });
        }

        var db = ActiveDb;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var layoutDict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            if (layoutDict.Contains(args.Name))
            {
                return ForgeResult.Failure(
                    command.Id,
                    "layout_not_unique",
                    $"Layout already exists: {args.Name}",
                    "List the existing tabs with forge_doc_list_layouts and pick another name.");
            }

            tr.Commit();
        }

        // LayoutManager opens its own transaction internally, so CreateLayout must run strictly
        // outside the transaction above.
        ObjectId layoutId;
        try
        {
            layoutId = LayoutManager.Current.CreateLayout(args.Name);
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(command.Id, "layout_create_failed", $"CreateLayout failed for '{args.Name}': {ex.Message}");
        }

        return ForgeResult.Success(
            command.Id,
            new { name = args.Name, layoutHandle = layoutId.Handle.ToString() },
            verification: new ForgeVerification
            {
                Attempted = true,
                Passed = !layoutId.IsNull,
                ReadBack = new { name = args.Name, layoutHandle = layoutId.Handle.ToString() }
            });
    }

    // EntitySummary returns an anonymous object, which C# cannot spread into another — the
    // verification read-back copies its properties and puts the attribute echo beside them.
    private static object EntityReadBackWithAttributes(object summary, object attributes)
    {
        var readBack = new Dictionary<string, object?>();
        foreach (var property in summary.GetType().GetProperties())
        {
            readBack[property.Name] = property.GetValue(summary);
        }

        readBack["attributes"] = attributes;
        return readBack;
    }

    private sealed record BlockCreateArgs
    {
        public string? Name { get; init; }
        public double[]? BasePoint { get; init; }
        public string[]? Handles { get; init; }
        public BlockAttributeDefArgs[]? AttributeDefs { get; init; }
    }

    private sealed record BlockAttributeDefArgs
    {
        public string? Tag { get; init; }
        public string? Prompt { get; init; }
        public double Height { get; init; }
        public double[]? Position { get; init; }
        public bool Invisible { get; init; }
    }

    private sealed record BlockInsertArgs
    {
        public string? Name { get; init; }
        public double[]? Position { get; init; }
        public double Scale { get; init; } = 1;
        public double RotationDeg { get; init; }
        public string? Layer { get; init; }
        public Dictionary<string, string>? Attributes { get; init; }
    }

    private sealed record LayerCreateArgs
    {
        public string? Name { get; init; }
        public int? ColorIndex { get; init; }
        public string? Linetype { get; init; }
        public bool Plottable { get; init; } = true;
    }

    private sealed record LayoutCreateArgs { public string? Name { get; init; } }
}
