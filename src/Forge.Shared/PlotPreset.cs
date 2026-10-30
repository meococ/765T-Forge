namespace Forge.Shared;

/// <summary>
/// Pure helpers for forge_plot_preset_apply: which layouts a preset targets and how a page-setup
/// name is validated. The PlotSettingsValidator work stays in the plugin.
/// </summary>
public static class PlotPreset
{
    public const int MaxLayouts = 200;

    /// <summary>
    /// Resolves requested layout names against the drawing's layouts (case-insensitive, exact
    /// names returned). Empty → the current layout; "*" → every paper-space layout; "Model" is
    /// only included when named explicitly. Unknown names are reported, never guessed.
    /// </summary>
    public static IReadOnlyList<string> ResolveLayouts(
        IReadOnlyList<string>? requested,
        IReadOnlyList<string> available,
        string currentLayout,
        out List<string> unknown)
    {
        unknown = [];
        var result = new List<string>();
        var wanted = requested?.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToList() ?? [];
        if (wanted.Count == 0)
        {
            wanted.Add(currentLayout);
        }

        foreach (var name in wanted)
        {
            if (name.Contains('*', StringComparison.Ordinal) || name.Contains('?', StringComparison.Ordinal))
            {
                var hits = available.Where(l => !IsModel(l) && WildcardSet.MatchOne(name, l)).ToList();
                if (hits.Count == 0)
                {
                    unknown.Add(name);
                }

                foreach (var hit in hits)
                {
                    AddDistinct(result, hit);
                }

                continue;
            }

            var match = available.FirstOrDefault(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                unknown.Add(name);
            }
            else
            {
                AddDistinct(result, match);
            }
        }

        return result;
    }

    public static bool IsModel(string layout) => string.Equals(layout, "Model", StringComparison.OrdinalIgnoreCase);

    public static bool IsValidSetupName(string? name) => DrawingImport.IsValidSymbolName(name);

    private static void AddDistinct(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(value);
        }
    }
}
