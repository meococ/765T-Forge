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

            var shift = Point3d.Origin - basePoint;
            var unknownHandles = new List<string>();
            var lockedHandles = new List<string>();
            var sources = OpenHandlesForWrite(db, tr, args.Handles, unknownHandles, lockedHandles)
                .DistinctBy(source => source.Id) // a duplicated handle must not clone or erase twice
                .ToList();
            if (sources.Count == 0)
            {
                return ForgeResult.Failure(
                    command.Id,
                    lockedHandles.Count > 0 ? "layer_locked" : "handle_not_found",
                    lockedHandles.Count > 0 ? "All source entities are on locked layers." : "No valid layout entities found for the supplied handles.",
                    data: new { unknownHandles = unknownHandles.ToArray(), lockedHandles = lockedHandles.ToArray() });
            }

            blockTable.UpgradeOpen();
            // Entities and attribute definitions are shifted by -basePoint into block-local space,
            // so the BlockTableRecord origin itself must remain at Point3d.Origin (avoiding double shift).
            var btr = new BlockTableRecord { Name = args.Name, Origin = Point3d.Origin };
            blockTable.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            // Deep-clone each source entity into the block definition (preserving Polyline2d vertices
            // and nested BlockReference attributes), shift into block-local space, and erase the
            // originals only after every clone is in place — all inside this one transaction.
            foreach (var source in sources)
            {
                var clone = CloneEntityToOwner(db, tr, source, btr.ObjectId);
                clone.TransformBy(Matrix3d.Displacement(shift));
            }

            foreach (var source in sources)
            {
                source.Erase();
            }

            foreach (var def in attributeDefs)
            {
                var attribute = new AttributeDefinition
                {
                    Position = def.Position + shift,
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

        if (args.Attributes is { Count: > 0 })
        {
            foreach (var pair in args.Attributes)
            {
                if (AuthorizeAttributeWrite(command, pair.Key, pair.Value) is { } denied)
                {
                    return denied;
                }
            }
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

    // ===================== C27 — styles & standards (linetype, text style, dim style) =====================
    //
    // Style tools never silently overwrite: an existing style is refused with `style_exists`
    // unless updateExisting=true, and an update writes ONLY the fields the caller supplied
    // (null = keep). Defaults apply to NEW styles only. Read-back comes from the record after
    // the write, together with created/updated and the list of fields that actually changed.

    private const string DefaultLinFile = "acadiso.lin";
    private const string FallbackLinFile = "acad.lin";
    private const string DefaultTextStyleFont = "Arial"; // TrueType family — full Unicode (Vietnamese) coverage
    private const int MaxLinetypesPerCall = 100;

    private static T? NewOnly<T>(bool exists, T value) where T : struct => exists ? (T?)null : value;

    private static ForgeResult LinetypeLoad(ForgeCommand command)
    {
        var args = Args<LinetypeLoadArgs>(command);
        var requested = new List<string>();
        foreach (var raw in new[] { args.Name }.Concat(args.Names ?? Array.Empty<string>()))
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var trimmed = raw.Trim();
            if (!requested.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                requested.Add(trimmed);
            }
        }

        if (requested.Count == 0)
        {
            return ForgeResult.Failure(command.Id, "missing_linetype_name", "Linetype name is required (name or names[]).");
        }

        if (requested.Count > MaxLinetypesPerCall)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", $"At most {MaxLinetypesPerCall} linetypes per call.");
        }

        foreach (var name in requested)
        {
            if (RejectControlChars(command, ("name", name)) is { } rejectName)
            {
                return rejectName;
            }

            // LoadLineTypeFile treats * and ? as wildcards — refuse them so one call cannot pull in a whole LIN file.
            if (name.IndexOfAny(['*', '?']) >= 0)
            {
                return ForgeResult.Failure(command.Id, "invalid_linetype_name", $"Wildcards are not allowed in linetype names: {name}", "Pass explicit names, e.g. names=[\"DASHED\",\"CENTER\"].");
            }
        }

        if (RejectControlChars(command, ("file", args.File)) is { } reject)
        {
            return reject;
        }

        var fileSupplied = !string.IsNullOrWhiteSpace(args.File);
        if (fileSupplied && !args.File!.Trim().EndsWith(".lin", StringComparison.OrdinalIgnoreCase))
        {
            return ForgeResult.Failure(command.Id, "invalid_args", $"file must be a .lin file: {args.File}");
        }

        // Default search order acadiso.lin → acad.lin; an explicit file is used alone.
        var candidates = fileSupplied ? new[] { args.File!.Trim() } : new[] { DefaultLinFile, FallbackLinFile };
        var db = ActiveDb;
        var resolved = new List<(string Requested, string Path)>();
        foreach (var candidate in candidates)
        {
            var path = TryFindSupportFile(db, candidate);
            if (path is not null)
            {
                resolved.Add((candidate, path));
            }
        }

        if (resolved.Count == 0)
        {
            return ForgeResult.Failure(
                command.Id,
                "lin_file_not_found",
                $"LIN file not found on the AutoCAD support path: {string.Join(", ", candidates)}",
                "Pass a full path, or a file name on the Support File Search Path (acadiso.lin / acad.lin).");
        }

        var alreadyLoaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            foreach (var name in requested.Where(n => LinetypeExists(db, tr, n)))
            {
                alreadyLoaded.Add(name);
            }

            tr.Commit();
        }

        if (command.DryRun)
        {
            return DryRun(command, new
            {
                tool = "forge_linetype_load",
                names = requested,
                file = resolved[0].Requested,
                searchOrder = resolved.Select(r => r.Path).ToArray(),
                alreadyLoaded = requested.Where(alreadyLoaded.Contains).ToArray(),
                toLoad = requested.Where(n => !alreadyLoaded.Contains(n)).ToArray()
            });
        }

        var results = new List<object>();
        var notFound = new List<string>();
        var loadErrors = new List<string>();
        foreach (var name in requested)
        {
            if (alreadyLoaded.Contains(name))
            {
                // Already in the drawing: never reloaded (reloading would redefine the pattern).
                results.Add(new { name, alreadyLoaded = true, loaded = true, file = (string?)null });
                continue;
            }

            string? loadedFrom = null;
            foreach (var (requestedFile, path) in resolved)
            {
                try
                {
                    db.LoadLineTypeFile(name, path);
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    // eUndefinedLineType = the name is not defined in this LIN file → try the next file.
                    if (!string.Equals(ex.ErrorStatus.ToString(), "UndefinedLineType", StringComparison.OrdinalIgnoreCase))
                    {
                        loadErrors.Add($"{name} ({requestedFile}): {ex.ErrorStatus}");
                    }

                    continue;
                }
                catch (System.Exception ex)
                {
                    loadErrors.Add($"{name} ({requestedFile}): {ex.Message}");
                    continue;
                }

                // Some releases return silently when the name is absent — confirm against the table.
                if (LinetypeExistsNow(db, name))
                {
                    loadedFrom = requestedFile;
                    break;
                }
            }

            if (loadedFrom is null)
            {
                notFound.Add(name);
            }

            results.Add(new { name, alreadyLoaded = false, loaded = loadedFrom is not null, file = loadedFrom });
        }

        var loadedLinetypes = ListLinetypeNames(db);
        var data = new
        {
            name = requested[0],
            names = requested,
            alreadyLoaded = requested.All(alreadyLoaded.Contains),
            file = resolved[0].Requested,
            results,
            loadedLinetypes
        };

        if (notFound.Count > 0)
        {
            return loadErrors.Count > 0
                ? ForgeResult.Failure(
                    command.Id,
                    "linetype_load_failed",
                    $"Could not load: {string.Join(", ", notFound)}. Errors: {string.Join("; ", loadErrors)}",
                    "Check the LIN file and linetype names.",
                    data: data)
                : ForgeResult.Failure(
                    command.Id,
                    "linetype_not_found_in_file",
                    $"Linetype(s) not defined in {string.Join(" / ", resolved.Select(r => r.Requested))}: {string.Join(", ", notFound)}",
                    "Use standard names like DASHED, CENTER, HIDDEN, PHANTOM, DOT, DASHDOT, DASHED2, CENTER2, or pass file=<custom.lin>.",
                    data: data);
        }

        return ForgeResult.Success(
            command.Id,
            data,
            verification: new ForgeVerification
            {
                Attempted = true,
                Passed = requested.All(n => loadedLinetypes.Contains(n, StringComparer.OrdinalIgnoreCase)),
                ReadBack = new { names = requested, loadedLinetypes }
            });
    }

    private static string? TryFindSupportFile(Database db, string file)
    {
        if (System.IO.Path.IsPathRooted(file))
        {
            return System.IO.File.Exists(file) ? file : null;
        }

        try
        {
            var found = HostApplicationServices.Current.FindFile(file, db, FindFileHint.Default);
            return string.IsNullOrWhiteSpace(found) ? null : found;
        }
        catch
        {
            // FindFile throws eFileNotFound when the file is not on the support path.
            return null;
        }
    }

    private static bool LinetypeExistsNow(Database db, string name)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var exists = LinetypeExists(db, tr, name);
        tr.Commit();
        return exists;
    }

    private static string[] ListLinetypeNames(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        var names = new List<string>();
        foreach (ObjectId id in table)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is LinetypeTableRecord record)
            {
                names.Add(record.Name);
            }
        }

        tr.Commit();
        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? StyleArg(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ForgeResult StyleTextCreate(ForgeCommand command)
    {
        var args = Args<StyleTextCreateArgs>(command);
        if (!IsValidBlockName(args.Name))
        {
            return ForgeResult.Failure(command.Id, "invalid_style_name", "Valid style name is required (no < > / \\ \" : ; ? * | = `, max 255 characters).");
        }

        var name = args.Name!.Trim();
        var fontInput = StyleArg(args.FontName) ?? StyleArg(args.FontFile);
        var bigFont = StyleArg(args.BigFont);
        var textSize = args.TextSize ?? args.Height;
        var makeCurrent = args.MakeCurrent || args.SetCurrent;

        if (RejectControlChars(command, ("name", name), ("fontName", fontInput), ("bigFont", bigFont)) is { } reject)
        {
            return reject;
        }

        if (textSize is { } size && (!double.IsFinite(size) || size < 0))
        {
            return ForgeResult.Failure(command.Id, "invalid_args", "textSize must be >= 0 (0 = variable height).");
        }

        if (args.WidthFactor is { } width && (!double.IsFinite(width) || width <= 0 || width > 100))
        {
            return ForgeResult.Failure(command.Id, "invalid_args", "widthFactor must be > 0 (and <= 100).");
        }

        if (args.ObliqueAngleDeg is { } oblique && (!double.IsFinite(oblique) || Math.Abs(oblique) > 85))
        {
            return ForgeResult.Failure(command.Id, "invalid_args", "obliqueAngleDeg must be within -85..85 degrees.");
        }

        if (bigFont is not null && !bigFont.EndsWith(".shx", StringComparison.OrdinalIgnoreCase))
        {
            return ForgeResult.Failure(command.Id, "invalid_args", "bigFont must be an .shx file (e.g. bigfont.shx).");
        }

        if (bigFont is not null && fontInput is not null && !fontInput.EndsWith(".shx", StringComparison.OrdinalIgnoreCase))
        {
            return ForgeResult.Failure(command.Id, "invalid_args", "bigFont only applies when fontName is an .shx file.");
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var textStyleTable = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        var exists = textStyleTable.Has(name);
        if (exists && !args.UpdateExisting)
        {
            return ForgeResult.Failure(
                command.Id,
                "style_exists",
                $"Text style '{name}' already exists; it was not modified.",
                "Pass updateExisting=true to change it (only the fields you supply are written), or choose a new name.");
        }

        // Defaults for a NEW style: Arial (TrueType family), variable height, width 1, no oblique.
        var font = fontInput ?? (exists ? null : DefaultTextStyleFont);
        var newSize = textSize ?? NewOnly(exists, 0.0);
        var newWidth = args.WidthFactor ?? NewOnly(exists, 1.0);
        var newOblique = args.ObliqueAngleDeg ?? NewOnly(exists, 0.0);
        if (command.DryRun)
        {
            return DryRun(command, new
            {
                tool = "forge_style_text_create",
                name,
                action = exists ? "update" : "create",
                fontName = font,
                fontKind = font is null ? null : TextStyleFontKind(font),
                bigFont,
                textSize = newSize,
                widthFactor = newWidth,
                obliqueAngleDeg = newOblique,
                makeCurrent
            });
        }

        TextStyleTableRecord record;
        Dictionary<string, object?>? before = null;
        if (exists)
        {
            record = (TextStyleTableRecord)tr.GetObject(textStyleTable[name], OpenMode.ForWrite);
            before = TextStyleSnapshot(db, record);
            if (bigFont is not null && fontInput is null && !(record.FileName ?? "").EndsWith(".shx", StringComparison.OrdinalIgnoreCase))
            {
                return ForgeResult.Failure(command.Id, "invalid_args", $"Text style '{name}' does not use an .shx font; bigFont needs an .shx fontName.");
            }
        }
        else
        {
            textStyleTable.UpgradeOpen();
            record = new TextStyleTableRecord { Name = name };
            textStyleTable.Add(record);
            tr.AddNewlyCreatedDBObject(record, true);
        }

        if (font is not null)
        {
            ApplyTextStyleFont(record, font, bigFont);
        }
        else if (bigFont is not null)
        {
            record.BigFontFileName = bigFont;
        }

        if (newSize is { } sizeToWrite)
        {
            record.TextSize = sizeToWrite;
        }

        if (newWidth is { } widthToWrite)
        {
            record.XScale = widthToWrite;
        }

        if (newOblique is { } obliqueToWrite)
        {
            record.ObliquingAngle = DegreesToRadians(obliqueToWrite);
        }

        if (makeCurrent)
        {
            db.Textstyle = record.ObjectId;
        }

        var after = TextStyleSnapshot(db, record);
        var changedFields = ChangedFields(before, after);
        var warnings = new List<string>();
        foreach (var file in new[] { font, bigFont })
        {
            if (file is not null
                && (file.EndsWith(".shx", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
                && TryFindSupportFile(db, file) is null)
            {
                warnings.Add($"Font file '{file}' was not found on the support path; AutoCAD will substitute a font.");
            }
        }

        var readBack = new
        {
            name = record.Name,
            handle = HandleString(record),
            created = !exists,
            updated = exists && changedFields.Length > 0,
            changedFields,
            fontName = after["fontName"],
            fontFile = after["fontFile"],
            bigFont = after["bigFont"],
            textSize = after["textSize"],
            widthFactor = after["widthFactor"],
            obliqueAngleDeg = after["obliqueAngleDeg"],
            isCurrent = after["isCurrent"],
            warnings
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            readBack,
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static string TextStyleFontKind(string font)
    {
        if (font.EndsWith(".shx", StringComparison.OrdinalIgnoreCase))
        {
            return "shx";
        }

        return font.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ? "ttf_file" : "truetype_family";
    }

    // .shx → FileName (+ optional BigFontFileName); .ttf → FileName; anything else is a TrueType
    // family name set through FontDescriptor (FileName = "Arial" would be an invalid SHX lookup).
    private static void ApplyTextStyleFont(TextStyleTableRecord record, string font, string? bigFont)
    {
        var emptyTypeface = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor(string.Empty, false, false, 0, 0);
        switch (TextStyleFontKind(font))
        {
            case "shx":
                record.Font = emptyTypeface; // clear any TrueType typeface so the SHX file is used
                record.FileName = font;
                if (bigFont is not null)
                {
                    record.BigFontFileName = bigFont;
                }

                break;
            case "ttf_file":
                record.Font = emptyTypeface;
                record.FileName = font;
                record.BigFontFileName = string.Empty;
                break;
            default:
                try
                {
                    record.FileName = string.Empty;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    // Best effort: the TrueType typeface below takes precedence over FileName.
                }

                record.BigFontFileName = string.Empty;
                record.Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor(font, false, false, 0, 0);
                break;
        }
    }

    private static Dictionary<string, object?> TextStyleSnapshot(Database db, TextStyleTableRecord record)
    {
        var typeface = record.Font.TypeFace;
        return new Dictionary<string, object?>
        {
            ["fontName"] = string.IsNullOrEmpty(typeface) ? record.FileName : typeface,
            ["fontFile"] = record.FileName,
            ["bigFont"] = record.BigFontFileName,
            ["textSize"] = record.TextSize,
            ["widthFactor"] = record.XScale,
            ["obliqueAngleDeg"] = Math.Round(record.ObliquingAngle * 180.0 / Math.PI, 9),
            ["isCurrent"] = db.Textstyle == record.ObjectId
        };
    }

    /// <summary>All snapshot keys for a new record; otherwise only keys whose value changed.</summary>
    private static string[] ChangedFields(Dictionary<string, object?>? before, Dictionary<string, object?> after)
    {
        return before is null
            ? after.Keys.ToArray()
            : after.Where(kv => !Equals(before.TryGetValue(kv.Key, out var old) ? old : null, kv.Value)).Select(kv => kv.Key).ToArray();
    }

    private static ForgeResult StyleDimCreate(ForgeCommand command)
    {
        var args = Args<StyleDimCreateArgs>(command);
        if (!IsValidBlockName(args.Name))
        {
            return ForgeResult.Failure(command.Id, "invalid_style_name", "Valid dimension style name is required (no < > / \\ \" : ; ? * | = `, max 255 characters).");
        }

        var name = args.Name!.Trim();
        var textStyle = StyleArg(args.TextStyle);
        var makeCurrent = args.MakeCurrent || args.SetCurrent;
        if (RejectControlChars(command, ("name", name), ("textStyle", textStyle)) is { } reject)
        {
            return reject;
        }

        foreach (var (field, value, allowZero) in new (string, double?, bool)[]
                 {
                     ("textHeight", args.TextHeight, false),
                     ("arrowSize", args.ArrowSize, true),
                     ("dimScale", args.DimScale, false),
                     ("extensionOffset", args.ExtensionOffset, true),
                     ("extensionExtend", args.ExtensionExtend, true),
                     ("textGap", args.TextGap, true),
                     ("linearScaleFactor", args.LinearScaleFactor, false)
                 })
        {
            if (value is { } v && (!double.IsFinite(v) || v < 0 || (!allowZero && v == 0)))
            {
                return ForgeResult.Failure(command.Id, "invalid_args", allowZero ? $"{field} must be >= 0." : $"{field} must be > 0.");
            }
        }

        if (args.Precision is < 0 or > 8)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", "precision (DIMDEC) must be 0..8.");
        }

        if (args.ColorIndex is < 0 or > 256)
        {
            return ForgeResult.Failure(command.Id, "invalid_args", "colorIndex must be 0 (ByBlock), 1–255, or 256 (ByLayer).");
        }

        var db = ActiveDb;
        using var tr = db.TransactionManager.StartTransaction();
        var dimStyleTable = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
        var exists = dimStyleTable.Has(name);
        if (exists && !args.UpdateExisting)
        {
            return ForgeResult.Failure(
                command.Id,
                "style_exists",
                $"Dimension style '{name}' already exists; it was not modified.",
                "Pass updateExisting=true to change it (only the fields you supply are written), or choose a new name.");
        }

        var textStyleTable = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        if (textStyle is not null && !textStyleTable.Has(textStyle))
        {
            return ForgeResult.Failure(command.Id, "textstyle_not_found", $"Text style not found: {textStyle}", "Create it first with forge_style_text_create.");
        }

        // Metric (mm) defaults for NEW styles only — an update writes just the supplied fields.
        var textHeight = args.TextHeight ?? NewOnly(exists, 2.5);
        var arrowSize = args.ArrowSize ?? NewOnly(exists, 2.5);
        var dimScale = args.DimScale ?? NewOnly(exists, 1.0);
        var extensionOffset = args.ExtensionOffset ?? NewOnly(exists, 0.625);
        var extensionExtend = args.ExtensionExtend ?? NewOnly(exists, 1.25);
        var textGap = args.TextGap ?? NewOnly(exists, 0.625);
        var precision = args.Precision ?? NewOnly(exists, 0);
        var linearScaleFactor = args.LinearScaleFactor ?? NewOnly(exists, 1.0);
        var textStyleToApply = textStyle ?? (exists ? null : (textStyleTable.Has("Standard") ? "Standard" : null));

        if (command.DryRun)
        {
            return DryRun(command, new
            {
                tool = "forge_style_dim_create",
                name,
                action = exists ? "update" : "create",
                textHeight,
                arrowSize,
                dimScale,
                extensionOffset,
                extensionExtend,
                textGap,
                precision,
                linearScaleFactor,
                textStyle = textStyleToApply,
                colorIndex = args.ColorIndex,
                makeCurrent
            });
        }

        DimStyleTableRecord record;
        Dictionary<string, object?>? before = null;
        if (exists)
        {
            record = (DimStyleTableRecord)tr.GetObject(dimStyleTable[name], OpenMode.ForWrite);
            before = DimStyleSnapshot(db, tr, record);
        }
        else
        {
            dimStyleTable.UpgradeOpen();
            record = new DimStyleTableRecord();
            record.CopyFrom(db.GetDimstyleData());
            record.Name = name;
            dimStyleTable.Add(record);
            tr.AddNewlyCreatedDBObject(record, true);
        }

        if (textHeight is { } dimtxt)
        {
            record.Dimtxt = dimtxt;
        }

        if (arrowSize is { } dimasz)
        {
            record.Dimasz = dimasz;
        }

        if (dimScale is { } dimscale)
        {
            record.Dimscale = dimscale;
        }

        if (extensionOffset is { } dimexo)
        {
            record.Dimexo = dimexo;
        }

        if (extensionExtend is { } dimexe)
        {
            record.Dimexe = dimexe;
        }

        if (textGap is { } dimgap)
        {
            record.Dimgap = dimgap;
        }

        if (precision is { } dimdec)
        {
            record.Dimdec = dimdec;
        }

        if (linearScaleFactor is { } dimlfac)
        {
            record.Dimlfac = dimlfac;
        }

        if (textStyleToApply is not null)
        {
            record.Dimtxsty = textStyleTable[textStyleToApply];
        }

        if (args.ColorIndex is { } colorIndex)
        {
            var color = Color.FromColorIndex(ColorMethod.ByAci, (short)colorIndex);
            record.Dimclrd = color;
            record.Dimclre = color;
            record.Dimclrt = color;
        }

        if (makeCurrent)
        {
            db.Dimstyle = record.ObjectId;
        }

        if (db.Dimstyle == record.ObjectId)
        {
            // The header DIM* variables are a copy of the current style — refresh them so an
            // update of the current style (or makeCurrent) is visible to new dimensions.
            db.SetDimstyleData(record);
        }

        var after = DimStyleSnapshot(db, tr, record);
        var changedFields = ChangedFields(before, after);
        var readBack = new
        {
            name = record.Name,
            handle = HandleString(record),
            created = !exists,
            updated = exists && changedFields.Length > 0,
            changedFields,
            textHeight = after["textHeight"],
            arrowSize = after["arrowSize"],
            dimScale = after["dimScale"],
            extensionOffset = after["extensionOffset"],
            extensionExtend = after["extensionExtend"],
            textGap = after["textGap"],
            precision = after["precision"],
            linearScaleFactor = after["linearScaleFactor"],
            textStyle = after["textStyle"],
            colorIndex = after["colorIndex"],
            isCurrent = after["isCurrent"]
        };
        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            readBack,
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static Dictionary<string, object?> DimStyleSnapshot(Database db, Transaction tr, DimStyleTableRecord record)
    {
        string? textStyleName = null;
        if (!record.Dimtxsty.IsNull && record.Dimtxsty.IsValid && !record.Dimtxsty.IsErased
            && tr.GetObject(record.Dimtxsty, OpenMode.ForRead) is TextStyleTableRecord textStyleRecord)
        {
            textStyleName = textStyleRecord.Name;
        }

        return new Dictionary<string, object?>
        {
            ["textHeight"] = record.Dimtxt,
            ["arrowSize"] = record.Dimasz,
            ["dimScale"] = record.Dimscale,
            ["extensionOffset"] = record.Dimexo,
            ["extensionExtend"] = record.Dimexe,
            ["textGap"] = record.Dimgap,
            ["precision"] = record.Dimdec,
            ["linearScaleFactor"] = record.Dimlfac,
            ["textStyle"] = textStyleName,
            ["colorIndex"] = (int)record.Dimclrt.ColorIndex,
            ["isCurrent"] = db.Dimstyle == record.ObjectId
        };
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

    private sealed record LinetypeLoadArgs
    {
        public string? Name { get; init; }
        public string[]? Names { get; init; }
        public string? File { get; init; }
    }

    // Every optional field is nullable: null = "not supplied" → default on create, unchanged on update.
    private sealed record StyleTextCreateArgs
    {
        public string? Name { get; init; }
        public string? FontName { get; init; }
        public string? FontFile { get; init; } // legacy alias of FontName
        public string? BigFont { get; init; }
        public double? TextSize { get; init; }
        public double? Height { get; init; } // legacy alias of TextSize
        public double? WidthFactor { get; init; }
        public double? ObliqueAngleDeg { get; init; }
        public bool MakeCurrent { get; init; }
        public bool SetCurrent { get; init; } // legacy alias of MakeCurrent
        public bool UpdateExisting { get; init; }
    }

    private sealed record StyleDimCreateArgs
    {
        public string? Name { get; init; }
        public double? TextHeight { get; init; }        // DIMTXT
        public double? ArrowSize { get; init; }         // DIMASZ
        public double? DimScale { get; init; }          // DIMSCALE
        public double? ExtensionOffset { get; init; }   // DIMEXO
        public double? ExtensionExtend { get; init; }   // DIMEXE
        public double? TextGap { get; init; }           // DIMGAP
        public int? Precision { get; init; }            // DIMDEC 0..8
        public double? LinearScaleFactor { get; init; } // DIMLFAC
        public string? TextStyle { get; init; }         // DIMTXSTY (must exist)
        public int? ColorIndex { get; init; }           // DIMCLRD / DIMCLRE / DIMCLRT
        public bool MakeCurrent { get; init; }
        public bool SetCurrent { get; init; } // legacy alias of MakeCurrent
        public bool UpdateExisting { get; init; }
    }
}
