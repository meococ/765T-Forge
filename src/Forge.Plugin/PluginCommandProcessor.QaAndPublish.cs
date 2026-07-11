using System.Text;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.PlottingServices;
using Forge.Shared;

namespace Forge.Plugin;

public sealed partial class PluginCommandProcessor
{
    private static ForgeResult SystemCapabilities(ForgeCommand command)
    {
        var devices = new List<object>();
        try
        {
            var deviceList = PlotConfigManager.Devices;
            for (var i = 0; i < deviceList.Count; i++)
            {
                var info = deviceList[i];
                var media = new List<string>();
                try
                {
                    var config = PlotConfigManager.SetCurrentConfig(info.DeviceName);
                    var mediaProp = config.GetType().GetProperty("CanonicalMediaNameList")
                                    ?? config.GetType().GetProperty("CanonicalMediaNames");
                    var mediaValue = mediaProp?.GetValue(config);
                    if (mediaValue is System.Collections.IEnumerable enumerable and not string)
                    {
                        foreach (var item in enumerable)
                        {
                            if (item is not null)
                            {
                                media.Add(item.ToString()!);
                            }
                        }
                    }
                    else
                    {
                        var method = config.GetType().GetMethod("GetCanonicalMediaNameList", Type.EmptyTypes);
                        if (method?.Invoke(config, null) is System.Collections.IEnumerable list)
                        {
                            foreach (var item in list)
                            {
                                if (item is not null)
                                {
                                    media.Add(item.ToString()!);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Some devices reject media enumeration outside plot context.
                }

                devices.Add(new
                {
                    name = info.DeviceName,
                    media = media.Take(40).ToArray(),
                    mediaTruncated = media.Count > 40
                });
            }
        }
        catch (System.Exception ex)
        {
            devices.Add(new { error = ex.Message });
        }

        var pageSetups = new List<string>();
        var layouts = new List<string>();
        using (var tr = ActiveDb.TransactionManager.StartTransaction())
        {
            var plotDict = (DBDictionary)tr.GetObject(ActiveDb.PlotSettingsDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in plotDict)
            {
                pageSetups.Add(entry.Key);
            }

            var layoutDict = (DBDictionary)tr.GetObject(ActiveDb.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in layoutDict)
            {
                layouts.Add(entry.Key);
            }

            tr.Commit();
        }

        var plotStyles = new List<string>();
        try
        {
            var stylePath = Convert.ToString(Application.GetSystemVariable("ROAMABLEROOTPREFIX")) ?? "";
            var plotters = Path.Combine(stylePath, "Plotters", "Plot Styles");
            if (Directory.Exists(plotters))
            {
                plotStyles.AddRange(Directory.EnumerateFiles(plotters, "*.ctb").Select(Path.GetFileName)!);
                plotStyles.AddRange(Directory.EnumerateFiles(plotters, "*.stb").Select(Path.GetFileName)!);
            }
        }
        catch
        {
            // Best effort.
        }

        return ForgeResult.Success(command.Id, new
        {
            pstyleMode = Application.GetSystemVariable("PSTYLEMODE"),
            devices,
            pageSetups,
            layouts,
            layerStates = InvokeLayerStateManager("GetLayerStateNames"),
            plotStyles = plotStyles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray()
        });
    }

    private static ForgeResult NormalizeXrefsRelative(ForgeCommand command)
    {
        var args = Args<NormalizeXrefArgs>(command);
        var hostPath = ActiveDocumentPath();
        if (string.IsNullOrWhiteSpace(hostPath) || !File.Exists(hostPath))
        {
            return ForgeResult.Failure(command.Id, "document_not_saved", "Active document must be saved before relative xref normalization.");
        }

        var hostDir = Path.GetDirectoryName(Path.GetFullPath(hostPath))!;
        var changes = new List<object>();

        using var tr = ActiveDb.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)tr.GetObject(ActiveDb.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId id in blockTable)
        {
            var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (!btr.IsFromExternalReference && !btr.IsFromOverlayReference)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(args.Name) &&
                !btr.Name.Equals(args.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var current = btr.PathName;
            if (string.IsNullOrWhiteSpace(current))
            {
                continue;
            }

            string absolute;
            try
            {
                absolute = Path.IsPathRooted(current)
                    ? Path.GetFullPath(current)
                    : Path.GetFullPath(Path.Combine(hostDir, current));
            }
            catch
            {
                continue;
            }

            if (!File.Exists(absolute))
            {
                changes.Add(new { name = btr.Name, current, skipped = "path_missing" });
                continue;
            }

            var relative = GetRelativePath(hostDir, absolute);
            if (string.Equals(current, relative, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(new { name = btr.Name, current, relative, changed = false });
                continue;
            }

            changes.Add(new { name = btr.Name, current, relative, changed = true });
            if (!command.DryRun)
            {
                btr.UpgradeOpen();
                btr.PathName = relative;
            }
        }

        if (command.DryRun)
        {
            return DryRun(command, new { hostDir, changes });
        }

        tr.Commit();
        if (args.Reload)
        {
            using var reloadTr = ActiveDb.TransactionManager.StartTransaction();
            var reloadIds = FindXrefIds(reloadTr, args.Name);
            if (reloadIds.Count > 0)
            {
                ActiveDb.ReloadXrefs(reloadIds);
            }

            reloadTr.Commit();
        }

        return ForgeResult.Success(
            command.Id,
            new { hostDir, changes },
            verification: new ForgeVerification
            {
                Attempted = true,
                Passed = true,
                ReadBack = ReadXrefs()
            });
    }

    private static string GetRelativePath(string relativeTo, string path)
    {
        var fromUri = new Uri(AppendDirectorySeparator(relativeTo));
        var toUri = new Uri(path);
        var relative = Uri.UnescapeDataString(fromUri.MakeRelativeUri(toUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
        return relative;
    }

    private static string AppendDirectorySeparator(string path)
    {
        if (!path.EndsWith(Path.DirectorySeparatorChar) && !path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path + Path.DirectorySeparatorChar;
        }

        return path;
    }

    private static ForgeResult QaPreflight(ForgeCommand command)
    {
        var args = Args<PreflightArgs>(command);
        var report = BuildPreflightReport(args.RequiredTitleblockTags, args.TitleblockBlockName, args.ExpectedLayers);
        var artifactPath = TryWriteQaArtifact(report);
        report = report with { ArtifactPath = artifactPath };
        return ForgeResult.Success(command.Id, report, verification: new ForgeVerification
        {
            Attempted = true,
            Passed = report.Passed,
            Message = report.Passed ? "Publish readiness gate passed." : "Publish readiness gate failed.",
            ReadBack = report
        });
    }

    private static ForgeResult IssueSetValidate(ForgeCommand command)
    {
        var args = Args<IssueSetValidateArgs>(command);
        if (!string.IsNullOrWhiteSpace(args.ContractPath))
        {
            if (!File.Exists(args.ContractPath))
            {
                return ForgeResult.Failure(command.Id, "contract_not_found", $"Contract not found: {args.ContractPath}");
            }

            try
            {
                var loaded = IssueSetContract.LoadFromFile(args.ContractPath);
                IssueSetContractStore.Set(loaded);
            }
            catch (Exception ex)
            {
                return ForgeResult.Failure(command.Id, "contract_invalid", ex.Message);
            }
        }

        var contract = IssueSetContractStore.Current;
        if (contract is null)
        {
            return ForgeResult.Failure(
                command.Id,
                "contract_not_loaded",
                "No issue-set contract loaded. Pass contractPath or import via forge_sheet_inventory_import then load the JSON.");
        }

        var findings = contract.ValidateAgainst(ListLayoutNames(), DrawingRegistryStore.Current).ToList();

        // Revision consistency: REV attr vs contract/registry when present
        var attrs = FindBlockAttributes(null, null, forWrite: false).ToArray();
        var revAttr = attrs.FirstOrDefault(a => a.Tag.Equals("REV", StringComparison.OrdinalIgnoreCase));
        if (revAttr is not null && !string.IsNullOrWhiteSpace(contract.Rev)
            && !revAttr.Value.Equals(contract.Rev, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new QaFinding(
                "rev_attr_contract_mismatch",
                "error",
                $"Titleblock REV '{revAttr.Value}' != contract rev '{contract.Rev}'.",
                "Align revision before publish.",
                "forge_block_set_attr"));
        }

        var report = QaReport.FromFindings(ActiveDocumentPath(), findings, new
        {
            contractId = contract.ContractId,
            sheetCount = contract.Sheets.Count
        });
        return ForgeResult.Success(command.Id, report, verification: new ForgeVerification
        {
            Attempted = true,
            Passed = report.Passed,
            Message = report.Passed ? "Issue set contract valid." : "Issue set contract failed.",
            ReadBack = report
        });
    }

    private sealed record IssueSetValidateArgs
    {
        public string? ContractPath { get; init; }
    }

    private static QaReport BuildPreflightReport(
        string[]? requiredTags,
        string? titleblockBlockName,
        string[]? expectedLayers = null)
    {
        var findings = new List<QaFinding>();
        var document = ActiveDocumentPath();

        var xrefs = ReadXrefs();
        foreach (var xref in xrefs)
        {
            var type = xref.GetType();
            var name = type.GetProperty("name")?.GetValue(xref)?.ToString() ?? "?";
            var unloaded = Equals(type.GetProperty("isUnloaded")?.GetValue(xref), true);
            var pathExists = Equals(type.GetProperty("pathExists")?.GetValue(xref), true);
            if (unloaded || !pathExists)
            {
                findings.Add(new QaFinding(
                    "xref_unhealthy",
                    "error",
                    $"Xref '{name}' is unloaded or missing.",
                    "Use forge_xref_repath / forge_xref_reload / forge_xref_normalize_relative.",
                    "forge_xref_list"));
            }
        }

        var pstyleMode = Application.GetSystemVariable("PSTYLEMODE");
        findings.Add(new QaFinding(
            "pstyle_mode",
            "info",
            $"PSTYLEMODE={pstyleMode} (1=CTB color-dependent, 0=STB named).",
            "Confirm plot style type matches office CTB/STB standards."));

        if (requiredTags is { Length: > 0 })
        {
            var attrs = FindBlockAttributes(titleblockBlockName, null, forWrite: false).ToArray();
            foreach (var tag in requiredTags)
            {
                var match = attrs.FirstOrDefault(a => a.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    findings.Add(new QaFinding(
                        "titleblock_tag_missing",
                        "error",
                        $"Required titleblock tag '{tag}' was not found.",
                        "Confirm block name / paper-space titleblock.",
                        "forge_block_list_attributes"));
                }
                else if (string.IsNullOrWhiteSpace(match.Value) || match.Value.Contains("####", StringComparison.Ordinal))
                {
                    findings.Add(new QaFinding(
                        "titleblock_tag_empty",
                        "error",
                        $"Titleblock tag '{tag}' is empty or unresolved ('{match.Value}').",
                        "Fill via forge_block_set_attr or forge_block_campaign.",
                        "forge_block_set_attr"));
                }
            }
        }

        if (expectedLayers is { Length: > 0 })
        {
            using var tr = ActiveDb.TransactionManager.StartTransaction();
            var layerTable = (LayerTable)tr.GetObject(ActiveDb.LayerTableId, OpenMode.ForRead);
            var names = layerTable.Cast<ObjectId>()
                .Select(id => ((LayerTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            tr.Commit();
            foreach (var layer in expectedLayers)
            {
                if (!names.Contains(layer))
                {
                    findings.Add(new QaFinding(
                        "layer_missing",
                        "warning",
                        $"Expected layer '{layer}' is missing.",
                        SuggestedTool: "forge_qa_audit_layers"));
                }
            }
        }

        var fieldHits = ScanUnresolvedFields();
        foreach (var hit in fieldHits)
        {
            findings.Add(new QaFinding(
                "unresolved_field",
                "error",
                hit,
                "Resolve sheet fields or fill attributes from the sheet register.",
                "forge_block_campaign"));
        }

        var pack = StandardsPackStore.Current;
        if (pack is not null)
        {
            using var layerTr = ActiveDb.TransactionManager.StartTransaction();
            var layerTable = (LayerTable)layerTr.GetObject(ActiveDb.LayerTableId, OpenMode.ForRead);
            var present = layerTable.Cast<ObjectId>()
                .Select(id => ((LayerTableRecord)layerTr.GetObject(id, OpenMode.ForRead)).Name)
                .ToArray();
            layerTr.Commit();
            findings.AddRange(pack.EvaluateLayers(present));

            int? bgPlot = null;
            try
            {
                bgPlot = Convert.ToInt32(Application.GetSystemVariable("BACKGROUNDPLOT"));
            }
            catch
            {
                // Best effort.
            }

            findings.AddRange(pack.EvaluatePlotBindings(null, null, null, bgPlot));

            foreach (var tag in pack.RequiredTitleblockTags)
            {
                if (requiredTags is { Length: > 0 } &&
                    requiredTags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var attrs = FindBlockAttributes(titleblockBlockName, null, forWrite: false).ToArray();
                var match = attrs.FirstOrDefault(a => a.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));
                if (match is null || string.IsNullOrWhiteSpace(match.Value) || match.Value.Contains("####", StringComparison.Ordinal))
                {
                    findings.Add(new QaFinding(
                        "pack_titleblock_required",
                        "error",
                        $"Standards pack '{pack.PackId}' requires titleblock tag '{tag}'.",
                        "Fill via forge_block_campaign from the drawing registry.",
                        "forge_block_set_attr"));
                }
            }
        }
        else
        {
            // Without a pack, still warn when background plot is enabled (tribal knowledge).
            try
            {
                var bg = Convert.ToInt32(Application.GetSystemVariable("BACKGROUNDPLOT"));
                if (bg != 0)
                {
                    findings.Add(new QaFinding(
                        "background_plot_enabled",
                        "warning",
                        $"BACKGROUNDPLOT={bg}; Forge publish forces 0, but other plot paths may hang.",
                        "Set BACKGROUNDPLOT=0 before agent plot sessions.",
                        "forge_system_setvar"));
                }
            }
            catch
            {
                // Best effort.
            }
        }

        var contract = IssueSetContractStore.Current;
        if (contract is not null)
        {
            var layoutNames = ListLayoutNames();
            findings.AddRange(contract.ValidateAgainst(layoutNames, DrawingRegistryStore.Current));
        }

        return QaReport.FromFindings(document, findings, new
        {
            xrefCount = xrefs.Length,
            pstyleMode,
            packId = pack?.PackId,
            contractId = contract?.ContractId,
            registryProjectId = DrawingRegistryStore.Current?.ProjectId
        });
    }

    private static string[] ListLayoutNames()
    {
        var names = new List<string>();
        using var tr = ActiveDb.TransactionManager.StartTransaction();
        var dict = (DBDictionary)tr.GetObject(ActiveDb.LayoutDictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry entry in dict)
        {
            names.Add(entry.Key);
        }

        tr.Commit();
        return names.ToArray();
    }

    private static List<string> ScanUnresolvedFields()
    {
        var hits = new List<string>();
        using var tr = ActiveDb.TransactionManager.StartTransaction();
        foreach (var spaceId in EnumerateSpaceIds(tr))
        {
            var space = (BlockTableRecord)tr.GetObject(spaceId, OpenMode.ForRead);
            foreach (ObjectId id in space)
            {
                var obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is DBText dbText && dbText.TextString.Contains("####", StringComparison.Ordinal))
                {
                    hits.Add($"DBText handle {dbText.Handle} contains ####");
                }
                else if (obj is MText mText && mText.Contents.Contains("####", StringComparison.Ordinal))
                {
                    hits.Add($"MText handle {mText.Handle} contains ####");
                }
                else if (obj is AttributeDefinition attDef && attDef.TextString.Contains("####", StringComparison.Ordinal))
                {
                    hits.Add($"AttributeDefinition {attDef.Tag} contains ####");
                }
                else if (obj is BlockReference br)
                {
                    foreach (ObjectId attrId in br.AttributeCollection)
                    {
                        if (tr.GetObject(attrId, OpenMode.ForRead) is AttributeReference attr &&
                            attr.TextString.Contains("####", StringComparison.Ordinal))
                        {
                            hits.Add($"Attribute {attr.Tag} on block {br.Name} contains ####");
                        }
                    }
                }
            }
        }

        tr.Commit();
        return hits;
    }

    private static string? TryWriteQaArtifact(QaReport report)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "765T-Forge",
                "qa");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"qa-{report.Id}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(report, ForgeJson.Options), Encoding.UTF8);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private ForgeResult BlockCampaign(ForgeCommand command)
    {
        var args = Args<CampaignArgs>(command);
        if (args.Entries.Length == 0)
        {
            return ForgeResult.Failure(command.Id, "missing_campaign_entries", "entries array is required.");
        }

        var diffs = new List<object>();
        var updated = 0;

        foreach (var entry in args.Entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.Layout))
            {
                try
                {
                    LayoutManager.Current.CurrentLayout = entry.Layout;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    return ForgeResult.Failure(command.Id, "layout_not_found", $"Layout not found: {entry.Layout}");
                }
            }

            foreach (var pair in entry.Attributes)
            {
                if (AuthorizeAttributeWrite(command, pair.Key, pair.Value) is { } denied)
                {
                    return denied;
                }

                var before = FindBlockAttributes(entry.BlockName, entry.Handle, forWrite: false)
                    .FirstOrDefault(a => a.Tag.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
                diffs.Add(new
                {
                    layout = entry.Layout,
                    blockName = entry.BlockName,
                    tag = pair.Key,
                    before = before?.Value,
                    after = pair.Value
                });

                if (command.DryRun)
                {
                    continue;
                }

                var setCommand = new ForgeCommand
                {
                    Id = command.Id,
                    Tool = "forge_block_set_attr",
                    Args = ForgeJson.ToElement(new
                    {
                        tag = pair.Key,
                        value = pair.Value,
                        blockName = entry.BlockName,
                        handle = entry.Handle
                    }),
                    DryRun = false
                };
                var setResult = SetBlockAttribute(setCommand);
                if (!setResult.Ok)
                {
                    return setResult;
                }

                updated++;
            }
        }

        if (command.DryRun)
        {
            return DryRun(command, new { diffs });
        }

        return ForgeResult.Success(
            command.Id,
            new { updated, diffs },
            verification: new ForgeVerification { Attempted = true, Passed = updated > 0, ReadBack = diffs });
    }

    private ForgeResult RecipeIssueSet(ForgeCommand command)
    {
        var args = Args<IssueSetArgs>(command);
        if (string.IsNullOrWhiteSpace(args.OutputPath) || args.Layouts.Length == 0)
        {
            return ForgeResult.Failure(command.Id, "missing_issue_set_args", "outputPath and layouts are required.");
        }

        var steps = new List<object>();

        if (args.NormalizeXrefs)
        {
            var normalize = NormalizeXrefsRelative(new ForgeCommand
            {
                Id = command.Id,
                Tool = "forge_xref_normalize_relative",
                Args = ForgeJson.ToElement(new { reload = true }),
                DryRun = command.DryRun
            });
            steps.Add(new { step = "normalize_xrefs", normalize.Ok, normalize.Data, normalize.Error });
            if (!normalize.Ok)
            {
                return normalize;
            }
        }

        if (!string.IsNullOrWhiteSpace(args.LayerState))
        {
            var restore = RestoreLayerState(new ForgeCommand
            {
                Id = command.Id,
                Tool = "forge_layer_state_restore",
                Args = ForgeJson.ToElement(new { name = args.LayerState }),
                DryRun = command.DryRun
            });
            steps.Add(new { step = "layer_state", restore.Ok, restore.Data, restore.Error });
            if (!restore.Ok)
            {
                return restore;
            }
        }

        if (args.Campaign is { Entries.Length: > 0 })
        {
            var campaign = BlockCampaign(new ForgeCommand
            {
                Id = command.Id,
                Tool = "forge_block_campaign",
                Args = ForgeJson.ToElement(args.Campaign),
                DryRun = command.DryRun
            });
            steps.Add(new { step = "titleblock_campaign", campaign.Ok, campaign.Data, campaign.Error });
            if (!campaign.Ok)
            {
                return campaign;
            }
        }

        var preflight = BuildPreflightReport(args.RequiredTitleblockTags, args.TitleblockBlockName, args.ExpectedLayers);
        steps.Add(new { step = "preflight", preflight.Passed, preflight });
        if (!preflight.Passed && !args.Force)
        {
            return new ForgeResult
            {
                Id = command.Id,
                Ok = false,
                Error = new ForgeError(
                    "issue_set_preflight_failed",
                    "Issue-set recipe stopped: publish readiness gate failed.",
                    "Fix QA findings or pass force=true."),
                Data = new { steps, preflight }
            };
        }

        if (command.DryRun)
        {
            return DryRun(command, new { steps, wouldPublish = args.OutputPath, args.Layouts });
        }

        var publish = PlotPublish(new ForgeCommand
        {
            Id = command.Id,
            Tool = "forge_plot_publish",
            Args = ForgeJson.ToElement(new
            {
                outputPath = args.OutputPath,
                layouts = args.Layouts,
                singlePdf = args.SinglePdf,
                overwriteAcknowledged = args.OverwriteAcknowledged,
                requirePreflight = false,
                force = args.Force
            }),
            DryRun = false
        });
        steps.Add(new { step = "publish", publish.Ok, publish.Data, publish.Error });
        if (!publish.Ok)
        {
            return publish with { Data = new { steps, publish.Data } };
        }

        return ForgeResult.Success(
            command.Id,
            new { steps, publish = publish.Data, preflight },
            verification: publish.Verification);
    }

    private ForgeResult PackAndGo(ForgeCommand command)
    {
        var args = Args<PackArgs>(command);
        if (string.IsNullOrWhiteSpace(args.OutputDirectory))
        {
            return ForgeResult.Failure(command.Id, "missing_output_directory", "outputDirectory is required.");
        }

        var hostPath = ActiveDocumentPath();
        if (string.IsNullOrWhiteSpace(hostPath) || !File.Exists(hostPath))
        {
            return ForgeResult.Failure(command.Id, "document_not_saved", "Save the active DWG before pack-and-go.");
        }

        var outputDir = Path.GetFullPath(args.OutputDirectory!);
        var files = new List<object>();
        var hostName = Path.GetFileName(hostPath);
        var plannedHost = Path.Combine(outputDir, hostName);

        files.Add(new { role = "host", source = hostPath, destination = plannedHost });

        foreach (var xref in ReadXrefs())
        {
            var type = xref.GetType();
            var path = type.GetProperty("path")?.GetValue(xref)?.ToString();
            var name = type.GetProperty("name")?.GetValue(xref)?.ToString() ?? "xref";
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string absolute;
            try
            {
                absolute = Path.IsPathRooted(path)
                    ? Path.GetFullPath(path)
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(hostPath)!, path));
            }
            catch
            {
                continue;
            }

            if (!File.Exists(absolute))
            {
                files.Add(new { role = "xref", name, source = path, missing = true });
                continue;
            }

            files.Add(new
            {
                role = "xref",
                name,
                source = absolute,
                destination = Path.Combine(outputDir, Path.GetFileName(absolute))
            });
        }

        foreach (var style in args.IncludePlotStyles)
        {
            var resolved = ResolvePlotStylePath(style);
            if (resolved is null)
            {
                files.Add(new { role = "plot_style", name = style, missing = true });
                continue;
            }

            files.Add(new
            {
                role = "plot_style",
                source = resolved,
                destination = Path.Combine(outputDir, Path.GetFileName(resolved))
            });
        }

        if (command.DryRun)
        {
            return DryRun(command, new { outputDir, files });
        }

        Directory.CreateDirectory(outputDir);
        foreach (var file in files)
        {
            var type = file.GetType();
            if (Equals(type.GetProperty("missing")?.GetValue(file), true))
            {
                continue;
            }

            var source = type.GetProperty("source")?.GetValue(file)?.ToString();
            var destination = type.GetProperty("destination")?.GetValue(file)?.ToString();
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
            {
                continue;
            }

            if (File.Exists(destination) && !args.OverwriteAcknowledged)
            {
                return ForgeResult.Failure(
                    command.Id,
                    "pack_overwrite_not_acknowledged",
                    $"Refusing to overwrite existing pack file: {destination}",
                    "Pass overwriteAcknowledged=true after confirming the destination folder is safe.");
            }

            File.Copy(source, destination, overwrite: args.OverwriteAcknowledged);
        }

        // Rewrite host copy xref paths to same-folder relative names.
        if (args.RewritePaths)
        {
            using var db = new Database(false, true);
            db.ReadDwgFile(plannedHost, FileOpenMode.OpenForReadAndWriteNoShare, true, "");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in blockTable)
                {
                    var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                    if (!btr.IsFromExternalReference && !btr.IsFromOverlayReference)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(btr.PathName))
                    {
                        btr.PathName = Path.GetFileName(btr.PathName);
                    }
                }

                tr.Commit();
            }

            db.SaveAs(plannedHost, DwgVersion.Current);
        }

        var manifestPath = Path.Combine(outputDir, "manifest.json");
        var manifest = new
        {
            createdUtc = DateTimeOffset.UtcNow,
            host = hostName,
            files,
            forge = ForgeConstants.ProductVersion
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, ForgeJson.Options), Encoding.UTF8);

        return ForgeResult.Success(
            command.Id,
            new { outputDir, manifestPath, files },
            verification: new ForgeVerification
            {
                Attempted = true,
                Passed = File.Exists(plannedHost) && File.Exists(manifestPath),
                ReadBack = new { manifestPath }
            });
    }

    private static string? ResolvePlotStylePath(string styleName)
    {
        if (File.Exists(styleName))
        {
            return Path.GetFullPath(styleName);
        }

        try
        {
            var stylePath = Convert.ToString(Application.GetSystemVariable("ROAMABLEROOTPREFIX")) ?? "";
            var candidate = Path.Combine(stylePath, "Plotters", "Plot Styles", styleName);
            return File.Exists(candidate) ? candidate : null;
        }
        catch
        {
            return null;
        }
    }

    private sealed record NormalizeXrefArgs
    {
        public string? Name { get; init; }
        public bool Reload { get; init; } = true;
    }

    private sealed record PreflightArgs
    {
        public string[] RequiredTitleblockTags { get; init; } = [];
        public string? TitleblockBlockName { get; init; }
        public string[] ExpectedLayers { get; init; } = [];
    }

    private sealed record CampaignEntry
    {
        public string? Layout { get; init; }
        public string? BlockName { get; init; }
        public string? Handle { get; init; }
        public Dictionary<string, string> Attributes { get; init; } = new();
    }

    private sealed record CampaignArgs
    {
        public CampaignEntry[] Entries { get; init; } = [];
    }

    private sealed record IssueSetArgs
    {
        public string? OutputPath { get; init; }
        public string[] Layouts { get; init; } = [];
        public bool SinglePdf { get; init; } = true;
        public bool OverwriteAcknowledged { get; init; }
        public bool Force { get; init; }
        public bool NormalizeXrefs { get; init; } = true;
        public string? LayerState { get; init; }
        public CampaignArgs? Campaign { get; init; }
        public string[] RequiredTitleblockTags { get; init; } = [];
        public string? TitleblockBlockName { get; init; }
        public string[] ExpectedLayers { get; init; } = [];
    }

    private sealed record PackArgs
    {
        public string? OutputDirectory { get; init; }
        public string[] IncludePlotStyles { get; init; } = [];
        public bool RewritePaths { get; init; } = true;
        public bool OverwriteAcknowledged { get; init; }
    }
}
