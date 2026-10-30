namespace Forge.Shared;

/// <summary>One kind of named definition forge_standards_import can copy from a template.</summary>
public enum StandardsKind
{
    Linetypes,
    TextStyles,
    Layers,
    DimStyles,
    MLeaderStyles,
    TableStyles,
    PageSetups
}

public enum StandardsAction
{
    Add,
    Overwrite,
    SkipExisting,
    SkipProtected,
    SkipFiltered
}

public sealed record StandardsItemPlan(string Name, StandardsAction Action);

/// <summary>
/// Pure planning for forge_standards_import: which template definitions are added, overwritten
/// or skipped. The plugin does the copy (WblockCloneObjects per table/dictionary, PlotSettings
/// CopyFrom for page setups) and reports exactly this plan back.
/// </summary>
public static class StandardsImport
{
    /// <summary>Dependency order: linetypes before layers, text styles before dim/mleader/table styles.</summary>
    public static readonly StandardsKind[] Order =
    [
        StandardsKind.Linetypes,
        StandardsKind.TextStyles,
        StandardsKind.Layers,
        StandardsKind.DimStyles,
        StandardsKind.MLeaderStyles,
        StandardsKind.TableStyles,
        StandardsKind.PageSetups
    ];

    public static string WireName(StandardsKind kind) => kind switch
    {
        StandardsKind.Linetypes => "linetypes",
        StandardsKind.TextStyles => "textStyles",
        StandardsKind.Layers => "layers",
        StandardsKind.DimStyles => "dimStyles",
        StandardsKind.MLeaderStyles => "mleaderStyles",
        StandardsKind.TableStyles => "tableStyles",
        _ => "pageSetups"
    };

    /// <summary>Parses the include list (wire names, case/underscore-insensitive); null/empty → every kind.</summary>
    public static bool TryParseKinds(IEnumerable<string>? include, out IReadOnlyList<StandardsKind> kinds, out string? error)
    {
        error = null;
        var list = include?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (list is null || list.Count == 0 || list.Any(s => s.Trim() is "*" or "all"))
        {
            kinds = Order;
            return true;
        }

        var picked = new HashSet<StandardsKind>();
        foreach (var raw in list)
        {
            var key = raw.Trim().Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
            StandardsKind? kind = key switch
            {
                "linetype" or "linetypes" or "ltypes" => StandardsKind.Linetypes,
                "textstyle" or "textstyles" or "styles" => StandardsKind.TextStyles,
                "layer" or "layers" => StandardsKind.Layers,
                "dimstyle" or "dimstyles" or "dimensionstyles" => StandardsKind.DimStyles,
                "mleaderstyle" or "mleaderstyles" or "multileaderstyles" => StandardsKind.MLeaderStyles,
                "tablestyle" or "tablestyles" => StandardsKind.TableStyles,
                "pagesetup" or "pagesetups" or "plotsettings" => StandardsKind.PageSetups,
                _ => null
            };
            if (kind is null)
            {
                kinds = [];
                error = $"Unknown standards kind '{raw}'. Use linetypes, textStyles, layers, dimStyles, mleaderStyles, tableStyles, pageSetups.";
                return false;
            }

            picked.Add(kind.Value);
        }

        kinds = Order.Where(picked.Contains).ToList();
        return true;
    }

    /// <summary>Names that are never overwritten (system records) — they still count as present.</summary>
    public static bool IsProtected(StandardsKind kind, string name) => kind switch
    {
        StandardsKind.Layers => name.Equals("0", StringComparison.OrdinalIgnoreCase) || name.Equals("Defpoints", StringComparison.OrdinalIgnoreCase),
        StandardsKind.Linetypes => name.Equals("ByLayer", StringComparison.OrdinalIgnoreCase) || name.Equals("ByBlock", StringComparison.OrdinalIgnoreCase) || name.Equals("Continuous", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    /// <summary>True for names AutoCAD manages itself: xref-dependent (a|b), anonymous (*X) or empty.</summary>
    public static bool IsSystemName(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.StartsWith('*') || name.Contains('|', StringComparison.Ordinal);

    public static IReadOnlyList<StandardsItemPlan> Plan(
        StandardsKind kind,
        IEnumerable<string> sourceNames,
        IEnumerable<string> destinationNames,
        WildcardSet filter,
        bool overwrite)
    {
        var existing = new HashSet<string>(destinationNames, StringComparer.OrdinalIgnoreCase);
        var plan = new List<StandardsItemPlan>();
        foreach (var name in sourceNames.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (IsSystemName(name))
            {
                continue;
            }

            if (!filter.Matches(name, emptyMatchesAll: true))
            {
                plan.Add(new StandardsItemPlan(name, StandardsAction.SkipFiltered));
                continue;
            }

            if (!existing.Contains(name))
            {
                plan.Add(new StandardsItemPlan(name, StandardsAction.Add));
            }
            else if (IsProtected(kind, name))
            {
                plan.Add(new StandardsItemPlan(name, StandardsAction.SkipProtected));
            }
            else
            {
                plan.Add(new StandardsItemPlan(name, overwrite ? StandardsAction.Overwrite : StandardsAction.SkipExisting));
            }
        }

        return plan;
    }

    public static bool TryParseConflict(string? value, out bool overwrite, out string? error)
    {
        error = null;
        switch (value?.Trim().ToLowerInvariant())
        {
            case null or "" or "skip" or "keep" or "ignore":
                overwrite = false;
                return true;
            case "overwrite" or "replace":
                overwrite = true;
                return true;
            default:
                overwrite = false;
                error = $"Unknown onConflict '{value}'. Use skip (default) or overwrite.";
                return false;
        }
    }
}
