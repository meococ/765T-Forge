using System.Text.RegularExpressions;
using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// Guards the third source of truth: plugin switch arms must cover every registry tool
/// except the intentional server-only AccoreConsole / local tools, and the server-only list
/// must match what ForgeToolRunner actually resolves before the named pipe.
/// </summary>
public sealed class PluginDispatchSyncTests
{
    /// <summary>Tools whose literal switch arm in ForgeToolRunner.InvokeAsync resolves them server-side.</summary>
    private static readonly HashSet<string> ServerRoutedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "forge_run_script",
        "forge_batch_run",
        "forge_batch_status",
        "forge_system_tool_profile",
        "forge_audit_summarize",
        "forge_sheet_inventory_import",
        "forge_issue_set_diff",
        "forge_linework_transform", // pure server-side coordinate math — never reaches the plugin
    };

    /// <summary>
    /// Server-routed tools that ALSO keep a plugin dispatch arm (the plugin answers when called
    /// directly over the pipe). forge_system_tool_profile is resolved by ForgeToolRunner, but
    /// PluginCommandProcessor still has a "forge_system_tool_profile" arm.
    /// </summary>
    private static readonly HashSet<string> ServerRoutedWithPluginArm = new(StringComparer.OrdinalIgnoreCase)
    {
        "forge_system_tool_profile",
    };

    private static IEnumerable<string> ServerOnlyTools => ServerRoutedTools.Where(t => !ServerRoutedWithPluginArm.Contains(t));

    private static readonly Regex SwitchArm = new(@"""(forge_[a-z0-9_]+)""\s*=>", RegexOptions.Compiled);

    [Fact]
    public void PluginSwitchCoversRegistryToolsExceptServerOnly()
    {
        var pluginArms = PluginSwitchArms();
        var serverOnly = ServerOnlyTools.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = ForgeToolRegistry.All
            .Select(t => t.Name)
            .Where(tool => !serverOnly.Contains(tool) && !pluginArms.Contains(tool))
            .OrderBy(x => x)
            .ToArray();

        Assert.True(missing.Length == 0, "Registry tools missing plugin switch arms: " + string.Join(", ", missing));
    }

    [Fact]
    public void ServerOnlyToolsHaveNoPluginArm()
    {
        var pluginArms = PluginSwitchArms();
        var unexpected = ServerOnlyTools.Where(pluginArms.Contains).OrderBy(x => x).ToArray();
        Assert.True(
            unexpected.Length == 0,
            "Tools listed as server-only have a plugin arm (move them to ServerRoutedWithPluginArm): " + string.Join(", ", unexpected));

        foreach (var tool in ServerRoutedWithPluginArm)
        {
            Assert.True(pluginArms.Contains(tool), $"{tool} is listed in ServerRoutedWithPluginArm but has no plugin switch arm.");
        }
    }

    [Fact]
    public void ServerRoutedListMatchesForgeToolRunnerSwitch()
    {
        var runnerSource = File.ReadAllText(Path.Combine(RepoPath("src", "Forge.Server"), "ForgeToolRunner.cs"));
        var start = runnerSource.IndexOf("tool.ToLowerInvariant() switch", StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not locate the routing switch in ForgeToolRunner.cs.");
        var end = runnerSource.IndexOf("};", start, StringComparison.Ordinal);
        var switchBody = runnerSource[start..end];

        var routed = SwitchArm.Matches(switchBody).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.True(
            routed.SetEquals(ServerRoutedTools),
            $"ForgeToolRunner routes [{string.Join(", ", routed.OrderBy(x => x))}] server-side, test lists [{string.Join(", ", ServerRoutedTools.OrderBy(x => x))}].");
    }

    [Fact]
    public void ServerOnlyToolsAreDocumentedInRegistry()
    {
        Assert.Contains(ForgeToolRegistry.All, t => t.Name == "forge_run_script" && !t.RequiresAutoCad);
        Assert.Contains(ForgeToolRegistry.All, t => t.Name == "forge_batch_run" && !t.RequiresAutoCad);
        Assert.Contains(ForgeToolRegistry.All, t => t.Name == "forge_batch_status" && !t.RequiresAutoCad);
    }

    private static HashSet<string> PluginSwitchArms()
    {
        var pluginDir = RepoPath("src", "Forge.Plugin");
        var joined = string.Join('\n', Directory.GetFiles(pluginDir, "PluginCommandProcessor*.cs").Select(File.ReadAllText));
        return SwitchArm.Matches(joined).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal static string RepoPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate {Path.Combine(parts)} from test BaseDirectory.");
    }
}
