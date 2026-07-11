using System.ComponentModel;
using Forge.Shared;
using ModelContextProtocol.Server;

namespace Forge.Server;

[McpServerToolType]
public sealed class ForgeMcpTools
{
    [McpServerTool(Name = "forge_system_health", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Check whether 765T-Forge server is alive and whether the AutoCAD plugin named pipe responds.")]
    public static Task<ForgeResult> SystemHealth(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_system_health", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_system_version", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Return 765T-Forge product/envelope versions plus AutoCAD and .NET information.")]
    public static Task<ForgeResult> SystemVersion(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_system_version", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_system_getvar", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Read one AutoCAD system variable, such as FILEDIA, PSTYLEMODE, BACKGROUNDPLOT, or CMDDIA.")]
    public static Task<ForgeResult> SystemGetVar(ForgeToolRunner runner, [Description("AutoCAD system variable name.")] string name, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_system_getvar", new { name }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_system_setvar", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Set one AutoCAD system variable with backup/audit/read-back around the write.")]
    public static Task<ForgeResult> SystemSetVar(ForgeToolRunner runner, string name, string value, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_system_setvar", new { name, value }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_system_capabilities", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Discover plot devices, media names, page setups, layouts, layer states, and plot styles available in the active session.")]
    public static Task<ForgeResult> SystemCapabilities(ForgeToolRunner runner, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_system_capabilities", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_doc_list_open", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("List open AutoCAD documents.")]
    public static Task<ForgeResult> DocListOpen(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_doc_list_open", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_doc_list_layouts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("List layouts in the active or specified AutoCAD document.")]
    public static Task<ForgeResult> DocListLayouts(ForgeToolRunner runner, string? document = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_doc_list_layouts", new { }, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_doc_open", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Open a DWG in AutoCAD with dialog suppression.")]
    public static Task<ForgeResult> DocOpen(ForgeToolRunner runner, string path, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_doc_open", new { path }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_doc_save", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Save the active document or save as a specified path with audit and backup.")]
    public static Task<ForgeResult> DocSave(ForgeToolRunner runner, string? path = null, bool overwriteAcknowledged = false, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_doc_save", new { path, overwriteAcknowledged }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_xref_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("List xrefs with names, paths, load state, and unresolved path hints.")]
    public static Task<ForgeResult> XrefList(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_xref_list", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_xref_reload", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Reload one xref by name, or all xrefs when name is omitted.")]
    public static Task<ForgeResult> XrefReload(ForgeToolRunner runner, string? name = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_xref_reload", new { name }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_xref_repath", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Set an xref path, preserving audit/read-back so missing metro xrefs can be fixed before publish.")]
    public static Task<ForgeResult> XrefRepath(ForgeToolRunner runner, string name, string path, bool reload = true, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_xref_repath", new { name, path, reload }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_xref_normalize_relative", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Rewrite absolute xref paths to host-relative paths and optionally reload.")]
    public static Task<ForgeResult> XrefNormalizeRelative(ForgeToolRunner runner, string? name = null, bool reload = true, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_xref_normalize_relative", new { name, reload }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_layer_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("List layer properties relevant to plotting and locked/frozen states.")]
    public static Task<ForgeResult> LayerList(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_layer_list", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_layer_state_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("List known layer states.")]
    public static Task<ForgeResult> LayerStateList(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_layer_state_list", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_layer_state_restore", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Restore a named layer state synchronously before publishing.")]
    public static Task<ForgeResult> LayerStateRestore(ForgeToolRunner runner, string name, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_layer_state_restore", new { name }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_block_list_attributes", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("List attributes on block references, optionally filtered by block name or handle.")]
    public static Task<ForgeResult> BlockListAttributes(ForgeToolRunner runner, string? blockName = null, string? handle = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_block_list_attributes", new { blockName, handle }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_block_get_attr", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Read a single block attribute from a titleblock or other attributed block.")]
    public static Task<ForgeResult> BlockGetAttr(ForgeToolRunner runner, string tag, string? blockName = null, string? handle = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_block_get_attr", new { tag, blockName, handle }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_block_set_attr", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Set a block attribute and read it back for titleblock verification.")]
    public static Task<ForgeResult> BlockSetAttr(ForgeToolRunner runner, string tag, string value, string? blockName = null, string? handle = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_block_set_attr", new { tag, value, blockName, handle }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_block_campaign", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Apply a titleblock attribute campaign (multi layout/tag) with dry-run diffs. Supports Unicode values.")]
    public static Task<ForgeResult> BlockCampaign(ForgeToolRunner runner, CampaignEntryDto[] entries, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_block_campaign", new { entries }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_layout_page_setup_import", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Import page setup from a DWT/DWG template using AutoCAD command automation.")]
    public static Task<ForgeResult> PageSetupImport(ForgeToolRunner runner, string templatePath, string setupName, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_layout_page_setup_import", new { templatePath, setupName }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_layout_page_setup_apply", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Apply an existing page setup to a layout synchronously via PlotSettings API.")]
    public static Task<ForgeResult> PageSetupApply(ForgeToolRunner runner, string setupName, string? layout = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_layout_page_setup_apply", new { setupName, layout }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_plot_to_pdf", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Plot one layout to PDF with configurable device, paper, CTB/STB plot style, area, and overwrite acknowledgement.")]
    public static Task<ForgeResult> PlotToPdf(
        ForgeToolRunner runner,
        string outputPath,
        string? layout = null,
        string? device = null,
        string? paperSize = null,
        string? plotStyle = null,
        string? plotArea = null,
        string? orientation = null,
        string? scale = null,
        string? units = null,
        bool overwriteAcknowledged = false,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_plot_to_pdf", new { outputPath, layout, device, paperSize, plotStyle, plotArea, orientation, scale, units, overwriteAcknowledged }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_plot_publish", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Publish layouts to PDF via DSD + Publisher.PublishDsd when possible; falls back to per-layout -PLOT if DSD yields no file. Returns PublishReceipt with PDF probe. Preflight gate unless force=true.")]
    public static Task<ForgeResult> PlotPublish(
        ForgeToolRunner runner,
        string outputPath,
        string[] layouts,
        bool singlePdf = true,
        bool overwriteAcknowledged = false,
        bool requirePreflight = true,
        bool force = false,
        string[]? requiredTitleblockTags = null,
        string? titleblockBlockName = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_plot_publish", new { outputPath, layouts, singlePdf, overwriteAcknowledged, requirePreflight, force, requiredTitleblockTags = requiredTitleblockTags ?? [], titleblockBlockName }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_verify_titleblock", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Compare titleblock attributes against expected tag/value pairs.")]
    public static Task<ForgeResult> QaVerifyTitleblock(ForgeToolRunner runner, Dictionary<string, string> expected, string? blockName = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_qa_verify_titleblock", new { expected, blockName }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_check_xrefs", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Detect unloaded, missing, or unresolved xrefs before plotting.")]
    public static Task<ForgeResult> QaCheckXrefs(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_qa_check_xrefs", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_audit_layers", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Compare current layers against expected layer names and plotting policy.")]
    public static Task<ForgeResult> QaAuditLayers(ForgeToolRunner runner, string[] expectedLayers, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_qa_audit_layers", new { expectedLayers }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_readback", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Run a read-back query after a write operation.")]
    public static Task<ForgeResult> QaReadback(ForgeToolRunner runner, string targetTool, Dictionary<string, string>? keys = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_qa_readback", new { targetTool, keys }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_readback_after_timeout", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Timeout recovery protocol: read-back after a plugin timeout. Never retry the write blind — verify state first.")]
    public static Task<ForgeResult> QaReadbackAfterTimeout(ForgeToolRunner runner, string targetTool, Dictionary<string, string>? keys = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_qa_readback_after_timeout", new { targetTool, keys }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_audit_summarize", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Summarize the last N server/plugin audit JSONL records for this machine (paths may be sensitive).")]
    public static Task<ForgeResult> AuditSummarize(ForgeToolRunner runner, int limit = 20, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_audit_summarize", new { limit }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_preflight", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Run the publish readiness gate and write a structured QaReport artifact (xrefs, titleblock tags, unresolved #### fields, layers, pack v2, issue-set contract).")]
    public static Task<ForgeResult> QaPreflight(
        ForgeToolRunner runner,
        string[]? requiredTitleblockTags = null,
        string? titleblockBlockName = null,
        string[]? expectedLayers = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_qa_preflight", new { requiredTitleblockTags = requiredTitleblockTags ?? [], titleblockBlockName, expectedLayers = expectedLayers ?? [] }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_issue_set_validate", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Load/validate an IssueSetContract (layouts↔drawingNos↔rev) against the active drawing and optional registry.")]
    public static Task<ForgeResult> IssueSetValidate(ForgeToolRunner runner, string? contractPath = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_issue_set_validate", new { contractPath }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_sheet_inventory_import", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Import a read-only CSV sheet inventory (layout,drawingNo,rev[,title]) into an IssueSetContract. Does not write DST/SSM.")]
    public static Task<ForgeResult> SheetInventoryImport(
        ForgeToolRunner runner,
        string csvPath,
        string? contractId = null,
        string? projectId = null,
        string? outputContractPath = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_sheet_inventory_import", new { csvPath, contractId, projectId, outputContractPath }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_issue_set_diff", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Diff two PublishReceipt JSON artifacts (layouts added/removed, output bytes changed).")]
    public static Task<ForgeResult> IssueSetDiff(ForgeToolRunner runner, string currentReceiptPath, string? previousReceiptPath = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_issue_set_diff", new { currentReceiptPath, previousReceiptPath }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_recipe_issue_set", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Orchestrate inspect→normalize→fill→preflight→publish for a metro issue set. Refuses publish when preflight fails unless force=true.")]
    public static Task<ForgeResult> RecipeIssueSet(
        ForgeToolRunner runner,
        string outputPath,
        string[] layouts,
        bool singlePdf = true,
        bool overwriteAcknowledged = false,
        bool force = false,
        bool normalizeXrefs = true,
        string? layerState = null,
        CampaignEntryDto[]? campaignEntries = null,
        string[]? requiredTitleblockTags = null,
        string? titleblockBlockName = null,
        string[]? expectedLayers = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_recipe_issue_set", new
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
        }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_pack_and_go", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Pack host DWG, xrefs, and optional CTB/STB into an output folder with manifest.json and relative path rewrite.")]
    public static Task<ForgeResult> PackAndGo(
        ForgeToolRunner runner,
        string outputDirectory,
        string[]? includePlotStyles = null,
        bool rewritePaths = true,
        bool overwriteAcknowledged = false,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_pack_and_go", new { outputDirectory, includePlotStyles = includePlotStyles ?? [], rewritePaths, overwriteAcknowledged }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_registry_load", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Load a project drawing-number registry JSON. Subsequent titleblock writes for drawing-number tags must match the registry.")]
    public static Task<ForgeResult> RegistryLoad(ForgeToolRunner runner, string path, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_registry_load", new { path }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_registry_lookup", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Look up sheets in the loaded drawing registry, or a single drawingNo. Never invent drawing numbers.")]
    public static Task<ForgeResult> RegistryLookup(ForgeToolRunner runner, string? drawingNo = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_registry_lookup", new { drawingNo }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_pack_load", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Load a declarative project CAD standards pack (layers, forbidden layers, drawingNo regex, required titleblock tags).")]
    public static Task<ForgeResult> PackLoad(ForgeToolRunner runner, string path, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_pack_load", new { path }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_pack_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Show which standards pack and drawing registry are loaded in the plugin session.")]
    public static Task<ForgeResult> PackStatus(ForgeToolRunner runner, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_pack_status", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_system_tool_profile", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("List tool profiles (core|plot|qa) or return the tool allowlist for one profile to reduce agent tool noise.")]
    public static Task<ForgeResult> SystemToolProfile(ForgeToolRunner runner, string? name = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_system_tool_profile", new { name }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_viewport_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("List paper-space viewports (handle, layout, scale, lock).")]
    public static Task<ForgeResult> ViewportList(ForgeToolRunner runner, string? layout = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_viewport_list", new { layout }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_viewport_set_layer_freeze", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Freeze or thaw a layer inside a specific viewport by handle (VP freeze), without changing global layer state.")]
    public static Task<ForgeResult> ViewportSetLayerFreeze(ForgeToolRunner runner, string handle, string layer, bool freeze = true, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_viewport_set_layer_freeze", new { handle, layer, freeze }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_plot_fingerprint", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Capture plot environment fingerprint (PSTYLEMODE, BACKGROUNDPLOT, devices) and evaluate against the loaded standards pack.")]
    public static Task<ForgeResult> QaPlotFingerprint(ForgeToolRunner runner, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_qa_plot_fingerprint", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_dependency_closure", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Scan xref and pack plot-style dependencies for missing files before publish.")]
    public static Task<ForgeResult> QaDependencyClosure(ForgeToolRunner runner, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_qa_dependency_closure", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_dual_source", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Compare drawing-registry expected titleblock values against live attributes (dual-source truth).")]
    public static Task<ForgeResult> QaDualSource(ForgeToolRunner runner, string? layout = null, string? drawingNo = null, string? titleblockBlockName = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_qa_dual_source", new { layout, drawingNo, titleblockBlockName }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_modal_trap", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Detect automation modal traps (FILEDIA, EXPERT) before plot/publish.")]
    public static Task<ForgeResult> QaModalTrap(ForgeToolRunner runner, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_qa_modal_trap", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_xref_closure", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("List host xref closure graph (flat) with path existence for issue-set freeze.")]
    public static Task<ForgeResult> XrefClosure(ForgeToolRunner runner, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_xref_closure", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_xref_pin_save", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Save an xref closure pin snapshot (path/length/hash) for later verify before publish.")]
    public static Task<ForgeResult> XrefPinSave(ForgeToolRunner runner, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_xref_pin_save", new { }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_xref_pin_verify", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Compare current xref closure against a saved pin artifact.")]
    public static Task<ForgeResult> XrefPinVerify(ForgeToolRunner runner, string pinPath, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_xref_pin_verify", new { pinPath }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_transmittal_seal", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Create a cryptographic transmittal seal over host DWG, xrefs, and optional PDF/receipt (SHA256 or HMAC).")]
    public static Task<ForgeResult> TransmittalSealTool(
        ForgeToolRunner runner,
        string? outputPdfPath = null,
        string? receiptId = null,
        string? hmacKey = null,
        string? outputDirectory = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_transmittal_seal", new { outputPdfPath, receiptId, hmacKey, outputDirectory }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_publish_ceremony_check", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Evaluate publish ceremony (dry-run, preflight, human issue ack) and blast-radius budgets.")]
    public static Task<ForgeResult> PublishCeremonyCheck(
        ForgeToolRunner runner,
        bool dryRunDone = false,
        bool preflightPassed = false,
        bool issueAcknowledged = false,
        bool publishedStatusAck = false,
        bool requirePublishedAck = false,
        int? maxSheets = null,
        int? sheetsUsed = null,
        int? maxDestructiveExecs = null,
        int? destructiveExecsUsed = null,
        int? maxPathRewrites = null,
        int? pathRewritesUsed = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_publish_ceremony_check", new
        {
            dryRunDone,
            preflightPassed,
            issueAcknowledged,
            publishedStatusAck,
            requirePublishedAck,
            maxSheets,
            sheetsUsed,
            maxDestructiveExecs,
            destructiveExecsUsed,
            maxPathRewrites,
            pathRewritesUsed
        }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_cde_gate_evaluate", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("CAD-side ISO 19650-lite CDE gate for status/rev/naming; optional sidecar JSON for upload metadata.")]
    public static Task<ForgeResult> CdeGateEvaluate(
        ForgeToolRunner runner,
        string? status = null,
        string? rev = null,
        string? drawingNo = null,
        bool treatingAsIssued = false,
        string? namingRegex = null,
        string? revisionScheme = null,
        string[]? allowedStatuses = null,
        bool writeSidecar = false,
        string? sidecarDirectory = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_cde_gate_evaluate", new
        {
            status,
            rev,
            drawingNo,
            treatingAsIssued,
            namingRegex,
            revisionScheme,
            allowedStatuses,
            writeSidecar,
            sidecarDirectory
        }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_batch_run", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Run an AccoreConsole job queue over multiple DWG/script pairs with partial-success reporting and optional resume batchId.")]
    public static Task<ForgeResult> BatchRun(
        ForgeToolRunner runner,
        BatchJobDto[]? jobs = null,
        bool continueOnError = true,
        string? resumeBatchId = null,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_batch_run", new { jobs = jobs ?? [], continueOnError, resumeBatchId }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_batch_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Load a saved AccoreConsole batch resume state by batchId or artifact path.")]
    public static Task<ForgeResult> BatchStatus(ForgeToolRunner runner, string batchIdOrPath, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_batch_status", new { batchIdOrPath }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_exec_command", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Execute a raw AutoCAD command string after denylist, backup, audit, and dry-run checks. Prefers sync Editor.Command; if queued, completed=false.")]
    public static Task<ForgeResult> ExecCommand(ForgeToolRunner runner, string command, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_exec_command", new { command }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_exec_lisp", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Execute an AutoLISP expression after denylist, backup, audit, and UTF-8-safe transport. Prefers sync; if queued, completed=false.")]
    public static Task<ForgeResult> ExecLisp(ForgeToolRunner runner, string lisp, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_exec_lisp", new { lisp }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_run_script", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Run a .scr or .lsp script against a DWG through accoreconsole.exe with pre-run backup.")]
    public static Task<ForgeResult> RunScript(ForgeToolRunner runner, string dwgPath, string scriptPath, int? timeoutSeconds = null, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_run_script", new { dwgPath, scriptPath, timeoutSeconds }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_exec_dotnet", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true, OutputSchemaType = typeof(ForgeResult))]
    [Description("Execute a C# script inside the AutoCAD plugin context. Disabled unless unsafe ops are enabled and acknowledged.")]
    public static Task<ForgeResult> ExecDotNet(ForgeToolRunner runner, string code, bool unsafeAcknowledged = false, bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_exec_dotnet", new { code }, dryRun, unsafeAcknowledged: unsafeAcknowledged, cancellationToken: cancellationToken);
}

public sealed record CampaignEntryDto
{
    public string? Layout { get; init; }
    public string? BlockName { get; init; }
    public string? Handle { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = new();
}

public sealed record BatchJobDto
{
    public string DwgPath { get; init; } = "";
    public string ScriptPath { get; init; } = "";
    public int? TimeoutSeconds { get; init; }
}
