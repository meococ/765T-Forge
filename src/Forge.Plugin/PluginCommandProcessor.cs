using System.Collections;
using System.Globalization;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.PlottingServices;
using Autodesk.AutoCAD.Publishing;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Forge.Shared;

namespace Forge.Plugin;

public sealed partial class PluginCommandProcessor
{
    private readonly ForgeEnvironment _environment;
    private readonly SafetyPolicy _safetyPolicy;
    private readonly BackupPlanner _backupPlanner;
    private readonly FileAuditSink _auditSink;

    public PluginCommandProcessor(
        ForgeEnvironment environment,
        SafetyPolicy safetyPolicy,
        BackupPlanner backupPlanner,
        FileAuditSink auditSink)
    {
        _environment = environment;
        _safetyPolicy = safetyPolicy;
        _backupPlanner = backupPlanner;
        _auditSink = auditSink;
    }

    public ForgeResult Process(ForgeCommand command)
    {
        if (!string.Equals(command.AuthToken, _environment.Token, StringComparison.Ordinal))
        {
            return ForgeResult.Failure(command.Id, "unauthorized", "Invalid Forge named pipe token.");
        }

        var decision = _safetyPolicy.Evaluate(command);
        var auditId = _auditSink.WriteBestEffort(new AuditRecord
        {
            Source = "plugin",
            Tool = command.Tool,
            CommandId = command.Id,
            DryRun = command.DryRun,
            Allowed = decision.Allowed,
            DecisionCode = decision.Code,
            Args = command.Args
        });

        command = command with { AuditId = auditId };

        if (!decision.Allowed)
        {
            return ForgeResult.Failure(command.Id, decision.Code, decision.Message, decision.Suggestion, auditId);
        }

        if (ForgeToolRegistry.Get(command.Tool).Unsafe && !_environment.EnableUnsafeOps)
        {
            return ForgeResult.Failure(command.Id, "unsafe_ops_disabled", "Unsafe operations are disabled by FORGE_ENABLE_UNSAFE_OPS.", "Leave exec_dotnet disabled unless this is a controlled local session.", auditId);
        }

        var metadata = ForgeToolRegistry.Get(command.Tool);
        string? backupPath = null;
        if (!metadata.ReadOnly && metadata.RequiresBackup && !command.DryRun)
        {
            backupPath = _backupPlanner.TryBackup(ActiveDocumentPath());
        }

        ForgeResult Dispatch() => command.Tool.ToLowerInvariant() switch
        {
            "forge_system_health" => Health(command),
            "forge_system_version" => Version(command),
            "forge_system_getvar" => GetVar(command),
            "forge_system_setvar" => SetVar(command),
            "forge_system_capabilities" => SystemCapabilities(command),
            "forge_doc_list_open" => ListOpenDocuments(command),
            "forge_doc_list_layouts" => ListLayouts(command),
            "forge_doc_open" => OpenDocument(command),
            "forge_doc_save" => SaveDocument(command),
            "forge_xref_list" => ListXrefs(command),
            "forge_xref_reload" => ReloadXrefs(command),
            "forge_xref_repath" => RepathXref(command),
            "forge_xref_normalize_relative" => NormalizeXrefsRelative(command),
            "forge_layer_list" => ListLayers(command),
            "forge_layer_state_list" => ListLayerStates(command),
            "forge_layer_state_restore" => RestoreLayerState(command),
            "forge_block_list_attributes" => ListBlockAttributes(command),
            "forge_block_get_attr" => GetBlockAttribute(command),
            "forge_block_set_attr" => SetBlockAttribute(command),
            "forge_block_campaign" => BlockCampaign(command),
            "forge_layout_page_setup_import" => PageSetupImport(command),
            "forge_layout_page_setup_apply" => PageSetupApply(command),
            "forge_plot_to_pdf" => PlotToPdf(command),
            "forge_plot_publish" => PlotPublish(command),
            "forge_qa_verify_titleblock" => VerifyTitleblock(command),
            "forge_qa_check_xrefs" => CheckXrefs(command),
            "forge_qa_audit_layers" => AuditLayers(command),
            "forge_qa_readback" => Readback(command),
            "forge_qa_readback_after_timeout" => ReadbackAfterTimeout(command),
            "forge_qa_preflight" => QaPreflight(command),
            "forge_issue_set_validate" => IssueSetValidate(command),
            "forge_recipe_issue_set" => RecipeIssueSet(command),
            "forge_pack_and_go" => PackAndGo(command),
            "forge_registry_load" => RegistryLoad(command),
            "forge_registry_lookup" => RegistryLookup(command),
            "forge_pack_load" => PackLoad(command),
            "forge_pack_status" => PackStatus(command),
            "forge_system_tool_profile" => ToolProfile(command),
            "forge_viewport_list" => ViewportList(command),
            "forge_viewport_set_layer_freeze" => ViewportSetLayerFreeze(command),
            "forge_exec_command" => ExecCommand(command),
            "forge_exec_lisp" => ExecLisp(command),
            "forge_exec_dotnet" => ExecDotNet(command),
            "forge_qa_plot_fingerprint" => QaPlotFingerprint(command),
            "forge_qa_dependency_closure" => QaDependencyClosure(command),
            "forge_qa_dual_source" => QaDualSource(command),
            "forge_qa_modal_trap" => QaModalTrap(command),
            "forge_xref_closure" => XrefClosure(command),
            "forge_xref_pin_save" => XrefPinSave(command),
            "forge_xref_pin_verify" => XrefPinVerify(command),
            "forge_transmittal_seal" => TransmittalSealCreate(command),
            "forge_publish_ceremony_check" => PublishCeremonyCheck(command),
            "forge_cde_gate_evaluate" => CdeGateEvaluate(command),
            _ => ForgeResult.Failure(command.Id, "unknown_tool", $"Unknown Forge tool: {command.Tool}")
        };

        var result = metadata.ReadOnly || command.DryRun
            ? Dispatch()
            : WithUndoMark(Dispatch);

        return result with
        {
            AuditId = auditId,
            Data = result.Ok && backupPath is not null
                ? new { result.Data, backupPath }
                : result.Data
        };
    }

    private static ForgeResult WithUndoMark(Func<ForgeResult> action)
    {
        // AutoCAD 2026 Document no longer exposes StartUndoMark/EndUndoMark on the managed Document type.
        // Use a synchronous UNDO group so typed writes remain one Ctrl+Z unit when possible.
        try
        {
            ActiveEditor.Command("_.UNDO", "_M");
        }
        catch
        {
            // Best effort — still execute the action if undo mark cannot be opened.
        }

        try
        {
            return action();
        }
        finally
        {
            try
            {
                ActiveEditor.Command("_.UNDO", "_E");
            }
            catch
            {
                // Best effort close.
            }
        }
    }

    private static DocumentCollection Documents => Application.DocumentManager;
    private static Document ActiveDoc => Documents.MdiActiveDocument ?? throw new InvalidOperationException("No active AutoCAD document.");
    private static Database ActiveDb => ActiveDoc.Database;
    private static Editor ActiveEditor => ActiveDoc.Editor;

    private static T Args<T>(ForgeCommand command) where T : new()
    {
        return ForgeJson.FromElement<T>(command.Args) ?? new T();
    }

    private static string? ActiveDocumentPath()
    {
        var name = Documents.MdiActiveDocument?.Name;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static ForgeResult DryRun(ForgeCommand command, object data)
    {
        return ForgeResult.Success(command.Id, new { dryRun = true, data });
    }

    private ForgeResult Health(ForgeCommand command)
    {
        return ForgeResult.Success(command.Id, new
        {
            status = "ok",
            server = ForgeConstants.ServerName,
            pipe = _environment.PipeName,
            activeDocument = ActiveDocumentPath()
        });
    }

    private static ForgeResult Version(ForgeCommand command)
    {
        return ForgeResult.Success(command.Id, new
        {
            forge = ForgeConstants.ProductVersion,
            envelope = ForgeConstants.EnvelopeVersion,
            autocadTarget = ForgeConstants.AutoCadVersion,
            autocadApplication = Application.Version.ToString(),
            dotnet = Environment.Version.ToString()
        });
    }

    private static ForgeResult GetVar(ForgeCommand command)
    {
        var args = Args<NameArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Name))
        {
            return ForgeResult.Failure(command.Id, "missing_var_name", "System variable name is required.");
        }

        var value = Application.GetSystemVariable(args.Name);
        return ForgeResult.Success(command.Id, new { name = args.Name, value });
    }

    private static ForgeResult SetVar(ForgeCommand command)
    {
        var args = Args<SetVarArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Name))
        {
            return ForgeResult.Failure(command.Id, "missing_var_name", "System variable name is required.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { args.Name, args.Value });
        }

        var before = Application.GetSystemVariable(args.Name);
        var converted = ConvertSystemVariableValue(args.Value, before);
        Application.SetSystemVariable(args.Name, converted);
        var after = Application.GetSystemVariable(args.Name);

        return ForgeResult.Success(
            command.Id,
            new { name = args.Name, before, after },
            verification: new ForgeVerification
            {
                Attempted = true,
                Passed = Equals(after?.ToString(), converted?.ToString()),
                Message = "System variable read back after set.",
                ReadBack = after
            });
    }

    private static object? ConvertSystemVariableValue(string? value, object? current)
    {
        if (current is null || value is null)
        {
            return value;
        }

        var target = current.GetType();
        if (target == typeof(short)) return short.Parse(value, CultureInfo.InvariantCulture);
        if (target == typeof(int)) return int.Parse(value, CultureInfo.InvariantCulture);
        if (target == typeof(double)) return double.Parse(value, CultureInfo.InvariantCulture);
        if (target == typeof(bool)) return bool.Parse(value);
        return value;
    }

    private static ForgeResult ListOpenDocuments(ForgeCommand command)
    {
        var docs = Documents.Cast<Document>().Select(d => new { name = d.Name, isActive = ReferenceEquals(d, Documents.MdiActiveDocument) }).ToArray();
        return ForgeResult.Success(command.Id, new { documents = docs });
    }

    private static ForgeResult ListLayouts(ForgeCommand command)
    {
        var layouts = new List<object>();
        using var tr = ActiveDb.TransactionManager.StartTransaction();
        var layoutDict = (DBDictionary)tr.GetObject(ActiveDb.LayoutDictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry entry in layoutDict)
        {
            var layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
            layouts.Add(new
            {
                name = layout.LayoutName,
                tabOrder = layout.TabOrder,
                modelType = layout.ModelType
            });
        }

        tr.Commit();
        return ForgeResult.Success(command.Id, new { layouts = layouts.OrderBy(x => x.GetType().GetProperty("tabOrder")?.GetValue(x)) });
    }

    private static ForgeResult OpenDocument(ForgeCommand command)
    {
        var args = Args<PathArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Path))
        {
            return ForgeResult.Failure(command.Id, "missing_path", "DWG path is required.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { args.Path });
        }

        if (!File.Exists(args.Path))
        {
            return ForgeResult.Failure(command.Id, "dwg_not_found", $"DWG not found: {args.Path}");
        }

        var previousFileDia = Application.GetSystemVariable("FILEDIA");
        Application.SetSystemVariable("FILEDIA", 0);
        Documents.Open(args.Path, false);
        Application.SetSystemVariable("FILEDIA", previousFileDia);
        return ForgeResult.Success(command.Id, new { opened = args.Path });
    }

    private ForgeResult SaveDocument(ForgeCommand command)
    {
        var args = Args<PathArgs>(command);
        if (command.DryRun)
        {
            return DryRun(command, new { current = ActiveDocumentPath(), saveAs = args.Path, args.OverwriteAcknowledged });
        }

        if (string.IsNullOrWhiteSpace(args.Path))
        {
            ActiveDb.Save();
            return ForgeResult.Success(command.Id, new { saved = ActiveDocumentPath() });
        }

        var activePath = ActiveDocumentPath();
        var destinationExists = File.Exists(args.Path);
        var samePath = PathsEqual(activePath, args.Path);

        if (destinationExists && !samePath && !args.OverwriteAcknowledged)
        {
            return ForgeResult.Failure(
                command.Id,
                "saveas_overwrite_not_acknowledged",
                $"Refusing to overwrite an existing different drawing: {args.Path}",
                "Pass overwriteAcknowledged=true after confirming the destination backup policy.");
        }

        string? destinationBackup = null;
        if (destinationExists && !samePath)
        {
            destinationBackup = _backupPlanner.TryBackup(args.Path);
        }

        ActiveDb.SaveAs(args.Path, DwgVersion.Current);
        return ForgeResult.Success(command.Id, new { savedAs = args.Path, destinationBackup });
    }

    private static ForgeResult ListXrefs(ForgeCommand command)
    {
        return ForgeResult.Success(command.Id, new { xrefs = ReadXrefs() });
    }

    private static object[] ReadXrefs()
    {
        var xrefs = new List<object>();
        using var tr = ActiveDb.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)tr.GetObject(ActiveDb.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId id in blockTable)
        {
            var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (!btr.IsFromExternalReference && !btr.IsFromOverlayReference)
            {
                continue;
            }

            xrefs.Add(new
            {
                name = btr.Name,
                path = btr.PathName,
                isUnloaded = btr.IsUnloaded,
                isOverlay = btr.IsFromOverlayReference,
                status = btr.XrefStatus.ToString(),
                pathExists = XrefPathExists(btr.PathName)
            });
        }

        tr.Commit();
        return xrefs.ToArray();
    }

    private static bool XrefPathExists(string? pathName)
    {
        if (string.IsNullOrWhiteSpace(pathName))
        {
            return false;
        }

        try
        {
            if (Path.IsPathRooted(pathName))
            {
                return File.Exists(Path.GetFullPath(pathName));
            }

            var host = ActiveDocumentPath();
            if (string.IsNullOrWhiteSpace(host))
            {
                return File.Exists(pathName);
            }

            var hostDir = Path.GetDirectoryName(Path.GetFullPath(host));
            if (string.IsNullOrWhiteSpace(hostDir))
            {
                return File.Exists(pathName);
            }

            return File.Exists(Path.GetFullPath(Path.Combine(hostDir, pathName)));
        }
        catch
        {
            return false;
        }
    }

    private static ForgeResult ReloadXrefs(ForgeCommand command)
    {
        var args = Args<NameArgs>(command);
        using var tr = ActiveDb.TransactionManager.StartTransaction();
        var ids = FindXrefIds(tr, args.Name);
        if (ids.Count == 0)
        {
            return ForgeResult.Failure(command.Id, "xref_not_found", string.IsNullOrWhiteSpace(args.Name) ? "No xrefs found." : $"Xref not found: {args.Name}");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { args.Name, count = ids.Count });
        }

        ActiveDb.ReloadXrefs(ids);
        tr.Commit();
        return ForgeResult.Success(command.Id, new { reloaded = ids.Count }, verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = ReadXrefs() });
    }

    private static ForgeResult RepathXref(ForgeCommand command)
    {
        var args = Args<XrefRepathArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Name) || string.IsNullOrWhiteSpace(args.Path))
        {
            return ForgeResult.Failure(command.Id, "missing_xref_args", "name and path are required.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { args.Name, args.Path, args.Reload });
        }

        using var tr = ActiveDb.TransactionManager.StartTransaction();
        var ids = FindXrefIds(tr, args.Name);
        if (ids.Count == 0)
        {
            return ForgeResult.Failure(command.Id, "xref_not_found", $"Xref not found: {args.Name}");
        }

        foreach (ObjectId id in ids)
        {
            var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForWrite);
            btr.PathName = args.Path;
        }

        tr.Commit();
        if (args.Reload)
        {
            ActiveDb.ReloadXrefs(ids);
        }

        var readBack = ReadXrefs();
        return ForgeResult.Success(command.Id, new { repathed = args.Name, args.Path }, verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = readBack });
    }

    private static ObjectIdCollection FindXrefIds(Transaction tr, string? name)
    {
        var ids = new ObjectIdCollection();
        var blockTable = (BlockTable)tr.GetObject(ActiveDb.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId id in blockTable)
        {
            var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if ((!btr.IsFromExternalReference && !btr.IsFromOverlayReference) ||
                (!string.IsNullOrWhiteSpace(name) && !btr.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            ids.Add(id);
        }

        return ids;
    }

    private static ForgeResult ListLayers(ForgeCommand command)
    {
        var layers = new List<object>();
        using var tr = ActiveDb.TransactionManager.StartTransaction();
        var layerTable = (LayerTable)tr.GetObject(ActiveDb.LayerTableId, OpenMode.ForRead);
        foreach (ObjectId id in layerTable)
        {
            var layer = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            layers.Add(new
            {
                name = layer.Name,
                color = layer.Color.ColorIndex,
                isOff = layer.IsOff,
                isFrozen = layer.IsFrozen,
                isLocked = layer.IsLocked,
                lineWeight = layer.LineWeight.ToString(),
                isPlottable = layer.IsPlottable
            });
        }

        tr.Commit();
        return ForgeResult.Success(command.Id, new { layers });
    }

    private static ForgeResult ListLayerStates(ForgeCommand command)
    {
        var result = InvokeLayerStateManager("GetLayerStateNames");
        return ForgeResult.Success(command.Id, new { layerStates = result });
    }

    private static ForgeResult RestoreLayerState(ForgeCommand command)
    {
        var args = Args<NameArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Name))
        {
            return ForgeResult.Failure(command.Id, "missing_layer_state", "Layer state name is required.");
        }

        if (RejectControlChars(command, ("name", args.Name)) is { } reject)
        {
            return reject;
        }

        if (command.DryRun)
        {
            return DryRun(command, new { args.Name });
        }

        try
        {
            var manager = ActiveDb.LayerStateManager;
            var mask = (LayerStateMasks)0xFFFF;
            manager.RestoreLayerState(args.Name, ObjectId.Null, 1, mask);
            return ForgeResult.Success(
                command.Id,
                new { restored = args.Name, sync = true },
                verification: new ForgeVerification
                {
                    Attempted = true,
                    Passed = true,
                    Message = "Layer state restored synchronously via LayerStateManager.",
                    ReadBack = InvokeLayerStateManager("GetLayerStateNames")
                });
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(
                command.Id,
                "layer_state_restore_failed",
                $"Failed to restore layer state '{args.Name}': {ex.Message}",
                "Call forge_layer_state_list and verify the exact state name.");
        }
    }

    private static object InvokeLayerStateManager(string methodName)
    {
        var prop = typeof(Database).GetProperty("LayerStateManager");
        var manager = prop?.GetValue(ActiveDb);
        if (manager is null)
        {
            return Array.Empty<string>();
        }

        var method = manager.GetType().GetMethod(methodName, Type.EmptyTypes);
        var value = method?.Invoke(manager, null);
        if (value is IEnumerable enumerable && value is not string)
        {
            return enumerable.Cast<object>().Select(x => x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        }

        return value?.ToString() ?? "";
    }

    private static ForgeResult ListBlockAttributes(ForgeCommand command)
    {
        var args = Args<BlockAttrArgs>(command);
        var attrs = FindBlockAttributes(args.BlockName, args.Handle, forWrite: false)
            .Select(x => new { x.BlockHandle, x.BlockName, x.Tag, x.Value })
            .ToArray();
        return ForgeResult.Success(command.Id, new { attributes = attrs });
    }

    private static ForgeResult GetBlockAttribute(ForgeCommand command)
    {
        var args = Args<BlockAttrArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Tag))
        {
            return ForgeResult.Failure(command.Id, "missing_attr_tag", "Attribute tag is required.");
        }

        var attr = FindBlockAttributes(args.BlockName, args.Handle, forWrite: false)
            .FirstOrDefault(x => x.Tag.Equals(args.Tag, StringComparison.OrdinalIgnoreCase));
        return attr is null
            ? ForgeResult.Failure(command.Id, "attr_not_found", $"Attribute not found: {args.Tag}")
            : ForgeResult.Success(command.Id, new { attr.BlockHandle, attr.BlockName, attr.Tag, attr.Value });
    }

    private static ForgeResult SetBlockAttribute(ForgeCommand command)
    {
        var args = Args<BlockAttrArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Tag))
        {
            return ForgeResult.Failure(command.Id, "missing_attr_tag", "Attribute tag is required.");
        }

        if (AuthorizeAttributeWrite(command, args.Tag!, args.Value) is { } denied)
        {
            return denied;
        }

        if (command.DryRun)
        {
            return DryRun(command, new { args.BlockName, args.Handle, args.Tag, args.Value });
        }

        using var tr = ActiveDb.TransactionManager.StartTransaction();
        var matches = FindBlockReferences(tr, args.BlockName, args.Handle).ToArray();
        var updated = 0;
        var readBack = new List<object>();
        foreach (var br in matches)
        {
            foreach (ObjectId attrId in br.AttributeCollection)
            {
                if (tr.GetObject(attrId, OpenMode.ForWrite) is not AttributeReference attr ||
                    !attr.Tag.Equals(args.Tag, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                attr.TextString = args.Value ?? "";
                updated++;
                readBack.Add(new { blockHandle = br.Handle.Value.ToString("X"), blockName = br.Name, tag = attr.Tag, value = attr.TextString });
            }
        }

        tr.Commit();
        return ForgeResult.Success(
            command.Id,
            new { updated, readBack },
            verification: new ForgeVerification { Attempted = true, Passed = updated > 0, ReadBack = readBack });
    }

    private static ForgeResult? AuthorizeAttributeWrite(ForgeCommand command, string tag, string? value)
    {
        var registry = DrawingRegistryStore.Current;
        if (registry is not null && !registry.TryAuthorizeAttribute(tag, value ?? "", out var code, out var message))
        {
            return ForgeResult.Failure(command.Id, code!, message!, "Load the project sheet register or use a drawingNo from forge_registry_lookup.");
        }

        var pack = StandardsPackStore.Current;
        if (pack is not null && DrawingRegistry.IsDrawingNumberTag(tag) &&
            !pack.TryValidateDrawingNo(value ?? "", out var packCode, out var packMessage))
        {
            return ForgeResult.Failure(command.Id, packCode!, packMessage!);
        }

        return null;
    }

    private static IEnumerable<BlockAttributeValue> FindBlockAttributes(string? blockName, string? handle, bool forWrite)
    {
        using var tr = ActiveDb.TransactionManager.StartTransaction();
        foreach (var br in FindBlockReferences(tr, blockName, handle))
        {
            foreach (ObjectId attrId in br.AttributeCollection)
            {
                if (tr.GetObject(attrId, forWrite ? OpenMode.ForWrite : OpenMode.ForRead) is AttributeReference attr)
                {
                    yield return new BlockAttributeValue(br.Handle.Value.ToString("X"), br.Name, attr.Tag, attr.TextString);
                }
            }
        }

        tr.Commit();
    }

    private static IEnumerable<BlockReference> FindBlockReferences(Transaction tr, string? blockName, string? handle)
    {
        if (!string.IsNullOrWhiteSpace(handle))
        {
            if (!TryObjectIdFromHandle(handle, out var id))
            {
                yield break;
            }

            if (tr.GetObject(id, OpenMode.ForRead) is BlockReference br)
            {
                yield return br;
            }

            yield break;
        }

        foreach (ObjectId spaceId in EnumerateSpaceIds(tr))
        {
            var space = (BlockTableRecord)tr.GetObject(spaceId, OpenMode.ForRead);
            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is BlockReference br &&
                    (string.IsNullOrWhiteSpace(blockName) || br.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return br;
                }
            }
        }
    }

    private static IEnumerable<ObjectId> EnumerateSpaceIds(Transaction tr)
    {
        var blockTable = (BlockTable)tr.GetObject(ActiveDb.BlockTableId, OpenMode.ForRead);
        yield return blockTable[BlockTableRecord.ModelSpace];

        var layoutDict = (DBDictionary)tr.GetObject(ActiveDb.LayoutDictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry entry in layoutDict)
        {
            var layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
            if (!layout.BlockTableRecordId.IsNull)
            {
                yield return layout.BlockTableRecordId;
            }
        }
    }

    private static bool TryObjectIdFromHandle(string handle, out ObjectId objectId)
    {
        objectId = ObjectId.Null;
        try
        {
            objectId = ActiveDb.GetObjectId(false, new Handle(Convert.ToInt64(handle, 16)), 0);
            return !objectId.IsNull;
        }
        catch
        {
            return false;
        }
    }

    private static ForgeResult PageSetupImport(ForgeCommand command)
    {
        var args = Args<PageSetupImportArgs>(command);
        if (string.IsNullOrWhiteSpace(args.TemplatePath) || string.IsNullOrWhiteSpace(args.SetupName))
        {
            return ForgeResult.Failure(command.Id, "missing_page_setup_args", "templatePath and setupName are required.");
        }

        if (RejectControlChars(command, ("templatePath", args.TemplatePath), ("setupName", args.SetupName)) is { } reject)
        {
            return reject;
        }

        if (!File.Exists(args.TemplatePath))
        {
            return ForgeResult.Failure(command.Id, "template_not_found", $"Template not found: {args.TemplatePath}");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { args.TemplatePath, args.SetupName, mode = "sync_copy_preferred" });
        }

        try
        {
            using var sourceDb = new Database(false, true);
            sourceDb.ReadDwgFile(args.TemplatePath, FileOpenMode.OpenForReadAndAllShare, true, null);
            using var sourceTr = sourceDb.TransactionManager.StartTransaction();
            var sourceDict = (DBDictionary)sourceTr.GetObject(sourceDb.PlotSettingsDictionaryId, OpenMode.ForRead);
            if (!sourceDict.Contains(args.SetupName))
            {
                return ForgeResult.Failure(command.Id, "page_setup_not_in_template", $"Page setup '{args.SetupName}' was not found in the template.");
            }

            var sourcePs = (PlotSettings)sourceTr.GetObject(sourceDict.GetAt(args.SetupName), OpenMode.ForRead);
            using var destTr = ActiveDb.TransactionManager.StartTransaction();
            var destDict = (DBDictionary)destTr.GetObject(ActiveDb.PlotSettingsDictionaryId, OpenMode.ForWrite);
            PlotSettings destPs;
            if (destDict.Contains(args.SetupName))
            {
                destPs = (PlotSettings)destTr.GetObject(destDict.GetAt(args.SetupName), OpenMode.ForWrite);
            }
            else
            {
                destPs = new PlotSettings(sourcePs.ModelType);
                destPs.CopyFrom(sourcePs);
                destDict.SetAt(args.SetupName, destPs);
                destTr.AddNewlyCreatedDBObject(destPs, true);
            }

            destPs.CopyFrom(sourcePs);
            sourceTr.Commit();
            destTr.Commit();
            return ForgeResult.Success(command.Id, new
            {
                queued = false,
                completed = true,
                mode = "sync_copy",
                args.TemplatePath,
                args.SetupName
            });
        }
        catch (System.Exception ex)
        {
            // Fall back to classic command queue — honesty flags required.
            ActiveDoc.SendStringToExecute($"_.-PSETUPIN \"{EscapeCommand(args.TemplatePath)}\" \"{EscapeCommand(args.SetupName)}\" ", true, false, false);
            return ForgeResult.Success(command.Id, new
            {
                queued = true,
                completed = false,
                mode = "queued_psetupin",
                fallbackReason = ex.Message,
                args.TemplatePath,
                args.SetupName
            });
        }
    }

    private static ForgeResult PageSetupApply(ForgeCommand command)
    {
        var args = Args<PageSetupApplyArgs>(command);
        if (string.IsNullOrWhiteSpace(args.SetupName))
        {
            return ForgeResult.Failure(command.Id, "missing_page_setup", "setupName is required.");
        }

        if (RejectControlChars(command, ("setupName", args.SetupName), ("layout", args.Layout)) is { } reject)
        {
            return reject;
        }

        if (command.DryRun)
        {
            return DryRun(command, args);
        }

        var layoutName = string.IsNullOrWhiteSpace(args.Layout)
            ? LayoutManager.Current.CurrentLayout
            : args.Layout!;

        try
        {
            using var tr = ActiveDb.TransactionManager.StartTransaction();
            var plotDict = (DBDictionary)tr.GetObject(ActiveDb.PlotSettingsDictionaryId, OpenMode.ForRead);
            if (!plotDict.Contains(args.SetupName!))
            {
                return ForgeResult.Failure(
                    command.Id,
                    "page_setup_not_found",
                    $"Page setup not found: {args.SetupName}",
                    "Call forge_system_capabilities to list page setups, or import from a DWT first.");
            }

            var layoutId = LayoutManager.Current.GetLayoutId(layoutName);
            if (layoutId.IsNull)
            {
                return ForgeResult.Failure(command.Id, "layout_not_found", $"Layout not found: {layoutName}");
            }

            var layout = (Layout)tr.GetObject(layoutId, OpenMode.ForWrite);
            var source = (PlotSettings)tr.GetObject(plotDict.GetAt(args.SetupName!), OpenMode.ForRead);
            layout.CopyFrom(source);
            tr.Commit();

            return ForgeResult.Success(
                command.Id,
                new { applied = args.SetupName, layout = layoutName, sync = true },
                verification: new ForgeVerification
                {
                    Attempted = true,
                    Passed = true,
                    Message = "Page setup copied onto layout via PlotSettings API.",
                    ReadBack = new { layout = layoutName, setupName = args.SetupName }
                });
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(
                command.Id,
                "page_setup_apply_failed",
                $"Failed to apply page setup: {ex.Message}",
                "Verify the setup exists and the layout name is exact.");
        }
    }

    private static ForgeResult PlotToPdf(ForgeCommand command)
    {
        var args = Args<PlotArgs>(command);
        if (string.IsNullOrWhiteSpace(args.OutputPath))
        {
            return ForgeResult.Failure(command.Id, "missing_output_path", "outputPath is required.");
        }

        if (RejectControlChars(command, ("outputPath", args.OutputPath), ("layout", args.Layout), ("device", args.Device), ("paperSize", args.PaperSize), ("plotStyle", args.PlotStyle)) is { } reject)
        {
            return reject;
        }

        var outputPath = Path.GetFullPath(args.OutputPath!);
        var device = string.IsNullOrWhiteSpace(args.Device) ? "DWG To PDF.pc3" : args.Device!;
        var paperSize = string.IsNullOrWhiteSpace(args.PaperSize) ? "ISO A1 (841.00 x 594.00 MM)" : args.PaperSize!;
        var orientation = string.IsNullOrWhiteSpace(args.Orientation) ? "Landscape" : args.Orientation!;
        var scale = string.IsNullOrWhiteSpace(args.Scale) ? "Fit" : args.Scale!;
        var plotStyle = string.IsNullOrWhiteSpace(args.PlotStyle) ? "." : args.PlotStyle!;
        var units = string.IsNullOrWhiteSpace(args.Units) ? (paperSize.Contains("MM", StringComparison.OrdinalIgnoreCase) ? "Millimeters" : "Inches") : args.Units!;

        if (command.DryRun)
        {
            return DryRun(command, new
            {
                outputPath,
                args.Layout,
                device,
                paperSize,
                orientation,
                scale,
                plotStyle,
                units,
                args.PlotArea,
                args.OverwriteAcknowledged,
                pstyleMode = Application.GetSystemVariable("PSTYLEMODE")
            });
        }

        if (File.Exists(outputPath) && !args.OverwriteAcknowledged)
        {
            return ForgeResult.Failure(
                command.Id,
                "plot_overwrite_not_acknowledged",
                $"Refusing to overwrite existing PDF: {outputPath}",
                "Pass overwriteAcknowledged=true after confirming the previous issue PDF is archived.");
        }

        if (!string.IsNullOrWhiteSpace(args.Layout))
        {
            try
            {
                LayoutManager.Current.CurrentLayout = args.Layout;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return ForgeResult.Failure(command.Id, "layout_not_found", $"Layout not found: {args.Layout}", "Call forge_doc_list_layouts to see available layouts.");
            }
        }

        var layoutName = LayoutManager.Current.CurrentLayout;
        var isModel = string.Equals(layoutName, "Model", StringComparison.OrdinalIgnoreCase);
        var plotArea = string.IsNullOrWhiteSpace(args.PlotArea)
            ? (isModel ? "Extents" : "Layout")
            : args.PlotArea!;

        var previousFileDia = Application.GetSystemVariable("FILEDIA");
        var previousBgPlot = Application.GetSystemVariable("BACKGROUNDPLOT");
        Application.SetSystemVariable("FILEDIA", 0);
        Application.SetSystemVariable("BACKGROUNDPLOT", 0);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var tokens = new List<object>
            {
                "-PLOT",
                "Yes",
                layoutName,
                device,
                paperSize,
                units,
                orientation,
                "No",
                plotArea,
                scale,
                "0,0",
                "Yes",
                plotStyle,
                "Yes",
            };

            if (isModel)
            {
                tokens.Add("As displayed");
            }
            else
            {
                tokens.Add("No");
                tokens.Add("No");
                tokens.Add("No");
            }

            tokens.Add(outputPath);
            tokens.Add("No");
            tokens.Add("Yes");

            ActiveEditor.Command(tokens.ToArray());
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(
                command.Id,
                "plot_failed",
                $"Plot command failed: {ex.Message}",
                "Verify device/paper/plot style via forge_system_capabilities and that the layout is plottable.");
        }
        finally
        {
            Application.SetSystemVariable("FILEDIA", previousFileDia);
            Application.SetSystemVariable("BACKGROUNDPLOT", previousBgPlot);
        }

        if (!File.Exists(outputPath))
        {
            return ForgeResult.Failure(command.Id, "plot_no_output", $"Plot ran but no PDF was produced at {outputPath}.", "Check the output directory is writable and the layout has plottable content.");
        }

        return ForgeResult.Success(
            command.Id,
            new
            {
                outputPath,
                layout = layoutName,
                device,
                paperSize,
                plotStyle,
                bytes = new FileInfo(outputPath).Length
            },
            verification: new ForgeVerification
            {
                Attempted = true,
                Passed = true,
                Message = "PDF file exists after plot.",
                ReadBack = new { outputPath, exists = true }
            });
    }

    private ForgeResult PlotPublish(ForgeCommand command)
    {
        var args = Args<PublishArgs>(command);
        if (string.IsNullOrWhiteSpace(args.OutputPath) || args.Layouts.Length == 0)
        {
            return ForgeResult.Failure(command.Id, "missing_publish_args", "outputPath and at least one layout are required.");
        }

        if (!ForcePublishGate.IsAllowed(args.Force, _environment.AllowForcePublish))
        {
            return ForgeResult.Failure(
                command.Id,
                ForcePublishGate.DenyCode,
                ForcePublishGate.DenyMessage,
                ForcePublishGate.DenySuggestion);
        }

        var outputPath = Path.GetFullPath(args.OutputPath!);
        var dwgPath = ActiveDocumentPath();
        if (string.IsNullOrWhiteSpace(dwgPath) || !File.Exists(dwgPath))
        {
            return ForgeResult.Failure(command.Id, "document_not_saved", "Active document must be saved to a DWG path before DSD publish.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new
            {
                outputPath,
                dwgPath,
                args.Layouts,
                args.SinglePdf,
                args.OverwriteAcknowledged,
                sheetType = args.SinglePdf ? "MultiPdf" : "SinglePdf",
                force = args.Force,
                allowForcePublish = _environment.AllowForcePublish
            });
        }

        if (File.Exists(outputPath) && !args.OverwriteAcknowledged)
        {
            return ForgeResult.Failure(
                command.Id,
                "publish_overwrite_not_acknowledged",
                $"Refusing to overwrite existing publish output: {outputPath}",
                "Pass overwriteAcknowledged=true after confirming the previous issue set is archived.");
        }

        QaReport? preflightReport = null;
        if (args.RequirePreflight)
        {
            preflightReport = BuildPreflightReport(args.RequiredTitleblockTags, args.TitleblockBlockName);
            if (!preflightReport.Passed && !args.Force)
            {
                return new ForgeResult
                {
                    Id = command.Id,
                    Ok = false,
                    Error = new ForgeError(
                        "publish_preflight_failed",
                        "Publish readiness gate failed.",
                        "Fix findings from forge_qa_preflight or pass force=true with FORGE_ALLOW_FORCE_PUBLISH=true."),
                    Data = preflightReport
                };
            }
        }

        var dsdPath = Path.Combine(Path.GetTempPath(), $"forge-publish-{command.Id}.dsd");
        var previousBgPlot = Application.GetSystemVariable("BACKGROUNDPLOT");
        object? previousBgCore = null;
        var bgCoreRestored = false;
        Application.SetSystemVariable("BACKGROUNDPLOT", 0);
        try
        {
            previousBgCore = Application.GetSystemVariable("BGCOREPUBLISH");
            Application.SetSystemVariable("BGCOREPUBLISH", 0);
        }
        catch
        {
            previousBgCore = null;
        }

        var dsdFailed = false;
        string? dsdError = null;
        string? dsdExceptionType = null;
        var dsdWritten = false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            WriteDsdFile(dsdPath, dwgPath!, outputPath, args.Layouts, args.SinglePdf);
            dsdWritten = File.Exists(dsdPath);
            using var progress = new PlotProgressDialog(false, args.Layouts.Length, true);
            Application.Publisher.PublishDsd(dsdPath, progress);
        }
        catch (System.Exception ex)
        {
            dsdFailed = true;
            dsdError = ex.Message;
            dsdExceptionType = ex.GetType().FullName;
        }
        finally
        {
            Application.SetSystemVariable("BACKGROUNDPLOT", previousBgPlot);
            if (previousBgCore is not null)
            {
                try
                {
                    Application.SetSystemVariable("BGCOREPUBLISH", previousBgCore);
                    bgCoreRestored = true;
                }
                catch
                {
                    // Best-effort restore.
                }
            }

            try
            {
                if (File.Exists(dsdPath))
                {
                    File.Delete(dsdPath);
                }
            }
            catch
            {
                // Best-effort temp cleanup.
            }
        }

        if (dsdFailed || !File.Exists(outputPath))
        {
            var fallbackReason = dsdFailed ? "dsd_exception" : "publish_no_output";
            var fallback = TryPublishViaPlotFallback(
                command,
                outputPath,
                args.Layouts,
                args.SinglePdf,
                preflightReport,
                new
                {
                    fallbackReason,
                    dsdFailed,
                    dsdError,
                    dsdExceptionType,
                    dsdWritten,
                    bgCorePublishForced = previousBgCore is not null,
                    bgCoreRestored
                });
            if (fallback is not null)
            {
                return fallback;
            }

            return ForgeResult.Failure(
                command.Id,
                dsdFailed ? "publish_failed" : "publish_no_output",
                dsdFailed
                    ? $"DSD publish failed ({dsdExceptionType}): {dsdError}"
                    : $"Publisher finished but output was not found at {outputPath}, and plot fallback also failed.",
                "Check plot log and that layouts contain plottable content.");
        }

        return BuildPublishSuccess(
            command,
            dwgPath,
            outputPath,
            args,
            dsd: true,
            fallback: null,
            preflightReport,
            extraData: new { dsdWritten, bgCorePublishForced = previousBgCore is not null, bgCoreRestored });
    }

    private static ForgeResult? TryPublishViaPlotFallback(
        ForgeCommand command,
        string outputPath,
        string[] layouts,
        bool singlePdf,
        QaReport? preflightReport,
        object? diagnostics = null)
    {
        var produced = new List<string>();
        var dir = Path.GetDirectoryName(outputPath)!;
        var baseName = Path.GetFileNameWithoutExtension(outputPath);
        Directory.CreateDirectory(dir);

        foreach (var layout in layouts)
        {
            var target = singlePdf && layouts.Length == 1
                ? outputPath
                : Path.Combine(dir, $"{baseName}-{SanitizeFileToken(layout)}.pdf");

            if (!TryPlotLayoutToPdf(target, layout, out var error))
            {
                return ForgeResult.Failure(
                    command.Id,
                    "publish_fallback_failed",
                    $"DSD produced no output; plot fallback failed on layout '{layout}': {error}",
                    "Verify device/paper via forge_system_capabilities.");
            }

            produced.Add(target);
        }

        var primary = singlePdf && layouts.Length == 1
            ? outputPath
            : produced[0];

        var singlePdfUnmerged = singlePdf && layouts.Length > 1 && !File.Exists(outputPath) && produced.Count > 0;
        if (singlePdfUnmerged)
        {
            primary = produced[0];
        }

        if (!File.Exists(primary))
        {
            return null;
        }

        var args = Args<PublishArgs>(command);
        return BuildPublishSuccess(
            command,
            ActiveDocumentPath(),
            primary,
            args,
            dsd: false,
            fallback: "plot_to_pdf",
            preflightReport: preflightReport,
            extraData: new
            {
                producedFiles = produced.ToArray(),
                requestedSinglePdf = singlePdf,
                singlePdfUnmerged,
                note = singlePdfUnmerged
                    ? "singlePdf requested but DSD fallback cannot merge multi-layout into one PDF; primary is first layout only."
                    : null,
                diagnostics
            });
    }

    private static string SanitizeFileToken(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }

        return value;
    }

    private static bool TryPlotLayoutToPdf(
        string outputPath,
        string? layout,
        out string? error,
        string? device = null,
        string? paperSize = null,
        string? plotStyle = null,
        string? plotArea = null,
        string? orientation = null,
        string? scale = null,
        string? units = null)
    {
        error = null;
        device = string.IsNullOrWhiteSpace(device) ? "DWG To PDF.pc3" : device!;
        paperSize = string.IsNullOrWhiteSpace(paperSize) ? "ISO A1 (841.00 x 594.00 MM)" : paperSize!;
        orientation = string.IsNullOrWhiteSpace(orientation) ? "Landscape" : orientation!;
        scale = string.IsNullOrWhiteSpace(scale) ? "Fit" : scale!;
        plotStyle = string.IsNullOrWhiteSpace(plotStyle) ? "." : plotStyle!;
        units ??= paperSize.Contains("MM", StringComparison.OrdinalIgnoreCase) ? "Millimeters" : "Inches";

        if (!string.IsNullOrWhiteSpace(layout))
        {
            try
            {
                LayoutManager.Current.CurrentLayout = layout;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                error = $"Layout not found: {layout}";
                return false;
            }
        }

        var layoutName = LayoutManager.Current.CurrentLayout;
        var isModel = string.Equals(layoutName, "Model", StringComparison.OrdinalIgnoreCase);
        plotArea = string.IsNullOrWhiteSpace(plotArea)
            ? (isModel ? "Extents" : "Layout")
            : plotArea!;

        var previousFileDia = Application.GetSystemVariable("FILEDIA");
        var previousBgPlot = Application.GetSystemVariable("BACKGROUNDPLOT");
        Application.SetSystemVariable("FILEDIA", 0);
        Application.SetSystemVariable("BACKGROUNDPLOT", 0);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var tokens = new List<object>
            {
                "-PLOT",
                "Yes",
                layoutName,
                device,
                paperSize,
                units,
                orientation,
                "No",
                plotArea,
                scale,
                "0,0",
                "Yes",
                plotStyle,
                "Yes",
            };

            if (isModel)
            {
                tokens.Add("As displayed");
            }
            else
            {
                tokens.Add("No");
                tokens.Add("No");
                tokens.Add("No");
            }

            tokens.Add(outputPath);
            tokens.Add("No");
            tokens.Add("Yes");

            ActiveEditor.Command(tokens.ToArray());
        }
        catch (System.Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            Application.SetSystemVariable("FILEDIA", previousFileDia);
            Application.SetSystemVariable("BACKGROUNDPLOT", previousBgPlot);
        }

        if (!File.Exists(outputPath))
        {
            error = $"No PDF at {outputPath}";
            return false;
        }

        return true;
    }

    private static ForgeResult BuildPublishSuccess(
        ForgeCommand command,
        string? dwgPath,
        string outputPath,
        PublishArgs args,
        bool dsd,
        string? fallback,
        QaReport? preflightReport,
        object? extraData = null)
    {
        var probe = PdfProbeResult.Probe(outputPath, expectedPages: args.SinglePdf ? Math.Max(1, args.Layouts.Length) : 1);
        if (fallback == "plot_to_pdf" && args.SinglePdf && args.Layouts.Length > 1)
        {
            // Per-layout PDFs: probe the primary file only (1 page expected).
            probe = PdfProbeResult.Probe(outputPath, expectedPages: 1);
        }

        var message = fallback is null
            ? (probe.Passed ? "Publish + PDF probe passed." : "Publish wrote a file but PDF probe failed.")
            : (probe.Passed
                ? $"Publish via fallback '{fallback}' + PDF probe passed."
                : $"Publish via fallback '{fallback}' wrote a file but PDF probe failed.");
        if (fallback == "plot_to_pdf" && args.SinglePdf && args.Layouts.Length > 1)
        {
            message += " Warning: singlePdf+multi-layout fallback did not produce one merged PDF.";
        }

        var receipt = new PublishReceipt
        {
            DocumentPath = dwgPath,
            OutputPath = outputPath,
            Layouts = args.Layouts,
            SinglePdf = args.SinglePdf,
            DeviceName = "DWG To PDF.pc3",
            PreflightReportId = preflightReport?.Id ?? (args.RequirePreflight ? "preflight" : null),
            PreflightHash = preflightReport is null ? null : new PublishReceipt().ComputePreflightHash(preflightReport),
            AuditId = command.AuditId,
            OutputBytes = new FileInfo(outputPath).Length,
            OutputMtimeUtc = File.GetLastWriteTimeUtc(outputPath),
            PdfProbe = probe,
            VerificationPassed = probe.Passed,
            Message = message
        };
        receipt.ArtifactPath = PublishReceipt.TryWriteArtifact(receipt);

        return ForgeResult.Success(
            command.Id,
            new
            {
                outputPath,
                layouts = args.Layouts,
                singlePdf = args.SinglePdf,
                bytes = receipt.OutputBytes,
                dsd,
                fallback,
                receipt,
                extra = extraData
            },
            verification: new ForgeVerification
            {
                Attempted = true,
                Passed = probe.Passed,
                Message = receipt.Message,
                ReadBack = receipt
            });
    }

    private static ForgeResult VerifyTitleblock(ForgeCommand command)
    {
        var args = Args<VerifyTitleblockArgs>(command);
        var attrs = FindBlockAttributes(args.BlockName, null, forWrite: false).ToArray();
        var diffs = new List<object>();
        foreach (var expected in args.Expected)
        {
            var actual = attrs.FirstOrDefault(a => a.Tag.Equals(expected.Key, StringComparison.OrdinalIgnoreCase));
            if (actual is null || !string.Equals(actual.Value, expected.Value, StringComparison.Ordinal))
            {
                diffs.Add(new { tag = expected.Key, expected = expected.Value, actual = actual?.Value });
            }
        }

        return ForgeResult.Success(command.Id, new { passed = diffs.Count == 0, diffs, readBack = attrs });
    }

    private static ForgeResult CheckXrefs(ForgeCommand command)
    {
        var xrefs = ReadXrefs();
        var bad = xrefs.Where(x =>
        {
            var type = x.GetType();
            return Equals(type.GetProperty("isUnloaded")?.GetValue(x), true) ||
                   Equals(type.GetProperty("pathExists")?.GetValue(x), false);
        }).ToArray();
        return ForgeResult.Success(command.Id, new { passed = bad.Length == 0, issues = bad, xrefs });
    }

    private static ForgeResult AuditLayers(ForgeCommand command)
    {
        var args = Args<AuditLayerArgs>(command);
        var layersResult = ListLayers(command);
        var layerJson = ForgeJson.ToElement(layersResult.Data);
        var names = layerJson.GetProperty("layers").EnumerateArray().Select(x => x.GetProperty("name").GetString()).Where(x => x is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = args.ExpectedLayers.Where(x => !names.Contains(x)).ToArray();
        return ForgeResult.Success(command.Id, new { passed = missing.Length == 0, missing });
    }

    private static ForgeResult Readback(ForgeCommand command)
    {
        var args = Args<ReadbackArgs>(command);
        return args.TargetTool switch
        {
            "forge_xref_repath" or "forge_xref_reload" => ListXrefs(command),
            "forge_block_set_attr" => ListBlockAttributes(command),
            "forge_system_setvar" => ForgeResult.Success(command.Id, new { message = "Call forge_system_getvar with the variable name for precise read-back." }),
            _ => ForgeResult.Success(command.Id, new { message = $"No specialized read-back registered for {args.TargetTool}." })
        };
    }

    private static ForgeResult ReadbackAfterTimeout(ForgeCommand command)
    {
        var inner = Readback(command);
        return ForgeResult.Success(command.Id, new
        {
            protocol = "timeout_recovery",
            guidance = "Do not retry the timed-out write. Inspect read-back; only re-issue if the mutation did not apply.",
            readBack = inner.Data,
            innerOk = inner.Ok
        }, verification: new ForgeVerification
        {
            Attempted = true,
            Passed = inner.Ok,
            Message = "Timeout recovery read-back completed.",
            ReadBack = inner.Data
        });
    }

    private static ForgeResult ExecCommand(ForgeCommand command)
    {
        var args = Args<ExecCommandArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Command))
        {
            return ForgeResult.Failure(command.Id, "missing_command", "command is required.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { args.Command });
        }

        var tokens = TokenizeAutoCadCommand(args.Command);
        if (tokens.Length > 0)
        {
            try
            {
                ActiveEditor.Command(tokens.Cast<object>().ToArray());
                return ForgeResult.Success(command.Id, new
                {
                    queued = false,
                    completed = true,
                    mode = "editor_command",
                    args.Command,
                    tokens
                });
            }
            catch (System.Exception ex)
            {
                ActiveDoc.SendStringToExecute(args.Command.TrimEnd() + " ", true, false, false);
                return ForgeResult.Success(command.Id, new
                {
                    queued = true,
                    completed = false,
                    mode = "queued_fallback",
                    fallbackReason = ex.Message,
                    args.Command
                });
            }
        }

        ActiveDoc.SendStringToExecute(args.Command.TrimEnd() + " ", true, false, false);
        return ForgeResult.Success(command.Id, new { queued = true, completed = false, mode = "queued", args.Command });
    }

    private static ForgeResult ExecLisp(ForgeCommand command)
    {
        var args = Args<ExecLispArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Lisp))
        {
            return ForgeResult.Failure(command.Id, "missing_lisp", "lisp is required.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { args.Lisp });
        }

        var lisp = args.Lisp.Trim();
        try
        {
            // Synchronous when AutoCAD accepts the expression as a command-line token.
            ActiveEditor.Command(lisp);
            return ForgeResult.Success(command.Id, new
            {
                queued = false,
                completed = true,
                mode = "editor_command"
            });
        }
        catch (System.Exception ex)
        {
            ActiveDoc.SendStringToExecute(lisp + " ", true, false, false);
            return ForgeResult.Success(command.Id, new
            {
                queued = true,
                completed = false,
                mode = "queued_fallback",
                fallbackReason = ex.Message
            });
        }
    }

    private static string[] TokenizeAutoCadCommand(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in command.Trim())
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens.ToArray();
    }

    private static ForgeResult ExecDotNet(ForgeCommand command)
    {
        var args = Args<ExecDotNetArgs>(command);
        if (string.IsNullOrWhiteSpace(args.Code))
        {
            return ForgeResult.Failure(command.Id, "missing_code", "code is required.");
        }

        if (!command.UnsafeAcknowledged)
        {
            return ForgeResult.Failure(command.Id, "dotnet_not_acknowledged", "exec_dotnet requires unsafeAcknowledged=true.");
        }

        if (command.DryRun)
        {
            return DryRun(command, new { chars = args.Code.Length });
        }

        if (ContainsAwait(args.Code))
        {
            return ForgeResult.Failure(
                command.Id,
                "dotnet_async_not_supported",
                "forge_exec_dotnet currently rejects await/async scripts to avoid deadlocks inside AutoCAD's command context.",
                "Use synchronous snippets only, or promote the workflow into a typed tool.");
        }

        var options = ScriptOptions.Default
            .AddReferences(typeof(Application).Assembly, typeof(Database).Assembly, typeof(Editor).Assembly)
            .AddImports(
                "System",
                "System.Linq",
                "Autodesk.AutoCAD.ApplicationServices",
                "Autodesk.AutoCAD.DatabaseServices",
                "Autodesk.AutoCAD.EditorInput",
                "Autodesk.AutoCAD.Geometry");

        var globals = new DotNetScriptGlobals(ActiveDoc, ActiveDb, ActiveEditor);
        var result = CSharpScript.EvaluateAsync<object?>(args.Code, options, globals).GetAwaiter().GetResult();
        return ForgeResult.Success(command.Id, new { result });
    }

    private static bool ContainsAwait(string code)
    {
        return code.Contains("await ", StringComparison.Ordinal) ||
               code.Contains("async ", StringComparison.Ordinal);
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

        private static void WriteDsdFile(string dsdPath, string dwgPath, string outputPath, string[] layouts, bool singlePdf)
        {
            DsdWriter.WriteFile(dsdPath, dwgPath, outputPath, layouts, singlePdf);
        }

    private static string EscapeCommand(string value) => value.Replace("\"", "\\\"");

    private static readonly char[] CommandControlChars = { '\r', '\n' };

    // Typed tools that dispatch a name/path through SendStringToExecute only escape quotes,
    // so a newline in the value could smuggle a second command onto the AutoCAD command line.
    // Since the OpenWorld denylist does not run on typed tools, reject control characters here.
    private static ForgeResult? RejectControlChars(ForgeCommand command, params (string Field, string? Value)[] inputs)
    {
        foreach (var (field, value) in inputs)
        {
            if (!string.IsNullOrEmpty(value) && value.IndexOfAny(CommandControlChars) >= 0)
            {
                return ForgeResult.Failure(
                    command.Id,
                    "illegal_control_character",
                    $"Parameter '{field}' contains a newline; command-dispatched tools require single-line values.",
                    "Remove CR/LF characters from the name or path.");
            }
        }

        return null;
    }

    private sealed record NameArgs { public string? Name { get; init; } }
    private sealed record SetVarArgs { public string? Name { get; init; } public string? Value { get; init; } }
    private sealed record PathArgs { public string? Path { get; init; } public bool OverwriteAcknowledged { get; init; } }
    private sealed record XrefRepathArgs { public string? Name { get; init; } public string? Path { get; init; } public bool Reload { get; init; } = true; }
    private sealed record BlockAttrArgs { public string? BlockName { get; init; } public string? Handle { get; init; } public string? Tag { get; init; } public string? Value { get; init; } }
    private sealed record PageSetupImportArgs { public string? TemplatePath { get; init; } public string? SetupName { get; init; } }
    private sealed record PageSetupApplyArgs { public string? SetupName { get; init; } public string? Layout { get; init; } }
    private sealed record PlotArgs
    {
        public string? OutputPath { get; init; }
        public string? Layout { get; init; }
        public string? Device { get; init; }
        public string? PaperSize { get; init; }
        public string? PlotStyle { get; init; }
        public string? PlotArea { get; init; }
        public string? Orientation { get; init; }
        public string? Scale { get; init; }
        public string? Units { get; init; }
        public bool OverwriteAcknowledged { get; init; }
    }
    private sealed record PublishArgs
    {
        public string? OutputPath { get; init; }
        public string[] Layouts { get; init; } = [];
        public bool SinglePdf { get; init; } = true;
        public bool OverwriteAcknowledged { get; init; }
        public bool RequirePreflight { get; init; } = true;
        public bool Force { get; init; }
        public string[] RequiredTitleblockTags { get; init; } = [];
        public string? TitleblockBlockName { get; init; }
    }
    private sealed record VerifyTitleblockArgs { public Dictionary<string, string> Expected { get; init; } = []; public string? BlockName { get; init; } }
    private sealed record AuditLayerArgs { public string[] ExpectedLayers { get; init; } = []; }
    private sealed record ReadbackArgs { public string TargetTool { get; init; } = ""; }
    private sealed record ExecCommandArgs { public string? Command { get; init; } }
    private sealed record ExecLispArgs { public string? Lisp { get; init; } }
    private sealed record ExecDotNetArgs { public string? Code { get; init; } }
    private sealed record BlockAttributeValue(string BlockHandle, string BlockName, string Tag, string Value);
}

public sealed record DotNetScriptGlobals(Document Document, Database Database, Editor Editor);
