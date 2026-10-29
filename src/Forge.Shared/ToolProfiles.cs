namespace Forge.Shared;

/// <summary>Named tool profiles to reduce agent tool-selection noise.</summary>
public static class ToolProfiles
{
    private static readonly Dictionary<string, string[]> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["core"] =
        [
            "forge_system_health",
            "forge_system_version",
            "forge_system_capabilities",
            "forge_doc_list_open",
            "forge_doc_activate",
            "forge_doc_list_layouts",
            "forge_drawing_summary",
            "forge_text_list",
            "forge_registry_load",
            "forge_registry_lookup",
            "forge_pack_load",
            "forge_pack_status"
        ],
        ["plot"] =
        [
            "forge_system_health",
            "forge_system_capabilities",
            "forge_qa_preflight",
            "forge_issue_set_validate",
            "forge_plot_to_pdf",
            "forge_plot_publish",
            "forge_recipe_issue_set",
            "forge_layout_page_setup_apply",
            "forge_plot_preset_apply",
            "forge_standards_import",
            "forge_layer_state_restore",
            "forge_xref_normalize_relative"
        ],
        ["qa"] =
        [
            "forge_system_health",
            "forge_qa_preflight",
            "forge_qa_check_xrefs",
            "forge_qa_verify_titleblock",
            "forge_qa_audit_layers",
            "forge_qa_readback",
            "forge_qa_readback_after_timeout",
            "forge_qa_annotation_clash",
            "forge_text_list",
            "forge_audit_summarize",
            "forge_issue_set_diff",
            "forge_xref_list",
            "forge_layer_list",
            "forge_block_list_attributes"
        ],
        ["linework"] =
        [
            "forge_system_health",
            "forge_linework_dump",
            "forge_linework_trace",
            "forge_linework_topology",
            "forge_linework_coverage",
            "forge_linework_segments",
            "forge_linework_compare",
            "forge_linework_transform",
            "forge_doc_list_open",
            "forge_doc_activate",
            "forge_text_list",
            "forge_entity_list",
            "forge_layer_list"
        ],
        // Authoring wave C20–C30 (docs/cad-authoring-tools.md): the complete draw-and-modify surface.
        ["authoring"] =
        [
            "forge_system_health",
            "forge_doc_activate",
            "forge_doc_list_layouts",
            "forge_drawing_summary",
            "forge_doc_new",
            "forge_doc_save",
            "forge_doc_export_dxf",
            "forge_doc_import",
            "forge_standards_import",
            "forge_layer_create",
            "forge_layer_set",
            "forge_layer_list",
            "forge_linetype_load",
            "forge_style_text_create",
            "forge_style_dim_create",
            "forge_draw_line",
            "forge_draw_polyline",
            "forge_draw_rectangle",
            "forge_draw_polygon",
            "forge_draw_circle",
            "forge_draw_arc",
            "forge_draw_ellipse",
            "forge_draw_spline",
            "forge_draw_revcloud",
            "forge_draw_text",
            "forge_draw_mtext",
            "forge_draw_leader",
            "forge_draw_table",
            "forge_draw_hatch",
            "forge_draw_dimension",
            "forge_draw_grid",
            "forge_draw_wall",
            "forge_draw_batch",
            "forge_draw_polyline_from_csv",
            "forge_entity_get",
            "forge_entity_list",
            "forge_entity_search_text",
            "forge_text_list",
            "forge_entity_snap",
            "forge_entity_intersect",
            "forge_entity_move",
            "forge_entity_copy",
            "forge_entity_rotate",
            "forge_entity_scale",
            "forge_entity_mirror",
            "forge_entity_array",
            "forge_entity_offset",
            "forge_entity_fillet",
            "forge_entity_chamfer",
            "forge_entity_trim",
            "forge_entity_extend",
            "forge_entity_join",
            "forge_entity_explode",
            "forge_entity_erase",
            "forge_entity_set_props",
            "forge_measure_batch",
            "forge_undo",
            "forge_block_create",
            "forge_block_insert",
            "forge_block_insert_bulk",
            "forge_block_list",
            "forge_layout_create",
            "forge_viewport_add",
            "forge_plot_preset_apply",
            "forge_qa_annotation_clash",
            "forge_view_capture"
        ]
    };

    public static IReadOnlyCollection<string> Names => Profiles.Keys.OrderBy(x => x).ToArray();

    public static bool TryGet(string name, out IReadOnlyList<string> tools)
    {
        if (Profiles.TryGetValue(name, out var list))
        {
            tools = list;
            return true;
        }

        tools = Array.Empty<string>();
        return false;
    }
}
