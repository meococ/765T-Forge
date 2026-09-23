using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// Guards the third source of truth: plugin switch arms must cover every registry tool
/// except the intentional server-only AccoreConsole / local tools.
/// </summary>
public sealed class PluginDispatchSyncTests
{
    private static readonly HashSet<string> ServerOnlyTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "forge_run_script",
        "forge_batch_run",
        "forge_batch_status",
        "forge_system_tool_profile",
        "forge_audit_summarize",
        "forge_sheet_inventory_import",
        "forge_issue_set_diff",
        // forge_linework_transform is pure coordinate math and always resolves in
        // ForgeToolRunner. The other six linework tools have a plugin switch arm for
        // source=drawing and resolve server-side only for source=dumpFile, so they are
        // not server-only.
        "forge_linework_transform",
    };

    [Fact]
    public void PluginSwitchCoversRegistryToolsExceptServerOnly()
    {
        var pluginDir = FindPluginDir();
        var sources = Directory.GetFiles(pluginDir, "PluginCommandProcessor*.cs")
            .Select(File.ReadAllText)
            .ToArray();
        var joined = string.Join('\n', sources);

        var missing = new List<string>();
        foreach (var tool in ForgeToolRegistry.All.Select(t => t.Name).OrderBy(x => x))
        {
            if (ServerOnlyTools.Contains(tool))
            {
                continue;
            }

            var needle = $"\"{tool.ToLowerInvariant()}\"";
            if (!joined.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                missing.Add(tool);
            }
        }

        Assert.True(missing.Count == 0, "Registry tools missing plugin switch arms: " + string.Join(", ", missing));
    }

    [Fact]
    public void ServerOnlyToolsAreDocumentedInRegistry()
    {
        Assert.Contains(ForgeToolRegistry.All, t => t.Name == "forge_run_script" && !t.RequiresAutoCad);
        Assert.Contains(ForgeToolRegistry.All, t => t.Name == "forge_batch_run" && !t.RequiresAutoCad);
        Assert.Contains(ForgeToolRegistry.All, t => t.Name == "forge_batch_status" && !t.RequiresAutoCad);
    }

    private static string FindPluginDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Forge.Plugin");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/Forge.Plugin from test BaseDirectory.");
    }
}
