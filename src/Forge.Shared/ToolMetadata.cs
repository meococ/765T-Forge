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
        // C15: no active-document backup on the stated basis that the import runs in a fresh
        // temporary document and the source DGN is never written; an existing target is
        // backed up explicitly in the handler before SaveAs.
        ["forge_dgn_import"] = new ToolMetadata("forge_dgn_import", "dgn", false, false, false, false, false),
        // docs/cad-authoring-tools.md C20–C23. Draw tools and block_insert are additive-only
        // (new entities from explicit typed params — no selection, no command strings), so
        // they skip the pre-write DWG copy like C15. Entity mutations rewrite existing
        // geometry by explicit handle: destructive with the standard backup.
        ["forge_draw_line"] = new ToolMetadata("forge_draw_line", "draw", false, false, false, false, false),
        ["forge_draw_polyline"] = new ToolMetadata("forge_draw_polyline", "draw", false, false, false, false, false),
        ["forge_draw_circle"] = new ToolMetadata("forge_draw_circle", "draw", false, false, false, false, false),
        ["forge_draw_arc"] = new ToolMetadata("forge_draw_arc", "draw", false, false, false, false, false),
        ["forge_draw_text"] = new ToolMetadata("forge_draw_text", "draw", false, false, false, false, false),
        ["forge_draw_mtext"] = new ToolMetadata("forge_draw_mtext", "draw", false, false, false, false, false),
        ["forge_draw_hatch"] = new ToolMetadata("forge_draw_hatch", "draw", false, false, false, false, false),
        ["forge_draw_dimension"] = new ToolMetadata("forge_draw_dimension", "draw", false, false, false, false, false),
        ["forge_draw_batch"] = new ToolMetadata("forge_draw_batch", "draw", false, false, false, false, false),
        ["forge_entity_get"] = Read("forge_entity_get", "entity"),
        ["forge_entity_list"] = Read("forge_entity_list", "entity"),
        ["forge_entity_move"] = new ToolMetadata("forge_entity_move", "entity", false, true, false, false, true),
        ["forge_entity_copy"] = new ToolMetadata("forge_entity_copy", "entity", false, true, false, false, true),
        ["forge_entity_rotate"] = new ToolMetadata("forge_entity_rotate", "entity", false, true, false, false, true),
        ["forge_entity_scale"] = new ToolMetadata("forge_entity_scale", "entity", false, true, false, false, true),
        ["forge_entity_mirror"] = new ToolMetadata("forge_entity_mirror", "entity", false, true, false, false, true),
        ["forge_entity_offset"] = new ToolMetadata("forge_entity_offset", "entity", false, true, false, false, true),
        ["forge_entity_erase"] = new ToolMetadata("forge_entity_erase", "entity", false, true, false, false, true),
        ["forge_entity_set_props"] = new ToolMetadata("forge_entity_set_props", "entity", false, true, false, false, true),
        // forge_undo is the recovery mechanism itself — a pre-write backup would be absurd.
        ["forge_undo"] = new ToolMetadata("forge_undo", "entity", false, false, false, false, false),
        ["forge_block_create"] = new ToolMetadata("forge_block_create", "block", false, true, false, false, true),
        ["forge_block_insert"] = new ToolMetadata("forge_block_insert", "block", false, false, false, false, false),
        ["forge_block_list"] = Read("forge_block_list", "block"),
        ["forge_layer_create"] = new ToolMetadata("forge_layer_create", "layer", false, false, true, false, false),
        ["forge_layout_create"] = new ToolMetadata("forge_layout_create", "layout", false, false, false, false, false),
        ["forge_qa_verify_titleblock"] = Read("forge_qa_verify_titleblock", "qa"),
        ["forge_qa_check_xrefs"] = Read("forge_qa_check_xrefs", "qa"),
        ["forge_qa_audit_layers"] = Read("forge_qa_audit_layers", "qa"),
        ["forge_qa_readback"] = Read("forge_qa_readback", "qa"),
        ["forge_qa_preflight"] = Read("forge_qa_preflight", "qa"),
        ["forge_qa_readback_after_timeout"] = Read("forge_qa_readback_after_timeout", "qa"),
        ["forge_audit_summarize"] = Read("forge_audit_summarize", "qa", requiresAutoCad: false),
        ["forge_issue_set_validate"] = Read("forge_issue_set_validate", "recipe"),
        ["forge_sheet_inventory_import"] = Read("forge_sheet_inventory_import", "recipe", requiresAutoCad: false),
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
        ["forge_linework_dump"] = Read("forge_linework_dump", "linework", requiresAutoCad: false),
        ["forge_linework_trace"] = Read("forge_linework_trace", "linework", requiresAutoCad: false),
        ["forge_linework_topology"] = Read("forge_linework_topology", "linework", requiresAutoCad: false),
        ["forge_linework_coverage"] = Read("forge_linework_coverage", "linework", requiresAutoCad: false),
        ["forge_linework_segments"] = Read("forge_linework_segments", "linework", requiresAutoCad: false),
        ["forge_linework_compare"] = Read("forge_linework_compare", "linework", requiresAutoCad: false),
        ["forge_linework_transform"] = Read("forge_linework_transform", "linework", requiresAutoCad: false),
        ["forge_batch_run"] = new ToolMetadata("forge_batch_run", "batch", false, true, false, false, true, RequiresAutoCad: false),
        ["forge_batch_status"] = Read("forge_batch_status", "batch", requiresAutoCad: false),
        ["forge_exec_command"] = Destructive("forge_exec_command", "exec"),
        ["forge_exec_lisp"] = Destructive("forge_exec_lisp", "exec"),
        ["forge_run_script"] = Destructive("forge_run_script", "exec", requiresAutoCad: false),
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
