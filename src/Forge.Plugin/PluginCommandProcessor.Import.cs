using System.Diagnostics;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Forge.Shared;

namespace Forge.Plugin;

// forge_doc_import — bring an external DXF/DWG into the active drawing in ONE call.
//
// The 6-hour HVAC session spent most of its time getting an ezdxf DXF into AutoCAD: opening it as
// a document, copy/paste, fighting units, then re-layering. This tool reads the file into a side
// database (ReadDwgFile / DxfIn — no document, no UI), applies the layer map/prefix there, and
// lands it with one native call:
//   • block — Database.Insert(tempName, side) → rename (or redefine an existing block in place,
//     keeping every existing INSERT pointing at it) → one INSERT with scale/rotation/layer;
//   • merge — the source model space cloned into OUR model space (WblockCloneObjects into
//     the model-space BTR, then the position/scale/rotation matrix on the clones) — never
//     into the current space, which is a paper layout whenever CTAB is not Model (D11);
//   • xref  — AttachXref (a DXF is first saved to xrefSavePath as DWG) + one INSERT.
// There is no per-entity round trip: counts/extents come from one pass over the new objects.
// Safety: registry Destructive + RequiresBackup (redefining a block changes existing geometry),
// UNDO group from Process(); on any failure after the native insert the group is popped.
public sealed partial class PluginCommandProcessor
{
    private sealed record DocImportArgs
    {
        public string? Path { get; init; }
        public string? Mode { get; init; }
        public string? BlockName { get; init; }
        public double[]? Position { get; init; }
        public double? Scale { get; init; }
        public double? RotationDeg { get; init; }
        public double[]? BasePoint { get; init; }
        public string? Layer { get; init; }
        public string? LayerPrefix { get; init; }
        public Dictionary<string, string>? LayerMap { get; init; }
        public bool ReplaceExisting { get; init; }
        public bool InsertReference { get; init; } = true;
        public bool MatchUnits { get; init; } = true;
        public bool Overlay { get; init; }
        public string? XrefSavePath { get; init; }
        public bool OverwriteAcknowledged { get; init; }
        public bool ReturnHandles { get; init; }
    }

    private static ForgeResult DocImport(ForgeCommand command)
    {
        var args = Args<DocImportArgs>(command);
        var total = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(args.Path))
        {
            return ForgeResult.Failure(command.Id, "missing_path", "path (a .dxf or .dwg file) is required.");
        }

        var controlInputs = new List<(string, string?)>
        {
            ("path", args.Path), ("mode", args.Mode), ("blockName", args.BlockName), ("layer", args.Layer),
            ("layerPrefix", args.LayerPrefix), ("xrefSavePath", args.XrefSavePath)
        };
        if (args.LayerMap is not null)
        {
            controlInputs.AddRange(args.LayerMap.SelectMany(pair => new (string, string?)[] { ("layerMap key", pair.Key), ("layerMap value", pair.Value) }));
        }

        if (RejectControlChars(command, controlInputs.ToArray()) is { } reject)
        {
            return reject;
        }

        var kind = DrawingImport.SourceKind(args.Path);
        if (kind is null)
        {
            return ForgeResult.Failure(command.Id, "unsupported_format", $"Only .dxf, .dwg, .dwt and .dws files can be imported: {args.Path}", "Convert other formats first (forge_dgn_import handles DGN).");
        }

        var sourcePath = System.IO.Path.GetFullPath(args.Path!);
        if (!File.Exists(sourcePath))
        {
            return ForgeResult.Failure(command.Id, "import_source_not_found", $"File not found: {sourcePath}");
        }

        if (PathsEqual(sourcePath, ActiveDocumentPath()))
        {
            return ForgeResult.Failure(command.Id, "self_import", "Refusing to import the active drawing into itself.", "Import a different file, or copy entities with forge_entity_copy.");
        }

        if (!DrawingImport.TryParseMode(args.Mode, out var mode, out var modeError))
        {
            return ForgeResult.Failure(command.Id, "invalid_mode", modeError!);
        }

        if (!DrawingImport.TryPoint(args.Position, "position", out var position, out var pointError) ||
            !DrawingImport.TryPoint(args.BasePoint, "basePoint", out var basePointArg, out pointError))
        {
            return ForgeResult.Failure(command.Id, "invalid_args", pointError!);
        }

        var scale = args.Scale ?? 1.0;
        var rotationDeg = args.RotationDeg ?? 0.0;
        if (!(scale > 0) || !double.IsFinite(scale) || !double.IsFinite(rotationDeg))
        {
            return ForgeResult.Failure(command.Id, "invalid_args", "scale must be > 0 and rotationDeg finite.");
        }

        var blockName = string.IsNullOrWhiteSpace(args.BlockName)
            ? DrawingImport.SanitizeBlockName(System.IO.Path.GetFileNameWithoutExtension(sourcePath))
            : args.BlockName!.Trim();
        if (!DrawingImport.IsValidSymbolName(blockName))
        {
            return ForgeResult.Failure(command.Id, "invalid_block_name", $"Invalid block/xref name '{blockName}'.", "Avoid < > / \\ \" : ; ? * | = ` characters.");
        }

        if (!string.IsNullOrEmpty(args.LayerPrefix) && !DrawingImport.IsValidSymbolName(args.LayerPrefix + "X"))
        {
            return ForgeResult.Failure(command.Id, "invalid_layer_prefix", $"layerPrefix '{args.LayerPrefix}' would produce invalid layer names.");
        }

        string? xrefSavePath = null;
        if (mode == DrawingImportMode.Xref && kind == "dxf")
        {
            if (string.IsNullOrWhiteSpace(args.XrefSavePath))
            {
                return ForgeResult.Failure(command.Id, "xref_requires_dwg", "An xref must be a DWG; a DXF source needs xrefSavePath (where the converted DWG is written).", "Pass xrefSavePath=\"...\\name.dwg\", or use mode=block / mode=merge.");
            }

            xrefSavePath = System.IO.Path.GetFullPath(args.XrefSavePath!);
            if (!string.Equals(System.IO.Path.GetExtension(xrefSavePath), ".dwg", StringComparison.OrdinalIgnoreCase))
            {
                return ForgeResult.Failure(command.Id, "invalid_xref_save_path", "xrefSavePath must end in .dwg.");
            }

            if (File.Exists(xrefSavePath) && !args.OverwriteAcknowledged && !command.DryRun)
            {
                return ForgeResult.Failure(command.Id, "xref_save_overwrite_not_acknowledged", $"Refusing to overwrite {xrefSavePath}.", "Pass overwriteAcknowledged=true, or choose a new xrefSavePath.");
            }
        }

        var db = ActiveDb;
        var warnings = new List<string>();
        bool blockExists;
        ObjectId existingBlockId = ObjectId.Null;
        var targetUnits = (int)db.Insunits;
        using (var check = db.TransactionManager.StartTransaction())
        {
            if (args.Layer is not null && !LayerExists(db, check, args.Layer))
            {
                return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}", "Create it first with forge_layer_create.");
            }

            var bt = (BlockTable)check.GetObject(db.BlockTableId, OpenMode.ForRead);
            blockExists = bt.Has(blockName);
            if (blockExists)
            {
                existingBlockId = bt[blockName];
                var existing = (BlockTableRecord)check.GetObject(existingBlockId, OpenMode.ForRead);
                if (mode == DrawingImportMode.Xref)
                {
                    var samePath = existing.IsFromExternalReference && PathsEqual(TryFullPath(existing.PathName, db), xrefSavePath ?? sourcePath);
                    if (!samePath)
                    {
                        return ForgeResult.Failure(command.Id, "xref_name_exists", $"A block or xref named '{blockName}' already exists.", "Pass a different blockName, or forge_xref_repath to point the existing xref elsewhere.");
                    }
                }
                else if (mode == DrawingImportMode.Block)
                {
                    if (!args.ReplaceExisting)
                    {
                        return ForgeResult.Failure(command.Id, "block_exists", $"Block '{blockName}' already exists.", "Pass replaceExisting=true to redefine it in place (existing inserts update), or choose another blockName.");
                    }

                    if (existing.IsLayout || existing.IsFromExternalReference || existing.IsFromOverlayReference || existing.IsDynamicBlock || existing.IsAnonymous)
                    {
                        return ForgeResult.Failure(command.Id, "block_not_replaceable", $"'{blockName}' is a layout, xref, anonymous or dynamic block and cannot be redefined by import.");
                    }
                }
            }

            check.Commit();
        }

        // ---- read the source into a side database (no document, no UI) ----
        // fix-smoke-20261029-r2 D10 + review-wp2-r1 finding 5: a DWG with proxy objects
        // raises the modal "Proxy Information" dialog during DxfIn/ReadDwgFile/Insert/
        // AttachXref and wedges every later pipe call. PROXYNOTICE=0 for the whole
        // foreign-database section, restored on Dispose — and the call fails closed with
        // proxynotice_scope_failed when it cannot be applied (no foreign load without
        // suppression).
        using var proxyNotice = ProxyNoticeScope.EnterOrFail(command, out var scopeFailure);
        if (scopeFailure is not null)
        {
            return scopeFailure;
        }
        var phase = Stopwatch.StartNew();
        using var side = new Database(false, true);
        string? dxfLog = null;
        try
        {
            if (kind == "dxf")
            {
                dxfLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"forge-dxfin-{Guid.NewGuid():N}.log");
                side.DxfIn(sourcePath, dxfLog);
            }
            else
            {
                side.ReadDwgFile(sourcePath, FileOpenMode.OpenForReadAndAllShare, true, null);
                side.CloseInput(true);
            }
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(command.Id, "import_read_failed", $"Could not read {sourcePath}: {ex.Message}", "Check the file opens in AutoCAD (version, password, corruption); a DXF must be ASCII or binary DXF R12+.");
        }
        finally
        {
            if (dxfLog is not null)
            {
                warnings.AddRange(ReadDxfLog(dxfLog));
            }
        }

        var readMs = phase.ElapsedMilliseconds;
        var sourceUnits = (int)side.Insunits;
        var unitScale = 1.0;
        if (args.MatchUnits)
        {
            unitScale = DrawingImport.UnitScale(sourceUnits, targetUnits, out var unitWarning);
            if (unitWarning is not null)
            {
                warnings.Add(unitWarning);
            }
        }

        var effectiveScale = scale * unitScale;
        var basePoint = args.BasePoint is null ? new[] { side.Insbase.X, side.Insbase.Y, side.Insbase.Z } : basePointArg;

        // ---- inventory + layer plan in the side database ----
        var sourceTypes = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sourceCount = 0;
        LayerMapPlan layerPlan;
        var nestedConflicts = new List<string>();
        using (var str = side.TransactionManager.StartTransaction())
        {
            var sbt = (BlockTable)str.GetObject(side.BlockTableId, OpenMode.ForRead);
            var sms = (BlockTableRecord)str.GetObject(sbt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in sms)
            {
                sourceCount++;
                DrawingImport.Tally(sourceTypes, id.ObjectClass.DxfName);
            }

            var sourceLayers = new List<string>();
            var slt = (LayerTable)str.GetObject(side.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in slt)
            {
                var ltr = (LayerTableRecord)str.GetObject(id, OpenMode.ForRead);
                if (!ltr.IsDependent)
                {
                    sourceLayers.Add(ltr.Name);
                }
            }

            layerPlan = mode == DrawingImportMode.Xref
                ? new LayerMapPlan([], [], [], [])
                : DrawingImport.PlanLayerMap(sourceLayers, args.LayerMap, args.LayerPrefix);
            if (mode == DrawingImportMode.Xref && (args.LayerMap is { Count: > 0 } || !string.IsNullOrEmpty(args.LayerPrefix)))
            {
                warnings.Add("layerMap/layerPrefix are ignored for xref mode: xref layers keep their names as <xref>|<layer>.");
            }

            if (mode != DrawingImportMode.Xref)
            {
                using var dtr = db.TransactionManager.StartTransaction();
                var dbt = (BlockTable)dtr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in sbt)
                {
                    var btr = (BlockTableRecord)str.GetObject(id, OpenMode.ForRead);
                    if (btr.IsLayout || btr.IsAnonymous || btr.IsDependent || btr.IsFromExternalReference)
                    {
                        continue;
                    }

                    if (dbt.Has(btr.Name))
                    {
                        nestedConflicts.Add(btr.Name);
                    }
                }

                dtr.Commit();
            }

            str.Commit();
        }

        if (nestedConflicts.Count > 0)
        {
            warnings.Add($"{nestedConflicts.Count} nested block name(s) already exist in this drawing; the existing definitions are kept (AutoCAD INSERT rule): {string.Join(", ", nestedConflicts.Take(20))}{(nestedConflicts.Count > 20 ? ", …" : "")}");
        }

        if (layerPlan.Invalid.Count > 0)
        {
            warnings.Add("Layer map targets skipped (invalid names): " + string.Join("; ", layerPlan.Invalid.Take(20)));
        }

        if (layerPlan.Protected.Count > 0)
        {
            warnings.Add("Layers 0/Defpoints are never renamed: " + string.Join(", ", layerPlan.Protected));
        }

        if (command.DryRun)
        {
            return DryRun(command, new
            {
                tool = "forge_doc_import",
                mode = ModeWord(mode),
                source = new { path = sourcePath, kind, units = DrawingImport.UnitName(sourceUnits), insbase = basePoint, entities = sourceCount, countsByType = sourceTypes },
                targetUnits = DrawingImport.UnitName(targetUnits),
                unitScale,
                effectiveScale,
                blockName,
                blockExists,
                willReplace = mode == DrawingImportMode.Block && blockExists,
                xrefSavePath,
                layerPlan = LayerPlanReport(layerPlan),
                nestedBlockConflicts = nestedConflicts,
                proxynotice = proxyNotice,
                warnings
            });
        }

        phase.Restart();
        var mapReport = mode == DrawingImportMode.Xref ? null : ApplyLayerPlan(side, layerPlan, warnings);
        var mapMs = phase.ElapsedMilliseconds;

        var layersBefore = LayerNames(db);
        phase.Restart();
        try
        {
            ImportOutcome outcome = mode switch
            {
                DrawingImportMode.Merge => ImportMerge(db, side, position, effectiveScale, rotationDeg, basePoint, args, warnings),
                DrawingImportMode.Block => ImportBlock(db, side, blockName, blockExists ? existingBlockId : ObjectId.Null, position, effectiveScale, rotationDeg, args, warnings),
                _ => ImportXref(db, side, blockName, sourcePath, xrefSavePath, blockExists ? existingBlockId : ObjectId.Null, position, effectiveScale, rotationDeg, args, warnings)
            };
            var insertMs = phase.ElapsedMilliseconds;
            var layersAfter = LayerNames(db);
            var created = layersAfter.Where(l => !layersBefore.Contains(l)).OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToArray();
            var reused = outcome.CountsByLayer.Keys
                .Where(l => layersBefore.Contains(l) && !l.Contains('|', StringComparison.Ordinal))
                .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (reused.Length > 0)
            {
                warnings.Add($"{reused.Length} imported layer(s) already existed here and keep THIS drawing's colour/linetype: {string.Join(", ", reused.Take(15))}{(reused.Length > 15 ? ", …" : "")}");
            }

            total.Stop();
            var data = new
            {
                mode = ModeWord(mode),
                source = new { path = sourcePath, kind, units = DrawingImport.UnitName(sourceUnits), entities = sourceCount },
                targetUnits = DrawingImport.UnitName(targetUnits),
                unitScale,
                scale = effectiveScale,
                position,
                rotationDeg,
                basePoint,
                blockName = mode == DrawingImportMode.Merge ? null : blockName,
                blockHandle = outcome.BlockHandle,
                insertHandle = outcome.InsertHandle,
                replaced = outcome.Replaced,
                existingReferences = outcome.ExistingReferences,
                xrefPath = outcome.XrefPath,
                created = outcome.Created,
                skippedOrUntransformed = outcome.SkippedOrUntransformed > 0 ? outcome.SkippedOrUntransformed : (int?)null,

                // An xref copies nothing into this drawing: its counts are the source inventory.
                countsByType = mode == DrawingImportMode.Xref ? sourceTypes : outcome.CountsByType,
                countsByLayer = outcome.CountsByLayer,
                layersCreated = created,
                layersReused = reused,
                layerMap = mapReport,
                extents = outcome.Extents,
                handles = outcome.Handles,
                handlesTruncated = outcome.HandlesTruncated ? true : (bool?)null,
                nestedBlockConflicts = nestedConflicts.Count > 0 ? nestedConflicts : null,
                proxynotice = proxyNotice,
                warnings,
                timingsMs = new { read = readMs, layerMap = mapMs, insert = insertMs, total = total.ElapsedMilliseconds }
            };

            // fix-smoke-20261029-r2 D11: ok derives from the read-back, never from "the call
            // ran" — a merge that lands nothing in model space fails import_verification_failed.
            var verified = DrawingImport.ImportVerified(
                mode, outcome.Created, outcome.InsertHandle is not null, outcome.BlockHandle is not null);
            var verification = new ForgeVerification
            {
                Attempted = true,
                Passed = verified,
                Message = mode switch
                {
                    DrawingImportMode.Xref => $"Attached as an xref after one native AttachXref call ({sourceCount} source entities stay in the xref file; nothing was copied into this drawing). Read back the xref INSERT and its world extents.",
                    DrawingImportMode.Merge => $"Read back {outcome.Created} new model-space entities after cloning the source model space into this drawing's model-space block table record.",
                    _ => $"Read back {outcome.Created} entities in the definition from the database after one native Database.Insert call."
                },
                ReadBack = new { created = outcome.Created, extents = outcome.Extents, insertHandle = outcome.InsertHandle }
            };

            // review-wp2-r1 finding 10 (D11): a partial import is success-with-warning — when
            // fewer entities land than the source model space held, or clones were skipped or
            // untransformed, the warning names BOTH counts.
            if (DrawingImport.PartialImportWarning(mode, outcome.Created, sourceCount, outcome.SkippedOrUntransformed) is { } partialWarning)
            {
                warnings.Add(partialWarning);
            }

            return DrawingImport.ImportResult(
                command.Id,
                verified,
                data,
                verification,
                mode switch
                {
                    DrawingImportMode.Merge => $"Merge read back {outcome.Created} entities in model space (the source model space had {sourceCount}); the import could not be verified.",
                    DrawingImportMode.Xref => "The xref attach produced no block-table definition; the attach could not be verified.",
                    _ => $"The import read back {outcome.Created} entities in '{blockName}' and produced no INSERT handle; the import could not be verified."
                },
                "Run with dryRun=true to inspect the source; the failed import was rolled back by the UNDO group.");
        }
        catch (System.Exception ex) when (ex is Autodesk.AutoCAD.Runtime.Exception or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Process() pops the UNDO group on a failed result, which also reverts the native insert.
            return ForgeResult.Failure(command.Id, "import_failed", $"Import failed: {ex.Message}", "Run with dryRun=true to inspect the source; nothing was kept (the UNDO group is rolled back).", data: new { proxynotice = proxyNotice, warnings });
        }
    }

    private sealed record ImportOutcome(
        int Created,
        SortedDictionary<string, int> CountsByType,
        SortedDictionary<string, int> CountsByLayer,
        double[]? Extents,
        string? BlockHandle,
        string? InsertHandle,
        bool Replaced,
        int? ExistingReferences,
        string? XrefPath,
        string[]? Handles,
        bool HandlesTruncated,
        int SkippedOrUntransformed);

    private static ImportOutcome ImportMerge(Database db, Database side, double[] position, double scale, double rotationDeg, double[] basePoint, DocImportArgs args, List<string> warnings)
    {
        var seed = db.Handseed.Value;
        var matrix = new Matrix3d(DrawingImport.MergeTransform(position, scale, DegreesToRadians(rotationDeg), basePoint));
        // fix-smoke-20261029-r2 D11: Database.Insert lands in the database's CURRENT space —
        // with CTAB on a paper layout the entities went there while the model-space readback
        // below stayed 0, and managed CurrentSpaceId is read-only. The merge now clones the
        // source model space into this drawing's model-space BTR explicitly (existing
        // definitions win, AutoCAD INSERT rule) and applies the matrix to the clones, so the
        // target is always model space regardless of CTAB.
        var sourceIds = new ObjectIdCollection();
        using (var str = side.TransactionManager.StartTransaction())
        {
            var sbt = (BlockTable)str.GetObject(side.BlockTableId, OpenMode.ForRead);
            var sms = (BlockTableRecord)str.GetObject(sbt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in sms)
            {
                sourceIds.Add(id);
            }

            str.Commit();
        }

        var modelSpaceId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        var mapping = new IdMapping();
        db.WblockCloneObjects(sourceIds, modelSpaceId, mapping, DuplicateRecordCloning.Ignore, false);

        var types = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var layers = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        double[]? extents = null;
        var handles = args.ReturnHandles ? new List<string>() : null;
        var truncated = false;
        var created = 0;
        var skipped = 0;
        var transformFailures = 0;
        using var tr = db.TransactionManager.StartTransaction();
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        ObjectId forcedLayer = ObjectId.Null;
        if (args.Layer is not null)
        {
            forcedLayer = ((LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead))[args.Layer];
        }

        // The position/scale/rotation matrix applies to the top-level clones only — entities
        // pulled in as part of a nested definition keep their definition coordinates.
        foreach (IdPair pair in mapping)
        {
            if (!pair.IsPrimary)
            {
                continue;
            }

            if (!pair.IsCloned)
            {
                skipped++;
                continue;
            }

            if (tr.GetObject(pair.Value, OpenMode.ForWrite) is not Entity clone || clone.BlockId != modelSpaceId)
            {
                skipped++;
                continue;
            }

            try
            {
                clone.TransformBy(matrix);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                transformFailures++;
            }
        }

        if (transformFailures > 0)
        {
            warnings.Add($"{transformFailures} merged entit(ies) could not be transformed (proxy content?); they kept their source coordinates.");
        }

        foreach (ObjectId id in ms)
        {
            if (id.Handle.Value < seed || id.IsErased)
            {
                continue;
            }

            if (tr.GetObject(id, OpenMode.ForRead) is not Entity entity)
            {
                continue;
            }

            if (!forcedLayer.IsNull && entity.LayerId != forcedLayer)
            {
                entity.UpgradeOpen();
                entity.LayerId = forcedLayer;
            }

            created++;
            DrawingImport.Tally(types, id.ObjectClass.DxfName);
            DrawingImport.Tally(layers, entity.Layer);
            extents = DrawingImport.UnionExtents(extents, TryExtents(entity));
            if (handles is not null)
            {
                if (handles.Count < DrawingImport.MaxReturnedHandles)
                {
                    handles.Add(entity.Handle.ToString());
                }
                else
                {
                    truncated = true;
                }
            }
        }

        tr.Commit();
        if (created == 0)
        {
            warnings.Add("The source model space was empty (paper-space content is not imported by merge).");
        }

        return new ImportOutcome(created, types, layers, extents, null, null, false, null, null, handles?.ToArray(), truncated, skipped + transformFailures);
    }

    private static ImportOutcome ImportBlock(Database db, Database side, string blockName, ObjectId existingId, double[] position, double scale, double rotationDeg, DocImportArgs args, List<string> warnings)
    {
        var tempName = "$FORGE_IMPORT_" + Guid.NewGuid().ToString("N")[..12];
        var tempId = db.Insert(tempName, side, false);
        try
        {
            var replaced = false;
            int? existingRefs = null;
            if (!existingId.IsNull)
            {
                // Redefine in place so every existing INSERT keeps pointing at the same record:
                // (1) erase the old contents, (2) deep-clone the new contents into it with no
                // transaction holding the owner open, (3) drop the temporary definition below.
                var ids = new ObjectIdCollection();
                using (var etr = db.TransactionManager.StartTransaction())
                {
                    var existing = (BlockTableRecord)etr.GetObject(existingId, OpenMode.ForRead);
                    existingRefs = existing.GetBlockReferenceIds(true, false).Count;
                    foreach (ObjectId oldId in existing)
                    {
                        if (!oldId.IsErased)
                        {
                            etr.GetObject(oldId, OpenMode.ForWrite).Erase();
                        }
                    }

                    var temp = (BlockTableRecord)etr.GetObject(tempId, OpenMode.ForRead);
                    foreach (ObjectId newId in temp)
                    {
                        ids.Add(newId);
                    }

                    etr.Commit();
                }

                if (ids.Count > 0)
                {
                    db.DeepCloneObjects(ids, existingId, new IdMapping(), false);
                }

                replaced = true;
                if (existingRefs > 0)
                {
                    warnings.Add($"Redefined '{blockName}' used by {existingRefs} insert(s); geometry updates on regen. Changed attribute definitions do not re-sync existing attributes (run ATTSYNC if tags changed).");
                }
            }

            using var tr = db.TransactionManager.StartTransaction();
            var tempRecord = (BlockTableRecord)tr.GetObject(tempId, OpenMode.ForWrite);
            if (args.BasePoint is { Length: >= 2 })
            {
                tempRecord.Origin = new Point3d(args.BasePoint[0], args.BasePoint[1], args.BasePoint.Length > 2 ? args.BasePoint[2] : 0);
            }

            ObjectId definitionId;
            if (existingId.IsNull)
            {
                tempRecord.Name = blockName;
                definitionId = tempId;
            }
            else
            {
                var existing = (BlockTableRecord)tr.GetObject(existingId, OpenMode.ForWrite);
                existing.Origin = tempRecord.Origin;
                existing.Units = tempRecord.Units;
                tempRecord.Erase();
                definitionId = existingId;
            }

            var definition = (BlockTableRecord)tr.GetObject(definitionId, OpenMode.ForRead);
            var types = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var layers = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var created = 0;
            var attDefs = new List<AttributeDefinition>();
            foreach (ObjectId id in definition)
            {
                if (id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not Entity entity)
                {
                    continue;
                }

                created++;
                DrawingImport.Tally(types, id.ObjectClass.DxfName);
                DrawingImport.Tally(layers, entity.Layer);
                if (entity is AttributeDefinition ad && !ad.Constant)
                {
                    attDefs.Add(ad);
                }
            }

            string? insertHandle = null;
            double[]? extents = null;
            if (args.InsertReference)
            {
                var bref = new BlockReference(new Point3d(position[0], position[1], position[2]), definitionId);
                var refId = AppendNew(db, tr, bref, args.Layer, null, b =>
                {
                    b.ScaleFactors = new Scale3d(scale);
                    b.Rotation = DegreesToRadians(rotationDeg);
                });
                AppendDefaultAttributes(db, tr, bref, attDefs);
                insertHandle = refId.Handle.ToString();
                extents = TryExtents(bref);
            }

            tr.Commit();
            return new ImportOutcome(created, types, layers, extents, definition.Handle.ToString(), insertHandle, replaced, existingRefs, null, null, false, 0);
        }
        catch
        {
            TryEraseBlock(db, tempId);
            throw;
        }
    }

    private static ImportOutcome ImportXref(Database db, Database side, string xrefName, string sourcePath, string? xrefSavePath, ObjectId existingId, double[] position, double scale, double rotationDeg, DocImportArgs args, List<string> warnings)
    {
        var xrefPath = sourcePath;
        if (xrefSavePath is not null)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(xrefSavePath)!);
            side.SaveAs(xrefSavePath, DwgVersion.Current);
            xrefPath = xrefSavePath;
            warnings.Add($"DXF saved as DWG for the xref: {xrefSavePath}");
        }

        using var tr = db.TransactionManager.StartTransaction();
        var xrefId = existingId;
        if (xrefId.IsNull)
        {
            xrefId = args.Overlay ? db.OverlayXref(xrefPath, xrefName) : db.AttachXref(xrefPath, xrefName);
        }
        else
        {
            warnings.Add($"Xref '{xrefName}' is already attached from this path; added another insert of it.");
        }

        if (xrefId.IsNull)
        {
            throw new InvalidOperationException("AutoCAD did not attach the xref.");
        }

        string? insertHandle = null;
        double[]? extents = null;
        if (args.InsertReference)
        {
            var bref = new BlockReference(new Point3d(position[0], position[1], position[2]), xrefId);
            var refId = AppendNew(db, tr, bref, args.Layer, null, b =>
            {
                b.ScaleFactors = new Scale3d(scale);
                b.Rotation = DegreesToRadians(rotationDeg);
            });
            insertHandle = refId.Handle.ToString();
            extents = TryExtents(bref);
        }

        var definition = (BlockTableRecord)tr.GetObject(xrefId, OpenMode.ForRead);
        tr.Commit();
        var types = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var layers = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return new ImportOutcome(0, types, layers, extents, definition.Handle.ToString(), insertHandle, false, null, xrefPath, null, false, 0);
    }

    /// <summary>Renames/merges layers in the side database before the native insert copies them.</summary>
    private static object? ApplyLayerPlan(Database side, LayerMapPlan plan, List<string> warnings)
    {
        if (plan.Changed == 0)
        {
            return null;
        }

        using var tr = side.TransactionManager.StartTransaction();
        var table = (LayerTable)tr.GetObject(side.LayerTableId, OpenMode.ForRead);
        var original = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId id in table)
        {
            var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            original[ltr.Name] = id;
        }

        foreach (var rename in plan.Renames)
        {
            if (original.TryGetValue(rename.Source, out var id))
            {
                var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                ltr.Name = rename.Target;
            }
        }

        var moveTo = new Dictionary<ObjectId, ObjectId>();
        if (plan.Merges.Count > 0)
        {
            table.UpgradeOpen();
            foreach (var merge in plan.Merges)
            {
                if (!original.TryGetValue(merge.Source, out var sourceId))
                {
                    continue;
                }

                ObjectId targetId;
                if (table.Has(merge.Target))
                {
                    targetId = table[merge.Target];
                }
                else
                {
                    // The target name is free (it was renamed away): create it with the source's look.
                    var source = (LayerTableRecord)tr.GetObject(sourceId, OpenMode.ForRead);
                    var created = new LayerTableRecord
                    {
                        Name = merge.Target,
                        Color = source.Color,
                        LinetypeObjectId = source.LinetypeObjectId,
                        LineWeight = source.LineWeight,
                        IsPlottable = source.IsPlottable
                    };
                    targetId = table.Add(created);
                    tr.AddNewlyCreatedDBObject(created, true);
                }

                moveTo[sourceId] = targetId;
            }

            var moved = 0;
            var bt = (BlockTable)tr.GetObject(side.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId btrId in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                if (btr.IsFromExternalReference || btr.IsDependent)
                {
                    continue;
                }

                foreach (ObjectId id in btr)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity entity)
                    {
                        continue;
                    }

                    if (moveTo.TryGetValue(entity.LayerId, out var target))
                    {
                        entity.UpgradeOpen();
                        entity.LayerId = target;
                        moved++;
                    }

                    if (entity is BlockReference br)
                    {
                        foreach (ObjectId attId in br.AttributeCollection)
                        {
                            if (tr.GetObject(attId, OpenMode.ForRead) is AttributeReference att && moveTo.TryGetValue(att.LayerId, out var attTarget))
                            {
                                att.UpgradeOpen();
                                att.LayerId = attTarget;
                                moved++;
                            }
                        }
                    }
                }
            }

            // Drop the now-empty source layers so they are not copied in as clutter.
            var emptied = new ObjectIdCollection(moveTo.Keys.ToArray());
            try
            {
                side.Purge(emptied);
                foreach (ObjectId id in emptied)
                {
                    tr.GetObject(id, OpenMode.ForWrite).Erase();
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                warnings.Add($"Merged source layers could not be purged ({ex.ErrorStatus}); they come in empty.");
            }

            warnings.Add($"Layer merge moved {moved} entities.");
        }

        tr.Commit();
        return LayerPlanReport(plan);
    }

    private static object LayerPlanReport(LayerMapPlan plan) => new
    {
        renamed = plan.Renames.Select(r => new { from = r.Source, to = r.Target }).ToArray(),
        merged = plan.Merges.Select(r => new { from = r.Source, to = r.Target }).ToArray(),
        invalid = plan.Invalid,
        @protected = plan.Protected
    };

    /// <summary>One attribute reference per non-constant definition, with the definition's default text.</summary>
    private static void AppendDefaultAttributes(Database db, Transaction tr, BlockReference bref, IReadOnlyList<AttributeDefinition> definitions, IReadOnlyDictionary<string, string>? values = null, List<string>? unknownTags = null)
    {
        Dictionary<string, string>? remaining = values is { Count: > 0 }
            ? new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
            : null;
        foreach (var definition in definitions)
        {
            var reference = new AttributeReference();
            try
            {
                reference.SetAttributeFromBlock(definition, bref.BlockTransform);
                if (remaining is not null && remaining.Remove(definition.Tag, out var value))
                {
                    reference.TextString = value;
                    if (!reference.IsMTextAttribute && reference.Justify != AttachmentPoint.BaseLeft)
                    {
                        try
                        {
                            reference.AdjustAlignment(db);
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception)
                        {
                            // Alignment is recomputed on the next regen.
                        }
                    }
                }

                bref.AttributeCollection.AppendAttribute(reference);
                tr.AddNewlyCreatedDBObject(reference, true);
            }
            catch
            {
                if (reference.ObjectId.IsNull)
                {
                    reference.Dispose();
                }

                throw;
            }
        }

        if (remaining is { Count: > 0 } && unknownTags is not null)
        {
            unknownTags.AddRange(remaining.Keys);
        }
    }

    private static void TryEraseBlock(Database db, ObjectId blockId)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (!blockId.IsNull && !blockId.IsErased && tr.GetObject(blockId, OpenMode.ForWrite) is BlockTableRecord btr && btr.GetBlockReferenceIds(true, false).Count == 0)
            {
                btr.Erase();
            }

            tr.Commit();
        }
        catch
        {
            // Best effort: the UNDO rollback in Process() removes it anyway.
        }
    }

    private static HashSet<string> LayerNames(Database db)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var tr = db.TransactionManager.StartTransaction();
        var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (ObjectId id in table)
        {
            names.Add(((LayerTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name);
        }

        tr.Commit();
        return names;
    }

    private static IEnumerable<string> ReadDxfLog(string path)
    {
        var lines = new List<string>();
        try
        {
            if (File.Exists(path))
            {
                lines.AddRange(File.ReadLines(path)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .Take(10)
                    .Select(l => "DXFIN: " + l));
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // The log is advisory only.
        }

        return lines;
    }

    private static string? TryFullPath(string? path, Database db)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            if (System.IO.Path.IsPathRooted(path))
            {
                return System.IO.Path.GetFullPath(path);
            }

            var hostDir = System.IO.Path.GetDirectoryName(db.Filename);
            return string.IsNullOrEmpty(hostDir) ? path : System.IO.Path.GetFullPath(System.IO.Path.Combine(hostDir, path));
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    private static string ModeWord(DrawingImportMode mode) => mode switch
    {
        DrawingImportMode.Merge => "merge",
        DrawingImportMode.Xref => "xref",
        _ => "block"
    };
}
