using System.Text.RegularExpressions;

namespace Forge.Shared;

/// <summary>Which text-bearing entities forge_text_list reports.</summary>
[Flags]
public enum TextKinds
{
    None = 0,
    Text = 1,
    MText = 2,
    Attrib = 4,
    AttDef = 8,
    MLeader = 16,
    Default = Text | MText | Attrib | AttDef | MLeader
}

/// <summary>Pure helpers for forge_text_list (block-aware text read): type and layer filters, world text metrics.</summary>
public static class TextRead
{
    public const int DefaultMaxResults = 500;
    public const int HardMaxResults = 5000;

    public static int ClampMaxResults(int? requested) => Math.Clamp(requested ?? DefaultMaxResults, 1, HardMaxResults);

    /// <summary>Parses "text,mtext,attrib,attdef,mleader" (case-insensitive; DBText/ATTRIB/etc. aliases accepted).</summary>
    public static bool TryParseKinds(string? value, out TextKinds kinds, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            kinds = TextKinds.Default;
            return true;
        }

        kinds = TextKinds.None;
        foreach (var part in value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            var kind = part.Trim().ToLowerInvariant() switch
            {
                "text" or "dbtext" => TextKinds.Text,
                "mtext" => TextKinds.MText,
                "attrib" or "attribute" or "attributereference" => TextKinds.Attrib,
                "attdef" or "attributedefinition" => TextKinds.AttDef,
                "mleader" or "multileader" => TextKinds.MLeader,
                _ => TextKinds.None
            };

            if (kind == TextKinds.None)
            {
                error = $"Unknown text type '{part.Trim()}'. Use text, mtext, attrib, attdef, mleader.";
                return false;
            }

            kinds |= kind;
        }

        return true;
    }

    /// <summary>
    /// Comma-separated layer patterns with * and ? wildcards, case-insensitive, matched against the
    /// full layer name and the xref-stripped tail (after the last '|' or '$'). Null/empty matches all.
    /// </summary>
    public static bool LayerMatches(string? patterns, params string?[] layers)
    {
        if (string.IsNullOrWhiteSpace(patterns))
        {
            return true;
        }

        foreach (var raw in patterns.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var pattern = raw.Trim();
            if (pattern.Length == 0)
            {
                continue;
            }

            var regex = new Regex(
                "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (var layer in layers)
            {
                if (string.IsNullOrEmpty(layer))
                {
                    continue;
                }

                if (regex.IsMatch(layer) || regex.IsMatch(Tail(layer)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string Tail(string layer)
    {
        var cut = Math.Max(layer.LastIndexOf('|'), layer.LastIndexOf('$'));
        return cut >= 0 && cut < layer.Length - 1 ? layer[(cut + 1)..] : layer;
    }

    /// <summary>World text metrics for a text whose local baseline direction is <paramref name="localRotation"/> (radians).</summary>
    public static (double RotationDeg, double Height, bool Mirrored) WorldMetrics(AffineXf xf, double localRotation, double localHeight)
    {
        var worldAngle = xf.TransformAngle(localRotation);
        var deg = worldAngle * 180.0 / Math.PI;
        if (deg < 0)
        {
            deg += 360.0;
        }

        var height = localHeight * xf.ScaleAlong(localRotation + Math.PI / 2);
        return (Math.Round(deg, 6), height, xf.IsMirrored2D);
    }
}
