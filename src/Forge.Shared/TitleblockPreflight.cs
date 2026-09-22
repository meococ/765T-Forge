namespace Forge.Shared;

public readonly record struct TitleblockSample(string Handle, string Tag, string? Value);

public static class TitleblockPreflight
{
    public static List<QaFinding> Evaluate(IEnumerable<TitleblockSample> samples, IEnumerable<string>? requiredTags)
    {
        var attrs = samples.ToArray();
        var tags = (requiredTags ?? []).Where(tag => !string.IsNullOrWhiteSpace(tag)).ToArray();
        var findings = new List<QaFinding>();
        var handles = attrs
            .Select(sample => sample.Handle)
            .Where(handle => !string.IsNullOrWhiteSpace(handle))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var tag in tags)
        {
            var matches = attrs.Where(sample => sample.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0)
            {
                findings.Add(Missing(tag, null));
                continue;
            }

            if (handles.Length > 1)
            {
                var withTag = matches.Select(match => match.Handle).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var handle in handles)
                {
                    if (!withTag.Contains(handle))
                    {
                        findings.Add(Missing(tag, handle));
                    }
                }
            }

            foreach (var match in matches)
            {
                if (string.IsNullOrWhiteSpace(match.Value) || match.Value.Contains("####", StringComparison.Ordinal))
                {
                    findings.Add(new QaFinding(
                        "titleblock_tag_empty",
                        "error",
                        $"Titleblock tag '{tag}' on handle '{match.Handle}' is empty or unresolved ('{match.Value}').",
                        "Fill every matching title block, not only the first attribute.",
                        "forge_block_set_attr"));
                }
            }
        }

        return findings;
    }

    public static QaFinding? MissingRequirements(bool hasCallerTags, bool hasPackTags)
    {
        if (hasCallerTags || hasPackTags)
        {
            return null;
        }

        return new QaFinding(
            "preflight_no_titleblock_requirements",
            "error",
            "No required titleblock tags were supplied and no standards pack requires any, so the gate cannot check the title block.",
            "Pass requiredTitleblockTags or load a pack that lists them. Do not treat a passing gate as proof the sheet number is correct.",
            "forge_qa_preflight");
    }

    private static QaFinding Missing(string tag, string? handle)
    {
        var where = string.IsNullOrWhiteSpace(handle) ? "" : $" on handle '{handle}'";
        return new QaFinding(
            "titleblock_tag_missing",
            "error",
            $"Required titleblock tag '{tag}' was not found{where}.",
            "Check every title block on every layout. One matching attribute does not pass the others.",
            "forge_block_list_attributes");
    }
}
