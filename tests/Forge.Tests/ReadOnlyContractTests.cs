using Forge.Shared;
using Xunit;

namespace Forge.Tests;

/// <summary>
/// Guards the <see cref="ToolMetadata.ReadOnly"/> contract.
///
/// ReadOnly means the tool has no write path at all, not "does not modify the drawing".
/// MCP hosts commonly auto-approve tools annotated read-only, so marking a tool that
/// writes a report, contract, receipt, sidecar or batch-state file as read-only grants an
/// unconsented write. Three tools were shipped with exactly that defect before this test
/// existed: forge_qa_preflight (always writes a QA artifact), forge_sheet_inventory_import
/// (writes the contract when outputContractPath is set) and forge_cde_gate_evaluate
/// (writes a sidecar when writeSidecar is set).
///
/// The allowlist below is the reviewed set of tools with no write path. Adding a tool here
/// is a deliberate assertion that it never writes; a new read-only tool that is not listed
/// fails this test rather than silently widening what a host may auto-approve.
/// </summary>
public sealed class ReadOnlyContractTests
{
    private static readonly HashSet<string> ToolsWithNoWritePath = new(StringComparer.OrdinalIgnoreCase)
    {
        "forge_audit_summarize",
        "forge_batch_status",
        "forge_block_get_attr",
        "forge_block_list_attributes",
        "forge_doc_list_layouts",
        "forge_doc_list_open",
        "forge_issue_set_diff",
        "forge_issue_set_validate",
        "forge_layer_list",
        "forge_layer_state_list",
        "forge_pack_status",
        "forge_publish_ceremony_check",
        "forge_qa_audit_layers",
        "forge_qa_check_xrefs",
        "forge_qa_dependency_closure",
        "forge_qa_dual_source",
        "forge_qa_modal_trap",
        "forge_qa_plot_fingerprint",
        "forge_qa_readback",
        "forge_qa_readback_after_timeout",
        "forge_qa_verify_titleblock",
        "forge_registry_lookup",
        "forge_system_capabilities",
        "forge_system_getvar",
        "forge_system_health",
        "forge_system_tool_profile",
        "forge_system_version",
        "forge_viewport_list",
        "forge_xref_closure",
        "forge_xref_list",
        "forge_xref_pin_verify",
    };

    [Fact]
    public void ReadOnlyToolsAreExactlyTheReviewedSet()
    {
        var declared = ForgeToolRegistry.All
            .Where(t => t.ReadOnly)
            .Select(t => t.Name)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var expected = ToolsWithNoWritePath
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var addedWithoutReview = declared.Except(expected, StringComparer.OrdinalIgnoreCase).ToArray();
        var stale = expected.Except(declared, StringComparer.OrdinalIgnoreCase).ToArray();

        Assert.True(
            addedWithoutReview.Length == 0 && stale.Length == 0,
            "ReadOnly contract drift."
            + (addedWithoutReview.Length > 0
                ? " Marked ReadOnly but not in the reviewed no-write-path set: " + string.Join(", ", addedWithoutReview) + "."
                : "")
            + (stale.Length > 0
                ? " Listed as no-write-path but no longer ReadOnly (remove from the allowlist): " + string.Join(", ", stale) + "."
                : ""));
    }

    [Theory]
    [InlineData("forge_qa_preflight")]
    [InlineData("forge_sheet_inventory_import")]
    [InlineData("forge_cde_gate_evaluate")]
    public void KnownWritingToolsAreNotReadOnly(string tool)
    {
        var metadata = ForgeToolRegistry.Get(tool);

        Assert.False(metadata.ReadOnly);
        Assert.False(metadata.Destructive);
    }

    [Fact]
    public void EveryFreeTextExecutorRequiresTheUnsafeGate()
    {
        string[] executors =
        [
            "forge_exec_command",
            "forge_exec_lisp",
            "forge_run_script",
            "forge_batch_run",
            "forge_exec_dotnet",
        ];

        foreach (var tool in executors)
        {
            var metadata = ForgeToolRegistry.Get(tool);
            Assert.True(metadata.Unsafe, $"{tool} must be flagged Unsafe.");
            Assert.True(metadata.Destructive, $"{tool} must be flagged Destructive.");
            Assert.False(metadata.ReadOnly, $"{tool} must not be flagged ReadOnly.");
        }
    }
}
