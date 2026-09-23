namespace Forge.Shared;

public static class TitleblockDualSource
{
    public static IReadOnlyList<QaFinding> Compare(
        IReadOnlyDictionary<string, string> expectedFromRegistry,
        IReadOnlyDictionary<string, string> liveAttrs,
        IReadOnlyDictionary<string, string>? titleBlockAttrMap = null)
    {
        var findings = new List<QaFinding>();
        foreach (var entry in expectedFromRegistry)
        {
            var logicalKey = entry.Key;
            var expected = entry.Value;
            var tag = logicalKey;
            if (titleBlockAttrMap is not null
                && titleBlockAttrMap.TryGetValue(logicalKey, out var mapped)
                && !string.IsNullOrWhiteSpace(mapped))
            {
                tag = mapped;
            }

            if (!liveAttrs.TryGetValue(tag, out var live)
                && !liveAttrs.TryGetValue(logicalKey, out live))
            {
                findings.Add(new QaFinding(
                    "titleblock_dual_source_missing",
                    "error",
                    $"Registry expects '{logicalKey}'→tag '{tag}' but attribute is missing on the titleblock.",
                    "Confirm block name / paper-space titleblock.",
                    "forge_qa_dual_source"));
                continue;
            }

            if (!string.Equals(Normalize(expected), Normalize(live), StringComparison.Ordinal))
            {
                findings.Add(new QaFinding(
                    "titleblock_dual_source_mismatch",
                    "error",
                    $"Dual-source mismatch for '{tag}': registry='{expected}' live='{live}'.",
                    "Fill via forge_block_campaign from the drawing registry only.",
                    "forge_block_campaign"));
            }
        }

        return findings;
    }

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim();
}
