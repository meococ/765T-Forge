using System.Diagnostics;
using Autodesk.AutoCAD.DatabaseServices;
using Forge.Shared;

namespace Forge.Plugin;

// forge_standards_import — copy named definitions (linetypes, text styles, layers, dim styles,
// mleader styles, table styles, page setups) from a template DWG/DWT into the active drawing in
// one call. The template is read into a side database; each table/dictionary is copied with ONE
// WblockCloneObjects call (Ignore = keep ours, Replace = overwrite), in dependency order so a
// layer's linetype or a dim style's text style is already present. Page setups use the
// PlotSettings CopyFrom path that forge_layout_page_setup_import already relies on. The plan
// (add / overwrite / skip) is Forge.Shared.StandardsImport, and every copied name is read back.
public sealed partial class PluginCommandProcessor
{
    private const int StandardsReportCap = 300;

    private sealed record StandardsImportArgs
    {
        public string? TemplatePath { get; init; }
        public string[]? Include { get; init; }
        public string? Filter { get; init; }
        public string? OnConflict { get; init; }
    }

    private static ForgeResult StandardsImportTool(ForgeCommand command)
    {
        var args = Args<StandardsImportArgs>(command);
        if (string.IsNullOrWhiteSpace(args.TemplatePath))
        {
            return ForgeResult.Failure(command.Id, "missing_template_path", "templatePath (a .dwg/.dwt/.dws) is required.");
        }

        if (RejectControlChars(command, ("templatePath", args.TemplatePath), ("filter", args.Filter), ("onConflict", args.OnConflict)) is { } reject)
        {
            return reject;
        }

        if (DrawingImport.SourceKind(args.TemplatePath) != "dwg")
        {
            return ForgeResult.Failure(command.Id, "unsupported_format", "templatePath must be a .dwg, .dwt or .dws file.");
        }

        var templatePath = System.IO.Path.GetFullPath(args.TemplatePath!);
        if (!File.Exists(templatePath))
        {
            return ForgeResult.Failure(command.Id, "template_not_found", $"Template not found: {templatePath}");
        }

        if (PathsEqual(templatePath, ActiveDocumentPath()))
        {
            return ForgeResult.Failure(command.Id, "self_import", "The template is the active drawing.");
        }

        if (!StandardsImport.TryParseKinds(args.Include, out var kinds, out var kindError))
        {
            return ForgeResult.Failure(command.Id, "invalid_include", kindError!);
        }

        if (!StandardsImport.TryParseConflict(args.OnConflict, out var overwrite, out var conflictError))
        {
            return ForgeResult.Failure(command.Id, "invalid_on_conflict", conflictError!);
        }

        var filter = WildcardSet.Parse(args.Filter);
        var watch = Stopwatch.StartNew();
        var db = ActiveDb;
        // D10: reading the template DWG loads a foreign database — suppress the proxy dialog.
        using var proxyNotice = ProxyNoticeScope.Enter();
        using var side = new Database(false, true);
        try
        {
            side.ReadDwgFile(templatePath, FileOpenMode.OpenForReadAndAllShare, true, null);
            side.CloseInput(true);
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(command.Id, "template_read_failed", $"Could not read {templatePath}: {ex.Message}");
        }

        // ---- plan: names on both sides ----
        var plans = new Dictionary<StandardsKind, (IReadOnlyList<StandardsItemPlan> Plan, Dictionary<string, ObjectId> SourceIds)>();
        using (var str = side.TransactionManager.StartTransaction())
        using (var dtr = db.TransactionManager.StartTransaction())
        {
            foreach (var kind in kinds)
            {
                var sourceIds = NamedIds(str, side, kind);
                var destNames = NamedIds(dtr, db, kind).Keys;
                plans[kind] = (StandardsImport.Plan(kind, sourceIds.Keys, destNames, filter, overwrite), sourceIds);
            }

            dtr.Commit();
            str.Commit();
        }

        if (command.DryRun)
        {
            return DryRun(command, new
            {
                tool = "forge_standards_import",
                templatePath,
                onConflict = overwrite ? "overwrite" : "skip",
                filter = filter.Patterns,
                kinds = kinds.ToDictionary(StandardsImport.WireName, kind => PlanReport(plans[kind].Plan))
            });
        }

        // ---- copy: one WblockCloneObjects per kind, dependency order ----
        var failures = new List<object>();
        foreach (var kind in kinds)
        {
            var (plan, sourceIds) = plans[kind];
            var wanted = plan
                .Where(p => p.Action is StandardsAction.Add or StandardsAction.Overwrite)
                .Select(p => sourceIds[p.Name])
                .ToArray();
            if (wanted.Length == 0)
            {
                continue;
            }

            try
            {
                if (kind == StandardsKind.PageSetups)
                {
                    CopyPageSetups(side, db, plan, sourceIds);
                }
                else
                {
                    var owner = OwnerId(db, kind);
                    side.WblockCloneObjects(
                        new ObjectIdCollection(wanted),
                        owner,
                        new IdMapping(),
                        overwrite ? DuplicateRecordCloning.Replace : DuplicateRecordCloning.Ignore,
                        false);
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                failures.Add(new { kind = StandardsImport.WireName(kind), code = ex.ErrorStatus.ToString(), message = ex.Message });
            }
        }

        // ---- read back ----
        var missing = new List<string>();
        using (var rtr = db.TransactionManager.StartTransaction())
        {
            foreach (var kind in kinds)
            {
                var present = NamedIds(rtr, db, kind);
                foreach (var item in plans[kind].Plan.Where(p => p.Action is StandardsAction.Add or StandardsAction.Overwrite))
                {
                    if (!present.ContainsKey(item.Name))
                    {
                        missing.Add($"{StandardsImport.WireName(kind)}:{item.Name}");
                    }
                }
            }

            rtr.Commit();
        }

        var added = plans.Values.Sum(p => p.Plan.Count(i => i.Action == StandardsAction.Add));
        var overwritten = plans.Values.Sum(p => p.Plan.Count(i => i.Action == StandardsAction.Overwrite));
        if (added + overwritten > 0 && missing.Count == added + overwritten)
        {
            return ForgeResult.Failure(
                command.Id,
                "standards_import_failed",
                "Nothing was copied from the template.",
                "Check the template opens in AutoCAD; see data.failures.",
                data: new { failures, missing = missing.Take(StandardsReportCap).ToArray() });
        }

        watch.Stop();
        var report = new
        {
            templatePath,
            onConflict = overwrite ? "overwrite" : "skip",
            filter = filter.Patterns,
            added,
            overwritten,
            skipped = plans.Values.Sum(p => p.Plan.Count(i => i.Action is StandardsAction.SkipExisting or StandardsAction.SkipProtected)),
            kinds = kinds.ToDictionary(StandardsImport.WireName, kind => PlanReport(plans[kind].Plan)),
            failures,
            missing = missing.Take(StandardsReportCap).ToArray(),
            note = overwrite
                ? "Overwritten records keep their identity, so existing entities pick up the template's definition (dependencies such as linetypes and text styles were replaced too)."
                : "Existing names were kept as they are; pass onConflict=\"overwrite\" to take the template's definitions.",
            elapsedMs = watch.ElapsedMilliseconds
        };

        return ForgeResult.Success(
            command.Id,
            report,
            verification: new ForgeVerification
            {
                Attempted = true,
                Passed = missing.Count == 0 && failures.Count == 0,
                Message = missing.Count == 0 ? "Every added/overwritten name was read back from the active drawing." : $"{missing.Count} name(s) were not found after the copy.",
                ReadBack = new { added, overwritten, missing = missing.Count }
            });
    }

    private static object PlanReport(IReadOnlyList<StandardsItemPlan> plan)
    {
        string[] Names(StandardsAction action) => plan.Where(p => p.Action == action).Select(p => p.Name).Take(StandardsReportCap).ToArray();
        return new
        {
            added = Names(StandardsAction.Add),
            overwritten = Names(StandardsAction.Overwrite),
            skippedExisting = Names(StandardsAction.SkipExisting),
            skippedProtected = Names(StandardsAction.SkipProtected),
            filteredOut = plan.Count(p => p.Action == StandardsAction.SkipFiltered)
        };
    }

    /// <summary>Name → id of every named record of one kind (system names excluded later by the plan).</summary>
    private static Dictionary<string, ObjectId> NamedIds(Transaction tr, Database db, StandardsKind kind)
    {
        var result = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
        switch (kind)
        {
            case StandardsKind.Linetypes:
                AddSymbols(tr, db.LinetypeTableId, result, _ => true);
                break;
            case StandardsKind.TextStyles:
                AddSymbols(tr, db.TextStyleTableId, result, record => record is TextStyleTableRecord { IsShapeFile: false });
                break;
            case StandardsKind.Layers:
                AddSymbols(tr, db.LayerTableId, result, _ => true);
                break;
            case StandardsKind.DimStyles:
                AddSymbols(tr, db.DimStyleTableId, result, _ => true);
                break;
            case StandardsKind.MLeaderStyles:
                AddEntries(tr, db.MLeaderStyleDictionaryId, result);
                break;
            case StandardsKind.TableStyles:
                AddEntries(tr, db.TableStyleDictionaryId, result);
                break;
            case StandardsKind.PageSetups:
                AddEntries(tr, db.PlotSettingsDictionaryId, result);
                break;
        }

        return result;
    }

    private static void AddSymbols(Transaction tr, ObjectId tableId, Dictionary<string, ObjectId> result, Func<SymbolTableRecord, bool> keep)
    {
        var table = (SymbolTable)tr.GetObject(tableId, OpenMode.ForRead);
        foreach (ObjectId id in table)
        {
            if (id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not SymbolTableRecord record || record.IsDependent || string.IsNullOrEmpty(record.Name) || !keep(record))
            {
                continue;
            }

            result[record.Name] = id;
        }
    }

    private static void AddEntries(Transaction tr, ObjectId dictionaryId, Dictionary<string, ObjectId> result)
    {
        if (dictionaryId.IsNull)
        {
            return;
        }

        var dictionary = (DBDictionary)tr.GetObject(dictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry entry in dictionary)
        {
            if (!entry.Value.IsErased)
            {
                result[entry.Key] = entry.Value;
            }
        }
    }

    private static ObjectId OwnerId(Database db, StandardsKind kind) => kind switch
    {
        StandardsKind.Linetypes => db.LinetypeTableId,
        StandardsKind.TextStyles => db.TextStyleTableId,
        StandardsKind.Layers => db.LayerTableId,
        StandardsKind.DimStyles => db.DimStyleTableId,
        StandardsKind.MLeaderStyles => db.MLeaderStyleDictionaryId,
        StandardsKind.TableStyles => db.TableStyleDictionaryId,
        _ => db.PlotSettingsDictionaryId
    };

    private static void CopyPageSetups(Database side, Database db, IReadOnlyList<StandardsItemPlan> plan, Dictionary<string, ObjectId> sourceIds)
    {
        using var str = side.TransactionManager.StartTransaction();
        using var dtr = db.TransactionManager.StartTransaction();
        var dict = (DBDictionary)dtr.GetObject(db.PlotSettingsDictionaryId, OpenMode.ForWrite);
        foreach (var item in plan.Where(p => p.Action is StandardsAction.Add or StandardsAction.Overwrite))
        {
            var source = (PlotSettings)str.GetObject(sourceIds[item.Name], OpenMode.ForRead);
            if (dict.Contains(item.Name))
            {
                var existing = (PlotSettings)dtr.GetObject(dict.GetAt(item.Name), OpenMode.ForWrite);
                existing.CopyFrom(source);
            }
            else
            {
                var created = new PlotSettings(source.ModelType);
                created.CopyFrom(source);
                dict.SetAt(item.Name, created);
                dtr.AddNewlyCreatedDBObject(created, true);
            }
        }

        dtr.Commit();
        str.Commit();
    }
}
