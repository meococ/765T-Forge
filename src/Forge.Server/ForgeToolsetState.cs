using Forge.Shared;

namespace Forge.Server;

/// <summary>
/// Which toolsets are enabled for this session. Parsed from FORGE_TOOLSETS
/// (comma list or "all"; unset/empty = "core" only) and mutable at runtime via
/// forge_toolset_enable. Meta tools (group "meta") are always enabled so a
/// minimal session can always discover and enable the rest.
/// </summary>
public sealed class ForgeToolsetState
{
    private readonly HashSet<string> _enabled;
    private readonly object _gate = new();

    public ForgeToolsetState(string? selection)
    {
        _enabled = new HashSet<string>(ParseSelection(selection), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Currently enabled toolset names (sorted).</summary>
    public IReadOnlyList<string> Enabled
    {
        get { lock (_gate) return _enabled.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(); }
    }

    /// <summary>Is a tool callable/listed right now?</summary>
    public bool IsEnabled(string toolName)
    {
        var toolset = ForgeToolsets.ToolsetForTool(toolName);
        if (string.Equals(toolset, ForgeToolsets.Meta, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        lock (_gate) return _enabled.Contains(toolset);
    }

    /// <summary>Enable one toolset; returns false when the name is unknown.</summary>
    public bool Enable(string toolset)
    {
        if (!ForgeToolsets.SetGroups.ContainsKey(toolset))
        {
            return false;
        }

        lock (_gate) _enabled.Add(toolset);
        return true;
    }

    /// <summary>Parse "all" or a comma list; unset/empty defaults to core.</summary>
    public static IReadOnlyList<string> ParseSelection(string? selection)
    {
        if (string.IsNullOrWhiteSpace(selection))
        {
            return ["core"];
        }

        var parts = selection.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Any(p => string.Equals(p, "all", StringComparison.OrdinalIgnoreCase)))
        {
            return ForgeToolsets.Names.ToArray();
        }

        return parts
            .Where(p => ForgeToolsets.SetGroups.ContainsKey(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
