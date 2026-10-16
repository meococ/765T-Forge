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
        ArgumentException.ThrowIfNullOrWhiteSpace(dwgPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (layouts is null || layouts.Count == 0)
        {
            throw new ArgumentException("At least one layout is required.", nameof(layouts));
        }

        var outputDir = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            outputDir = ".";
        }

        var sb = new StringBuilder();
        sb.AppendLine("[DWF6Version]");
        sb.AppendLine("Ver=1");
        sb.AppendLine("[DWF6MinorVersion]");
        sb.AppendLine("MinorVer=1");
        foreach (var layout in layouts)
        {
            sb.AppendLine($"[DWF6Sheet:{layout}]");
            sb.AppendLine($"Dwg={dwgPath}");
            sb.AppendLine($"Layout={layout}");
            sb.AppendLine("Setup=");
            sb.AppendLine($"OriginalSheetPath={dwgPath}");
            sb.AppendLine("Has Plot Port=0");
            sb.AppendLine("Has3DDWF=0");
        }

        sb.AppendLine("[Target]");
        // AutoCAD SheetType enum: Type=6 = MultiPdf (one multi-sheet PDF), Type=5 = SinglePdf (one PDF per layout).
        sb.AppendLine(singlePdf ? "Type=6" : "Type=5");
        sb.AppendLine($"DWF={outputPath}");
        sb.AppendLine($"OUT={outputDir}");
        sb.AppendLine($"Path={outputPath}");
        sb.AppendLine("PWD=");
        sb.AppendLine("[SheetSet Properties]");
        sb.AppendLine("IsSheetSet=FALSE");
        sb.AppendLine("IsHomogeneous=FALSE");
        sb.AppendLine("SheetSet Name=765T-Forge");
        sb.AppendLine("NoOfCopies=1");
        sb.AppendLine("PlotStampOn=FALSE");
        sb.AppendLine("ViewFile=FALSE");
        sb.AppendLine("JobID=0");
        sb.AppendLine("SelectionSetName=");
        sb.AppendLine("AcadProfile=");
        sb.AppendLine("CategoryName=");
        sb.AppendLine("LogFilePath=");
        sb.AppendLine("IncludeLayer=TRUE");
        sb.AppendLine("LineMerge=FALSE");
        sb.AppendLine("CurrentPrecision=");
        sb.AppendLine("PromptForDwfName=FALSE");
        sb.AppendLine("PwdProtectPublishedDWF=FALSE");
        sb.AppendLine("PromptForPassword=FALSE");
        sb.AppendLine("RepublishingMarkups=FALSE");
        sb.AppendLine("DSTPath=");
        sb.AppendLine("PublishSheetSetMetadata=FALSE");
        sb.AppendLine("3DDWFOptions=0 0");

        return sb.ToString();
    }

    public static void WriteFile(string dsdPath, string dwgPath, string outputPath, IReadOnlyList<string> layouts, bool singlePdf)
    {
        var text = Build(dwgPath, outputPath, layouts, singlePdf);
        File.WriteAllText(dsdPath, text, Encoding.Unicode);
    }

    /// <summary>
    /// Returns all candidate PDF paths that AutoCAD Publisher may write so overwrite-ack checks
    /// can refuse before running when singlePdf=false (Type=5 writes per-sheet PDFs in OUT=).
    /// </summary>
    public static string[] GetCandidateOutputPaths(string dwgPath, string outputPath, IReadOnlyList<string> layouts, bool singlePdf)
    {
        var fullOut = Path.GetFullPath(outputPath);
        if (singlePdf)
        {
            return [fullOut];
        }

        var outDir = Path.GetDirectoryName(fullOut);
        if (string.IsNullOrWhiteSpace(outDir))
        {
            outDir = ".";
        }

        var dwgStem = Path.GetFileNameWithoutExtension(dwgPath);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { fullOut };
        foreach (var layout in layouts)
        {
            set.Add(Path.Combine(outDir, $"{layout}.pdf"));
            if (!string.IsNullOrWhiteSpace(dwgStem))
            {
                set.Add(Path.Combine(outDir, $"{dwgStem}-{layout}.pdf"));
            }
        }

        return set.ToArray();
    }

    /// <summary>
    /// Resolves the actual per-layout PDF file produced in OUT= for Type=5 (singlePdf=false).
    /// Checks &lt;layout&gt;.pdf (from [DWF6Sheet:&lt;layout&gt;]), &lt;dwgStem&gt;-&lt;layout&gt;.pdf, and (when 1 layout) outputPath.
    /// </summary>
    public static string? ResolveSheetOutputPath(string dwgPath, string outputPath, string layout, int totalLayouts)
    {
        var fullOut = Path.GetFullPath(outputPath);
        var outDir = Path.GetDirectoryName(fullOut);
        if (string.IsNullOrWhiteSpace(outDir))
        {
            outDir = ".";
        }

        var bySheetName = Path.Combine(outDir, $"{layout}.pdf");
        if (File.Exists(bySheetName))
        {
            return bySheetName;
        }

        var dwgStem = Path.GetFileNameWithoutExtension(dwgPath);
        if (!string.IsNullOrWhiteSpace(dwgStem))
        {
            var byDwgLayout = Path.Combine(outDir, $"{dwgStem}-{layout}.pdf");
            if (File.Exists(byDwgLayout))
            {
                return byDwgLayout;
            }
        }

        if (totalLayouts == 1 && File.Exists(fullOut))
        {
            return fullOut;
        }

        return null;
    }
}
