namespace Forge.Shared;

/// <summary>
/// Named toolsets that bound the tools/list context a client pays for. Membership is
/// derived from each tool's registry group (<see cref="ToolMetadata.Group"/>), so the
/// sets cannot drift from the tool catalog. The "meta" group (toolset discovery tools)
/// is always enabled regardless of selection.
/// </summary>
public static class ForgeToolsets
{
    /// <summary>Pseudo-toolset for discovery tools — always enabled.</summary>
    public const string Meta = "meta";

    /// <summary>Toolset name → registry groups it contains.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> SetGroups =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            // Session + document essentials; what a fresh install loads.
            ["core"] = ["system", "document"],
            // Everything draw_* plus the AEC generators (group "draw").
            ["draw"] = ["draw"],
            // Entity mutation, layers/styles, measurement.
            ["edit"] = ["entity", "layer", "style", "measure"],
            // CAD↔model linework QA.
            ["linework"] = ["linework"],
            // Plot/publish path incl. layouts and viewports.
            ["plot"] = ["plot", "layout", "viewport"],
            // QA gates, issue-set recipes, visual capture.
            ["qa"] = ["qa", "recipe", "capture"],
            // Block attributes/inserts, drawing-number registry, standards packs.
            ["blocks"] = ["block", "registry", "standards"],
            // Xref list/reload/repath/normalize.
            ["xref"] = ["xref"],
            // Headless batch, pack-and-go, DGN conversion.
            ["batch"] = ["batch", "pack", "dgn"],
            // Unsafe executors — opt-in, off in every default.
            ["exec"] = ["exec"],
        };

    private static readonly IReadOnlyDictionary<string, string> GroupToSet = BuildGroupIndex();

    public static IReadOnlyCollection<string> Names => SetGroups.Keys.OrderBy(x => x).ToArray();

    /// <summary>Toolset a registry group belongs to; "meta" stays unmapped (always-on).</summary>
    public static string? ToolsetForGroup(string group)
        => GroupToSet.TryGetValue(group, out var set) ? set : null;

    /// <summary>Toolset a tool belongs to (meta tools return <see cref="Meta"/>).</summary>
    public static string ToolsetForTool(string toolName)
    {
        var group = ForgeToolRegistry.Get(toolName).Group;
        return string.Equals(group, Meta, StringComparison.OrdinalIgnoreCase)
            ? Meta
            : (ToolsetForGroup(group) ?? Meta);
    }

    /// <summary>Tools (names) in one toolset.</summary>
    public static IReadOnlyList<string> ToolsInSet(string toolset)
    {
        if (string.Equals(toolset, Meta, StringComparison.OrdinalIgnoreCase))
        {
            return ForgeToolRegistry.All.Where(t => string.Equals(t.Group, Meta, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Name).OrderBy(x => x).ToArray();
        }

        if (!SetGroups.TryGetValue(toolset, out var groups))
        {
            return Array.Empty<string>();
        }

        var wanted = new HashSet<string>(groups, StringComparer.OrdinalIgnoreCase);
        return ForgeToolRegistry.All.Where(t => wanted.Contains(t.Group))
            .Select(t => t.Name).OrderBy(x => x).ToArray();
    }

    /// <summary>Every tool the selection enables (meta tools always included).</summary>
    public static IReadOnlyList<string> EnabledTools(IEnumerable<string> toolsets)
    {
        var wanted = new HashSet<string>(toolsets, StringComparer.OrdinalIgnoreCase);
        return ForgeToolRegistry.All
            .Where(t => string.Equals(t.Group, Meta, StringComparison.OrdinalIgnoreCase)
                        || wanted.Contains(ToolsetForGroup(t.Group) ?? Meta))
            .Select(t => t.Name).OrderBy(x => x).ToArray();
    }

    private static IReadOnlyDictionary<string, string> BuildGroupIndex()
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (set, groups) in SetGroups)
        {
            foreach (var group in groups)
            {
                index[group] = set;
            }
        }

        return index;
    }
}
