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
            "forge_doc_list_layouts",
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
            "forge_audit_summarize",
            "forge_issue_set_diff",
            "forge_xref_list",
            "forge_layer_list",
            "forge_block_list_attributes"
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
