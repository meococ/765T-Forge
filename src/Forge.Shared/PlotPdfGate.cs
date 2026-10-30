namespace Forge.Shared;

/// <summary>
/// fix-smoke-20261029 D5: how much of the sheet the PDF actually inks, decided from the PDF readback
/// (PdfContentBox), never from the plan alone. <see cref="LooksTiny"/> is null when the PDF could not be read.
/// </summary>
public sealed record PlotCoverageDecision(
    string CoverageSource,
    bool? LooksTiny,
    double? FillX,
    double? FillY,
    string? FailureCode,
    string? FailureMessage,
    string? Warning)
{
    public bool Failed => FailureCode is not null;
}

/// <summary>
/// Turns a <see cref="PdfProbeResult"/> into the tool result for
/// <c>forge_plot_to_pdf</c>: a failed probe is a failed call that still carries
/// the probe data and the verification block, not a success.
/// </summary>
public static class PlotPdfGate
{
    public const string FailedCode = "plot_probe_failed";
    public const string ContentTinyCode = "plot_content_tiny";
    public const string ContentEmptyCode = "plot_content_empty";

    public const string SourcePdf = "pdf";
    public const string SourcePdfEmpty = "pdf_empty";
    public const string SourceUnavailable = "unavailable";

    public static ForgeResult FromProbe(string commandId, PdfProbeResult probe, object? data)
        => FromProbe(commandId, probe, data, coverage: null);

    public static ForgeResult FromProbe(string commandId, PdfProbeResult probe, object? data, PlotCoverageDecision? coverage)
    {
        var passed = probe.Passed && coverage?.Failed != true;
        var verification = new ForgeVerification
        {
            Attempted = true,
            Passed = passed,
            Message = !probe.Passed || coverage?.FailureMessage is not { } coverageMessage ? probe.Message : coverageMessage,
            ReadBack = probe
        };

        if (passed)
        {
            return ForgeResult.Success(commandId, data, verification: verification);
        }

        if (!probe.Passed)
        {
            return ForgeResult.Failure(
                commandId,
                FailedCode,
                "Plot wrote a file but the PDF probe failed.",
                "Inspect the PDF probe in data. Do not treat this output as published.",
                data: data,
                verification: verification);
        }

        return ForgeResult.Failure(
            commandId,
            coverage!.FailureCode!,
            coverage.FailureMessage!,
            "The PDF was kept; its ink box is in data.pdfContent. Re-plot with plotArea=extents and scale=fit, or a window around the drawing, and do not issue this PDF.",
            data: data,
            verification: verification);
    }

    /// <summary>
    /// Coverage gate (FIX-PLAN §D5 step 4).
    /// <list type="bullet">
    /// <item>extents + fit: a tiny or empty PDF is a failure (an empty PDF of a drawing with no content is only a warning).</item>
    /// <item>window: a small result may be what the caller asked for; it fails only when the plan expected a filled sheet.</item>
    /// <item>other areas and explicit scales: warnings only.</item>
    /// <item>PDF unreadable: looksTiny is null and a warning says coverage is not verified.</item>
    /// </list>
    /// </summary>
    /// <param name="pdfStatus">PdfContentBox status: "ok", "empty" or "unavailable".</param>
    /// <param name="planEstimateTiny">The plan-time estimate (null when there was none).</param>
    /// <param name="planHadContent">False when the drawing had nothing to plot at plan time.</param>
    public static PlotCoverageDecision DecideCoverage(
        PlotAreaKind area,
        bool fit,
        bool? planEstimateTiny,
        bool planHadContent,
        string pdfStatus,
        bool pdfLooksTiny,
        double? fillX,
        double? fillY,
        string? unavailableReason)
    {
        var gated = area == PlotAreaKind.Extents && fit;
        var windowExpectedFilled = area == PlotAreaKind.Window && planEstimateTiny == false;
        switch (pdfStatus)
        {
            case "ok":
            {
                if (!pdfLooksTiny)
                {
                    return new PlotCoverageDecision(SourcePdf, false, fillX, fillY, null, null, null);
                }

                var text = $"The PDF's ink fills only {Pct(fillX)} x {Pct(fillY)} of the sheet: the drawing is small in a corner.";
                return gated || windowExpectedFilled
                    ? new PlotCoverageDecision(SourcePdf, true, fillX, fillY, ContentTinyCode, text, null)
                    : new PlotCoverageDecision(SourcePdf, true, fillX, fillY, null, null, text);
            }

            case "empty":
            {
                var text = "The PDF has no visible marks: nothing was plotted.";
                var fails = (gated && planHadContent) || windowExpectedFilled;
                return fails
                    ? new PlotCoverageDecision(SourcePdfEmpty, true, 0, 0, ContentEmptyCode, text, null)
                    : new PlotCoverageDecision(SourcePdfEmpty, true, 0, 0, null, null, text);
            }

            default:
                return new PlotCoverageDecision(
                    SourceUnavailable,
                    null,
                    null,
                    null,
                    null,
                    null,
                    $"Coverage is not verified: the PDF content could not be measured ({(string.IsNullOrWhiteSpace(unavailableReason) ? "unknown reason" : unavailableReason)}). Check the PDF by eye before issuing it.");
        }
    }

    private static string Pct(double? value)
        => value is { } v ? v.ToString("P0", System.Globalization.CultureInfo.InvariantCulture) : "?";
}
