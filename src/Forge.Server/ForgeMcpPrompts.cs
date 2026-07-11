using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Forge.Server;

[McpServerPromptType]
public sealed class ForgeMcpPrompts
{
    [McpServerPrompt(Name = "issue_set_runbook")]
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

    [McpServerPrompt(Name = "safety_first")]
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
}
