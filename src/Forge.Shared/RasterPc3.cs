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
    /// <summary>
    /// The canonical name the raster driver gives a user-defined medium of wPx x hPx —
    /// live-r9 proved user media live in the PMP under exactly this name form, and the
    /// pc3's selected size{} references it. (An earlier Forge_WxHpx name never listed:
    /// renaming the pc3's selection does not create a medium.)
    /// </summary>
    public static string MediaName(int wPx, int hPx)
        => $"UserDefinedRaster ({wPx:0.00} x {hPx:0.00}Pixels)";

    /// <summary>The localized/display form the PMP stores alongside the canonical name.</summary>
    public static string LocalizedName(int wPx, int hPx)
        => $"User 1 ({wPx:0.00} x {hPx:0.00} Pixels)";

    /// <summary>The media_description_name the PMP's size{} entry points at (and its description{} entry is named).</summary>
    public static string DescriptionName(int wPx, int hPx)
    {
        var orient = wPx >= hPx ? "Landscape" : "Portrait";
        return $"UserDefinedRaster {orient} {wPx:0.00}W x {hPx:0.00}H - (0, 0) x ({wPx - 1}, {hPx - 1}) ={(long)wPx * hPx} Pixels";
    }

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
        // live-r8: the REAL PublishToWeb PNG.pc3 stores names UNTERMINATED — the value
        // runs to end-of-line, not to a closing quote. [^"]* here ate everything up to
        // the next '"' (media_bounds, io{, every resolution field). Capture to EOL.
        var nameMatch = Regex.Match(t, "\\bsize\\{\\s*name=\"(?<n>[^\"\\r\\n]*)");
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
        // Same unterminated-name fix: [^"]* would swallow the addr_/effective_ fields.
        t = Regex.Replace(t, "\\bresolution\\{\\s*name=\"[^\"\\r\\n]*", $"resolution{{\n name=\"{resName}");
        t = Regex.Replace(t, "addr_resolution_x=[\\d.]+", $"addr_resolution_x={(double)dpi}");
        t = Regex.Replace(t, "addr_resolution_y=[\\d.]+", $"addr_resolution_y={(double)dpi}");
        t = Regex.Replace(t, "effective_resolution_x=[\\d.]+", $"effective_resolution_x={(double)dpi}");
        t = Regex.Replace(t, "effective_resolution_y=[\\d.]+", $"effective_resolution_y={(double)dpi}");

        return t;
    }

    /// <summary>
    /// Injects a user-defined raster medium into a PMP text: one numbered entry in
    /// udm{media{description{}} (the bounds) plus one numbered entry in udm{media{size{}}
    /// (the selectable name pointing at it) — the two coordinated lists the plot wizard
    /// maintains. Idempotent: when the canonical name is already present the text is
    /// returned unchanged with injected=false. Returns null + error when the expected
    /// list structure is absent (not a raster pmp).
    /// </summary>
    public static string? EnsurePmpMedium(string pmpText, int wPx, int hPx, out bool injected, out string? error)
    {
        injected = false;
        error = null;
        if (wPx <= 0 || hPx <= 0)
        {
            error = $"invalid raster target {wPx}x{hPx}px";
            return null;
        }

        var canon = MediaName(wPx, hPx);
        var descName = DescriptionName(wPx, hPx);
        // name= is anchored at line start: media_description_name=" ends in name=" too,
        // so an unanchored Contains would see the size entry's back-reference.
        var sizePresent = pmpText.Contains($"\n    name=\"{canon}", StringComparison.Ordinal);
        var descPresent = pmpText.Contains($"\n    name=\"{descName}", StringComparison.Ordinal);
        if (sizePresent && descPresent)
        {
            return pmpText;
        }
        if (sizePresent != descPresent)
        {
            error = $"pmp is half-injected for '{canon}' (size entry: {sizePresent}, description entry: {descPresent}); refusing to write over a corrupt state — restore the .forge-bak";
            return null;
        }

        // Everything is scoped to udm{...}: mod{/del{ precede it and hidden{ follows it
        // in the real PMP; a first-match anchor outside this range would inject a user
        // medium into the stock or hidden lists. udm{ sits at column 0, so its own close
        // is the first 0-indent "}" after it.
        var udmAt = -1;
        for (var i = pmpText.IndexOf("udm{", StringComparison.Ordinal); i >= 0;
             i = pmpText.IndexOf("udm{", i + 1, StringComparison.Ordinal))
        {
            if (i == 0 || pmpText[i - 1] == '\n') // a value could literally contain "udm{" — require line start
            {
                udmAt = i;
                break;
            }
        }
        var udmEnd = udmAt < 0 ? -1 : pmpText.IndexOf("\n}", udmAt, StringComparison.Ordinal);
        if (udmAt < 0 || udmEnd < 0)
        {
            error = "pmp text has no udm{} user-media section (not a raster pmp?)";
            return null;
        }

        var sizeAt = pmpText.IndexOf("\n  size{", udmAt, StringComparison.Ordinal);
        var descAt = pmpText.IndexOf("\n  description{", udmAt, StringComparison.Ordinal);
        // Each list's own close is the FIRST 2-indent "}" after it opens — numbered
        // entries close at 3-indent ("\n   }"), which cannot match "\n  }".
        var sizeClose = sizeAt < 0 ? -1 : pmpText.IndexOf("\n  }", sizeAt, StringComparison.Ordinal);
        var descClose = descAt < 0 ? -1 : pmpText.IndexOf("\n  }", descAt, StringComparison.Ordinal);
        if (sizeAt < 0 || sizeAt >= udmEnd || sizeClose < 0 || sizeClose >= descAt
            || descAt < 0 || descAt >= udmEnd || descClose < 0 || descClose >= udmEnd)
        {
            error = "pmp udm media size/description lists are missing, misordered, or unterminated";
            return null;
        }

        var nextSize = NextIndex(pmpText, sizeAt, sizeClose);
        var nextDesc = NextIndex(pmpText, descAt, descClose);
        if (nextSize < 0 || nextDesc < 0)
        {
            error = "could not locate the next size/description entry index in the pmp";
            return null;
        }

        var sizeEntry =
            $"\n   {nextSize}{{\n    caps_type=2\n    name=\"{canon}\n    localized_name=\"{LocalizedName(wPx, hPx)}\n" +
            $"    media_description_name=\"{descName}\n    media_group=16\n    landscape_mode={(wPx >= hPx ? "TRUE" : "FALSE")}\n   }}";
        var descEntry =
            $"\n   {nextDesc}{{\n    caps_type=2\n    name=\"{descName}\n    media_bounds_urx={(double)wPx}\n    media_bounds_ury={(double)hPx}\n" +
            $"    printable_bounds_llx=0.0\n    printable_bounds_lly=0.0\n    printable_bounds_urx={(double)(wPx - 1)}\n" +
            $"    printable_bounds_ury={(double)(hPx - 1)}\n    printable_area={(double)wPx * hPx}\n    dimensional=FALSE\n   }}";

        // One-pass splice at both insertion points (sizeClose < descClose): computing
        // both on the original text avoids the index drift of sequential inserts.
        injected = true;
        return pmpText[..sizeClose] + sizeEntry + pmpText[sizeClose..descClose] + descEntry + pmpText[descClose..];
    }

    private static int NextIndex(string text, int listAt, int endAt)
    {
        var last = -1;
        var m = Regex.Match(text[listAt..endAt], "\\n   (\\d+)\\{");
        while (m.Success)
        {
            last = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            m = m.NextMatch();
        }
        return last + 1;
    }

    /// <summary>Post-write check: the patched text actually carries the requested medium + resolution.</summary>
    public static bool Verify(string pc3Text, int wPx, int hPx, int dpi)
    {
        // no closing quote on the name check: the patch preserves the source's own
        // terminator, and the real pc3 terminates names at end-of-line. The canonical
        // UserDefinedRaster name embeds exact dims, so a prefix match cannot collide.
        return pc3Text.Contains($"name=\"{MediaName(wPx, hPx)}", StringComparison.Ordinal)
            && pc3Text.Contains($"urx={(double)wPx}", StringComparison.Ordinal)
            && pc3Text.Contains($"effective_resolution_x={(double)dpi}", StringComparison.Ordinal);
    }
}
