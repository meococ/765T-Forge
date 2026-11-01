using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Forge.Server;

[McpServerPromptType]
public sealed class ForgeMcpPrompts
{
    [McpServerPrompt(Name = "issue_set_runbook", Title = "Issue Set Runbook")]
    [Description("Standard metro/AEC issue-set publish runbook for 765T-Forge.")]
    public static string IssueSetRunbook()
        => """
           765T-Forge issue-set runbook:
           1. forge_system_health — confirm pipe matches FORGE_PIPE_NAME.
           2. forge_registry_load + forge_pack_load when project files exist.
           3. forge_doc_list_layouts / forge_xref_list / forge_system_capabilities.
           4. Dry-run forge_xref_normalize_relative and forge_block_campaign (values ONLY from registry).
           5. forge_qa_preflight — refuse publish if passed=false unless human force.
           6. forge_plot_publish or forge_recipe_issue_set with overwriteAcknowledged when needed.
           7. Verify PDF exists; never invent drawing numbers; never ERASE ALL via executors.
           """;

    [McpServerPrompt(Name = "safety_first", Title = "Safety First")]
    [Description("Remind the agent of Forge safety contracts before mutating DWGs.")]
    public static string SafetyFirst()
        => """
           Safety first:
           - Prefer typed tools over forge_exec_*.
           - dryRun before writes; read-back after writes.
           - If exec returns queued=true or completed=false, do not chain writes.
           - forge_exec_dotnet requires FORGE_ENABLE_UNSAFE_OPS and unsafeAcknowledged.
           - On timeout, assume write may have committed — QA before retry.
           """;

    [McpServerPrompt(Name = "dgn_convert", Title = "DGN to DWG Conversion")]
    [Description("Convert one DGN file into a target DWG via forge_dgn_import, with preflight checks.")]
    public static string DgnConvert(
        [Description("Full path to the source .dgn file")] string dgnPath,
        [Description("Full path for the output .dwg (empty = same folder, same name)")] string? targetDwgPath = null,
        [Description("Conversion units: master|sub|mm (empty = importer default)")] string? units = null)
        => $"""
           Convert the DGN {dgnPath} to DWG{(targetDwgPath is null ? "" : $" at {targetDwgPath}")}:
           1. dryRun forge_dgn_import first — it answers the overwrite/unsupported gates without touching anything.
           2. If the target exists, the refusal is dgn_target_overwrite_not_acknowledged — confirm archiving, then
              pass overwriteAcknowledged=true (or answer the elicitation prompt if the client offers one).
           3. Run forge_dgn_import for real{(units is null ? "" : $" with units=\"{units}\"")}; long imports send
              progress notifications and can be detached as an MCP task.
           4. Read back: the reply carries entityCount/layerCount; verify the target DWG opens and the counts
              are nonzero before reporting success.
           Never retry blindly on a timeout — the import may have already committed.
           """;

    [McpServerPrompt(Name = "publish_set", Title = "Publish Sheet Set")]
    [Description("Preflight + dry-run + real publish of a set of layouts to PDF.")]
    public static string PublishSet(
        [Description("Comma-separated layout names to publish (empty = all paper layouts)")] string? layouts = null,
        [Description("Output PDF path")] string? outputPath = null,
        [Description("true = one multi-page PDF with bookmarks (default false = per-sheet files)")] string? singlePdf = null)
        => $"""
           Publish the sheet set{(layouts is null ? "" : $" ({layouts})")}{(outputPath is null ? "" : $" to {outputPath}")}:
           1. forge_doc_list_layouts — confirm every named layout exists (completions can list them).
           2. forge_qa_preflight — pass or explicit human force; never publish over a failed gate silently.
           3. dryRun forge_plot_publish — the plan lists per-sheet output and device readiness.
           4. Publish for real{(singlePdf is null ? "" : $" with singlePdf={singlePdf}")}; a live publish emits
              per-sheet progress notifications and survives as an MCP task if the client detaches.
           5. Verify bytes on disk (plot_no_output is a hard fail) — then report per-sheet counts.
           """;

    [McpServerPrompt(Name = "drawing_cleanup", Title = "Drawing Cleanup")]
    [Description("Audit then clean the active drawing (purge, scale list, errors) — dryRun first.")]
    public static string DrawingCleanup(
        [Description("Layout to audit (empty = active/Model)")] string? layout = null,
        [Description("audit = report only; fix = also apply safe cleanups")] string? scope = null)
        => $"""
           Clean the active drawing{(layout is null ? "" : $" (layout {layout})")}, scope {(scope ?? "audit")}:
           1. forge_drawing_summary{(layout is null ? "" : $" layout=\"{layout}\"")} + forge_entity_list — baseline.
           2. forge_qa_audit_layers for layer hygiene; forge_qa_check_xrefs; forge_qa_annotation_clash for collisions.
           3. forge_standards_import for layer/linetype/style hygiene; dryRun every mutation first.
           4. "fix" scope: apply the safe cleanups one tool at a time, read back each, keep DBMOD honest.
           5. Report counts removed/fixed; anything refused stays in the report with its code.
           """;

    [McpServerPrompt(Name = "qa_review", Title = "QA Review")]
    [Description("Full read-only QA pass over a layout: preflight, clash, annotation checks.")]
    public static string QaReview(
        [Description("Layout to review (empty = active/Model)")] string? layout = null)
        => $"""
           QA review{(layout is null ? "" : $" of layout {layout}")}:
           1. forge_qa_preflight — titleblock/tag gate.
           2. forge_qa_audit_layers — layer hygiene; forge_qa_check_xrefs — xref integrity.
           3. forge_qa_annotation_clash — text overlap/crossing/door-swing/wall-straddle checks
              {(layout is null ? "" : $"(layout=\"{layout}\")")}.
           4. forge_view_capture only when a visual is genuinely needed — read the typed findings first.
           Report findings grouped by check with severity + handles; passed=false means fix before publish.
           """;
}
