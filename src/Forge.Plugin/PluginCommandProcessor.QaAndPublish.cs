using System.Globalization;
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
        // Enumerating devices requires SetCurrentConfig, which mutates the session's current
        // plot device. Capture the exact API value first and restore it in a finally.
        string? previousDevice = null;
        string? previousDeviceCaptureError = null;
        var currentConfigRestored = false;
        string? currentConfigRestoreError = null;
        try
        {
            previousDevice = PlotConfigManager.CurrentConfig?.DeviceName;
        }
        catch (System.Exception ex)
        {
            previousDeviceCaptureError = $"{ex.GetType().Name}: {ex.Message}";
        }

        var devices = new List<object>();
        try
        {
            var deviceList = PlotConfigManager.Devices;
            for (var i = 0; i < deviceList.Count; i++)
            {
                var info = deviceList[i];
                var media = new List<string>();
                string? mediaError = null;
                try
                {
                    var config = PlotConfigManager.SetCurrentConfig(info.DeviceName);
                    foreach (var item in config.CanonicalMediaNames)
                    {
                        if (item is not null)
                        {
                            media.Add(item);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    // An empty media list must not read as "device has no paper sizes".
                    mediaError = $"{ex.GetType().Name}: {ex.Message}";
                }

                devices.Add(new
                {
                    name = info.DeviceName,
                    media = media.Take(40).ToArray(),
                    mediaTruncated = media.Count > 40,
                    mediaError
                });
            }
        }
        catch (System.Exception ex)
        {
            devices.Add(new { error = ex.Message });
        }
        finally
        {
            if (previousDevice is not null)
            {
                try
                {
                    PlotConfigManager.SetCurrentConfig(previousDevice);
                    currentConfigRestored = true;
                }
                catch (System.Exception ex)
                {
                    // Surface the failed restore: later plots without an explicit device would
                    // silently use whatever device was enumerated last.
                    currentConfigRestoreError = $"{ex.GetType().Name}: {ex.Message}";
                }
            }
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
        string? plotStylesError = null;
        try
        {
            var stylePath = Convert.ToString(Application.GetSystemVariable("ROAMABLEROOTPREFIX"), CultureInfo.InvariantCulture) ?? "";
            var plotters = Path.Combine(stylePath, "Plotters", "Plot Styles");
            if (Directory.Exists(plotters))
            {
                plotStyles.AddRange(Directory.EnumerateFiles(plotters, "*.ctb").Select(Path.GetFileName)!);
                plotStyles.AddRange(Directory.EnumerateFiles(plotters, "*.stb").Select(Path.GetFileName)!);
            }
        }
        catch (System.Exception ex)
        {
            // An empty plot-style list must not read as "no styles installed".
            plotStylesError = $"{ex.GetType().Name}: {ex.Message}";
        }

        return ForgeResult.Success(command.Id, new
        {
            pstyleMode = Application.GetSystemVariable("PSTYLEMODE"),
            devices,
            pageSetups,
            layouts,
            layerStates = GetLayerStateNames(),
            plotStyles = plotStyles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray(),
            plotStylesError,
            currentConfig = new
            {
                previousDevice,
                previousDeviceCaptureError,
                restored = currentConfigRestored,
                restoreError = currentConfigRestoreError
            }
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
            using var reloadIds = FindXrefIds(reloadTr, args.Name);
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
        var artifactPath = TryWriteQaArtifact(report, out var artifactError);
        report = report with { ArtifactPath = artifactPath };
        if (artifactError is not null)
        {
            // An unwritten artifact must be reported, not hidden behind a null path.
            var findings = new List<QaFinding>(report.Findings)
            {
                new(
                    "qa_artifact_write_failed",
                    "warning",
                    $"QA report artifact could not be written: {artifactError}",
                    "Check write access to %LOCALAPPDATA%\\765T-Forge\\qa.",
                    "forge_qa_preflight")
            };
            report = QaReport.FromFindings(report.Document, findings, report.Context)
                with { Id = report.Id, CreatedUtc = report.CreatedUtc, ArtifactPath = null };
        }
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
            if (xref.IsUnloaded || !xref.PathExists)
            {
                findings.Add(new QaFinding(
                    "xref_unhealthy",
                    "error",
                    $"Xref '{xref.Name}' is unloaded or missing.",
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
                else if (string.IsNullOrWhiteSpace(match.Value) || match.Value.Contains("####"))
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
            var names = new HashSet<string>(
                layerTable.Cast<ObjectId>()
                    .Select(id => ((LayerTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name),
                StringComparer.OrdinalIgnoreCase);
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
                bgPlot = Convert.ToInt32(Application.GetSystemVariable("BACKGROUNDPLOT"), CultureInfo.InvariantCulture);
            }
            catch (System.Exception ex)
            {
                findings.Add(new QaFinding(
                    "plot_env_read_failed",
                    "error",
                    $"BACKGROUNDPLOT could not be read: {ex.Message}",
                    "Repair the AutoCAD session profile; a publish gate must not assume the variable is unset.",
                    "forge_qa_plot_fingerprint"));
            }

            findings.AddRange(pack.EvaluatePlotBindings(
                pack.PlotDevice,
                pack.PaperSize,
                pack.CtbPath ?? pack.StbPath,
                bgPlot));

            if (!string.IsNullOrWhiteSpace(pack.CtbPath) && !File.Exists(pack.CtbPath))
            {
                findings.Add(new QaFinding(
                    "dependency_missing",
                    "error",
                    $"Pack CTB missing: {pack.CtbPath}",
                    SuggestedTool: "forge_qa_dependency_closure"));
            }

            if (!string.IsNullOrWhiteSpace(pack.StbPath) && !File.Exists(pack.StbPath))
            {
                findings.Add(new QaFinding(
                    "dependency_missing",
                    "error",
                    $"Pack STB missing: {pack.StbPath}",
                    SuggestedTool: "forge_qa_dependency_closure"));
            }

            foreach (var tag in pack.RequiredTitleblockTags)
            {
                if (requiredTags is { Length: > 0 } &&
                    requiredTags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var attrs = FindBlockAttributes(titleblockBlockName, null, forWrite: false).ToArray();
                var match = attrs.FirstOrDefault(a => a.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));
                if (match is null || string.IsNullOrWhiteSpace(match.Value) || match.Value.Contains("####"))
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
                var bg = Convert.ToInt32(Application.GetSystemVariable("BACKGROUNDPLOT"), CultureInfo.InvariantCulture);
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
            catch (System.Exception ex)
            {
                findings.Add(new QaFinding(
                    "plot_env_read_failed",
                    "error",
                    $"BACKGROUNDPLOT could not be read: {ex.Message}",
                    "Repair the AutoCAD session profile; a publish gate must not assume the variable is unset.",
                    "forge_qa_plot_fingerprint"));
            }
        }

        var contract = IssueSetContractStore.Current;
        if (contract is not null)
        {
            var layoutNames = ListLayoutNames();
            findings.AddRange(contract.ValidateAgainst(layoutNames, DrawingRegistryStore.Current));
        }

        var fingerprint = CapturePlotFingerprint(out var fingerprintReadErrors);
        foreach (var readError in fingerprintReadErrors)
        {
            findings.Add(new QaFinding(
                "plot_env_read_failed",
                "error",
                $"Plot environment read failed: {readError}",
                "Repair the plotter configuration or run forge_system_capabilities for details.",
                "forge_system_capabilities"));
        }

        findings.AddRange(fingerprint.EvaluateAgainstPack(pack));

        var registry = DrawingRegistryStore.Current;
        if (registry is not null)
        {
            var layout = LayoutManager.Current.CurrentLayout;
            var sheet = registry.Sheets.FirstOrDefault(s =>
                s.Layout is not null && s.Layout.Equals(layout, StringComparison.OrdinalIgnoreCase));
            if (sheet is not null)
            {
                var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["DWG_NO"] = sheet.DrawingNo
                };
                if (!string.IsNullOrWhiteSpace(sheet.Rev))
                {
                    expected["REV"] = sheet.Rev!;
                }

                var attrs = FindBlockAttributes(titleblockBlockName, null, forWrite: false)
                    .ToDictionary(a => a.Tag, a => a.Value ?? "", StringComparer.OrdinalIgnoreCase);
                findings.AddRange(TitleblockDualSource.Compare(expected, attrs, pack?.TitleBlockAttrMap));
            }
        }

        int? fileDia = null;
        try
        {
            fileDia = Convert.ToInt32(Application.GetSystemVariable("FILEDIA"), CultureInfo.InvariantCulture);
        }
        catch (System.Exception ex)
        {
            findings.Add(new QaFinding(
                "plot_env_read_failed",
                "error",
                $"FILEDIA could not be read: {ex.Message}",
                "Repair the AutoCAD session profile; modal-dialog risk cannot be assessed without FILEDIA.",
                "forge_qa_modal_trap"));
        }

        findings.AddRange(ModalTrapHints.EvaluateAutomationSysvars(fileDia));

        return QaReport.FromFindings(document, findings, new
        {
            xrefCount = xrefs.Length,
            pstyleMode,
            packId = pack?.PackId,
            contractId = contract?.ContractId,
            registryProjectId = DrawingRegistryStore.Current?.ProjectId,
            plotFingerprint = fingerprint.FingerprintHash
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
                if (obj is DBText dbText && dbText.TextString.Contains("####"))
                {
                    hits.Add($"DBText handle {dbText.Handle} contains ####");
                }
                else if (obj is MText mText && mText.Contents.Contains("####"))
                {
                    hits.Add($"MText handle {mText.Handle} contains ####");
                }
                else if (obj is AttributeDefinition attDef && attDef.TextString.Contains("####"))
                {
                    hits.Add($"AttributeDefinition {attDef.Tag} contains ####");
                }
                else if (obj is BlockReference br)
                {
                    foreach (ObjectId attrId in br.AttributeCollection)
                    {
                        if (tr.GetObject(attrId, OpenMode.ForRead) is AttributeReference attr &&
                            attr.TextString.Contains("####"))
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

    private static string? TryWriteQaArtifact(QaReport report, out string? error)
    {
        error = null;
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "765T-Forge",
                "qa");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"qa-{report.Id}.json");
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(report, ForgeJson.Options));
            return path;
        }
        catch (System.Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
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

        if (command.DryRun)
        {
            foreach (var entry in args.Entries)
            {
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
                }
            }

            return DryRun(command, new { diffs });
        }

        // One transaction for the whole campaign: a failure on any attribute aborts the
        // transaction and leaves the drawing completely unchanged (no per-attribute commits).
        var updated = 0;
        using (var tr = ActiveDb.TransactionManager.StartTransaction())
        {
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

                    var matches = FindBlockReferences(tr, entry.BlockName, entry.Handle).ToArray();
                    string? before = null;
                    var beforeCaptured = false;
                    var wrote = 0;
                    foreach (var br in matches)
                    {
                        foreach (ObjectId attrId in br.AttributeCollection)
                        {
                            if (tr.GetObject(attrId, OpenMode.ForWrite) is not AttributeReference attr ||
                                !attr.Tag.Equals(pair.Key, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (!beforeCaptured)
                            {
                                before = attr.TextString;
                                beforeCaptured = true;
                            }

                            attr.TextString = pair.Value ?? "";
                            wrote++;
                            updated++;
                        }
                    }

                    if (wrote == 0)
                    {
                        // A tag that matches nothing must not read as a successful campaign;
                        // returning here disposes the transaction without committing.
                        return ForgeResult.Failure(
                            command.Id,
                            "block_attribute_not_found",
                            $"No block attribute matched tag '{pair.Key}' (blockName='{entry.BlockName ?? "*"}', handle='{entry.Handle ?? "*"}').",
                            "Call forge_block_list_attributes to confirm the exact tag and block selection.");
                    }

                    diffs.Add(new
                    {
                        layout = entry.Layout,
                        blockName = entry.BlockName,
                        tag = pair.Key,
                        before,
                        after = pair.Value
                    });
                }
            }

            tr.Commit();
        }

        return ForgeResult.Success(
            command.Id,
            new { updated, diffs },
            verification: new ForgeVerification { Attempted = true, Passed = true, ReadBack = diffs });
    }

    private ForgeResult RecipeIssueSet(ForgeCommand command)
    {
        var args = Args<IssueSetArgs>(command);
        if (string.IsNullOrWhiteSpace(args.OutputPath) || args.Layouts.Length == 0)
        {
            return ForgeResult.Failure(command.Id, "missing_issue_set_args", "outputPath and layouts are required.");
        }

        // Each completed step is reported with an exact status of "completed" or "failed",
        // and a failure stops the chain immediately.
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
            steps.Add(new
            {
                step = "normalize_xrefs",
                status = normalize.Ok ? "completed" : "failed",
                normalize.Data,
                normalize.Error
            });
            if (!normalize.Ok)
            {
                return normalize with { Data = new { steps, failedStep = "normalize_xrefs", detail = normalize.Data } };
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
            steps.Add(new
            {
                step = "layer_state",
                status = restore.Ok ? "completed" : "failed",
                restore.Data,
                restore.Error
            });
            if (!restore.Ok)
            {
                return restore with { Data = new { steps, failedStep = "layer_state", detail = restore.Data } };
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
            steps.Add(new
            {
                step = "titleblock_campaign",
                status = campaign.Ok ? "completed" : "failed",
                campaign.Data,
                campaign.Error
            });
            if (!campaign.Ok)
            {
                return campaign with { Data = new { steps, failedStep = "titleblock_campaign", detail = campaign.Data } };
            }
        }

        var preflight = BuildPreflightReport(args.RequiredTitleblockTags, args.TitleblockBlockName, args.ExpectedLayers);
        steps.Add(new
        {
            step = "preflight",
            status = preflight.Passed ? "completed" : "failed",
            passed = preflight.Passed,
            preflight
        });

        if (!ForcePublishGate.IsAllowed(args.Force, _environment.AllowForcePublish))
        {
            return ForgeResult.Failure(
                command.Id,
                ForcePublishGate.DenyCode,
                ForcePublishGate.DenyMessage,
                ForcePublishGate.DenySuggestion);
        }

        if (!preflight.Passed && !args.Force)
        {
            return new ForgeResult
            {
                Id = command.Id,
                Ok = false,
                Error = new ForgeError(
                    "issue_set_preflight_failed",
                    "Issue-set recipe stopped: publish readiness gate failed.",
                    "Fix QA findings or pass force=true with FORGE_ALLOW_FORCE_PUBLISH=true."),
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
            DryRun = false,
            AuditId = command.AuditId
        });
        steps.Add(new
        {
            step = "publish",
            status = publish.Ok ? "completed" : "failed",
            publish.Data,
            publish.Error
        });
        if (!publish.Ok)
        {
            return publish with { Data = new { steps, failedStep = "publish", detail = publish.Data } };
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
        var plan = new List<PackPlanEntry>
        {
            new("host", Path.GetFileName(hostPath), hostPath, Missing: false, ResolveError: null)
        };

        foreach (var xref in ReadXrefs())
        {
            if (string.IsNullOrWhiteSpace(xref.Path))
            {
                continue;
            }

            string absolute;
            try
            {
                absolute = Path.IsPathRooted(xref.Path)
                    ? Path.GetFullPath(xref.Path)
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(hostPath)!, xref.Path));
            }
            catch (ArgumentException ex)
            {
                plan.Add(new PackPlanEntry("xref", xref.Name, xref.Path, Missing: true, ResolveError: ex.Message));
                continue;
            }

            if (!File.Exists(absolute))
            {
                plan.Add(new PackPlanEntry("xref", xref.Name, xref.Path, Missing: true, ResolveError: "File not found."));
                continue;
            }

            plan.Add(new PackPlanEntry("xref", xref.Name, absolute, Missing: false, ResolveError: null));
        }

        foreach (var style in args.IncludePlotStyles)
        {
            if (!TryResolvePlotStylePath(style, out var resolved, out var resolveError))
            {
                plan.Add(new PackPlanEntry("plot_style", style, style, Missing: true, ResolveError: resolveError));
                continue;
            }

            plan.Add(new PackPlanEntry("plot_style", style, resolved!, Missing: false, ResolveError: null));
        }

        var copyable = plan.Where(x => !x.Missing).ToArray();
        var destinations = PackDestinationResolver.Resolve(
            copyable.Select(x => x.Source).ToArray(),
            outputDir);

        var files = new List<object>();
        var destinationIndex = 0;
        foreach (var entry in plan)
        {
            if (entry.Missing)
            {
                files.Add(new
                {
                    role = entry.Role,
                    name = entry.Name,
                    source = entry.Source,
                    missing = true,
                    resolveError = entry.ResolveError
                });
                continue;
            }

            var destination = destinations[destinationIndex++];
            var desiredName = Path.GetFileName(entry.Source);
            var actualName = Path.GetFileName(destination);
            files.Add(new
            {
                role = entry.Role,
                name = entry.Name,
                source = entry.Source,
                destination,
                renamedFrom = string.Equals(desiredName, actualName, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : desiredName
            });
        }

        var hostDestination = destinations[0];

        if (command.DryRun)
        {
            return DryRun(command, new { outputDir, files });
        }

        // Overwrite gate is evaluated against the exact resolved destinations before any copy.
        var overwriteIndex = 0;
        foreach (var entry in plan)
        {
            if (entry.Missing)
            {
                continue;
            }

            var destination = destinations[overwriteIndex++];
            if (File.Exists(destination) && !args.OverwriteAcknowledged)
            {
                return ForgeResult.Failure(
                    command.Id,
                    "pack_overwrite_not_acknowledged",
                    $"Refusing to overwrite existing pack file: {destination}",
                    "Pass overwriteAcknowledged=true after confirming the destination folder is safe.");
            }
        }

        // Stage the whole pack in a temp directory and move it into place only on success, so a
        // mid-pack failure cannot leave a half-written pack at the destination.
        var stageDir = Path.Combine(Path.GetTempPath(), $"forge-pack-{command.Id}");
        string? manifestPath = null;
        var staged = new List<(string StagedPath, string Destination)>();
        try
        {
            if (Directory.Exists(stageDir))
            {
                Directory.Delete(stageDir, recursive: true);
            }

            Directory.CreateDirectory(stageDir);
            var index = 0;
            foreach (var entry in copyable)
            {
                var destination = destinations[index++];
                var stagedPath = Path.Combine(stageDir, Path.GetFileName(destination));
                File.Copy(entry.Source, stagedPath, overwrite: true);
                staged.Add((stagedPath, destination));
            }

            var stagedHost = staged[0].StagedPath;

            // Rewrite host copy xref paths to same-folder relative names.
            if (args.RewritePaths)
            {
                using var db = new Database(false, true);
                db.ReadDwgFile(stagedHost, FileOpenMode.OpenForReadAndWriteNoShare, true, "");
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

                db.SaveAs(stagedHost, DwgVersion.Current);
            }

            manifestPath = Path.Combine(stageDir, "manifest.json");
            var manifest = new
            {
                createdUtc = DateTimeOffset.UtcNow,
                host = Path.GetFileName(hostDestination),
                files,
                forge = ForgeConstants.ProductVersion
            };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, ForgeJson.Options), Encoding.UTF8);

            Directory.CreateDirectory(outputDir);
            foreach (var (stagedPath, destination) in staged)
            {
                if (File.Exists(destination))
                {
                    if (!args.OverwriteAcknowledged)
                    {
                        return ForgeResult.Failure(
                            command.Id,
                            "pack_overwrite_not_acknowledged",
                            $"Refusing to overwrite existing pack file: {destination}",
                            "Pass overwriteAcknowledged=true after confirming the destination folder is safe.");
                    }

                    File.Delete(destination);
                }

                File.Move(stagedPath, destination);
            }

            var finalManifestPath = Path.Combine(outputDir, "manifest.json");
            File.Move(manifestPath, finalManifestPath);
            manifestPath = finalManifestPath;
        }
        catch (System.Exception ex)
        {
            return ForgeResult.Failure(
                command.Id,
                "pack_failed",
                $"Pack-and-go failed while staging: {ex.Message}",
                "The destination was not modified; fix the reported error and retry.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(stageDir))
                {
                    Directory.Delete(stageDir, recursive: true);
                }
            }
            catch
            {
                // The staged temp directory is best-effort cleanup; the pack result is unaffected.
            }
        }

        return ForgeResult.Success(
            command.Id,
            new { outputDir, manifestPath, files },
            verification: new ForgeVerification
            {
                Attempted = true,
                Passed = manifestPath is not null && File.Exists(manifestPath) && File.Exists(hostDestination),
                ReadBack = new { manifestPath, hostDestination }
            });
    }

    private sealed record PackPlanEntry(string Role, string? Name, string Source, bool Missing, string? ResolveError);

    private static bool TryResolvePlotStylePath(string styleName, out string? resolved, out string? error)
    {
        resolved = null;
        error = null;
        if (File.Exists(styleName))
        {
            resolved = Path.GetFullPath(styleName);
            return true;
        }

        try
        {
            var stylePath = Convert.ToString(Application.GetSystemVariable("ROAMABLEROOTPREFIX"), CultureInfo.InvariantCulture) ?? "";
            var candidate = Path.Combine(stylePath, "Plotters", "Plot Styles", styleName);
            if (File.Exists(candidate))
            {
                resolved = candidate;
                return true;
            }

            error = $"Plot style not found: '{styleName}' or '{candidate}'.";
            return false;
        }
        catch (System.Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return false;
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
