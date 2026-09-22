#pragma warning disable MCPEXP001
using System.ComponentModel;
using Forge.Shared;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Forge.Server;

[McpServerToolType]
public sealed class ForgeMcpTools
{
    [McpServerTool(Name = "forge_system_health", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<HealthData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Check whether 765T-Forge is alive and whether the AutoCAD plugin named pipe responds. Read data.pipe and compare it with FORGE_PIPE_NAME.")]
    public static async Task<McpEnvelope<HealthData>> SystemHealth(ForgeToolRunner runner, CancellationToken cancellationToken)
        => ForgeOutputMap.Envelope<HealthData>(await runner.InvokeAsync("forge_system_health", new { }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_system_version", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<VersionData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Product and envelope versions plus hostVersion, builtForYear, configuredAutoCadRoot, and hostMismatch. builtForYear is the compile target. hostVersion is the running AutoCAD. Do not treat a constant year as the host.")]
    public static async Task<McpEnvelope<VersionData>> SystemVersion(ForgeToolRunner runner, CancellationToken cancellationToken)
        => ForgeOutputMap.Envelope<VersionData>(await runner.InvokeAsync("forge_system_version", new { }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_system_getvar", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<GetVarData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Read one AutoCAD system variable, such as FILEDIA, PSTYLEMODE, BACKGROUNDPLOT, or CMDDIA.")]
    public static async Task<McpEnvelope<GetVarData>> SystemGetVar(
        ForgeToolRunner runner,
        [Description(ForgeArgDescriptions.SysvarName)] string name,
        CancellationToken cancellationToken)
        => ForgeOutputMap.Envelope<GetVarData>(await runner.InvokeAsync("forge_system_getvar", new { name }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_system_setvar", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<SetVarData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Set one AutoCAD system variable and read it back. Security variables such as SECURELOAD and TRUSTEDPATHS return deny_sysvar. The command denylist is not used for this typed tool.")]
    public static async Task<McpEnvelope<SetVarData>> SystemSetVar(
        ForgeToolRunner runner,
        [Description(ForgeArgDescriptions.SysvarName)] string name,
        [Description("New value. Numbers are converted to the variable's current CLR type.")] string value,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<SetVarData>(await runner.InvokeAsync("forge_system_setvar", new { name, value }, dryRun, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_system_capabilities", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<CapabilitiesData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Discover plot devices, media names, page setups, layouts, layer states, and plot styles in the active session. Copy device and paper from here. Do not invent them.")]
    public static async Task<McpEnvelope<CapabilitiesData>> SystemCapabilities(ForgeToolRunner runner, CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<CapabilitiesData>(await runner.InvokeAsync("forge_system_capabilities", new { }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_doc_list_open", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<OpenDocumentsData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("List open AutoCAD documents. isActive marks the MDI document that fill and publish use when document is omitted.")]
    public static async Task<McpEnvelope<OpenDocumentsData>> DocListOpen(ForgeToolRunner runner, CancellationToken cancellationToken)
        => ForgeOutputMap.Envelope<OpenDocumentsData>(await runner.InvokeAsync("forge_doc_list_open", new { }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_doc_list_layouts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<LayoutListData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("List layouts. document selects a drawing for this call only and does not leave it active. Later fill and publish calls use the MDI active document unless they receive document.")]
    public static async Task<McpEnvelope<LayoutListData>> DocListLayouts(
        ForgeToolRunner runner,
        [Description(ForgeArgDescriptions.ListDocument)] string? document = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<LayoutListData>(await runner.InvokeAsync("forge_doc_list_layouts", new { }, document: document, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_doc_open", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<DocOpenData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Open a DWG with FILEDIA forced to 0 for the call and restored afterward, including when Open throws.")]
    public static async Task<McpEnvelope<DocOpenData>> DocOpen(
        ForgeToolRunner runner,
        [Description("Absolute path of the DWG to open.")] string path,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<DocOpenData>(await runner.InvokeAsync("forge_doc_open", new { path }, dryRun, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_doc_save", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<DocSaveData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Save the active document, or save as path. A different existing destination requires overwriteAcknowledged.")]
    public static async Task<McpEnvelope<DocSaveData>> DocSave(
        ForgeToolRunner runner,
        [Description("Save-as path. Omit to save the active document in place.")] string? path = null,
        [Description(ForgeArgDescriptions.Overwrite)] bool overwriteAcknowledged = false,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<DocSaveData>(await runner.InvokeAsync("forge_doc_save", new { path, overwriteAcknowledged }, dryRun, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_xref_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<XrefListData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("List xrefs with names, paths, and load state. Copy name from here before reload or repath.")]
    public static async Task<McpEnvelope<XrefListData>> XrefList(ForgeToolRunner runner, CancellationToken cancellationToken)
        => ForgeOutputMap.Envelope<XrefListData>(await runner.InvokeAsync("forge_xref_list", new { }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_xref_reload", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<XrefReloadData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Reload one xref by name. Omitting name reloads every xref.")]
    public static async Task<McpEnvelope<XrefReloadData>> XrefReload(
        ForgeToolRunner runner,
        [Description("Xref name from forge_xref_list. Omit only when every xref should reload.")] string? name = null,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<XrefReloadData>(await runner.InvokeAsync("forge_xref_reload", new { name }, dryRun, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_xref_repath", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<XrefRepathData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Replace an xref path and optionally reload it. This overwrites the stored path.")]
    public static async Task<McpEnvelope<XrefRepathData>> XrefRepath(
        ForgeToolRunner runner,
        [Description("Xref name from forge_xref_list.")] string name,
        [Description("New path, absolute or host-relative.")] string path,
        [Description("Reload the xref after the path change.")] bool reload = true,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<XrefRepathData>(await runner.InvokeAsync("forge_xref_repath", new { name, path, reload }, dryRun, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_xref_normalize_relative", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<XrefNormalizeData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Rewrite absolute xref paths to host-relative paths. Omitting name rewrites every absolute xref.")]
    public static async Task<McpEnvelope<XrefNormalizeData>> XrefNormalizeRelative(
        ForgeToolRunner runner,
        [Description("Xref name from forge_xref_list. Omit to rewrite every absolute xref.")] string? name = null,
        [Description("Reload xrefs after the path rewrite.")] bool reload = true,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<XrefNormalizeData>(await runner.InvokeAsync("forge_xref_normalize_relative", new { name, reload }, dryRun, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_layer_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<LayerListData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("List layer properties relevant to plotting, including locked and frozen state.")]
    public static async Task<McpEnvelope<LayerListData>> LayerList(ForgeToolRunner runner, CancellationToken cancellationToken)
        => ForgeOutputMap.Envelope<LayerListData>(await runner.InvokeAsync("forge_layer_list", new { }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_layer_state_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<LayerStateListData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("List known layer states.")]
    public static async Task<McpEnvelope<LayerStateListData>> LayerStateList(ForgeToolRunner runner, CancellationToken cancellationToken)
        => ForgeOutputMap.Envelope<LayerStateListData>(await runner.InvokeAsync("forge_layer_state_list", new { }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_layer_state_restore", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<LayerStateRestoreData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Restore a named layer state, replacing the current layer state.")]
    public static async Task<McpEnvelope<LayerStateRestoreData>> LayerStateRestore(
        ForgeToolRunner runner,
        [Description("Layer state name from forge_layer_state_list.")] string name,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<LayerStateRestoreData>(await runner.InvokeAsync("forge_layer_state_restore", new { name }, dryRun, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_block_list_attributes", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<BlockListData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("List attributes on block references. Use the handle from this list for later writes.")]
    public static async Task<McpEnvelope<BlockListData>> BlockListAttributes(
        ForgeToolRunner runner,
        [Description(ForgeArgDescriptions.BlockName)] string? blockName = null,
        [Description(ForgeArgDescriptions.BlockHandle)] string? handle = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<BlockListData>(await runner.InvokeAsync("forge_block_list_attributes", new { blockName, handle }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_block_get_attr", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<BlockGetData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Read one block attribute. Without handle and blockName the result is a single reference, Model space first, not every sheet.")]
    public static async Task<McpEnvelope<BlockGetData>> BlockGetAttr(
        ForgeToolRunner runner,
        [Description("Attribute tag to read.")] string tag,
        [Description(ForgeArgDescriptions.BlockName)] string? blockName = null,
        [Description(ForgeArgDescriptions.BlockHandle)] string? handle = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<BlockGetData>(await runner.InvokeAsync("forge_block_get_attr", new { tag, blockName, handle }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_block_set_attr", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<BlockSetData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Overwrite one block attribute and read it back. Drawing-number tags require a loaded registry (registry_not_loaded). Omitting handle and blockName is rejected.")]
    public static async Task<McpEnvelope<BlockSetData>> BlockSetAttr(
        ForgeToolRunner runner,
        [Description("Attribute tag to write.")] string tag,
        [Description("New attribute text. Drawing numbers must be copied from forge_registry_lookup.")] string value,
        [Description(ForgeArgDescriptions.BlockName)] string? blockName = null,
        [Description(ForgeArgDescriptions.BlockHandle)] string? handle = null,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        [Description(ForgeArgDescriptions.TargetDocument)] string? document = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<BlockSetData>(await runner.InvokeAsync("forge_block_set_attr", new { tag, value, blockName, handle }, dryRun, document, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_block_campaign", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<CampaignData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Overwrite title-block attributes. Dry-run does not change CurrentLayout. updated counts only read-backs that match. Drawing-number tags require a loaded registry.")]
    public static async Task<McpEnvelope<CampaignData>> BlockCampaign(
        ForgeToolRunner runner,
        [Description("Campaign entries. Each entry needs handle or blockName, and attributes copied from the registry.")] CampaignEntryDto[] entries,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        [Description(ForgeArgDescriptions.TargetDocument)] string? document = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<CampaignData>(await runner.InvokeAsync("forge_block_campaign", new { entries }, dryRun, document, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_layout_page_setup_import", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ExecEnvelope<PageSetupImportData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Import a page setup from a template. The sync path copies PlotSettings. A queued fallback returns Ok=false with queued=true and completed=false.")]
    public static async Task<ExecEnvelope<PageSetupImportData>> PageSetupImport(
        ForgeToolRunner runner,
        [Description("Absolute path of the DWT or DWG template.")] string templatePath,
        [Description("Page setup name to import. An existing setup of the same name is overwritten.")] string setupName,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Exec<PageSetupImportData>(await runner.InvokeAsync("forge_layout_page_setup_import", new { templatePath, setupName }, dryRun, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_layout_page_setup_apply", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<PageSetupApplyData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Apply an existing page setup onto a layout, replacing that layout's plot settings.")]
    public static async Task<McpEnvelope<PageSetupApplyData>> PageSetupApply(
        ForgeToolRunner runner,
        [Description("Page setup name already imported into the drawing.")] string setupName,
        [Description("Layout name from forge_doc_list_layouts. Omit to use the current layout.")] string? layout = null,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<PageSetupApplyData>(await runner.InvokeAsync("forge_layout_page_setup_apply", new { setupName, layout }, dryRun, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_plot_to_pdf", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PublishEnvelope<PlotToPdfData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Plot one layout to PDF. layout, device, and paperSize are required and must come from forge_system_capabilities or forge_doc_list_layouts. Blank values are not filled with A1 or DWG To PDF.pc3. This tool does not run preflight.")]
    public static async Task<PublishEnvelope<PlotToPdfData>> PlotToPdf(
        ForgeToolRunner runner,
        [Description("Absolute PDF output path.")] string outputPath,
        [Description(ForgeArgDescriptions.PlotLayout)] string? layout = null,
        [Description(ForgeArgDescriptions.PlotDevice)] string? device = null,
        [Description(ForgeArgDescriptions.PlotPaper)] string? paperSize = null,
        [Description(ForgeArgDescriptions.PlotStyle)] string? plotStyle = null,
        [Description("Plot area such as Layout or Extents. Optional.")] string? plotArea = null,
        [Description("Orientation, for example Landscape or Portrait.")] string? orientation = null,
        [Description("Plot scale, for example Fit.")] string? scale = null,
        [Description("Plot units, Millimeters or Inches.")] string? units = null,
        [Description(ForgeArgDescriptions.Overwrite)] bool overwriteAcknowledged = false,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        [Description(ForgeArgDescriptions.TargetDocument)] string? document = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Publish<PlotToPdfData>(await runner.InvokeAsync("forge_plot_to_pdf", new { outputPath, layout, device, paperSize, plotStyle, plotArea, orientation, scale, units, overwriteAcknowledged }, dryRun, document, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_plot_publish", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PublishEnvelope<PlotPublishData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Publish layouts to PDF. Unknown layout names return layout_not_found before any file is written. verificationPassed is required in the result. A failed probe or preflight returns Ok=false. force is only when a human asks in this session.")]
    public static async Task<PublishEnvelope<PlotPublishData>> PlotPublish(
        ForgeToolRunner runner,
        [Description("Absolute PDF output path.")] string outputPath,
        [Description(ForgeArgDescriptions.Layouts)] string[] layouts,
        [Description(ForgeArgDescriptions.SinglePdf)] bool singlePdf = true,
        [Description(ForgeArgDescriptions.Overwrite)] bool overwriteAcknowledged = false,
        [Description("When true, run the publish readiness gate before writing.")] bool requirePreflight = true,
        [Description(ForgeArgDescriptions.Force)] bool force = false,
        [Description(ForgeArgDescriptions.RequiredTags)] string[]? requiredTitleblockTags = null,
        [Description(ForgeArgDescriptions.TitleblockBlock)] string? titleblockBlockName = null,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        [Description(ForgeArgDescriptions.TargetDocument)] string? document = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new ProgressNotificationValue { Progress = 0, Total = 2, Message = "Publish requested. Waiting for AutoCAD; no mid-plot percentage is available." });
        var result = await runner.InvokeAsync("forge_plot_publish", new { outputPath, layouts, singlePdf, overwriteAcknowledged, requirePreflight, force, requiredTitleblockTags = requiredTitleblockTags ?? [], titleblockBlockName }, dryRun, document, cancellationToken: cancellationToken).ConfigureAwait(false);
        progress?.Report(new ProgressNotificationValue { Progress = 2, Total = 2, Message = "Publish call returned, including the PDF probe when a file was written." });
        return ForgeOutputMap.Publish<PlotPublishData>(result);
    }

    [McpServerTool(Name = "forge_qa_verify_titleblock", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(QaEnvelope<TitleblockQaData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Compare title-block attributes. passed is required. passed=false returns Ok=false. One matching attribute does not excuse other references.")]
    public static async Task<QaEnvelope<TitleblockQaData>> QaVerifyTitleblock(
        ForgeToolRunner runner,
        [Description("Expected tag to value map. Do not invent drawing numbers.")] Dictionary<string, string> expected,
        [Description(ForgeArgDescriptions.TitleblockBlock)] string? blockName = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Qa<TitleblockQaData>(await runner.InvokeAsync("forge_qa_verify_titleblock", new { expected, blockName }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_qa_check_xrefs", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(QaEnvelope<XrefQaData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Detect unloaded or missing xrefs. passed is required. passed=false returns Ok=false.")]
    public static async Task<QaEnvelope<XrefQaData>> QaCheckXrefs(ForgeToolRunner runner, CancellationToken cancellationToken)
        => ForgeOutputMap.Qa<XrefQaData>(await runner.InvokeAsync("forge_qa_check_xrefs", new { }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_qa_audit_layers", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(QaEnvelope<LayerAuditData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Check that expected layer names exist. passed is required. This does not check plot, freeze, or lock. Use forge_layer_list for those.")]
    public static async Task<QaEnvelope<LayerAuditData>> QaAuditLayers(
        ForgeToolRunner runner,
        [Description(ForgeArgDescriptions.ExpectedLayers)] string[] expectedLayers,
        CancellationToken cancellationToken)
        => ForgeOutputMap.Qa<LayerAuditData>(await runner.InvokeAsync("forge_qa_audit_layers", new { expectedLayers }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_qa_readback", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(QaEnvelope<ReadbackData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Read back after a write. passed is required. A tool with no comparator returns Ok=false and passed=false.")]
    public static async Task<QaEnvelope<ReadbackData>> QaReadback(
        ForgeToolRunner runner,
        [Description("Tool name to read back, for example forge_block_set_attr.")] string targetTool,
        [Description("Optional keys. They are not a substitute for a real read-back.")] Dictionary<string, string>? keys = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Qa<ReadbackData>(await runner.InvokeAsync("forge_qa_readback", new { targetTool, keys }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_qa_readback_after_timeout", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(QaEnvelope<ReadbackAfterTimeoutData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Timeout recovery. passed is required and is false when nothing was read back. Do not retry the write blindly.")]
    public static async Task<QaEnvelope<ReadbackAfterTimeoutData>> QaReadbackAfterTimeout(
        ForgeToolRunner runner,
        [Description("Tool that timed out.")] string targetTool,
        [Description("Optional keys. They do not prove the timed-out write committed.")] Dictionary<string, string>? keys = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Qa<ReadbackAfterTimeoutData>(await runner.InvokeAsync("forge_qa_readback_after_timeout", new { targetTool, keys }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_audit_summarize", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<AuditSummaryData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Summarize recent audit records. Paths and args are omitted. decisionCode result_recorded means the server stored a successful result. It does not mean an AutoCAD command finished. Trust ok and errorCode.")]
    public static async Task<McpEnvelope<AuditSummaryData>> AuditSummarize(
        ForgeToolRunner runner,
        [Description("Maximum records to return, from 1 to 200.")] int limit = 20,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<AuditSummaryData>(await runner.InvokeAsync("forge_audit_summarize", new { limit }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_qa_preflight", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(QaEnvelope<PreflightQaData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Publish readiness gate. passed is required. Every matching title block is checked. Empty requiredTitleblockTags with no pack fails the gate. layer_missing is an error. Pack device, paper, and CTB/STB are compared with layout plot settings. The call writes a QaReport file.")]
    public static async Task<QaEnvelope<PreflightQaData>> QaPreflight(
        ForgeToolRunner runner,
        [Description(ForgeArgDescriptions.RequiredTags)] string[]? requiredTitleblockTags = null,
        [Description(ForgeArgDescriptions.TitleblockBlock)] string? titleblockBlockName = null,
        [Description(ForgeArgDescriptions.ExpectedLayers)] string[]? expectedLayers = null,
        [Description(ForgeArgDescriptions.TargetDocument)] string? document = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Qa<PreflightQaData>(await runner.InvokeAsync("forge_qa_preflight", new { requiredTitleblockTags = requiredTitleblockTags ?? [], titleblockBlockName, expectedLayers = expectedLayers ?? [] }, document: document, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_issue_set_validate", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(QaEnvelope<IssueSetData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Validate an IssueSetContract against the active drawing. passed is required. passed=false returns Ok=false.")]
    public static async Task<QaEnvelope<IssueSetData>> IssueSetValidate(
        ForgeToolRunner runner,
        [Description("Path of the contract JSON. Omit to use the contract already loaded in the session.")] string? contractPath = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Qa<IssueSetData>(await runner.InvokeAsync("forge_issue_set_validate", new { contractPath }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_sheet_inventory_import", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<SheetImportData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Import a CSV sheet inventory into an IssueSetContract. Writes outputContractPath when set. Does not write DST/SSM. Omitting contractId uses a timestamp, so the tool is not idempotent.")]
    public static async Task<McpEnvelope<SheetImportData>> SheetInventoryImport(
        ForgeToolRunner runner,
        [Description("Absolute path of the CSV (layout,drawingNo,rev[,title]).")] string csvPath,
        [Description(ForgeArgDescriptions.ContractId)] string? contractId = null,
        [Description("Project id stored on the contract.")] string? projectId = null,
        [Description("Absolute JSON path to write. Omit to return the contract without writing a file.")] string? outputContractPath = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<SheetImportData>(await runner.InvokeAsync("forge_sheet_inventory_import", new { csvPath, contractId, projectId, outputContractPath }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_issue_set_diff", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<IssueDiffData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Diff two PublishReceipt JSON files: layouts added or removed, and whether output bytes changed.")]
    public static async Task<McpEnvelope<IssueDiffData>> IssueSetDiff(
        ForgeToolRunner runner,
        [Description("Absolute path of the current PublishReceipt JSON.")] string currentReceiptPath,
        [Description("Absolute path of the previous PublishReceipt JSON. Omit when there is no previous issue.")] string? previousReceiptPath = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<IssueDiffData>(await runner.InvokeAsync("forge_issue_set_diff", new { currentReceiptPath, previousReceiptPath }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_recipe_issue_set", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(PublishEnvelope<RecipeData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Preflight runs before normalize, layer restore, campaign, and publish. A failed gate does not change the drawing unless a human asked for force. Dry-run does not change CurrentLayout. verificationPassed is required. A bypass still returns Ok=false (preflight_forced).")]
    public static async Task<PublishEnvelope<RecipeData>> RecipeIssueSet(
        ForgeToolRunner runner,
        [Description("Absolute PDF output path.")] string outputPath,
        [Description(ForgeArgDescriptions.Layouts)] string[] layouts,
        [Description(ForgeArgDescriptions.SinglePdf)] bool singlePdf = true,
        [Description(ForgeArgDescriptions.Overwrite)] bool overwriteAcknowledged = false,
        [Description(ForgeArgDescriptions.Force)] bool force = false,
        [Description("Rewrite absolute xrefs to host-relative paths before publish. Runs only after preflight passes, or when force is set.")] bool normalizeXrefs = true,
        [Description("Layer state to restore after preflight passes.")] string? layerState = null,
        [Description("Title-block writes applied after preflight passes. Dry-run does not change the current layout.")] CampaignEntryDto[]? campaignEntries = null,
        [Description(ForgeArgDescriptions.RequiredTags)] string[]? requiredTitleblockTags = null,
        [Description(ForgeArgDescriptions.TitleblockBlock)] string? titleblockBlockName = null,
        [Description(ForgeArgDescriptions.ExpectedLayers)] string[]? expectedLayers = null,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        [Description(ForgeArgDescriptions.TargetDocument)] string? document = null,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new ProgressNotificationValue { Progress = 0, Total = 2, Message = "Issue-set recipe started. Preflight runs before any drawing change." });
        var result = await runner.InvokeAsync("forge_recipe_issue_set", new
        {
            outputPath,
            layouts,
            singlePdf,
            overwriteAcknowledged,
            force,
            normalizeXrefs,
            layerState,
            campaign = campaignEntries is null ? null : new { entries = campaignEntries },
            requiredTitleblockTags = requiredTitleblockTags ?? [],
            titleblockBlockName,
            expectedLayers = expectedLayers ?? []
        }, dryRun, document, cancellationToken: cancellationToken).ConfigureAwait(false);
        progress?.Report(new ProgressNotificationValue { Progress = 2, Total = 2, Message = "Issue-set recipe returned." });
        return ForgeOutputMap.Publish<RecipeData>(result);
    }

    [McpServerTool(Name = "forge_pack_and_go", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<PackData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Pack the host DWG, xrefs, and optional CTB/STB into an output folder with manifest.json.")]
    public static async Task<McpEnvelope<PackData>> PackAndGo(
        ForgeToolRunner runner,
        [Description("Absolute output folder.")] string outputDirectory,
        [Description("Plot style files to copy. Optional.")] string[]? includePlotStyles = null,
        [Description("Rewrite xref paths to be relative to the pack folder.")] bool rewritePaths = true,
        [Description(ForgeArgDescriptions.Overwrite)] bool overwriteAcknowledged = false,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<PackData>(await runner.InvokeAsync("forge_pack_and_go", new { outputDirectory, includePlotStyles = includePlotStyles ?? [], rewritePaths, overwriteAcknowledged }, dryRun, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_registry_load", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<RegistryLoadData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Load a drawing-number registry. Until this succeeds, writing DRAWING_NO, DWG_NO, SHEET_NO, or SO_HIEU returns registry_not_loaded.")]
    public static async Task<McpEnvelope<RegistryLoadData>> RegistryLoad(
        ForgeToolRunner runner,
        [Description("Absolute path of the registry JSON (projectId and drawingNo).")] string path,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<RegistryLoadData>(await runner.InvokeAsync("forge_registry_load", new { path }, dryRun, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_registry_lookup", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<RegistryLookupData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Look up sheets in the loaded registry, or one drawingNo. Never invent drawing numbers. Omit drawingNo to list sheets.")]
    public static async Task<McpEnvelope<RegistryLookupData>> RegistryLookup(
        ForgeToolRunner runner,
        [Description("Drawing number to look up. Omit to list every sheet in the loaded registry.")] string? drawingNo = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<RegistryLookupData>(await runner.InvokeAsync("forge_registry_lookup", new { drawingNo }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_pack_load", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<PackLoadData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Load a standards pack. Preflight then compares pack layers and, when set, device, paper, and CTB/STB against layout plot settings.")]
    public static async Task<McpEnvelope<PackLoadData>> PackLoad(
        ForgeToolRunner runner,
        [Description("Absolute path of the standards pack JSON.")] string path,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<PackLoadData>(await runner.InvokeAsync("forge_pack_load", new { path }, dryRun, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_pack_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<PackStatusData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Show which standards pack and drawing registry are loaded in the plugin session.")]
    public static async Task<McpEnvelope<PackStatusData>> PackStatus(ForgeToolRunner runner, CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<PackStatusData>(await runner.InvokeAsync("forge_pack_status", new { }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_system_tool_profile", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<ToolProfileData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Return the core, plot, qa, or linework allowlist. This does not remove tools from tools/list. The server filters tools/list only when it was started with FORGE_TOOL_PROFILE set to one of those names. The plot allowlist includes list layouts, registry lookup, campaign, and xref list. Still call any tool the hot path needs.")]
    public static async Task<McpEnvelope<ToolProfileData>> SystemToolProfile(
        ForgeToolRunner runner,
        [Description("Profile name: core, plot, qa, or linework. Omit to list profile names.")] string? name = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<ToolProfileData>(await runner.InvokeAsync("forge_system_tool_profile", new { name }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_viewport_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<ViewportListData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("List paper-space viewports (handle, layout, scale, lock).")]
    public static async Task<McpEnvelope<ViewportListData>> ViewportList(
        ForgeToolRunner runner,
        [Description("Layout name filter. Omit to list viewports on every paper layout.")] string? layout = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<ViewportListData>(await runner.InvokeAsync("forge_viewport_list", new { layout }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_viewport_set_layer_freeze", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<ViewportFreezeData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Freeze or thaw one layer inside one viewport. This does not change the global layer state.")]
    public static async Task<McpEnvelope<ViewportFreezeData>> ViewportSetLayerFreeze(
        ForgeToolRunner runner,
        [Description("Viewport handle from forge_viewport_list.")] string handle,
        [Description("Layer name to freeze or thaw in that viewport.")] string layer,
        [Description("True freezes the layer in the viewport. False thaws it.")] bool freeze = true,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<ViewportFreezeData>(await runner.InvokeAsync("forge_viewport_set_layer_freeze", new { handle, layer, freeze }, dryRun, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_linework_dump", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<LineworkDumpData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Extract line, polyline, arc, and circle entities as ordered vertices with handles. source=drawing reads the live model space and xref contents. source=dumpFile reads pl_dump.txt on the server.")]
    public static async Task<McpEnvelope<LineworkDumpData>> LineworkDump(
        ForgeToolRunner runner,
        [Description(ForgeArgDescriptions.LwSource)] string? source = null,
        [Description(ForgeArgDescriptions.LwDumpPath)] string? dumpPath = null,
        [Description(ForgeArgDescriptions.LwLayerFilter)] string? layerFilter = null,
        [Description(ForgeArgDescriptions.LwLayerSuffix)] string? layerSuffix = null,
        [Description(ForgeArgDescriptions.LwLayerMatch)] string? layerMatch = null,
        [Description(ForgeArgDescriptions.LwExcludeLayer)] string? excludeLayerFilter = null,
        [Description(ForgeArgDescriptions.LwEntityTypes)] string? entityTypes = null,
        [Description(ForgeArgDescriptions.LwBbox)] double[]? bbox = null,
        [Description(ForgeArgDescriptions.LwIncludeXref)] bool includeXrefContents = true,
        [Description(ForgeArgDescriptions.LwUnitsPerMeter)] double unitsPerMeter = 0,
        [Description(ForgeArgDescriptions.LwMaxEntities)] int maxEntities = 50000,
        [Description(ForgeArgDescriptions.LwOutputPath)] string? outputPath = null,
        [Description(ForgeArgDescriptions.TargetDocument)] string? document = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<LineworkDumpData>(await runner.InvokeAsync("forge_linework_dump", new { source, dumpPath, layerFilter, layerSuffix, layerMatch, excludeLayerFilter, entityTypes, bbox, includeXrefContents, unitsPerMeter, maxEntities, outputPath }, document: document, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_linework_trace", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<LineworkTraceData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Trace a point or handle to the CAD line or polyline that contains it, including the nearest segment index and both endpoint coordinates.")]
    public static async Task<McpEnvelope<LineworkTraceData>> LineworkTrace(
        ForgeToolRunner runner,
        [Description("X of the query point, in the units given by units.")] double x = 0,
        [Description("Y of the query point, in the units given by units.")] double y = 0,
        [Description("Coordinate units for x and y: drawing or meters.")] string? units = null,
        [Description("Search radius in drawing units. The default is 500.")] double tolerance = 500,
        [Description("Extra radius for neighbouring entities. 0 returns only the hit.")] double contextRadius = 0,
        [Description("Entity handle to trace instead of a point.")] string? handle = null,
        [Description(ForgeArgDescriptions.LwSource)] string? source = null,
        [Description(ForgeArgDescriptions.LwDumpPath)] string? dumpPath = null,
        [Description(ForgeArgDescriptions.LwLayerFilter)] string? layerFilter = null,
        [Description(ForgeArgDescriptions.LwLayerSuffix)] string? layerSuffix = null,
        [Description(ForgeArgDescriptions.LwLayerMatch)] string? layerMatch = null,
        [Description(ForgeArgDescriptions.LwExcludeLayer)] string? excludeLayerFilter = null,
        [Description(ForgeArgDescriptions.LwEntityTypes)] string? entityTypes = null,
        [Description(ForgeArgDescriptions.LwBbox)] double[]? bbox = null,
        [Description(ForgeArgDescriptions.LwIncludeXref)] bool includeXrefContents = true,
        [Description(ForgeArgDescriptions.LwUnitsPerMeter)] double unitsPerMeter = 0,
        [Description(ForgeArgDescriptions.LwMaxEntities)] int maxEntities = 50000,
        [Description(ForgeArgDescriptions.LwOutputPath)] string? outputPath = null,
        [Description(ForgeArgDescriptions.TargetDocument)] string? document = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<LineworkTraceData>(await runner.InvokeAsync("forge_linework_trace", new { x, y, units, tolerance, contextRadius, handle, source, dumpPath, layerFilter, layerSuffix, layerMatch, excludeLayerFilter, entityTypes, bbox, includeXrefContents, unitsPerMeter, maxEntities, outputPath }, document: document, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_linework_topology", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<LineworkTopologyData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Build the linework graph: shared-vertex nodes, edges, T-junctions, crossings, continuous runs, and dead ends.")]
    public static async Task<McpEnvelope<LineworkTopologyData>> LineworkTopology(
        ForgeToolRunner runner,
        [Description("Distance that snaps two vertices into one node. The default is 10 drawing units.")] double vertexTolerance = 10,
        [Description("Distance from a node to another edge that counts as a T-junction. The default is 50.")] double junctionTolerance = 50,
        [Description("When true, report interior crossings between segments.")] bool includeCrossings = true,
        [Description(ForgeArgDescriptions.LwSource)] string? source = null,
        [Description(ForgeArgDescriptions.LwDumpPath)] string? dumpPath = null,
        [Description(ForgeArgDescriptions.LwLayerFilter)] string? layerFilter = null,
        [Description(ForgeArgDescriptions.LwLayerSuffix)] string? layerSuffix = null,
        [Description(ForgeArgDescriptions.LwLayerMatch)] string? layerMatch = null,
        [Description(ForgeArgDescriptions.LwExcludeLayer)] string? excludeLayerFilter = null,
        [Description(ForgeArgDescriptions.LwEntityTypes)] string? entityTypes = null,
        [Description(ForgeArgDescriptions.LwBbox)] double[]? bbox = null,
        [Description(ForgeArgDescriptions.LwIncludeXref)] bool includeXrefContents = true,
        [Description(ForgeArgDescriptions.LwUnitsPerMeter)] double unitsPerMeter = 0,
        [Description(ForgeArgDescriptions.LwMaxEntities)] int maxEntities = 50000,
        [Description(ForgeArgDescriptions.LwOutputPath)] string? outputPath = null,
        [Description(ForgeArgDescriptions.TargetDocument)] string? document = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<LineworkTopologyData>(await runner.InvokeAsync("forge_linework_topology", new { vertexTolerance, junctionTolerance, includeCrossings, source, dumpPath, layerFilter, layerSuffix, layerMatch, excludeLayerFilter, entityTypes, bbox, includeXrefContents, unitsPerMeter, maxEntities, outputPath }, document: document, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_linework_coverage", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<LineworkCoverageData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Compare CAD linework with modeled pipe centerlines in meters. Reports CAD with no pipe, pipes with no CAD, and partial sub-ranges.")]
    public static async Task<McpEnvelope<LineworkCoverageData>> LineworkCoverage(
        ForgeToolRunner runner,
        [Description(ForgeArgDescriptions.LwModelSegments)] PipeSegmentDto[]? modelSegments = null,
        [Description(ForgeArgDescriptions.LwModelSegmentsPath)] string? modelSegmentsPath = null,
        [Description("Match tolerance in meters. The default is 0.3.")] double toleranceMeters = 0.3,
        [Description("Sample step along each segment, in meters. The default is 0.5.")] double stepMeters = 0.5,
        [Description("Fraction of a segment that must be covered before it counts as covered. The default is 0.5.")] double minCoverageFraction = 0.5,
        [Description("Model coordinate units in one meter. The default is 1.")] double modelUnitsPerMeter = 1,
        [Description(ForgeArgDescriptions.LwSource)] string? source = null,
        [Description(ForgeArgDescriptions.LwDumpPath)] string? dumpPath = null,
        [Description(ForgeArgDescriptions.LwLayerFilter)] string? layerFilter = null,
        [Description(ForgeArgDescriptions.LwLayerSuffix)] string? layerSuffix = null,
        [Description(ForgeArgDescriptions.LwLayerMatch)] string? layerMatch = null,
        [Description(ForgeArgDescriptions.LwExcludeLayer)] string? excludeLayerFilter = null,
        [Description(ForgeArgDescriptions.LwEntityTypes)] string? entityTypes = null,
        [Description(ForgeArgDescriptions.LwBbox)] double[]? bbox = null,
        [Description(ForgeArgDescriptions.LwIncludeXref)] bool includeXrefContents = true,
        [Description(ForgeArgDescriptions.LwUnitsPerMeter)] double unitsPerMeter = 0,
        [Description(ForgeArgDescriptions.LwMaxEntities)] int maxEntities = 50000,
        [Description(ForgeArgDescriptions.LwOutputPath)] string? outputPath = null,
        [Description(ForgeArgDescriptions.TargetDocument)] string? document = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<LineworkCoverageData>(await runner.InvokeAsync("forge_linework_coverage", new { modelSegments, modelSegmentsPath, toleranceMeters, stepMeters, minCoverageFraction, modelUnitsPerMeter, source, dumpPath, layerFilter, layerSuffix, layerMatch, excludeLayerFilter, entityTypes, bbox, includeXrefContents, unitsPerMeter, maxEntities, outputPath }, document: document, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_linework_segments", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<LineworkSegmentsData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Flatten linework to one row per segment: stable seg index, endpoints, layer, and length. units=meters scales the coordinates. transform or transformPath also returns transformed sT and eT.")]
    public static async Task<McpEnvelope<LineworkSegmentsData>> LineworkSegments(
        ForgeToolRunner runner,
        [Description("Output units. meters scales drawing units by unitsPerMeter.")] string? units = null,
        [Description("Calibration JSON written by forge_linework_transform. Applied when set.")] string? transformPath = null,
        [Description("Inline CAD to model transform. Used when transformPath is omitted.")] CadTransformSpec? transform = null,
        [Description(ForgeArgDescriptions.LwSource)] string? source = null,
        [Description(ForgeArgDescriptions.LwDumpPath)] string? dumpPath = null,
        [Description(ForgeArgDescriptions.LwLayerFilter)] string? layerFilter = null,
        [Description(ForgeArgDescriptions.LwLayerSuffix)] string? layerSuffix = null,
        [Description(ForgeArgDescriptions.LwLayerMatch)] string? layerMatch = null,
        [Description(ForgeArgDescriptions.LwExcludeLayer)] string? excludeLayerFilter = null,
        [Description(ForgeArgDescriptions.LwEntityTypes)] string? entityTypes = null,
        [Description(ForgeArgDescriptions.LwBbox)] double[]? bbox = null,
        [Description(ForgeArgDescriptions.LwIncludeXref)] bool includeXrefContents = true,
        [Description(ForgeArgDescriptions.LwUnitsPerMeter)] double unitsPerMeter = 0,
        [Description(ForgeArgDescriptions.LwMaxEntities)] int maxEntities = 50000,
        [Description(ForgeArgDescriptions.LwOutputPath)] string? outputPath = null,
        [Description(ForgeArgDescriptions.TargetDocument)] string? document = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<LineworkSegmentsData>(await runner.InvokeAsync("forge_linework_segments", new { units, transformPath, transform, source, dumpPath, layerFilter, layerSuffix, layerMatch, excludeLayerFilter, entityTypes, bbox, includeXrefContents, unitsPerMeter, maxEntities, outputPath }, document: document, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_linework_compare", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<LineworkCompareData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Mark each CAD segment and each model pipe as matched, partial, missing_in_revit, or extra_off_cad, with the nearest counterpart and distanceM. overlayPath writes an SVG. This tool does not change the drawing.")]
    public static async Task<McpEnvelope<LineworkCompareData>> LineworkCompare(
        ForgeToolRunner runner,
        [Description(ForgeArgDescriptions.LwModelSegments)] PipeSegmentDto[]? modelSegments = null,
        [Description(ForgeArgDescriptions.LwModelSegmentsPath)] string? modelSegmentsPath = null,
        [Description("Match tolerance in meters. The default is 0.3.")] double toleranceMeters = 0.3,
        [Description("Sample step along each segment, in meters. The default is 0.5.")] double stepMeters = 0.5,
        [Description("Fraction of a segment that must be covered before it counts as covered. The default is 0.5.")] double minCoverageFraction = 0.5,
        [Description("Model coordinate units in one meter. The default is 1.")] double modelUnitsPerMeter = 1,
        [Description("Drop model segments whose elevation is below this value.")] double? minZ = null,
        [Description("Drop model segments whose elevation is above this value.")] double? maxZ = null,
        [Description("Nearest-neighbour search radius in meters. The default is 50.")] double searchRadiusMeters = 50,
        [Description("Calibration JSON written by forge_linework_transform.")] string? transformPath = null,
        [Description("Inline CAD to model transform. Used when transformPath is omitted.")] CadTransformSpec? transform = null,
        [Description("Optional SVG path. Green is matched, red is missing, orange is extra, purple is partial.")] string? overlayPath = null,
        [Description(ForgeArgDescriptions.LwSource)] string? source = null,
        [Description(ForgeArgDescriptions.LwDumpPath)] string? dumpPath = null,
        [Description(ForgeArgDescriptions.LwLayerFilter)] string? layerFilter = null,
        [Description(ForgeArgDescriptions.LwLayerSuffix)] string? layerSuffix = null,
        [Description(ForgeArgDescriptions.LwLayerMatch)] string? layerMatch = null,
        [Description(ForgeArgDescriptions.LwExcludeLayer)] string? excludeLayerFilter = null,
        [Description(ForgeArgDescriptions.LwEntityTypes)] string? entityTypes = null,
        [Description(ForgeArgDescriptions.LwBbox)] double[]? bbox = null,
        [Description(ForgeArgDescriptions.LwIncludeXref)] bool includeXrefContents = true,
        [Description(ForgeArgDescriptions.LwUnitsPerMeter)] double unitsPerMeter = 0,
        [Description(ForgeArgDescriptions.LwMaxEntities)] int maxEntities = 50000,
        [Description(ForgeArgDescriptions.LwOutputPath)] string? outputPath = null,
        [Description(ForgeArgDescriptions.TargetDocument)] string? document = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<LineworkCompareData>(await runner.InvokeAsync("forge_linework_compare", new { modelSegments, modelSegmentsPath, toleranceMeters, stepMeters, minCoverageFraction, modelUnitsPerMeter, minZ, maxZ, searchRadiusMeters, transformPath, transform, overlayPath, source, dumpPath, layerFilter, layerSuffix, layerMatch, excludeLayerFilter, entityTypes, bbox, includeXrefContents, unitsPerMeter, maxEntities, outputPath }, document: document, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_linework_transform", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<LineworkTransformData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Calibrate or apply a 2D CAD to Revit transform on the server. Two or more pairs fit a similarity. Three or more with affine=true fit an affine transform. A residual above 0.5 m is a warning. This tool does not open AutoCAD.")]
    public static async Task<McpEnvelope<LineworkTransformData>> LineworkTransform(
        ForgeToolRunner runner,
        [Description("Anchor pairs {cad:[x,y], revit:[x,y], label?}. At least two for a similarity, three for affine.")] TransformPairDto[]? pairs = null,
        [Description("Fit a six-parameter affine transform. Requires at least three pairs.")] bool affine = false,
        [Description("Calibration file to write when pairs are set, or to read when applying.")] string? transformPath = null,
        [Description("Inline transform used when applying and transformPath is omitted.")] CadTransformSpec? transform = null,
        [Description("Points to convert, each [x,y].")] double[][]? points = null,
        [Description("Polylines to convert, each a list of [x,y].")] double[][][]? polylines = null,
        [Description("cadToRevit or revitToCad. Omit to use the calibration direction.")] string? direction = null,
        [Description("Name stored on the calibration.")] string? name = null,
        [Description("Notes stored on the calibration.")] string? notes = null,
        [Description("CAD units in one meter, such as 1000 for millimeters. 0 lets the fit decide.")] double cadUnitsPerMeter = 0,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<LineworkTransformData>(await runner.InvokeAsync("forge_linework_transform", new { pairs, affine, transformPath, transform, points, polylines, direction, name, notes, cadUnitsPerMeter }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_batch_run", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<BatchRunData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Run an AccoreConsole queue. Exit code 0 is not success when stdout contains *Cancel* or Unknown command. Resume with resumeBatchId. Dry-run scans each script body. OpenWorld stays false so paths are not scanned as commands.")]
    public static async Task<McpEnvelope<BatchRunData>> BatchRun(
        ForgeToolRunner runner,
        [Description("Jobs to run. Each item needs dwgPath and scriptPath. Omit when resuming.")] BatchJobDto[]? jobs = null,
        [Description("Continue later jobs after one job fails.")] bool continueOnError = true,
        [Description("Batch id from a previous partial run. Do not pass a different batchId name.")] string? resumeBatchId = null,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<BatchRunData>(await runner.InvokeAsync(
            "forge_batch_run",
            new { jobs = jobs ?? [], continueOnError, resumeBatchId },
            dryRun,
            reportProgress: (current, total, message) => progress?.Report(new ProgressNotificationValue { Progress = current, Total = total, Message = message }),
            cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_batch_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<BatchStatusData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Load a saved AccoreConsole batch resume state by batch id or artifact path.")]
    public static async Task<McpEnvelope<BatchStatusData>> BatchStatus(
        ForgeToolRunner runner,
        [Description("Batch id or absolute path of the batch state file.")] string batchIdOrPath,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<BatchStatusData>(await runner.InvokeAsync("forge_batch_status", new { batchIdOrPath }, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_exec_command", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true, OutputSchemaType = typeof(ExecEnvelope<ExecCommandData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Execute a raw AutoCAD command after the denylist, backup, and audit. queued and completed are required. A queued command returns Ok=false with completed=false.")]
    public static async Task<ExecEnvelope<ExecCommandData>> ExecCommand(
        ForgeToolRunner runner,
        [Description("AutoCAD command line. ERASE ALL, PURGE, and the other denylist forms are rejected.")] string command,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Exec<ExecCommandData>(await runner.InvokeAsync("forge_exec_command", new { command }, dryRun, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_exec_lisp", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true, OutputSchemaType = typeof(ExecEnvelope<ExecLispData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Execute AutoLISP after the denylist, backup, and audit. queued and completed are required. A queued expression returns Ok=false with completed=false.")]
    public static async Task<ExecEnvelope<ExecLispData>> ExecLisp(
        ForgeToolRunner runner,
        [Description("AutoLISP expression. ssget all-selection erase patterns are rejected.")] string lisp,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Exec<ExecLispData>(await runner.InvokeAsync("forge_exec_lisp", new { lisp }, dryRun, cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_run_script", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true, OutputSchemaType = typeof(McpEnvelope<RunScriptData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Run a script through accoreconsole.exe. The script body is scanned before spawn. Exit code 0 with *Cancel* or Unknown command in the transcript is accoreconsole_script_error. Client cancellation is not reported as accoreconsole_timeout.")]
    public static async Task<McpEnvelope<RunScriptData>> RunScript(
        ForgeToolRunner runner,
        [Description("Absolute DWG path.")] string dwgPath,
        [Description("Absolute .scr path. The file contents are scanned by the denylist.")] string scriptPath,
        [Description("Timeout in seconds. Cancellation of the MCP call is not this timeout.")] int? timeoutSeconds = null,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Envelope<RunScriptData>(await runner.InvokeAsync(
            "forge_run_script",
            new { dwgPath, scriptPath, timeoutSeconds },
            dryRun,
            reportProgress: (current, total, message) => progress?.Report(new ProgressNotificationValue { Progress = current, Total = total, Message = message }),
            cancellationToken: cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "forge_exec_dotnet", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true, OutputSchemaType = typeof(ExecEnvelope<ExecDotNetData>), TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Execute C# inside the AutoCAD plugin. Requires FORGE_ENABLE_UNSAFE_OPS=true on the server and the plugin, and unsafeAcknowledged=true on this call. There is no enable_unsafe_ops key. queued and completed are required.")]
    public static async Task<ExecEnvelope<ExecDotNetData>> ExecDotNet(
        ForgeToolRunner runner,
        [Description("C# snippet. Full assembly access, no sandbox.")] string code,
        [Description(ForgeArgDescriptions.UnsafeAck)] bool unsafeAcknowledged = false,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => ForgeOutputMap.Exec<ExecDotNetData>(await runner.InvokeAsync("forge_exec_dotnet", new { code }, dryRun, unsafeAcknowledged: unsafeAcknowledged, cancellationToken: cancellationToken).ConfigureAwait(false));
}

public sealed record CampaignEntryDto
{
    [Description("Layout name. Dry-run records it and does not change CurrentLayout.")]
    public string? Layout { get; init; }

    [Description(ForgeArgDescriptions.BlockName)]
    public string? BlockName { get; init; }

    [Description(ForgeArgDescriptions.BlockHandle)]
    public string? Handle { get; init; }

    [Description("Tag to value map. Drawing-number tags must come from forge_registry_lookup.")]
    public Dictionary<string, string> Attributes { get; init; } = new();
}

public sealed record BatchJobDto
{
    [Description("Absolute DWG path for this job.")]
    public string DwgPath { get; init; } = "";

    [Description("Absolute script path. The body is scanned even on dry-run.")]
    public string ScriptPath { get; init; } = "";

    [Description("Per-job timeout in seconds.")]
    public int? TimeoutSeconds { get; init; }
}
