namespace Forge.Shared;

/// <summary>
/// Exact open-document resolution shared by the per-call <c>document</c> target and
/// forge_doc_activate. A rooted query matches by full path; otherwise by exact file name (or the
/// exact document name for unsaved "Drawing1.dwg"). There is deliberately no loose suffix match:
/// "1.dwg" must never resolve "plan1.dwg". Callers fail closed on more than one match.
/// </summary>
public static class DocumentMatcher
{
    public static IReadOnlyList<int> Match(IReadOnlyList<string> openNames, string? query)
    {
        var matches = new List<int>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return matches;
        }

        var target = query.Trim();
        var rooted = Path.IsPathRooted(target);
        var targetFull = rooted ? TryGetFullPath(target) : null;
        var targetLeaf = Path.GetFileName(target);

        for (var i = 0; i < openNames.Count; i++)
        {
            var name = openNames[i] ?? "";
            var match = rooted
                ? string.Equals(TryGetFullPath(name) ?? name, targetFull, StringComparison.OrdinalIgnoreCase)
                : !string.IsNullOrEmpty(targetLeaf) &&
                  string.Equals(Path.GetFileName(name), targetLeaf, StringComparison.OrdinalIgnoreCase);

            if (match || string.Equals(name, target, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(i);
            }
        }

        return matches;
    }

    public static string? TryGetFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }
}
