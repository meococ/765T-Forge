namespace Forge.Shared;

public sealed record ToolMetadata(
    string Name,
    string Group,
    bool ReadOnly,
    bool Destructive,
    bool Idempotent,
    bool OpenWorld,
    bool RequiresBackup,
    bool RequiresAutoCad = true,
    bool Unsafe = false);

public static class ForgeToolRegistry
{
    /// <summary>
    /// <see cref="ToolMetadata.ReadOnly"/> means the tool has no write path at all.
    /// It is not "does not modify the drawing": a tool that writes a report, contract,
    /// receipt, sidecar or batch-state file must be <c>ReadOnly = false</c>, because MCP
    /// hosts commonly auto-approve tools annotated read-only. Three tools are deliberately
    /// not built with <c>Read(...)</c> for that reason: <c>forge_qa_preflight</c> always
    /// writes a QA artifact, <c>forge_sheet_inventory_import</c> writes the contract when
    /// <c>outputContractPath</c> is set, and <c>forge_cde_gate_evaluate</c> writes a sidecar
    /// when <c>writeSidecar</c> is set. The matching <c>[McpServerTool]</c> hints in
    /// <c>ForgeMcpTools</c> must agree; ToolMetadataTests enforces that.
    /// </summary>
    private static readonly Dictionary<string, ToolMetadata> Tools = new(StringComparer.OrdinalIgnoreCase)
    {
        ["forge_system_health"] = Read("forge_system_health", "system", requiresAutoCad: false),
        ["forge_system_version"] = Read("forge_system_version", "system"),
        ["forge_system_getvar"] = Read("forge_system_getvar", "system"),
        ["forge_system_setvar"] = Write("forge_system_setvar", "system", idempotent: true),
        ["forge_system_capabilities"] = Read("forge_system_capabilities", "system"),
        ["forge_doc_list_open"] = Read("forge_doc_list_open", "document"),
        ["forge_doc_list_layouts"] = Read("forge_doc_list_layouts", "document"),
        ["forge_doc_open"] = Write("forge_doc_open", "document", idempotent: true),
        ["forge_doc_save"] = Write("forge_doc_save", "document", idempotent: true),
        ["forge_xref_list"] = Read("forge_xref_list", "xref"),
        ["forge_xref_reload"] = Write("forge_xref_reload", "xref", idempotent: true),
        ["forge_xref_repath"] = Write("forge_xref_repath", "xref", idempotent: true),
        ["forge_xref_normalize_relative"] = Write("forge_xref_normalize_relative", "xref", idempotent: true),
        ["forge_layer_list"] = Read("forge_layer_list", "layer"),
        ["forge_layer_state_list"] = Read("forge_layer_state_list", "layer"),
        ["forge_layer_state_restore"] = Write("forge_layer_state_restore", "layer", idempotent: true),
        ["forge_block_list_attributes"] = Read("forge_block_list_attributes", "block"),
        ["forge_block_get_attr"] = Read("forge_block_get_attr", "block"),
        ["forge_block_set_attr"] = Write("forge_block_set_attr", "block", idempotent: true),
        ["forge_block_campaign"] = Write("forge_block_campaign", "block", idempotent: true),
        ["forge_layout_page_setup_import"] = Write("forge_layout_page_setup_import", "layout", idempotent: true),
        ["forge_layout_page_setup_apply"] = Write("forge_layout_page_setup_apply", "layout", idempotent: true),
        ["forge_plot_to_pdf"] = Write("forge_plot_to_pdf", "plot", idempotent: false),
        ["forge_plot_publish"] = Write("forge_plot_publish", "plot", idempotent: false),
        ["forge_qa_verify_titleblock"] = Read("forge_qa_verify_titleblock", "qa"),
        ["forge_qa_check_xrefs"] = Read("forge_qa_check_xrefs", "qa"),
        ["forge_qa_audit_layers"] = Read("forge_qa_audit_layers", "qa"),
        ["forge_qa_readback"] = Read("forge_qa_readback", "qa"),
        ["forge_qa_preflight"] = new ToolMetadata("forge_qa_preflight", "qa", false, false, true, false, false),
        ["forge_qa_readback_after_timeout"] = Read("forge_qa_readback_after_timeout", "qa"),
        ["forge_audit_summarize"] = Read("forge_audit_summarize", "qa", requiresAutoCad: false),
        ["forge_issue_set_validate"] = Read("forge_issue_set_validate", "recipe"),
        ["forge_sheet_inventory_import"] = new ToolMetadata("forge_sheet_inventory_import", "recipe", false, false, false, false, false, RequiresAutoCad: false),
        ["forge_issue_set_diff"] = Read("forge_issue_set_diff", "recipe", requiresAutoCad: false),
        ["forge_recipe_issue_set"] = Write("forge_recipe_issue_set", "recipe"),
        ["forge_pack_and_go"] = Write("forge_pack_and_go", "pack"),
        ["forge_registry_load"] = new ToolMetadata("forge_registry_load", "registry", false, false, true, false, false),
        ["forge_registry_lookup"] = Read("forge_registry_lookup", "registry"),
        ["forge_pack_load"] = new ToolMetadata("forge_pack_load", "standards", false, false, true, false, false),
        ["forge_pack_status"] = Read("forge_pack_status", "standards"),
        ["forge_system_tool_profile"] = Read("forge_system_tool_profile", "system", requiresAutoCad: false),
        ["forge_viewport_list"] = Read("forge_viewport_list", "viewport"),
        ["forge_viewport_set_layer_freeze"] = Write("forge_viewport_set_layer_freeze", "viewport", idempotent: true),
        ["forge_qa_plot_fingerprint"] = Read("forge_qa_plot_fingerprint", "qa"),
        ["forge_qa_dependency_closure"] = Read("forge_qa_dependency_closure", "qa"),
        ["forge_qa_dual_source"] = Read("forge_qa_dual_source", "qa"),
        ["forge_qa_modal_trap"] = Read("forge_qa_modal_trap", "qa"),
        ["forge_xref_closure"] = Read("forge_xref_closure", "xref"),
        ["forge_xref_pin_save"] = Write("forge_xref_pin_save", "xref", idempotent: true),
        ["forge_xref_pin_verify"] = Read("forge_xref_pin_verify", "xref"),
        ["forge_transmittal_seal"] = Write("forge_transmittal_seal", "pack", idempotent: false),
        ["forge_publish_ceremony_check"] = Read("forge_publish_ceremony_check", "qa"),
        ["forge_cde_gate_evaluate"] = new ToolMetadata("forge_cde_gate_evaluate", "qa", false, false, true, false, false),
        ["forge_batch_run"] = new ToolMetadata("forge_batch_run", "batch", false, true, false, false, true, RequiresAutoCad: false, Unsafe: true),
        ["forge_batch_status"] = Read("forge_batch_status", "batch", requiresAutoCad: false),
        ["forge_exec_command"] = Destructive("forge_exec_command", "exec", unsafeTool: true),
        ["forge_exec_lisp"] = Destructive("forge_exec_lisp", "exec", unsafeTool: true),
        ["forge_run_script"] = Destructive("forge_run_script", "exec", requiresAutoCad: false, unsafeTool: true),
        ["forge_exec_dotnet"] = Destructive("forge_exec_dotnet", "exec", unsafeTool: true),
    };

    public static IReadOnlyCollection<ToolMetadata> All => Tools.Values;

    public static ToolMetadata Get(string name)
    {
        return Tools.TryGetValue(name, out var metadata)
            ? metadata
            : Destructive(name, "unknown");
    }

    private static ToolMetadata Read(string name, string group, bool requiresAutoCad = true)
    {
        return new ToolMetadata(name, group, true, false, true, false, false, requiresAutoCad);
    }

    private static ToolMetadata Write(string name, string group, bool idempotent = false)
    {
        return new ToolMetadata(name, group, false, false, idempotent, false, true);
    }

    private static ToolMetadata Destructive(string name, string group, bool requiresAutoCad = true, bool unsafeTool = false)
    {
        return new ToolMetadata(name, group, false, true, false, true, true, requiresAutoCad, unsafeTool);
    }
}
