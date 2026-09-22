#pragma warning disable MCPEXP001
using System.ComponentModel;
using Forge.Shared;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Forge.Server;

[McpServerToolType]
public sealed class ForgeMcpTools
{
    // Không gắn UseStructuredContent/OutputSchemaType=ForgeResult: object? Data (và ReadBack)
    // sinh JSON Schema "data": true — Cursor/Zod reject hết tool ở listOfferingsForUI.
    [McpServerTool(Name = "forge_system_health", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Check whether 765T-Forge server is alive and whether the AutoCAD plugin named pipe responds.")]
    public static Task<ForgeResult> SystemHealth(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_system_health", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_system_version", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Return 765T-Forge product/envelope versions plus AutoCAD and .NET information.")]
    public static Task<ForgeResult> SystemVersion(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_system_version", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_system_getvar", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Read one AutoCAD system variable, such as FILEDIA, PSTYLEMODE, BACKGROUNDPLOT, or CMDDIA.")]
    public static Task<ForgeResult> SystemGetVar(ForgeToolRunner runner, [Description("AutoCAD system variable name.")] string name, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_system_getvar", new { name }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_system_setvar", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Set one AutoCAD system variable with backup/audit/read-back around the write.")]
    public static Task<ForgeResult> SystemSetVar(ForgeToolRunner runner, string name, string value, [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_system_setvar", new { name, value }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_system_capabilities", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Discover plot devices, media names, page setups, layouts, layer states, and plot styles available in the active session.")]
    public static Task<ForgeResult> SystemCapabilities(ForgeToolRunner runner, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_system_capabilities", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_doc_list_open", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("List open AutoCAD documents.")]
    public static Task<ForgeResult> DocListOpen(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_doc_list_open", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_doc_list_layouts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("List layouts in the active or specified AutoCAD document.")]
    public static Task<ForgeResult> DocListLayouts(ForgeToolRunner runner, string? document = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_doc_list_layouts", new { }, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_doc_open", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Open a DWG in AutoCAD with dialog suppression.")]
    public static Task<ForgeResult> DocOpen(ForgeToolRunner runner, string path, [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_doc_open", new { path }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_doc_save", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Save the active document or save as a specified path with audit and backup.")]
    public static Task<ForgeResult> DocSave(ForgeToolRunner runner, string? path = null, bool overwriteAcknowledged = false, [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_doc_save", new { path, overwriteAcknowledged }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_xref_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("List xrefs with names, paths, load state, and unresolved path hints.")]
    public static Task<ForgeResult> XrefList(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_xref_list", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_xref_reload", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Reload one xref by name, or all xrefs when name is omitted.")]
    public static Task<ForgeResult> XrefReload(ForgeToolRunner runner, string? name = null, [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_xref_reload", new { name }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_xref_repath", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Set an xref path, preserving audit/read-back so missing metro xrefs can be fixed before publish.")]
    public static Task<ForgeResult> XrefRepath(ForgeToolRunner runner, string name, string path, bool reload = true, [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_xref_repath", new { name, path, reload }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_xref_normalize_relative", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Rewrite absolute xref paths to host-relative paths and optionally reload.")]
    public static Task<ForgeResult> XrefNormalizeRelative(ForgeToolRunner runner, string? name = null, bool reload = true, [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_xref_normalize_relative", new { name, reload }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_layer_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("List layer properties relevant to plotting and locked/frozen states.")]
    public static Task<ForgeResult> LayerList(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_layer_list", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_layer_state_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("List known layer states.")]
    public static Task<ForgeResult> LayerStateList(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_layer_state_list", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_layer_state_restore", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Restore a named layer state synchronously before publishing.")]
    public static Task<ForgeResult> LayerStateRestore(ForgeToolRunner runner, string name, [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_layer_state_restore", new { name }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_block_list_attributes", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("List attributes on block references, optionally filtered by block name or handle.")]
    public static Task<ForgeResult> BlockListAttributes(ForgeToolRunner runner, string? blockName = null, string? handle = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_block_list_attributes", new { blockName, handle }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_block_get_attr", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Read a single block attribute from a titleblock or other attributed block.")]
    public static Task<ForgeResult> BlockGetAttr(ForgeToolRunner runner, string tag, string? blockName = null, string? handle = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_block_get_attr", new { tag, blockName, handle }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_block_set_attr", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Set one block attribute and read it back. Pass handle, or blockName when the handle is unknown. Omitting both is rejected.")]
    public static Task<ForgeResult> BlockSetAttr(ForgeToolRunner runner, string tag, string value, [Description(ForgeArgDescriptions.BlockName)] string? blockName = null, [Description(ForgeArgDescriptions.BlockHandle)] string? handle = null, [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_block_set_attr", new { tag, value, blockName, handle }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_block_campaign", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Apply a titleblock attribute campaign. Each entry needs handle or blockName. updated counts only read-backs that match.")]
    public static Task<ForgeResult> BlockCampaign(ForgeToolRunner runner, CampaignEntryDto[] entries, [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_block_campaign", new { entries }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_layout_page_setup_import", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Import page setup from a DWT/DWG template using AutoCAD command automation.")]
    public static Task<ForgeResult> PageSetupImport(ForgeToolRunner runner, string templatePath, string setupName, [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_layout_page_setup_import", new { templatePath, setupName }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_layout_page_setup_apply", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Apply an existing page setup to a layout synchronously via PlotSettings API.")]
    public static Task<ForgeResult> PageSetupApply(ForgeToolRunner runner, string setupName, string? layout = null, [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_layout_page_setup_apply", new { setupName, layout }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_plot_to_pdf", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
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
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_plot_to_pdf", new { outputPath, layout, device, paperSize, plotStyle, plotArea, orientation, scale, units, overwriteAcknowledged }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_plot_publish", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Publish layouts to PDF via real DSD + Publisher.PublishDsd. Returns a PublishReceipt. passed=false or a failed PDF probe returns Ok=false. force is only when a human asks in this session, and a bypass still returns Ok=false (preflight_forced).")]
    public static Task<ForgeResult> PlotPublish(
        ForgeToolRunner runner,
        string outputPath,
        string[] layouts,
        bool singlePdf = true,
        bool overwriteAcknowledged = false,
        bool requirePreflight = true,
        [Description(ForgeArgDescriptions.Force)] bool force = false,
        string[]? requiredTitleblockTags = null,
        string? titleblockBlockName = null,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_plot_publish", new { outputPath, layouts, singlePdf, overwriteAcknowledged, requirePreflight, force, requiredTitleblockTags = requiredTitleblockTags ?? [], titleblockBlockName }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_verify_titleblock", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Compare titleblock attributes against expected tag/value pairs. passed=false returns Ok=false; the diffs stay in data.")]
    public static Task<ForgeResult> QaVerifyTitleblock(ForgeToolRunner runner, Dictionary<string, string> expected, string? blockName = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_qa_verify_titleblock", new { expected, blockName }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_check_xrefs", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Detect unloaded, missing, or unresolved xrefs before plotting. passed=false returns Ok=false.")]
    public static Task<ForgeResult> QaCheckXrefs(ForgeToolRunner runner, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_qa_check_xrefs", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_audit_layers", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Compare current layers against expected layer names. passed=false returns Ok=false.")]
    public static Task<ForgeResult> QaAuditLayers(ForgeToolRunner runner, string[] expectedLayers, CancellationToken cancellationToken)
        => runner.InvokeAsync("forge_qa_audit_layers", new { expectedLayers }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_readback", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Run a read-back query after a write operation.")]
    public static Task<ForgeResult> QaReadback(ForgeToolRunner runner, string targetTool, Dictionary<string, string>? keys = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_qa_readback", new { targetTool, keys }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_readback_after_timeout", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Timeout recovery protocol: read-back after a plugin timeout. Never retry the write blind — verify state first.")]
    public static Task<ForgeResult> QaReadbackAfterTimeout(ForgeToolRunner runner, string targetTool, Dictionary<string, string>? keys = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_qa_readback_after_timeout", new { targetTool, keys }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_audit_summarize", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Summarize the last N server/plugin audit JSONL records for this machine (paths may be sensitive).")]
    public static Task<ForgeResult> AuditSummarize(ForgeToolRunner runner, int limit = 20, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_audit_summarize", new { limit }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_qa_preflight", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Run the publish readiness gate and write a QaReport file. passed=false returns Ok=false with the report still in data.")]
    public static Task<ForgeResult> QaPreflight(
        ForgeToolRunner runner,
        string[]? requiredTitleblockTags = null,
        string? titleblockBlockName = null,
        string[]? expectedLayers = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_qa_preflight", new { requiredTitleblockTags = requiredTitleblockTags ?? [], titleblockBlockName, expectedLayers = expectedLayers ?? [] }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_issue_set_validate", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Load/validate an IssueSetContract against the active drawing. passed=false returns Ok=false; the report stays in data.")]
    public static Task<ForgeResult> IssueSetValidate(ForgeToolRunner runner, string? contractPath = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_issue_set_validate", new { contractPath }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_sheet_inventory_import", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Import a CSV sheet inventory (layout,drawingNo,rev[,title]) into an IssueSetContract. Writes outputContractPath when set. Does not write DST/SSM.")]
    public static Task<ForgeResult> SheetInventoryImport(
        ForgeToolRunner runner,
        string csvPath,
        [Description(ForgeArgDescriptions.ContractId)] string? contractId = null,
        string? projectId = null,
        string? outputContractPath = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_sheet_inventory_import", new { csvPath, contractId, projectId, outputContractPath }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_issue_set_diff", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Diff two PublishReceipt JSON artifacts (layouts added/removed, output bytes changed).")]
    public static Task<ForgeResult> IssueSetDiff(ForgeToolRunner runner, string currentReceiptPath, string? previousReceiptPath = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_issue_set_diff", new { currentReceiptPath, previousReceiptPath }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_recipe_issue_set", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Orchestrate inspect→normalize→fill→preflight→publish for a metro issue set. A failed preflight returns Ok=false. force is only when a human asks in this session, and a bypass still returns Ok=false (preflight_forced).")]
    public static Task<ForgeResult> RecipeIssueSet(
        ForgeToolRunner runner,
        string outputPath,
        string[] layouts,
        bool singlePdf = true,
        bool overwriteAcknowledged = false,
        [Description(ForgeArgDescriptions.Force)] bool force = false,
        bool normalizeXrefs = true,
        string? layerState = null,
        CampaignEntryDto[]? campaignEntries = null,
        string[]? requiredTitleblockTags = null,
        string? titleblockBlockName = null,
        string[]? expectedLayers = null,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
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

    [McpServerTool(Name = "forge_pack_and_go", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Pack host DWG, xrefs, and optional CTB/STB into an output folder with manifest.json and relative path rewrite.")]
    public static Task<ForgeResult> PackAndGo(
        ForgeToolRunner runner,
        string outputDirectory,
        string[]? includePlotStyles = null,
        bool rewritePaths = true,
        bool overwriteAcknowledged = false,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_pack_and_go", new { outputDirectory, includePlotStyles = includePlotStyles ?? [], rewritePaths, overwriteAcknowledged }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_registry_load", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Load a project drawing-number registry JSON. Subsequent titleblock writes for drawing-number tags must match the registry.")]
    public static Task<ForgeResult> RegistryLoad(ForgeToolRunner runner, string path, [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_registry_load", new { path }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_registry_lookup", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Look up sheets in the loaded drawing registry, or a single drawingNo. Never invent drawing numbers.")]
    public static Task<ForgeResult> RegistryLookup(ForgeToolRunner runner, string? drawingNo = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_registry_lookup", new { drawingNo }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_pack_load", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Load a declarative project CAD standards pack (layers, forbidden layers, drawingNo regex, required titleblock tags).")]
    public static Task<ForgeResult> PackLoad(ForgeToolRunner runner, string path, [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_pack_load", new { path }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_pack_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Show which standards pack and drawing registry are loaded in the plugin session.")]
    public static Task<ForgeResult> PackStatus(ForgeToolRunner runner, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_pack_status", new { }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_system_tool_profile", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("List tool profiles (core|plot|qa) or return the tool allowlist for one profile to reduce agent tool noise.")]
    public static Task<ForgeResult> SystemToolProfile(ForgeToolRunner runner, string? name = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_system_tool_profile", new { name }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_viewport_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("List paper-space viewports (handle, layout, scale, lock).")]
    public static Task<ForgeResult> ViewportList(ForgeToolRunner runner, string? layout = null, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_viewport_list", new { layout }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_viewport_set_layer_freeze", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Freeze or thaw a layer inside a specific viewport by handle (VP freeze), without changing global layer state.")]
    public static Task<ForgeResult> ViewportSetLayerFreeze(ForgeToolRunner runner, string handle, string layer, bool freeze = true, [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_viewport_set_layer_freeze", new { handle, layer, freeze }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_linework_dump", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Extract CAD linework entities (line/polyline/arc[/circle]) as ordered vertices with handles. Source: live drawing (model space + xref contents) or a pl_dump.txt file via source=dumpFile. Layer matching is xref-prefix aware; use layerSuffix/layerMatch for explicit modes.")]
    public static Task<ForgeResult> LineworkDump(
        ForgeToolRunner runner,
        string? source = null,
        string? dumpPath = null,
        string? layerFilter = null,
        string? layerSuffix = null,
        string? layerMatch = null,
        string? excludeLayerFilter = null,
        string? entityTypes = null,
        double[]? bbox = null,
        bool includeXrefContents = true,
        double unitsPerMeter = 0,
        int maxEntities = 50000,
        string? outputPath = null,
        string? document = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_linework_dump", new { source, dumpPath, layerFilter, layerSuffix, layerMatch, excludeLayerFilter, entityTypes, bbox, includeXrefContents, unitsPerMeter, maxEntities, outputPath }, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_linework_trace", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Trace a point (or handle) to the exact CAD line/polyline it belongs to: ordered vertices, nearest segment index, layer, and both end coordinates for junction/branch queries.")]
    public static Task<ForgeResult> LineworkTrace(
        ForgeToolRunner runner,
        double x = 0,
        double y = 0,
        string? units = null,
        double tolerance = 500,
        double contextRadius = 0,
        string? handle = null,
        string? source = null,
        string? dumpPath = null,
        string? layerFilter = null,
        string? layerSuffix = null,
        string? layerMatch = null,
        string? excludeLayerFilter = null,
        string? entityTypes = null,
        double[]? bbox = null,
        bool includeXrefContents = true,
        double unitsPerMeter = 0,
        int maxEntities = 50000,
        string? outputPath = null,
        string? document = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_linework_trace", new { x, y, units, tolerance, contextRadius, handle, source, dumpPath, layerFilter, layerSuffix, layerMatch, excludeLayerFilter, entityTypes, bbox, includeXrefContents, unitsPerMeter, maxEntities, outputPath }, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_linework_topology", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Build the connectivity graph of CAD linework: shared-vertex nodes, edges, T-junctions, X-crossings, continuous runs, and dead-end vs junction endpoint classification.")]
    public static Task<ForgeResult> LineworkTopology(
        ForgeToolRunner runner,
        double vertexTolerance = 10,
        double junctionTolerance = 50,
        bool includeCrossings = true,
        string? source = null,
        string? dumpPath = null,
        string? layerFilter = null,
        string? layerSuffix = null,
        string? layerMatch = null,
        string? excludeLayerFilter = null,
        string? entityTypes = null,
        double[]? bbox = null,
        bool includeXrefContents = true,
        double unitsPerMeter = 0,
        int maxEntities = 50000,
        string? outputPath = null,
        string? document = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_linework_topology", new { vertexTolerance, junctionTolerance, includeCrossings, source, dumpPath, layerFilter, layerSuffix, layerMatch, excludeLayerFilter, entityTypes, bbox, includeXrefContents, unitsPerMeter, maxEntities, outputPath }, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_linework_coverage", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Compare CAD linework against modeled pipe centerlines (meters): report CAD segments with no modeled pipe (missing), modeled pipes with no CAD segment (extra), and partial-coverage sub-ranges.")]
    public static Task<ForgeResult> LineworkCoverage(
        ForgeToolRunner runner,
        PipeSegmentDto[]? modelSegments = null,
        string? modelSegmentsPath = null,
        double toleranceMeters = 0.3,
        double stepMeters = 0.5,
        double minCoverageFraction = 0.5,
        double modelUnitsPerMeter = 1,
        string? source = null,
        string? dumpPath = null,
        string? layerFilter = null,
        string? layerSuffix = null,
        string? layerMatch = null,
        string? excludeLayerFilter = null,
        string? entityTypes = null,
        double[]? bbox = null,
        bool includeXrefContents = true,
        double unitsPerMeter = 0,
        int maxEntities = 50000,
        string? outputPath = null,
        string? document = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_linework_coverage", new { modelSegments, modelSegmentsPath, toleranceMeters, stepMeters, minCoverageFraction, modelUnitsPerMeter, source, dumpPath, layerFilter, layerSuffix, layerMatch, excludeLayerFilter, entityTypes, bbox, includeXrefContents, unitsPerMeter, maxEntities, outputPath }, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_linework_segments", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Flatten CAD linework to individual segments: stable seg index, precise endpoints, layer, and length — the canonical feed for route/QA scripts. units=meters scales to meters; transform/transformPath also emits transformed sT/eT endpoints.")]
    public static Task<ForgeResult> LineworkSegments(
        ForgeToolRunner runner,
        string? units = null,
        string? transformPath = null,
        CadTransformSpec? transform = null,
        string? source = null,
        string? dumpPath = null,
        string? layerFilter = null,
        string? layerSuffix = null,
        string? layerMatch = null,
        string? excludeLayerFilter = null,
        string? entityTypes = null,
        double[]? bbox = null,
        bool includeXrefContents = true,
        double unitsPerMeter = 0,
        int maxEntities = 50000,
        string? outputPath = null,
        string? document = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_linework_segments", new { units, transformPath, transform, source, dumpPath, layerFilter, layerSuffix, layerMatch, excludeLayerFilter, entityTypes, bbox, includeXrefContents, unitsPerMeter, maxEntities, outputPath }, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_linework_compare", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Mark CAD linework vs Revit pipe segments: per-item classification (cad: matched/partial/missing_in_revit; model: matched/partial/extra_off_cad) with nearest counterpart + distanceM, plus matched pairs and optional SVG overlay at overlayPath (green=matched, red=missing, orange=extra, purple=partial).")]
    public static Task<ForgeResult> LineworkCompare(
        ForgeToolRunner runner,
        PipeSegmentDto[]? modelSegments = null,
        string? modelSegmentsPath = null,
        double toleranceMeters = 0.3,
        double stepMeters = 0.5,
        double minCoverageFraction = 0.5,
        double modelUnitsPerMeter = 1,
        double? minZ = null,
        double? maxZ = null,
        double searchRadiusMeters = 50,
        string? transformPath = null,
        CadTransformSpec? transform = null,
        string? overlayPath = null,
        string? source = null,
        string? dumpPath = null,
        string? layerFilter = null,
        string? layerSuffix = null,
        string? layerMatch = null,
        string? excludeLayerFilter = null,
        string? entityTypes = null,
        double[]? bbox = null,
        bool includeXrefContents = true,
        double unitsPerMeter = 0,
        int maxEntities = 50000,
        string? outputPath = null,
        string? document = null,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_linework_compare", new { modelSegments, modelSegmentsPath, toleranceMeters, stepMeters, minCoverageFraction, modelUnitsPerMeter, minZ, maxZ, searchRadiusMeters, transformPath, transform, overlayPath, source, dumpPath, layerFilter, layerSuffix, layerMatch, excludeLayerFilter, entityTypes, bbox, includeXrefContents, unitsPerMeter, maxEntities, outputPath }, document: document, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_linework_transform", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Calibrate or apply a 2D CAD<->Revit transform (similarity or affine). pairs[{cad:[x,y],revit:[x,y],label?}] fits (>=2 similarity, >=3 affine); transformPath persists/loads the calibration; points/polylines convert coordinates. Project convention: mm->m scale 0.001 origin-to-origin; warning when max residual > 0.5 m.")]
    public static Task<ForgeResult> LineworkTransform(
        ForgeToolRunner runner,
        TransformPairDto[]? pairs = null,
        bool affine = false,
        string? transformPath = null,
        CadTransformSpec? transform = null,
        double[][]? points = null,
        double[][][]? polylines = null,
        string? direction = null,
        string? name = null,
        string? notes = null,
        double cadUnitsPerMeter = 0,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_linework_transform", new { pairs, affine, transformPath, transform, points, polylines, direction, name, notes, cadUnitsPerMeter }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_batch_run", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Run an AccoreConsole job queue over multiple DWG/script pairs with partial-success reporting and optional resume batchId.")]
    public static Task<ForgeResult> BatchRun(
        ForgeToolRunner runner,
        BatchJobDto[]? jobs = null,
        bool continueOnError = true,
        string? resumeBatchId = null,
        [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_batch_run", new { jobs = jobs ?? [], continueOnError, resumeBatchId }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_batch_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Load a saved AccoreConsole batch resume state by batchId or artifact path.")]
    public static Task<ForgeResult> BatchStatus(ForgeToolRunner runner, string batchIdOrPath, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_batch_status", new { batchIdOrPath }, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_exec_command", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Execute a raw AutoCAD command string after denylist, backup, audit, and dry-run checks. Prefers sync Editor.Command. If the command is only queued, Ok=false with completed=false.")]
    public static Task<ForgeResult> ExecCommand(ForgeToolRunner runner, string command, [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_exec_command", new { command }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_exec_lisp", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Execute an AutoLISP expression after denylist, backup, audit, and UTF-8-safe transport. If the expression is only queued, Ok=false with completed=false.")]
    public static Task<ForgeResult> ExecLisp(ForgeToolRunner runner, string lisp, [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_exec_lisp", new { lisp }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_run_script", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Run a .scr or .lsp script against a DWG through accoreconsole.exe with pre-run backup.")]
    public static Task<ForgeResult> RunScript(ForgeToolRunner runner, string dwgPath, string scriptPath, int? timeoutSeconds = null, [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false, CancellationToken cancellationToken = default)
        => runner.InvokeAsync("forge_run_script", new { dwgPath, scriptPath, timeoutSeconds }, dryRun, cancellationToken: cancellationToken);

    [McpServerTool(Name = "forge_exec_dotnet", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, TaskSupport = ToolTaskSupport.Forbidden)]
    [Description("Execute a C# script inside the AutoCAD plugin context. Disabled unless unsafe ops are enabled and acknowledged.")]
    public static Task<ForgeResult> ExecDotNet(ForgeToolRunner runner, string code, bool unsafeAcknowledged = false, [Description(ForgeArgDescriptions.DryRun)] bool dryRun = false, CancellationToken cancellationToken = default)
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
