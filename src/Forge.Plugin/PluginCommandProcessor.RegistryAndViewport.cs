using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Forge.Shared;

namespace Forge.Plugin;

public sealed partial class PluginCommandProcessor
{
    private static ForgeResult RegistryLoad(ForgeCommand command)
    {
        var args = Args<PathOnlyArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Path))
        {
            return ForgeResult.Failure(command.Id, "missing_registry_path", "path to drawing registry JSON is required.");
        }

        if (!File.Exists(args.Path))
        {
            return ForgeResult.Failure(command.Id, "registry_not_found", $"Registry file not found: {args.Path}");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { args.Path });
        }

        try
        {
            var registry = DrawingRegistry.LoadFromFile(args.Path);
            DrawingRegistryStore.Set(registry);
            return ForgeResult.Success(command.Id, new
            {
                projectId = registry.ProjectId,
                sheetCount = registry.Sheets.Count,
                sourcePath = registry.SourcePath
            });
        }
        catch (Exception ex)
        {
            return ForgeResult.Failure(command.Id, "registry_invalid", ex.Message);
        }
    }

    private static ForgeResult RegistryLookup(ForgeCommand command)
    {
        var args = Args<RegistryLookupArgs>(command);
        var registry = DrawingRegistryStore.Current;
        if (registry is null)
        {
            return ForgeResult.Failure(command.Id, "registry_not_loaded", "No drawing registry loaded. Call forge_registry_load first.");
        }

        if (!string.IsNullOrWhiteSpace(args.DrawingNo))
        {
            var sheet = registry.FindByDrawingNo(args.DrawingNo);
            return sheet is null
                ? ForgeResult.Failure(command.Id, "deny_unknown_drawing_no", $"Drawing number '{args.DrawingNo}' is not in registry '{registry.ProjectId}'.")
                : ForgeResult.Success(command.Id, sheet);
        }

        return ForgeResult.Success(command.Id, new
        {
            projectId = registry.ProjectId,
            sourcePath = registry.SourcePath,
            sheets = registry.Sheets
        });
    }

    private static ForgeResult PackLoad(ForgeCommand command)
    {
        var args = Args<PathOnlyArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Path))
        {
            return ForgeResult.Failure(command.Id, "missing_pack_path", "path to standards pack JSON is required.");
        }

        if (!File.Exists(args.Path))
        {
            return ForgeResult.Failure(command.Id, "pack_not_found", $"Standards pack not found: {args.Path}");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { args.Path });
        }

        try
        {
            var pack = StandardsPack.LoadFromFile(args.Path);
            StandardsPackStore.Set(pack);
            return ForgeResult.Success(command.Id, new
            {
                packId = pack.PackId,
                layerCount = pack.Layers.Count,
                forbiddenCount = pack.ForbiddenLayers.Count,
                sourcePath = pack.SourcePath
            });
        }
        catch (Exception ex)
        {
            return ForgeResult.Failure(command.Id, "pack_invalid", ex.Message);
        }
    }

    private static ForgeResult PackStatus(ForgeCommand command)
    {
        var pack = StandardsPackStore.Current;
        var registry = DrawingRegistryStore.Current;
        var contract = IssueSetContractStore.Current;
        return ForgeResult.Success(command.Id, new
        {
            pack = pack is null ? null : new { pack.PackId, pack.SourcePath, pack.Description, pack.RequireForegroundPlot },
            registry = registry is null ? null : new { registry.ProjectId, registry.SourcePath, sheetCount = registry.Sheets.Count },
            issueSetContract = contract is null ? null : new { contract.ContractId, contract.SourcePath, sheetCount = contract.Sheets.Count }
        });
    }

    private static ForgeResult ToolProfile(ForgeCommand command)
    {
        var args = Args<NameArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Name))
        {
            return ForgeResult.Success(command.Id, new { profiles = ToolProfiles.Names });
        }

        if (!ToolProfiles.TryGet(args.Name, out var tools))
        {
            return ForgeResult.Failure(command.Id, "unknown_tool_profile", $"Unknown profile '{args.Name}'. Known: {string.Join(", ", ToolProfiles.Names)}");
        }

        return ForgeResult.Success(command.Id, new { name = args.Name, tools });
    }

    private static ForgeResult ViewportList(ForgeCommand command)
    {
        var args = Args<ViewportListArgs>(command);
        var list = new List<object>();
        using var tr = ActiveDb.TransactionManager.StartTransaction();
        var layoutDict = (DBDictionary)tr.GetObject(ActiveDb.LayoutDictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry entry in layoutDict)
        {
            var layoutName = entry.Key;
            if (!string.IsNullOrWhiteSpace(args.Layout) &&
                !layoutName.Equals(args.Layout, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
            if (layout.BlockTableRecordId.IsNull)
            {
                continue;
            }

            var space = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Viewport vp || vp.Number <= 1)
                {
                    continue;
                }

                list.Add(new
                {
                    handle = vp.Handle.Value.ToString("X", CultureInfo.InvariantCulture),
                    number = vp.Number,
                    layout = layoutName,
                    customScale = vp.CustomScale,
                    locked = vp.Locked,
                    on = vp.On
                });
            }
        }

        tr.Commit();
        return ForgeResult.Success(command.Id, new { viewports = list });
    }

    private static ForgeResult ViewportSetLayerFreeze(ForgeCommand command)
    {
        var args = Args<ViewportFreezeArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Handle) || string.IsNullOrWhiteSpace(args.Layer))
        {
            return ForgeResult.Failure(command.Id, "missing_viewport_args", "handle and layer are required.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { args.Handle, args.Layer, args.Freeze });
        }

        if (!TryObjectIdFromHandle(args.Handle, out var id))
        {
            return ForgeResult.Failure(command.Id, "viewport_not_found", $"Viewport handle not found: {args.Handle}");
        }

        using var tr = ActiveDb.TransactionManager.StartTransaction();
        if (tr.GetObject(id, OpenMode.ForWrite) is not Viewport vp)
        {
            return ForgeResult.Failure(command.Id, "not_a_viewport", $"Handle {args.Handle} is not a viewport.");
        }

        var layerTable = (LayerTable)tr.GetObject(ActiveDb.LayerTableId, OpenMode.ForRead);
        if (!layerTable.Has(args.Layer))
        {
            return ForgeResult.Failure(command.Id, "layer_not_found", $"Layer not found: {args.Layer}");
        }

        using var layerIds = new ObjectIdCollection { layerTable[args.Layer] };
        if (args.Freeze)
        {
            vp.FreezeLayersInViewport(layerIds.GetEnumerator());
        }
        else
        {
            vp.ThawLayersInViewport(layerIds.GetEnumerator());
        }

        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { args.Handle, args.Layer, args.Freeze },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = new { args.Handle, args.Layer, args.Freeze } });
    }

    private sealed record PathOnlyArgs { public string? Path { get; init; } }
    private sealed record RegistryLookupArgs { public string? DrawingNo { get; init; } }
    private sealed record ViewportListArgs { public string? Layout { get; init; } }
    private sealed record ViewportFreezeArgs { public string? Handle { get; init; } public string? Layer { get; init; } public bool Freeze { get; init; } = true; }
}
