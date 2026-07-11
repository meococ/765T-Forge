using System.Text.Json;
using System.Text.RegularExpressions;

namespace Forge.Shared;

public sealed class StandardsPack
{
    public string PackId { get; init; } = "";
    public string? SourcePath { get; set; }
    public string? Description { get; init; }
    public List<string> Layers { get; init; } = [];
    public List<string> ForbiddenLayers { get; init; } = [];
    public string? CtbPath { get; init; }
    public string? StbPath { get; init; }
    public string? PlotDevice { get; init; }
    public string? PaperSize { get; init; }
    public string? PageSetupName { get; init; }
    public string? LayerStateName { get; init; }
    public bool RequireForegroundPlot { get; init; } = true;
    public Dictionary<string, string> TitleBlockAttrMap { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string? DrawingNoRegex { get; init; }
    public string? RevisionScheme { get; init; }
    public List<string> VpScaleAllowlist { get; init; } = [];
    public List<string> RequiredTitleblockTags { get; init; } = [];

    public static StandardsPack LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        var pack = JsonSerializer.Deserialize<StandardsPack>(json, ForgeJson.Options)
                   ?? throw new InvalidOperationException("Standards pack JSON deserialized to null.");
        pack.SourcePath = Path.GetFullPath(path);
        pack.Validate();
        return pack;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PackId))
        {
            throw new InvalidOperationException("Standards pack requires packId.");
        }

        if (!string.IsNullOrWhiteSpace(DrawingNoRegex))
        {
            _ = new Regex(DrawingNoRegex, RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }
    }

    public IReadOnlyList<QaFinding> EvaluateLayers(IEnumerable<string> presentLayers)
    {
        var findings = new List<QaFinding>();
        var present = presentLayers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in Layers)
        {
            if (!present.Contains(layer))
            {
                findings.Add(new QaFinding(
                    "pack_layer_missing",
                    "warning",
                    $"Standards pack '{PackId}' expected layer '{layer}'.",
                    SuggestedTool: "forge_qa_audit_layers"));
            }
        }

        foreach (var layer in ForbiddenLayers)
        {
            if (present.Contains(layer))
            {
                findings.Add(new QaFinding(
                    "pack_layer_forbidden",
                    "error",
                    $"Standards pack '{PackId}' forbids layer '{layer}'.",
                    SuggestedTool: "forge_layer_list"));
            }
        }

        return findings;
    }

    /// <summary>
    /// Pack v2 plot bindings — blocking when pack declares device/CTB/paper expectations.
    /// </summary>
    public IReadOnlyList<QaFinding> EvaluatePlotBindings(
        string? deviceName,
        string? paperSize,
        string? plotStylePath,
        int? backgroundPlot)
    {
        var findings = new List<QaFinding>();
        if (RequireForegroundPlot && backgroundPlot is not null and not 0)
        {
            findings.Add(new QaFinding(
                "pack_background_plot",
                "error",
                $"Standards pack '{PackId}' requires BACKGROUNDPLOT=0 (foreground); current={backgroundPlot}.",
                "Forge publish forces foreground; fix session or pack override.",
                "forge_qa_preflight"));
        }

        if (!string.IsNullOrWhiteSpace(PlotDevice)
            && !string.IsNullOrWhiteSpace(deviceName)
            && !PlotDevice.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new QaFinding(
                "pack_plot_device_mismatch",
                "error",
                $"Pack expects plot device '{PlotDevice}' but got '{deviceName}'.",
                "Use forge_system_capabilities and pass the pack device.",
                "forge_system_capabilities"));
        }

        if (!string.IsNullOrWhiteSpace(PaperSize)
            && !string.IsNullOrWhiteSpace(paperSize)
            && !PaperSize.Equals(paperSize, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new QaFinding(
                "pack_paper_mismatch",
                "error",
                $"Pack expects paper '{PaperSize}' but got '{paperSize}'.",
                SuggestedTool: "forge_system_capabilities"));
        }

        var expectedStyle = CtbPath ?? StbPath;
        if (!string.IsNullOrWhiteSpace(expectedStyle)
            && !string.IsNullOrWhiteSpace(plotStylePath)
            && !string.Equals(
                Path.GetFileName(expectedStyle),
                Path.GetFileName(plotStylePath),
                StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new QaFinding(
                "pack_plot_style_mismatch",
                "error",
                $"Pack expects plot style '{Path.GetFileName(expectedStyle)}' but got '{Path.GetFileName(plotStylePath)}'.",
                SuggestedTool: "forge_plot_to_pdf"));
        }

        return findings;
    }

    public bool TryValidateDrawingNo(string drawingNo, out string? errorCode, out string? message)
    {
        errorCode = null;
        message = null;
        if (string.IsNullOrWhiteSpace(DrawingNoRegex))
        {
            return true;
        }

        if (!Regex.IsMatch(drawingNo, DrawingNoRegex))
        {
            errorCode = "deny_drawing_no_format";
            message = $"Drawing number '{drawingNo}' does not match pack regex '{DrawingNoRegex}'.";
            return false;
        }

        return true;
    }
}

public static class StandardsPackStore
{
    private static readonly object Gate = new();
    private static StandardsPack? _current;

    public static StandardsPack? Current
    {
        get { lock (Gate) return _current; }
    }

    public static void Set(StandardsPack? pack)
    {
        lock (Gate) _current = pack;
    }
}
