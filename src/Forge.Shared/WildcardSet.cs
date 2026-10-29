using System.Text.RegularExpressions;

namespace Forge.Shared;

/// <summary>
/// Pre-compiled, comma-separated wildcard patterns (<c>*</c> and <c>?</c>, case-insensitive) with
/// the same semantics as <see cref="TextRead.LayerMatches"/>: a name matches when the full name or
/// its xref-stripped tail (after the last '|' or '$') matches any pattern. Compiled once so a QA
/// pass over tens of thousands of entities does not rebuild a Regex per call.
/// </summary>
public sealed class WildcardSet
{
    private readonly Regex[] _patterns;

    private WildcardSet(Regex[] patterns, string[] raw)
    {
        _patterns = patterns;
        Patterns = raw;
    }

    /// <summary>A set with no patterns. <see cref="Matches"/> answers <c>emptyMatchesAll</c>.</summary>
    public static WildcardSet Empty { get; } = new([], []);

    public IReadOnlyList<string> Patterns { get; }

    public bool IsEmpty => _patterns.Length == 0;

    public static WildcardSet Parse(string? patterns) => From(Split(patterns));

    public static WildcardSet From(IEnumerable<string>? patterns)
    {
        if (patterns is null)
        {
            return Empty;
        }

        var raw = patterns
            .SelectMany(Split)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return raw.Length == 0 ? Empty : new WildcardSet(raw.Select(Compile).ToArray(), raw);
    }

    /// <summary>True when any name (full or xref tail) matches. An empty set answers <paramref name="emptyMatchesAll"/>.</summary>
    public bool Matches(string? name, bool emptyMatchesAll = false)
    {
        if (_patterns.Length == 0)
        {
            return emptyMatchesAll;
        }

        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var tail = Tail(name);
        foreach (var regex in _patterns)
        {
            if (regex.IsMatch(name) || (!ReferenceEquals(tail, name) && regex.IsMatch(tail)))
            {
                return true;
            }
        }

        return false;
    }

    public bool MatchesAny(IEnumerable<string?> names)
    {
        foreach (var name in names)
        {
            if (Matches(name))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsWildcard(string pattern) => pattern.IndexOfAny(['*', '?']) >= 0;

    /// <summary>Single-pattern match (full name only, no xref tail) — used by layer mapping.</summary>
    public static bool MatchOne(string pattern, string name) => Compile(pattern).IsMatch(name);

    private static IEnumerable<string> Split(string? patterns)
    {
        if (string.IsNullOrWhiteSpace(patterns))
        {
            yield break;
        }

        foreach (var part in patterns.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                yield return trimmed;
            }
        }
    }

    private static Regex Compile(string pattern) => new(
        "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static string Tail(string layer)
    {
        var cut = Math.Max(layer.LastIndexOf('|'), layer.LastIndexOf('$'));
        return cut >= 0 && cut < layer.Length - 1 ? layer[(cut + 1)..] : layer;
    }
}
