using Forge.Server;
using Forge.Shared;
using ModelContextProtocol.Server;

namespace Forge.Tests;

public sealed class ToolMetadataTests
{
    [Fact]
    public void HotPathToolsAreRegisteredInSharedMetadata()
    {
        var names = ForgeToolRegistry.All.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("forge_xref_repath", names);
        Assert.Contains("forge_layer_state_restore", names);
        Assert.Contains("forge_layout_page_setup_import", names);
        Assert.Contains("forge_plot_publish", names);
        Assert.Contains("forge_qa_verify_titleblock", names);
        Assert.Contains("forge_registry_load", names);
        Assert.Contains("forge_pack_load", names);
        Assert.Contains("forge_viewport_list", names);
    }

    [Fact]
    public void McpAttributesExposeExpectedSafetyHints()
    {
        var toolAttributes = typeof(ForgeMcpTools)
            .GetMethods()
            .Select(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false).Cast<McpServerToolAttribute>().SingleOrDefault())
            .Where(a => a is not null)
            .Cast<McpServerToolAttribute>()
            .ToDictionary(a => a.Name ?? "", StringComparer.OrdinalIgnoreCase);

        Assert.True(toolAttributes["forge_system_health"].ReadOnly);
        Assert.False(toolAttributes["forge_system_health"].Destructive);
        Assert.False(toolAttributes["forge_system_health"].OpenWorld);

        Assert.False(toolAttributes["forge_exec_command"].ReadOnly);
        Assert.True(toolAttributes["forge_exec_command"].Destructive);
        Assert.True(toolAttributes["forge_exec_command"].OpenWorld);
    }

    [Fact]
    public void McpToolsMatchRegistryOneToOne()
    {
        var mcpNames = typeof(ForgeMcpTools)
            .GetMethods()
            .Select(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false).Cast<McpServerToolAttribute>().SingleOrDefault())
            .Where(a => a?.Name is not null)
            .Select(a => a!.Name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var registryNames = ForgeToolRegistry.All.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingInRegistry = mcpNames.Where(n => !registryNames.Contains(n)).OrderBy(x => x).ToArray();
        var missingInMcp = registryNames.Where(n => !mcpNames.Contains(n)).OrderBy(x => x).ToArray();

        Assert.True(missingInRegistry.Length == 0, "MCP tools missing from registry: " + string.Join(", ", missingInRegistry));
        Assert.True(missingInMcp.Length == 0, "Registry tools missing from MCP: " + string.Join(", ", missingInMcp));
    }

    [Fact]
    public void McpReadOnlyHintsAlignWithRegistry()
    {
        var toolAttributes = typeof(ForgeMcpTools)
            .GetMethods()
            .Select(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false).Cast<McpServerToolAttribute>().SingleOrDefault())
            .Where(a => a?.Name is not null)
            .Cast<McpServerToolAttribute>();

        foreach (var attr in toolAttributes)
        {
            var meta = ForgeToolRegistry.Get(attr.Name!);
            Assert.Equal(meta.ReadOnly, attr.ReadOnly);
            Assert.Equal(meta.Destructive, attr.Destructive);
            Assert.Equal(meta.OpenWorld, attr.OpenWorld);
        }
    }

    [Fact]
    public void AuthoringWaveSafetyContract()
    {
        // docs/cad-authoring-tools.md C20–C23: draw tools and block_insert are additive-only
        // (no pre-write DWG copy), every entity mutation is destructive with backup, and no
        // authoring tool is OpenWorld (typed params only — SafetyPolicy allow-lists them).
        foreach (var name in new[]
                 {
                     "forge_draw_line", "forge_draw_polyline", "forge_draw_circle", "forge_draw_arc",
                     "forge_draw_text", "forge_draw_mtext", "forge_draw_hatch", "forge_draw_dimension",
                     "forge_draw_batch", "forge_block_insert", "forge_layer_create"
                 })
        {
            var tool = ForgeToolRegistry.Get(name);
            Assert.False(tool.ReadOnly);
            Assert.False(tool.Destructive);
            Assert.False(tool.RequiresBackup);
            Assert.False(tool.OpenWorld);
        }

        foreach (var name in new[]
                 {
                     "forge_entity_move", "forge_entity_copy", "forge_entity_rotate", "forge_entity_scale",
                     "forge_entity_mirror", "forge_entity_offset", "forge_entity_erase", "forge_entity_set_props",
                     "forge_block_create"
                 })
        {
            var tool = ForgeToolRegistry.Get(name);
            Assert.False(tool.ReadOnly);
            Assert.True(tool.Destructive);
            Assert.True(tool.RequiresBackup);
            Assert.False(tool.OpenWorld);
        }

        Assert.True(ForgeToolRegistry.Get("forge_entity_get").ReadOnly);
        Assert.True(ForgeToolRegistry.Get("forge_entity_list").ReadOnly);
        Assert.True(ForgeToolRegistry.Get("forge_block_list").ReadOnly);
        Assert.True(ForgeToolRegistry.Get("forge_layer_create").Idempotent);

        // C24 — capture/measure/export never rewrite the drawing; array is a mutation with
        // backup; the CSV importer is additive like the other draw tools.
        Assert.True(ForgeToolRegistry.Get("forge_measure_batch").ReadOnly);
        foreach (var name in new[] { "forge_view_capture", "forge_doc_export_dxf", "forge_doc_new" })
        {
            var tool = ForgeToolRegistry.Get(name);
            Assert.False(tool.ReadOnly);
            Assert.False(tool.Destructive);
            Assert.False(tool.RequiresBackup);
        }

        var arrayTool = ForgeToolRegistry.Get("forge_entity_array");
        Assert.True(arrayTool.Destructive);
        Assert.True(arrayTool.RequiresBackup);
        var csvTool = ForgeToolRegistry.Get("forge_draw_polyline_from_csv");
        Assert.False(csvTool.Destructive);
        Assert.False(csvTool.RequiresBackup);
        Assert.True(ForgeToolRegistry.Get("forge_layer_set").Idempotent);
    }
}
