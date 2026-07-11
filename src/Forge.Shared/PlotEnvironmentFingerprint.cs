using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Forge.Shared;

/// <summary>Snapshot of plot-related session environment for fail-closed publish gates.</summary>
public sealed class PlotEnvironmentFingerprint
{
    public string? ProductVersion { get; init; }
    public string? ProfileHint { get; init; }
    public int? PstyleMode { get; init; }
    public int? BackgroundPlot { get; init; }
    public int? BgCorePublish { get; init; }
    public string[] PlotStyleSearchPaths { get; init; } = [];
    public string[] PrinterSearchPaths { get; init; } = [];
    public string[] DeviceNames { get; init; } = [];
    public string? ExpectedDevice { get; init; }
    public string? ExpectedPaper { get; init; }
    public string? ExpectedPlotStyle { get; init; }
    public string FingerprintHash { get; init; } = "";

    public string ComputeHash()
    {
        var payload = JsonSerializer.Serialize(new
        {
            ProductVersion,
            ProfileHint,
            PstyleMode,
            BackgroundPlot,
            BgCorePublish,
            PlotStyleSearchPaths,
            PrinterSearchPaths,
            DeviceNames,
            ExpectedDevice,
            ExpectedPaper,
            ExpectedPlotStyle
        }, ForgeJson.Options);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes)[..16];
    }

    public PlotEnvironmentFingerprint WithHash()
        => new()
        {
            ProductVersion = ProductVersion,
            ProfileHint = ProfileHint,
            PstyleMode = PstyleMode,
            BackgroundPlot = BackgroundPlot,
            BgCorePublish = BgCorePublish,
            PlotStyleSearchPaths = PlotStyleSearchPaths,
            PrinterSearchPaths = PrinterSearchPaths,
            DeviceNames = DeviceNames,
            ExpectedDevice = ExpectedDevice,
            ExpectedPaper = ExpectedPaper,
            ExpectedPlotStyle = ExpectedPlotStyle,
            FingerprintHash = ComputeHash()
        };

    public IReadOnlyList<QaFinding> EvaluateAgainstPack(StandardsPack? pack)
    {
        var findings = new List<QaFinding>();
        if (BackgroundPlot is not null and not 0)
        {
            findings.Add(new QaFinding(
                "plot_env_background_plot",
                pack?.RequireForegroundPlot == true ? "error" : "warning",
                $"BACKGROUNDPLOT={BackgroundPlot}; issue-set publish expects foreground (0).",
                "Forge forces BACKGROUNDPLOT=0 during publish; fix the session profile.",
                "forge_qa_plot_fingerprint"));
        }

        if (pack is null)
        {
            return findings;
        }

        findings.AddRange(pack.EvaluatePlotBindings(
            ExpectedDevice ?? (DeviceNames.Length > 0 ? DeviceNames[0] : null),
            ExpectedPaper,
            ExpectedPlotStyle,
            BackgroundPlot));

        if (!string.IsNullOrWhiteSpace(pack.PlotDevice)
            && DeviceNames.Length > 0
            && !DeviceNames.Any(d => d.Equals(pack.PlotDevice, StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(new QaFinding(
                "plot_env_fingerprint_mismatch",
                "error",
                $"Pack device '{pack.PlotDevice}' is not in the session device list.",
                "Install/select the pack plot device or update the standards pack.",
                "forge_system_capabilities"));
        }

        return findings;
    }
}
