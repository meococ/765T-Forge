using System.Diagnostics;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Forge.Shared;

namespace Forge.Plugin;

// forge_block_insert_bulk — N block references (diffusers, VAVs, fixtures …) in ONE call and ONE
// transaction, each with its own position/rotation/scale/layer/attributes and an optional
// MLEADER tag. Blocks come from the active drawing or, by name, from a library DWG (one side-
// database read + one WblockCloneObjects for every missing name). All-or-nothing: every item is
// validated (Forge.Shared.BlockBulkInsert + block/layer/style existence + registry attribute
// rules) before the first INSERT is created, so a bad row never leaves half a set behind.
// Per-call caches: model space opened once, layer/style ids looked up once, attribute
// definitions enumerated once per block — no per-item table walks.
public sealed partial class PluginCommandProcessor
{
    private sealed record BlockInsertBulkArgs
    {
        public string? BlockName { get; init; }
        public string? LibraryPath { get; init; }
        public bool Redefine { get; init; }
        public BulkInsertItem?[]? Items { get; init; }
        public double? Scale { get; init; }
        public double? RotationDeg { get; init; }
        public string? Layer { get; init; }
        public string? LeaderLayer { get; init; }
        public string? LeaderStyle { get; init; }
        public double? LeaderTextHeight { get; init; }
        public bool ReturnHandles { get; init; } = true;
    }

    private static ForgeResult BlockInsertBulk(ForgeCommand command)
    {
        var args = Args<BlockInsertBulkArgs>(command);
        if (RejectControlChars(command, ("blockName", args.BlockName), ("libraryPath", args.LibraryPath), ("layer", args.Layer), ("leaderLayer", args.LeaderLayer), ("leaderStyle", args.LeaderStyle)) is { } reject)
        {
            return reject;
        }

        var resolved = BlockBulkInsert.Resolve(args.Items, args.BlockName, args.Scale, args.RotationDeg, args.Layer, args.LeaderLayer, args.LeaderStyle, args.LeaderTextHeight, out var errors);
        if (errors.Count > 0)
        {
            return BulkFailure(command, errors);
        }

        string? libraryPath = null;
        if (!string.IsNullOrWhiteSpace(args.LibraryPath))
        {
            libraryPath = System.IO.Path.GetFullPath(args.LibraryPath!);
            if (DrawingImport.SourceKind(libraryPath) != "dwg" || !File.Exists(libraryPath))
            {
                return ForgeResult.Failure(command.Id, "library_not_found", $"Block library DWG not found: {libraryPath}");
            }
        }

        var watch = Stopwatch.StartNew();
        var db = ActiveDb;
        var names = BlockBulkInsert.DistinctNames(resolved);

        // ---- which names must come from the library ----
        List<string> fromLibrary;
        HashSet<string> missingHere;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            missingHere = names.Where(n => !bt.Has(n)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            fromLibrary = names.Where(n => missingHere.Contains(n) || (args.Redefine && libraryPath is not null)).ToList();
            tr.Commit();
        }

        var libraryReport = (object?)null;
        if (fromLibrary.Count > 0)
        {
            if (libraryPath is null)
            {
                return BulkFailure(command, fromLibrary.Select(n => new BulkInsertError(-1, "block_not_found", $"Block not found: {n}. Pass libraryPath to load it from a library DWG.")).ToList());
            }

            if (command.DryRun)
            {
                libraryReport = new { libraryPath, wouldLoad = fromLibrary };
            }
            else
            {
                var loaded = LoadLibraryBlocks(db, libraryPath, fromLibrary, args.Redefine, out var notInLibrary, out var loadError);
                if (loadError is not null)
                {
                    return ForgeResult.Failure(command.Id, "library_read_failed", loadError);
                }

                // With redefine=true a name that only exists here simply keeps its definition.
                var unresolved = notInLibrary.Where(missingHere.Contains).ToList();
                if (unresolved.Count > 0)
                {
                    return BulkFailure(command, unresolved.Select(n => new BulkInsertError(-1, "block_not_found", $"Block '{n}' is neither in this drawing nor in {libraryPath}.")).ToList());
                }

                libraryReport = new { libraryPath, loaded, redefined = args.Redefine };
            }
        }

        // ---- validate everything that needs the database, before creating anything ----
        using var main = db.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)main.GetObject(db.BlockTableId, OpenMode.ForRead);
        var layerTable = (LayerTable)main.GetObject(db.LayerTableId, OpenMode.ForRead);
        var mleaderStyles = (DBDictionary)main.GetObject(db.MLeaderStyleDictionaryId, OpenMode.ForRead);
        var layerIds = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
        var styleIds = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
        var dbErrors = new List<BulkInsertError>();
        var registry = DrawingRegistryStore.Current;
        var pack = StandardsPackStore.Current;

        void RequireLayerId(int index, string? layer)
        {
            if (layer is null || layerIds.ContainsKey(layer))
            {
                return;
            }

            if (layerTable.Has(layer))
            {
                layerIds[layer] = layerTable[layer];
            }
            else
            {
                dbErrors.Add(new BulkInsertError(index, "layer_not_found", $"Layer not found: {layer}"));
            }
        }

        foreach (var item in resolved)
        {
            if (!command.DryRun || !fromLibrary.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!blockTable.Has(item.Name))
                {
                    dbErrors.Add(new BulkInsertError(item.Index, "block_not_found", $"Block not found: {item.Name}"));
                }
            }

            RequireLayerId(item.Index, item.Layer);
            if (item.Leader is { } leader)
            {
                RequireLayerId(item.Index, leader.Layer);
                if (leader.Style is not null && !styleIds.ContainsKey(leader.Style))
                {
                    if (mleaderStyles.Contains(leader.Style))
                    {
                        styleIds[leader.Style] = mleaderStyles.GetAt(leader.Style);
                    }
                    else
                    {
                        dbErrors.Add(new BulkInsertError(item.Index, "mleader_style_not_found", $"MLeader style not found: {leader.Style}. Import it with forge_standards_import."));
                    }
                }
            }

            foreach (var pair in item.Attributes)
            {
                if (registry is not null && !registry.TryAuthorizeAttribute(pair.Key, pair.Value, out var code, out var message))
                {
                    dbErrors.Add(new BulkInsertError(item.Index, code!, message!));
                }

                if (pack is not null && DrawingRegistry.IsDrawingNumberTag(pair.Key) && !pack.TryValidateDrawingNo(pair.Value, out var packCode, out var packMessage))
                {
                    dbErrors.Add(new BulkInsertError(item.Index, packCode!, packMessage!));
                }
            }
        }

        if (dbErrors.Count > 0)
        {
            return BulkFailure(command, dbErrors);
        }

        if (command.DryRun)
        {
            return DryRun(command, new
            {
                tool = "forge_block_insert_bulk",
                items = resolved.Count,
                blocks = names,
                leaders = resolved.Count(r => r.Leader is not null),
                library = libraryReport
            });
        }

        // ---- create: one transaction, cached owner/definitions ----
        var modelSpace = ModelSpaceForWrite(db, main);
        var definitions = new Dictionary<string, (ObjectId Id, List<AttributeDefinition> AttDefs)>(StringComparer.OrdinalIgnoreCase);
        var handles = new List<object>(resolved.Count);
        var unknownTagWarnings = new List<string>();
        var leaders = 0;
        foreach (var item in resolved)
        {
            if (!definitions.TryGetValue(item.Name, out var definition))
            {
                var blockId = blockTable[item.Name];
                var attDefs = new List<AttributeDefinition>();
                var btr = (BlockTableRecord)main.GetObject(blockId, OpenMode.ForRead);
                if (btr.HasAttributeDefinitions)
                {
                    foreach (ObjectId id in btr)
                    {
                        if (main.GetObject(id, OpenMode.ForRead) is AttributeDefinition ad && !ad.Constant)
                        {
                            attDefs.Add(ad);
                        }
                    }
                }

                definition = (blockId, attDefs);
                definitions[item.Name] = definition;
            }

            var bref = new BlockReference(new Point3d(item.Position[0], item.Position[1], item.Position[2]), definition.Id);
            ObjectId refId;
            try
            {
                bref.SetDatabaseDefaults(db);
                bref.ScaleFactors = new Scale3d(item.Scale);
                bref.Rotation = item.RotationRad;
                if (item.Layer is not null)
                {
                    bref.LayerId = layerIds[item.Layer];
                }

                ApplyColor(bref, item.ColorIndex);
                refId = modelSpace.AppendEntity(bref);
                main.AddNewlyCreatedDBObject(bref, true);
            }
            catch
            {
                if (bref.ObjectId.IsNull)
                {
                    bref.Dispose();
                }

                throw;
            }

            var unknown = new List<string>();
            AppendDefaultAttributes(db, main, bref, definition.AttDefs, item.Attributes, unknown);
            if (unknown.Count > 0)
            {
                unknownTagWarnings.Add($"item {item.Index}: no attribute tag(s) {string.Join(", ", unknown)} on {item.Name}");
            }

            string? leaderHandle = null;
            if (item.Leader is { } spec)
            {
                leaderHandle = CreateBulkLeader(db, main, modelSpace, spec, spec.Layer is null ? ObjectId.Null : layerIds[spec.Layer], spec.Style is null ? ObjectId.Null : styleIds[spec.Style]).Handle.ToString();
                leaders++;
            }

            if (args.ReturnHandles)
            {
                handles.Add(new { index = item.Index, handle = refId.Handle.ToString(), leader = leaderHandle });
            }
        }

        main.Commit();
        watch.Stop();
        return ForgeResult.Success(
            command.Id,
            new
            {
                created = resolved.Count,
                leaders,
                blocks = definitions.Keys.ToArray(),
                library = libraryReport,
                items = args.ReturnHandles ? handles : null,
                warnings = unknownTagWarnings.Count > 0 ? unknownTagWarnings.Take(50).ToArray() : null,
                elapsedMs = watch.ElapsedMilliseconds
            },
            verification: new ForgeVerification
            {
                Attempted = true,
                Passed = true,
                Message = "All items were validated before creation and committed in one transaction (all-or-nothing).",
                ReadBack = new { created = resolved.Count, leaders }
            });
    }

    private static ForgeResult BulkFailure(ForgeCommand command, IReadOnlyList<BulkInsertError> errors) =>
        ForgeResult.Failure(
            command.Id,
            errors.Count == 1 ? errors[0].Code : "bulk_insert_invalid",
            errors.Count == 1 ? errors[0].Message : $"{errors.Count} item(s) are invalid; nothing was created.",
            "Fix the listed items (index = position in items[]); the call is all-or-nothing.",
            data: new { errors = errors.Take(200).Select(e => new { index = e.Index, code = e.Code, message = e.Message }).ToArray(), errorCount = errors.Count });

    /// <summary>Copies the named block definitions from a library DWG (one read, one clone call).</summary>
    private static string[] LoadLibraryBlocks(Database db, string libraryPath, IReadOnlyList<string> names, bool redefine, out List<string> notFound, out string? error)
    {
        notFound = [];
        error = null;
        // D10: reading the library DWG loads a foreign database — suppress the proxy dialog.
        using var proxyNotice = ProxyNoticeScope.Enter();
        using var side = new Database(false, true);
        try
        {
            side.ReadDwgFile(libraryPath, FileOpenMode.OpenForReadAndAllShare, true, null);
            side.CloseInput(true);
        }
        catch (System.Exception ex)
        {
            error = $"Could not read {libraryPath}: {ex.Message}";
            return [];
        }

        var ids = new ObjectIdCollection();
        var loaded = new List<string>();
        using (var str = side.TransactionManager.StartTransaction())
        {
            var bt = (BlockTable)str.GetObject(side.BlockTableId, OpenMode.ForRead);
            foreach (var name in names)
            {
                if (bt.Has(name))
                {
                    ids.Add(bt[name]);
                    loaded.Add(name);
                }
                else
                {
                    notFound.Add(name);
                }
            }

            str.Commit();
        }

        if (ids.Count > 0)
        {
            side.WblockCloneObjects(ids, db.BlockTableId, new IdMapping(), redefine ? DuplicateRecordCloning.Replace : DuplicateRecordCloning.Ignore, false);
        }

        return loaded.ToArray();
    }

    private static ObjectId CreateBulkLeader(Database db, Transaction tr, BlockTableRecord owner, ResolvedLeader spec, ObjectId layerId, ObjectId styleId)
    {
        var mleader = new MLeader();
        try
        {
            mleader.SetDatabaseDefaults(db);
            if (!styleId.IsNull)
            {
                mleader.MLeaderStyle = styleId;
            }

            var leaderIndex = mleader.AddLeader();
            var lineIndex = mleader.AddLeaderLine(leaderIndex);
            foreach (var p in spec.Points)
            {
                mleader.AddLastVertex(lineIndex, new Point3d(p[0], p[1], p[2]));
            }

            if (!string.IsNullOrWhiteSpace(spec.Text))
            {
                mleader.ContentType = ContentType.MTextContent;
                using var mtext = new MText();
                mtext.SetDatabaseDefaults(db);
                mtext.Contents = MTextContents(spec.Text!);
                if (spec.TextHeight is { } height)
                {
                    mtext.TextHeight = height;
                }

                var landing = spec.Points[^1];
                mtext.Location = new Point3d(landing[0], landing[1], landing[2]);
                if (spec.BackgroundMask)
                {
                    mtext.BackgroundFill = true;
                    mtext.UseBackgroundColor = true;
                    mtext.BackgroundScaleFactor = 1.25;
                }

                mleader.MText = mtext;
                if (spec.TextHeight is { } h)
                {
                    mleader.TextHeight = h;
                }
            }
            else
            {
                mleader.ContentType = ContentType.NoneContent;
            }

            if (!layerId.IsNull)
            {
                mleader.LayerId = layerId;
            }

            var id = owner.AppendEntity(mleader);
            tr.AddNewlyCreatedDBObject(mleader, true);
            return id;
        }
        catch
        {
            if (mleader.ObjectId.IsNull)
            {
                mleader.Dispose();
            }

            throw;
        }
    }
}
