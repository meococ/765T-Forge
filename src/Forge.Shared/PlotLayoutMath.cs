using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Forge.Shared;

/// <summary>What part of the layout/model forge_plot_to_pdf sends to the sheet.</summary>
public enum PlotAreaKind
{
    Layout,
    Extents,
    Display,
    Limits,
    Window,

    /// <summary>A named view — only reachable via a layout's stored plot type (D12), never a caller word.</summary>
    View
}

/// <summary>
/// live-smoke-20261030 fix4 (D12): a paper layout's stored plot type, carried by NAME.
/// AutoCAD's managed Autodesk.AutoCAD.DatabaseServices.PlotType is 0-based
/// (Display=0, Extents=1, Limits=2, View=3, Window=4, Layout=5) — one below the 1-based
/// ActiveX AcPlotType numbering the r2 code assumed (live readback: a stored Window read
/// 4, a stored View 3, a stored Layout 5). Forge.Shared cannot reference the AutoCAD
/// assemblies (ServerOnly CI), so the plugin converts member-by-member by name and no int
/// ever crosses this boundary.
/// </summary>
public enum StoredPlotType
{
    Display,
    Extents,
    Limits,
    View,
    Window,
    Layout
}

/// <summary>Parsed plot scale: fit-to-sheet, or paper units : drawing units.</summary>
public readonly record struct PlotScaleSpec(bool Fit, double PaperUnits, double DrawingUnits)
{
    public static PlotScaleSpec FitToSheet => new(true, 1, 1);

    public override string ToString() => Fit
        ? "fit"
        : $"{PaperUnits.ToString("0.######", CultureInfo.InvariantCulture)}:{DrawingUnits.ToString("0.######", CultureInfo.InvariantCulture)}";
}

/// <summary>Fit read-back: the scale that maps the plotted area onto the printable area and how much of the sheet it fills.</summary>
public sealed record PlotFitResult(double PaperUnitsPerDrawingUnit, double FillX, double FillY, bool Rotated)
{
    /// <summary>Fraction of the printable sheet the plotted area occupies along its tighter axis (1.0 = touches both margins on one axis).</summary>
    public double FillMax => Math.Max(FillX, FillY);
}

/// <summary>Content-vs-area coverage: how much of the plotted area the drawing's own geometry actually occupies.</summary>
public sealed record PlotCoverage(double ContentFillX, double ContentFillY, double AreaFraction, bool LooksTiny, string? Warning);

/// <summary>D12: how the plotted area was chosen and the window to apply when it is a stored Window.</summary>
public sealed record PlotAreaResolution(PlotAreaKind Area, string Source, double[]? Window);

/// <summary>
/// review-wp2-r1 finding 6 (D12): centring for a plot. When the plotted area came from the
/// stored page setup (areaSource == "stored") the setup's own centring/offset wins — unless
/// the caller passed centered explicitly. KeepStored=true means the plugin must NOT call
/// SetPlotCentered/SetPlotOrigin: the CopyFrom already carried the stored values into the
/// settings (writing centered=true there is what dropped the stored offset in r1).
/// </summary>
public readonly record struct PlotCentring(bool Centered, bool KeepStored)
{
    public static PlotCentring Resolve(bool? callerCentered, string? areaSource, bool storedCentered)
    {
        if (callerCentered is { } explicitCentered)
        {
            return new PlotCentring(explicitCentered, KeepStored: false);
        }

        return areaSource == "stored"
            ? new PlotCentring(storedCentered, KeepStored: true)
            : new PlotCentring(Centered: true, KeepStored: false);
    }

    /// <summary>
    /// review-wp2-r2 M1 (D12): the -PLOT "plot offset (x,y) or [Center]" answer for the legacy
    /// engine="command" path. KeepStored answers "" — Enter accepts the prompt's default, which is
    /// the layout's own stored page-setup value, so the stored offset AND the stored centred flag
    /// both survive (the same "do not touch" the API path gets by skipping
    /// SetPlotCentered/SetPlotOrigin). An explicit stored "x,y" is never written: on a
    /// stored-centred setup PlotOrigin can hold a stale/manual value, and answering it would
    /// actively override centring — the r1 defect again.
    ///
    /// live-smoke-20261030 fix4 (D12): Layout and View answer "" unconditionally — the api
    /// engine never calls SetPlotCentered/SetPlotOrigin for those two areas, so Enter is the
    /// only answer that makes the engines agree. The r2 "0,0" for Layout is what reset a
    /// stored Layout's 12,8 mm offset to 0,0 live; and on AutoCAD 2025 a Layout-area offset
    /// prompt reads "Enter plot offset (x,y) &lt;…&gt;:" with no [Center] option, so "Center"
    /// must never be sent for it.
    /// </summary>
    public string OffsetPromptAnswer(PlotAreaKind area)
        => KeepStored || area is PlotAreaKind.Layout or PlotAreaKind.View
            ? ""
            : Centered ? "Center" : "0,0";
}

/// <summary>fix-smoke-20261029 D4: what forge_plot_to_pdf must do so AutoCAD's plot validator accepts the layout.</summary>
public enum PlotLayoutAction
{
    /// <summary>The target layout is already current and ready.</summary>
    None,

    /// <summary>Make the target layout current (restored afterwards).</summary>
    Switch,

    /// <summary>The paper layout is current but a model-space viewport is active: switch to paper space.</summary>
    SwitchToPaperSpace,

    /// <summary>Dry run on a layout that is not current: nothing is switched, so validation is deferred to the real plot.</summary>
    DeferValidation
}

/// <summary>
/// Pure plot planning for forge_plot_to_pdf (no AutoCAD types): media-name resolution, orientation
/// that does not depend on how a PC3 defines its media, fit math, and the "drawing came out tiny in
/// one corner" detector.
/// </summary>
public static class PlotLayoutMath
{
    /// <summary>Below this fraction of the plotted area (on BOTH axes) the content is reported as tiny.</summary>
    public const double TinyContentThreshold = 0.5;

    public static bool TryParseArea(string? value, out PlotAreaKind kind)
    {
        kind = PlotAreaKind.Extents;
        switch (value?.Trim().ToLowerInvariant())
        {
            case "layout":
                kind = PlotAreaKind.Layout;
                return true;
            case "extents":
            case "extent":
                kind = PlotAreaKind.Extents;
                return true;
            case "display":
            case "view":
                kind = PlotAreaKind.Display;
                return true;
            case "limits":
                kind = PlotAreaKind.Limits;
                return true;
            case "window":
                kind = PlotAreaKind.Window;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Parses "fit", "1:100", "1=100", "1:1", "2:1" (paper units : drawing units).</summary>
    public static bool TryParseScale(string? value, out PlotScaleSpec scale)
    {
        scale = PlotScaleSpec.FitToSheet;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var text = value.Trim();
        if (text.Equals("fit", StringComparison.OrdinalIgnoreCase) || text.Equals("scaled to fit", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var match = Regex.Match(text, @"^\s*(?<p>\d+(?:\.\d+)?)\s*(?:[a-z""']*)\s*[:=]\s*(?<d>\d+(?:\.\d+)?)\s*(?:[a-z""']*)\s*$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        var p = double.Parse(match.Groups["p"].Value, CultureInfo.InvariantCulture);
        var d = double.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture);
        if (p <= 0 || d <= 0)
        {
            return false;
        }

        scale = new PlotScaleSpec(false, p, d);
        return true;
    }

    /// <summary>
    /// Resolves a requested paper size to a device's canonical media name. Accepts the canonical
    /// name ("ISO_A1_(841.00_x_594.00_MM)"), the localized name ("ISO A1 (841.00 x 594.00 MM)"), or
    /// the same with spaces/underscores/case differing. Exact matches win over normalized ones.
    /// </summary>
    public static string? ResolveMedia(string? requested, IEnumerable<(string Canonical, string? Locale)> media)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return null;
        }

        var list = media.ToList();
        var wanted = requested.Trim();
        foreach (var (canonical, locale) in list)
        {
            if (string.Equals(canonical, wanted, StringComparison.Ordinal) || string.Equals(locale, wanted, StringComparison.Ordinal))
            {
                return canonical;
            }
        }

        var key = NormalizeMedia(wanted);
        foreach (var (canonical, locale) in list)
        {
            if (NormalizeMedia(canonical) == key || (locale is not null && NormalizeMedia(locale) == key))
            {
                return canonical;
            }
        }

        return null;
    }

    private static string NormalizeMedia(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '.' || ch == '(' || ch == ')')
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// fix-smoke-20261029-r2 D12: chooses the plot area. An explicit caller area always wins;
    /// Model keeps the extents default; a PAPER layout with no caller area falls back to its
    /// own stored plot type (<see cref="StoredPlotType"/> by name — stored Window carries the
    /// layout's stored window box, stored View needs its stored view name), and only when the
    /// stored type is unusable do the media/default rules apply. The stored-window case is
    /// what the r2 smoke missed: AEON sheets store Window and the old code silently plotted
    /// "layout" into an empty PDF.
    /// </summary>
    public static PlotAreaResolution ResolveArea(
        PlotAreaKind? requested,
        bool isModel,
        StoredPlotType? storedPlotType,
        double[]? storedWindow,
        string? storedViewName,
        bool mediaChanged)
    {
        if (requested is { } r)
        {
            return new PlotAreaResolution(r, "requested", null);
        }

        if (isModel)
        {
            return new PlotAreaResolution(PlotAreaKind.Extents, "default_model_extents", null);
        }

        var storedKind = storedPlotType switch
        {
            StoredPlotType.Display => PlotAreaKind.Display,
            StoredPlotType.Extents => PlotAreaKind.Extents,
            StoredPlotType.Limits => PlotAreaKind.Limits,
            StoredPlotType.View when !string.IsNullOrWhiteSpace(storedViewName) => PlotAreaKind.View,
            StoredPlotType.Window when storedWindow is { Length: 4 } w && w[2] - w[0] > 0 && w[3] - w[1] > 0 => PlotAreaKind.Window,
            StoredPlotType.Layout => PlotAreaKind.Layout,
            _ => (PlotAreaKind?)null
        };

        if (storedKind is { } kind)
        {
            return new PlotAreaResolution(kind, "stored", kind == PlotAreaKind.Window ? storedWindow : null);
        }

        return new PlotAreaResolution(
            mediaChanged ? PlotAreaKind.Extents : PlotAreaKind.Layout,
            mediaChanged ? "default_extents_because_paper_differs_from_layout" : "default_layout",
            null);
    }

    /// <summary>
    /// Plot rotation in degrees (0 or 90) relative to the media as the device defines it.
    /// "landscape"/"portrait" are honoured against the media's real width/height (a PC3 may define
    /// an A1 sheet either way round); "auto" lines the area's long side up with the sheet's long side.
    /// Returns null for an unknown orientation word.
    /// </summary>
    public static int? ChooseRotation(string? orientation, double paperWidth, double paperHeight, double areaWidth, double areaHeight)
    {
        var paperLandscape = paperWidth >= paperHeight;
        var mode = string.IsNullOrWhiteSpace(orientation) ? "auto" : orientation.Trim().ToLowerInvariant();
        if (mode == "auto")
        {
            mode = areaWidth >= areaHeight ? "landscape" : "portrait";
        }

        return mode switch
        {
            "landscape" => paperLandscape ? 0 : 90,
            "portrait" => paperLandscape ? 90 : 0,
            _ => null
        };
    }

    /// <summary>Fit-to-sheet scale (paper units per drawing unit) and fill fractions for an area on a printable sheet.</summary>
    public static PlotFitResult ComputeFit(double areaWidth, double areaHeight, double printableWidth, double printableHeight, int rotationDegrees, PlotScaleSpec scale)
    {
        var rotated = rotationDegrees % 180 != 0;
        var pw = rotated ? printableHeight : printableWidth;
        var ph = rotated ? printableWidth : printableHeight;
        if (areaWidth <= 0 || areaHeight <= 0 || pw <= 0 || ph <= 0)
        {
            return new PlotFitResult(0, 0, 0, rotated);
        }

        var factor = scale.Fit
            ? Math.Min(pw / areaWidth, ph / areaHeight)
            : scale.PaperUnits / scale.DrawingUnits;
        return new PlotFitResult(factor, areaWidth * factor / pw, areaHeight * factor / ph, rotated);
    }

    /// <summary>
    /// How much of the plotted area the drawing's geometry fills. The classic failure — a layout
    /// plotted with plotArea=layout onto a bigger sheet than its page setup — leaves the content in
    /// one corner; this flags it so the agent re-plots with extents/window + fit instead of shipping it.
    /// </summary>
    public static PlotCoverage ComputeCoverage(double[] area, double[]? content)
    {
        if (area is not { Length: 4 } || content is not { Length: 4 })
        {
            return new PlotCoverage(0, 0, 0, false, null);
        }

        var aw = area[2] - area[0];
        var ah = area[3] - area[1];
        if (aw <= 0 || ah <= 0)
        {
            return new PlotCoverage(0, 0, 0, false, null);
        }

        var minX = Math.Max(area[0], content[0]);
        var minY = Math.Max(area[1], content[1]);
        var maxX = Math.Min(area[2], content[2]);
        var maxY = Math.Min(area[3], content[3]);
        var cw = Math.Max(0, maxX - minX);
        var ch = Math.Max(0, maxY - minY);
        var fx = cw / aw;
        var fy = ch / ah;
        var tiny = fx < TinyContentThreshold && fy < TinyContentThreshold;
        var warning = tiny
            ? $"Drawing content fills only {fx:P0} x {fy:P0} of the plotted area — it will look small in a corner of the sheet. Re-plot with plotArea=extents (scale=fit) or plotArea=window."
            : null;
        return new PlotCoverage(Math.Round(fx, 4), Math.Round(fy, 4), Math.Round(fx * fy, 4), tiny, warning);
    }

    /// <summary>Normalizes a window given as [[x1,y1],[x2,y2]] or [x1,y1,x2,y2] to [minX,minY,maxX,maxY].</summary>
    public static bool TryNormalizeWindow(double[][]? nested, double[]? flat, out double[] box, out string? error)
    {
        box = [];
        error = null;
        double x1, y1, x2, y2;
        if (nested is { Length: > 0 })
        {
            if (nested.Length != 2 || nested[0] is not { Length: >= 2 } a || nested[1] is not { Length: >= 2 } b)
            {
                error = "window must be [[x1,y1],[x2,y2]].";
                return false;
            }

            (x1, y1, x2, y2) = (a[0], a[1], b[0], b[1]);
        }
        else if (flat is { Length: > 0 })
        {
            if (flat.Length != 4)
            {
                error = "bbox must be [x1,y1,x2,y2].";
                return false;
            }

            (x1, y1, x2, y2) = (flat[0], flat[1], flat[2], flat[3]);
        }
        else
        {
            error = "window is required.";
            return false;
        }

        if (!double.IsFinite(x1) || !double.IsFinite(y1) || !double.IsFinite(x2) || !double.IsFinite(y2))
        {
            error = "window coordinates must be finite numbers.";
            return false;
        }

        box = [Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2)];
        if (box[2] - box[0] <= 0 || box[3] - box[1] <= 0)
        {
            error = "window has zero width or height.";
            box = [];
            return false;
        }

        return true;
    }

    /// <summary>
    /// Best-effort read of the first page's /MediaBox from a PDF (points → millimetres) so the plot
    /// read-back can state the real sheet size and orientation that came out.
    /// </summary>
    public static bool TryReadFirstMediaBox(byte[] pdf, out double widthMm, out double heightMm)
    {
        widthMm = 0;
        heightMm = 0;
        if (pdf is null || pdf.Length < 16)
        {
            return false;
        }

        var text = Encoding.Latin1.GetString(pdf);
        var match = Regex.Match(
            text,
            @"/MediaBox\s*\[\s*(?<x0>-?\d+(?:\.\d+)?)\s+(?<y0>-?\d+(?:\.\d+)?)\s+(?<x1>-?\d+(?:\.\d+)?)\s+(?<y1>-?\d+(?:\.\d+)?)\s*\]");
        if (!match.Success)
        {
            return false;
        }

        double P(string g) => double.Parse(match.Groups[g].Value, CultureInfo.InvariantCulture);
        const double MmPerPoint = 25.4 / 72.0;
        widthMm = Math.Round(Math.Abs(P("x1") - P("x0")) * MmPerPoint, 1);
        heightMm = Math.Round(Math.Abs(P("y1") - P("y0")) * MmPerPoint, 1);
        return widthMm > 0 && heightMm > 0;
    }

    /// <summary>
    /// fix-smoke-20261029 D4: PlotInfoValidator rejects a layout that is not current (eLayoutNotCurrent).
    /// A real plot switches to it and restores afterwards; a dry run never switches.
    /// </summary>
    public static PlotLayoutAction PlanLayoutSwitch(string? currentLayout, string targetLayout, bool targetIsModel, int tilemode, int cvport, bool dryRun)
    {
        var isCurrent = SameLayout(currentLayout, targetLayout);
        if (dryRun)
        {
            return isCurrent ? PlotLayoutAction.None : PlotLayoutAction.DeferValidation;
        }

        if (!isCurrent || (targetIsModel && tilemode != 1) || (!targetIsModel && tilemode != 0))
        {
            return PlotLayoutAction.Switch;
        }

        return !targetIsModel && cvport != 1 ? PlotLayoutAction.SwitchToPaperSpace : PlotLayoutAction.None;
    }

    /// <summary>
    /// The plot gate after the switch: CTAB and LayoutManager.CurrentLayout name the target; Model needs
    /// TILEMODE=1; a paper layout needs TILEMODE=0 and paper space active (CVPORT=1).
    /// </summary>
    public static bool IsLayoutReady(string targetLayout, string? ctab, string? currentLayout, bool targetIsModel, int tilemode, int cvport)
        => SameLayout(ctab, targetLayout)
           && SameLayout(currentLayout, targetLayout)
           && (targetIsModel ? tilemode == 1 : tilemode == 0 && cvport == 1);

    /// <summary>What differs between the state saved before the plot and the state read back after the restore.</summary>
    public static IReadOnlyList<string> LayoutRestoreMismatches(string? ctabBefore, int tilemodeBefore, int cvportBefore, string? ctabAfter, int tilemodeAfter, int cvportAfter)
    {
        var mismatches = new List<string>();
        if (!SameLayout(ctabBefore, ctabAfter))
        {
            mismatches.Add($"CTAB is '{ctabAfter}' (was '{ctabBefore}')");
        }

        if (tilemodeBefore != tilemodeAfter)
        {
            mismatches.Add($"TILEMODE is {tilemodeAfter} (was {tilemodeBefore})");
        }

        if (cvportBefore != cvportAfter)
        {
            mismatches.Add($"CVPORT is {cvportAfter} (was {cvportBefore})");
        }

        return mismatches;
    }

    /// <summary>Layout names are case-insensitive in AutoCAD.</summary>
    public static bool SameLayout(string? a, string? b)
        => a is not null && b is not null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
