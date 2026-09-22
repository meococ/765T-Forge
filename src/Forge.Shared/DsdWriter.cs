using System.Text;

namespace Forge.Shared;

/// <summary>
/// Builds AutoCAD Drawing Set Description (DSD) text for Publisher.PublishDsd.
/// Kept in Shared so unit tests can assert format without AutoCAD.
/// </summary>
public static class DsdWriter
{
    public static string Build(string dwgPath, string outputPath, IReadOnlyList<string> layouts, bool singlePdf)
    {
        BclCompat.ThrowIfNullOrWhiteSpace(dwgPath, nameof(dwgPath));
        BclCompat.ThrowIfNullOrWhiteSpace(outputPath, nameof(outputPath));
        if (layouts is null || layouts.Count == 0)
        {
            throw new ArgumentException("At least one layout is required.", nameof(layouts));
        }

        var sb = new StringBuilder();
        sb.AppendLine("[DWF6Version]");
        sb.AppendLine("Ver=1");
        sb.AppendLine("[DWF6MinorVersion]");
        sb.AppendLine("MinorVer=1");
        sb.AppendLine("[Target]");
        // Type 6 = multi-sheet PDF; Type 7 = single-sheet PDF per layout (AutoCAD DSD convention).
        sb.AppendLine(singlePdf ? "Type=6" : "Type=7");
        sb.AppendLine($"Path={outputPath}");
        sb.AppendLine("PWD=");
        sb.AppendLine("[SheetSet]");
        sb.AppendLine("PromptForDwfName=FALSE");
        sb.AppendLine("IsHomogeneous=TRUE");
        sb.AppendLine("SheetSetName=765T-Forge");
        sb.AppendLine("NoOfCopies=1");
        sb.AppendLine("PlotStampOn=FALSE");
        sb.AppendLine("Viewports=FALSE");

        foreach (var layout in layouts)
        {
            sb.AppendLine($"[DWF6Sheet:{layout}]");
            sb.AppendLine($"Dwg={dwgPath}");
            sb.AppendLine($"Layout={layout}");
            sb.AppendLine($"Title={layout}");
            sb.AppendLine($"OriginalSheetPath={dwgPath}");
        }

        return sb.ToString();
    }

    public static void WriteFile(string dsdPath, string dwgPath, string outputPath, IReadOnlyList<string> layouts, bool singlePdf)
    {
        var text = Build(dwgPath, outputPath, layouts, singlePdf);
        File.WriteAllText(dsdPath, text, Encoding.Unicode);
    }
}
