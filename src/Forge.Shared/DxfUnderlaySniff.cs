using System.Globalization;

namespace Forge.Shared;

/// <summary>
/// TOOL-GAP #26: scans a DXF text stream for underlay definition objects in the OBJECTS section —
/// PDFDEFINITION/PDFUNDERLAYDEFINITION, DWFDEFINITION/DWFUNDERLAYDEFINITION, DGNDEFINITION/
/// DGNUNDERLAYDEFINITION (kind pdf/dwf/dgn) and IMAGEDEF (raster images, kind "image") — and
/// extracts each one's file path (group code 1), item name (group code 2) and handle (group 5).
/// The display name comes from the parent DICTIONARY entry (code 3 name + code 350 handle).
/// Exists so the missing-underlay rule is testable on a fixture DXF — and it documents exactly
/// what the plugin-side named-object-dictionary enumeration reads (ACAD_PDFDEFINITIONS etc.).
/// </summary>
public static class DxfUnderlaySniff
{
    public sealed record Def(string Kind, string Name, string Path, string? ItemName, string? Handle);

    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
    {
        "PDFDEFINITION", "PDFUNDERLAYDEFINITION",
        "DWFDEFINITION", "DWFUNDERLAYDEFINITION",
        "DGNDEFINITION", "DGNUNDERLAYDEFINITION",
        "IMAGEDEF", "IMAGEDEFINITION",
    };

    public static IReadOnlyList<Def> Read(string dxfText) => Read(new StringReader(dxfText));

    public static IReadOnlyList<Def> Read(TextReader reader)
    {
        // DXF is alternating group-code / value lines; the code line is a right-justified integer.
        var pairs = new List<(int Code, string Value)>();
        while (reader.ReadLine() is { } codeLine)
        {
            var value = reader.ReadLine() ?? "";
            if (!int.TryParse(codeLine.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            {
                continue; // not a group-code line — skip it alone so pairing stays aligned
            }

            pairs.Add((code, value));
        }

        // Group pairs into records; every record starts at code 0 (the entity/object name).
        var records = new List<(string Type, List<(int Code, string Value)> Groups)>();
        List<(int Code, string Value)>? current = null;
        foreach (var pair in pairs)
        {
            if (pair.Code == 0)
            {
                current = new List<(int Code, string Value)>();
                records.Add((pair.Value.Trim(), current));
            }
            else
            {
                current?.Add(pair);
            }
        }

        // Pass 1: DICTIONARY entry name per child handle (3 = entry name, 350 = child handle).
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        string? section = null;
        foreach (var (type, groups) in records)
        {
            section = TrackSection(section, type, groups);
            if (section != "OBJECTS" || type != "DICTIONARY")
            {
                continue;
            }

            string? entryName = null;
            foreach (var (code, value) in groups)
            {
                if (code == 3)
                {
                    entryName = value;
                }
                else if (code == 350 && entryName is not null)
                {
                    names[value.Trim()] = entryName;
                    entryName = null;
                }
            }
        }

        // Pass 2: the underlay definitions themselves.
        var result = new List<Def>();
        section = null;
        foreach (var (type, groups) in records)
        {
            section = TrackSection(section, type, groups);
            if (section != "OBJECTS" || !Kinds.Contains(type))
            {
                continue;
            }

            string? handle = null, path = null, item = null;
            foreach (var (code, value) in groups)
            {
                switch (code)
                {
                    case 5 when handle is null:
                        handle = value.Trim();
                        break;
                    case 1 when path is null:
                        path = value;
                        break;
                    case 2 when item is null:
                        item = value;
                        break;
                }
            }

            var kind = type.StartsWith("PDF", StringComparison.Ordinal) ? "pdf"
                : type.StartsWith("DWF", StringComparison.Ordinal) ? "dwf"
                : type.StartsWith("DGN", StringComparison.Ordinal) ? "dgn"
                : "image";
            var name = (handle is not null && names.TryGetValue(handle, out var dictName) ? dictName : null)
                       ?? (string.IsNullOrWhiteSpace(item) ? null : item)
                       ?? handle
                       ?? "";
            result.Add(new Def(kind, name, path ?? "", item, handle));
        }

        return result;
    }

    private static string? TrackSection(string? section, string type, List<(int Code, string Value)> groups) =>
        type switch
        {
            "SECTION" => groups.FirstOrDefault(g => g.Code == 2).Value?.Trim().ToUpperInvariant(),
            "ENDSEC" => null,
            _ => section,
        };
}
