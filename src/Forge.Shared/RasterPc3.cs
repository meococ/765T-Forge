using System.Text.RegularExpressions;

namespace Forge.Shared;

/// <summary>
/// TOOL-GAP #27: PublishToWeb PNG.pc3 stores its raster output size as ONE
/// user-defined <c>size{ name=... media_bounds{...} }</c> block plus a
/// <c>resolution{ addr/effective_resolution }</c> block (both are plain text inside
/// the pc3 container — see Pc3File). There is no canonical pixel medium to select:
/// the Forge clone rewrites that stored medium to the requested W x H px at the
/// requested dpi, so GetCanonicalMediaList() on the clone exposes exactly our
/// media name and the normal TryResolveMedia/SetCanonicalMediaName path stays valid.
/// All methods are pure text transforms for unit testing.
/// </summary>
public static class RasterPc3
{
    /// <summary>Deterministic medium name injected into the clone pc3 and resolved like any other media.</summary>
    public static string MediaName(int wPx, int hPx) => $"Forge_{wPx}x{hPx}px";

    /// <summary>Deterministic resolution name for the resolution block.</summary>
    public static string ResolutionName(int dpi) => $"Forge_{dpi}dpi";

    /// <summary>mm -> pixels at a dpi, floored to an even count (the raster driver mishandles odd dims).</summary>
    public static int MmToPixels(double mm, int dpi)
    {
        var px = (int)Math.Round(mm * dpi / 25.4);
        return px % 2 == 0 ? px : px - 1;
    }

    /// <summary>
    /// Rewrites the stored raster medium to wPx x hPx at dpi. Returns the patched text;
    /// error is non-null when the text lacks the expected raster fields (not a
    /// PublishToWeb-style raster pc3) and the text is returned unmodified.
    /// </summary>
    public static string Apply(string pc3Text, int wPx, int hPx, int dpi, out string? error)
    {
        error = null;
        if (wPx <= 0 || hPx <= 0 || dpi <= 0)
        {
            error = $"invalid raster target {wPx}x{hPx}px @{dpi}dpi";
            return pc3Text;
        }

        if (!pc3Text.Contains("media_bounds", StringComparison.Ordinal)
            || !pc3Text.Contains("addr_resolution_x", StringComparison.Ordinal))
        {
            error = "pc3 text has no raster media/resolution block (not a raster plotter?)";
            return pc3Text;
        }

        var t = pc3Text;
        var mediaName = MediaName(wPx, hPx);
        var resName = ResolutionName(dpi);

        // Rename every stored size name: the pc3 may reference the same name in more
        // than one place (media block + selected-media back-references). Capture the
        // first name, then replace every occurrence of it so they stay consistent.
        var nameMatch = Regex.Match(t, "\\bsize\\{\\s*name=\"(?<n>[^\"]*)\"");
        if (!nameMatch.Success || nameMatch.Groups["n"].Value.Length == 0)
        {
            error = "pc3 media block has no user-defined size name to rewrite";
            return pc3Text;
        }

        var oldName = nameMatch.Groups["n"].Value;
        t = t.Replace(oldName, mediaName);

        // Printable bounds are 0-indexed pixels (stored 1599 for a 1600-px medium).
        t = Regex.Replace(t, "actual_printable_bounds_urx=[\\d.]+", $"actual_printable_bounds_urx={wPx - 1}");
        t = Regex.Replace(t, "actual_printable_bounds_ury=[\\d.]+", $"actual_printable_bounds_ury={hPx - 1}");
        t = Regex.Replace(t, "printable_bounds_urx=[\\d.]+", $"printable_bounds_urx={wPx - 1}");
        t = Regex.Replace(t, "printable_bounds_ury=[\\d.]+", $"printable_bounds_ury={hPx - 1}");
        t = Regex.Replace(t, "printable_area=[\\d.]+", $"printable_area={((double)wPx * hPx).ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}");
        t = Regex.Replace(t, "\\bdm_orientation=\\d+", wPx >= hPx ? "dm_orientation=1" : "dm_orientation=0");
        t = Regex.Replace(
            t,
            "\\bmedia_bounds\\{\\s*urx=[\\d.]+\\s*ury=[\\d.]+\\s*\\}",
            $"media_bounds{{\n    urx={(double)wPx}\n    ury={(double)hPx}\n   }}");
        t = Regex.Replace(t, "\\blandscape_mode=(TRUE|FALSE)", wPx >= hPx ? "landscape_mode=TRUE" : "landscape_mode=FALSE");

        // Resolution block: stored selected resolution only (name + addr/effective x/y).
        t = Regex.Replace(t, "\\bresolution\\{\\s*name=\"[^\"]*\"", $"resolution{{\n name=\"{resName}\"");
        t = Regex.Replace(t, "addr_resolution_x=[\\d.]+", $"addr_resolution_x={(double)dpi}");
        t = Regex.Replace(t, "addr_resolution_y=[\\d.]+", $"addr_resolution_y={(double)dpi}");
        t = Regex.Replace(t, "effective_resolution_x=[\\d.]+", $"effective_resolution_x={(double)dpi}");
        t = Regex.Replace(t, "effective_resolution_y=[\\d.]+", $"effective_resolution_y={(double)dpi}");

        return t;
    }

    /// <summary>Post-write check: the patched text actually carries the requested medium + resolution.</summary>
    public static bool Verify(string pc3Text, int wPx, int hPx, int dpi)
    {
        return pc3Text.Contains($"name=\"{MediaName(wPx, hPx)}\"", StringComparison.Ordinal)
            && pc3Text.Contains($"urx={(double)wPx}", StringComparison.Ordinal)
            && pc3Text.Contains($"effective_resolution_x={(double)dpi}", StringComparison.Ordinal);
    }
}
