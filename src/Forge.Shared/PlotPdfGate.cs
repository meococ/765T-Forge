namespace Forge.Shared;

public static class PlotPdfGate
{
    public const string FailedCode = "plot_probe_failed";

    public static ForgeResult FromProbe(string commandId, PdfProbeResult probe, object? data)
    {
        var verification = new ForgeVerification
        {
            Attempted = true,
            Passed = probe.Passed,
            Message = probe.Message,
            ReadBack = probe
        };

        if (probe.Passed)
        {
            return ForgeResult.Success(commandId, data, verification: verification);
        }

        return ForgeResult.Failure(
            commandId,
            FailedCode,
            "Plot wrote a file but the PDF probe failed.",
            "Inspect the PDF probe in data. Do not treat this output as published.",
            data: data,
            verification: verification);
    }
}
