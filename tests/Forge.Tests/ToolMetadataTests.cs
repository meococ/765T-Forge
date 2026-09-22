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
    public void FileWritingToolsAreNotReadOnlyAndDoNotRequireBackup()
    {
        var preflight = ForgeToolRegistry.Get("forge_qa_preflight");
        Assert.False(preflight.ReadOnly);
        Assert.False(preflight.Destructive);
        Assert.False(preflight.RequiresBackup);

        var import = ForgeToolRegistry.Get("forge_sheet_inventory_import");
        Assert.False(import.ReadOnly);
        Assert.False(import.Destructive);
        Assert.False(import.RequiresBackup);
        Assert.False(import.Idempotent);
        Assert.False(import.RequiresAutoCad);
    }
}
