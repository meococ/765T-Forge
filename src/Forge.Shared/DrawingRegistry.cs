using System.Text.Json;
using System.Text.RegularExpressions;

namespace Forge.Shared;

public sealed class DrawingRegistry
{
    public string ProjectId { get; init; } = "";
    public string? SourcePath { get; set; }
    public List<DrawingRegistrySheet> Sheets { get; init; } = [];

    public static DrawingRegistry LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        var registry = JsonSerializer.Deserialize<DrawingRegistry>(json, ForgeJson.Options)
                       ?? throw new InvalidOperationException("Drawing registry JSON deserialized to null.");
        registry.SourcePath = Path.GetFullPath(path);
        registry.Validate();
        return registry;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProjectId))
        {
            throw new InvalidOperationException("Drawing registry requires projectId.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in Sheets)
        {
            if (string.IsNullOrWhiteSpace(sheet.DrawingNo))
            {
                throw new InvalidOperationException("Each sheet requires drawingNo.");
            }

            if (!seen.Add(sheet.DrawingNo))
            {
                throw new InvalidOperationException($"Duplicate drawingNo '{sheet.DrawingNo}'.");
            }
        }
    }

    public DrawingRegistrySheet? FindByDrawingNo(string drawingNo)
    {
        return Sheets.FirstOrDefault(s => s.DrawingNo.Equals(drawingNo, StringComparison.OrdinalIgnoreCase));
    }

    public bool TryAuthorizeAttribute(string tag, string value, out string? errorCode, out string? message)
    {
        errorCode = null;
        message = null;
        if (!IsDrawingNumberTag(tag))
        {
            return true;
        }

        if (FindByDrawingNo(value) is null)
        {
            errorCode = "deny_unknown_drawing_no";
            message = $"Drawing number '{value}' is not in the loaded registry (project '{ProjectId}'). Do not invent sheet numbers.";
            return false;
        }

        return true;
    }

    public static bool IsDrawingNumberTag(string tag)
    {
        return tag.Equals("DRAWING_NO", StringComparison.OrdinalIgnoreCase)
               || tag.Equals("DWG_NO", StringComparison.OrdinalIgnoreCase)
               || tag.Equals("SHEET_NO", StringComparison.OrdinalIgnoreCase)
               || tag.Equals("SO_HIEU", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class DrawingRegistrySheet
{
    public string DrawingNo { get; init; } = "";
    public string? Rev { get; init; }
    public string? Layout { get; init; }
    public string? Title { get; init; }
    public Dictionary<string, string> TitleAttrs { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Process-wide registry loaded via forge_registry_load (plugin session).</summary>
public static class DrawingRegistryStore
{
    private static readonly object Gate = new();
    private static DrawingRegistry? _current;

    public static DrawingRegistry? Current
    {
        get { lock (Gate) return _current; }
    }

    public static void Set(DrawingRegistry? registry)
    {
        lock (Gate) _current = registry;
    }
}
