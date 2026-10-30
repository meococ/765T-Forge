using System.Text.RegularExpressions;

namespace Forge.Shared;

/// <summary>
/// smoke-r2 D9: when a layout's stored media name is not offered by the plot device, the
/// stored sheet size is mapped onto a device medium of equal size. One device media entry:
/// the canonical (list) name, the localized display name, and the parsed sheet size in mm.
/// </summary>
public sealed record PaperMedia(string Canonical, string? Locale, double? WidthMm, double? HeightMm)
{
    /// <summary>The name a human recognizes: locale when present, else canonical.</summary>
    public string Display => Locale ?? Canonical;
}

/// <summary>Outcome of mapping a stored sheet size onto the device's media list.</summary>
public sealed record PaperMatchResult(
    bool Matched,
    string? Canonical,
    bool Ambiguous,
    IReadOnlyList<string> Nearest);

/// <summary>
/// D9 paper mapping, pure and device-agnostic. Canonical/locale media names carry their
/// sheet size ("ISO_A1_(841.00_x_594.00_MM)", "ANSI B (17.00 x 11.00 Inches)",
/// "ezdxf_(841.00_x_594.00_MM)"), so a stored media that the device does not list can be
/// mapped by equal size instead of dying as eInvalidInput.
/// </summary>
public static class PaperMatch
{
    /// <summary>Size equality window: device and stored sizes may differ by up to 0.5 mm.</summary>
    public const double ToleranceMm = 0.5;

    /// <summary>Failure code when no device medium maps the stored sheet size.</summary>
    public const string NotOnDeviceCode = "plot_paper_not_on_device";

    private const double MmPerInch = 25.4;

    // "841.00_x_594.00_MM", "17.00 x 11.00 Inches", "8.5_x_11.00_Mm" — separators may be
    // underscores or spaces; the unit must be mm or inches (pixels are not comparable).
    private static readonly Regex SizePattern = new(
        @"(?<w>\d+(?:\.\d+)?)[\s_]*[xX][\s_]*(?<h>\d+(?:\.\d+)?)[\s_]*(?<u>mm|in(?:ch(?:es)?)?)(?=[\s_]*\))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] VariantMarkers = ["expand", "bleed", "overflow"];

    /// <summary>Parse a "(W x H MM/Inches)" size out of a canonical or locale media name.</summary>
    public static (double WidthMm, double HeightMm)? ParseSizeMm(string? mediaName)
    {
        if (string.IsNullOrWhiteSpace(mediaName))
        {
            return null;
        }

        var m = SizePattern.Match(mediaName);
        if (!m.Success ||
            !double.TryParse(m.Groups["w"].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var w) ||
            !double.TryParse(m.Groups["h"].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var h) ||
            w <= 0 || h <= 0)
        {
            return null;
        }

        var factor = m.Groups["u"].Value.StartsWith("mm", StringComparison.OrdinalIgnoreCase) ? 1.0 : MmPerInch;
        return (Math.Round(w * factor, 3), Math.Round(h * factor, 3));
    }

    /// <summary>Build a media entry from device names; the size is parsed out of the names.</summary>
    public static PaperMedia ToMedia(string canonical, string? locale)
    {
        var size = ParseSizeMm(canonical) ?? ParseSizeMm(locale);
        return new PaperMedia(canonical, locale, size?.WidthMm, size?.HeightMm);
    }

    /// <summary>
    /// Map a stored sheet size onto the device media list. A device medium qualifies when
    /// both axes agree within <see cref="ToleranceMm"/> in either orientation. Deterministic
    /// preference among qualifiers: stored-orientation matches outrank rotated ones, then
    /// plainer names outrank margin variants (names containing "expand", "bleed" or
    /// "overflow"); when the best group still holds more than one medium the result is
    /// <see cref="PaperMatchResult.Ambiguous"/> rather than an arbitrary pick.
    /// <paramref name="Nearest"/> always names up to <paramref name="nearestCount"/> device
    /// media — the ambiguous candidates when ambiguous, else the size-closest entries —
    /// so a failure message can offer real paperSize values.
    /// </summary>
    public static PaperMatchResult MatchBySize(
        double? storedWidthMm,
        double? storedHeightMm,
        IEnumerable<PaperMedia> deviceMedia,
        int nearestCount = 3)
    {
        var list = deviceMedia as IReadOnlyList<PaperMedia> ?? deviceMedia.ToArray();
        if (list.Count == 0)
        {
            return new PaperMatchResult(false, null, false, []);
        }

        if (storedWidthMm is not > 0 || storedHeightMm is not > 0)
        {
            // No stored size to compare against: report the device's first names and give up.
            return new PaperMatchResult(false, null, false,
                list.Take(nearestCount).Select(m => m.Display).ToArray());
        }

        var sw = storedWidthMm.Value;
        var sh = storedHeightMm.Value;
        var scored = list
            .Select(m =>
            {
                var same = m.WidthMm is { } w && m.HeightMm is { } h
                    ? Math.Max(Math.Abs(w - sw), Math.Abs(h - sh))
                    : double.MaxValue;
                var rotated = m.WidthMm is { } w2 && m.HeightMm is { } h2
                    ? Math.Max(Math.Abs(h2 - sw), Math.Abs(w2 - sh))
                    : double.MaxValue;
                var distance = Math.Min(same, rotated);
                return (Media: m, Distance: distance, Rotated: rotated < same);
            })
            .ToArray();

        var nearest = scored
            .Where(s => s.Distance < double.MaxValue)
            .OrderBy(s => s.Distance)
            .ThenBy(s => s.Media.Canonical, StringComparer.Ordinal)
            .Take(nearestCount)
            .Select(s => s.Media.Display)
            .ToArray();

        var candidates = scored.Where(s => s.Distance <= ToleranceMm).ToArray();
        if (candidates.Length == 0)
        {
            return new PaperMatchResult(false, null, false,
                nearest.Length > 0 ? nearest : list.Take(nearestCount).Select(m => m.Display).ToArray());
        }

        var bestGroup = candidates
            .GroupBy(c => (c.Rotated, Markers: MarkerCount(c.Media.Canonical)))
            .OrderBy(g => g.Key.Rotated)
            .ThenBy(g => g.Key.Markers)
            .First()
            .Select(c => c.Media)
            .OrderBy(m => m.Canonical, StringComparer.Ordinal)
            .ToArray();

        return bestGroup.Length == 1
            ? new PaperMatchResult(true, bestGroup[0].Canonical, false, nearest)
            : new PaperMatchResult(false, null, true,
                bestGroup.Take(nearestCount).Select(m => m.Display).ToArray());
    }

    /// <summary>How many margin-variant markers a canonical name carries (lower = plainer).</summary>
    private static int MarkerCount(string canonical)
    {
        var count = 0;
        foreach (var marker in VariantMarkers)
        {
            if (canonical.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }
}
