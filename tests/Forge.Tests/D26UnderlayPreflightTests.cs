using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// TOOL-GAP #26: the plot must refuse to run while a LOADED underlay's file is missing
/// (a PDF-only sheet issues blank otherwise — V-101 plotted without its underlay and the
/// probe still passed). Pure path/sniff rules here; source guards pin the plugin call sites.
/// </summary>
public sealed class D26UnderlayPreflightTests
{
    [Fact]
    public void AbsolutePath_PassesThrough()
    {
        var p = @"D:\site\pdf\sheet1.pdf";
        Assert.Equal(p, UnderlayResolution.ResolveCandidatePath(p, @"D:\drawings"));
    }

    [Fact]
    public void UncPath_PassesThrough()
    {
        var p = @"\\server\share\pdf\sheet1.pdf";
        Assert.Equal(p, UnderlayResolution.ResolveCandidatePath(p, @"D:\drawings"));
    }

    [Fact]
    public void RelativePath_JoinsDrawingFolder()
    {
        Assert.Equal(
            @"D:\drawings\work\pdf-source\sheet1.pdf",
            UnderlayResolution.ResolveCandidatePath(@"work\pdf-source\sheet1.pdf", @"D:\drawings"));
    }

    [Fact]
    public void DotAndDotDot_Resolve()
    {
        Assert.Equal(
            @"D:\drawings\sheet1.pdf",
            UnderlayResolution.ResolveCandidatePath(@".\sheet1.pdf", @"D:\drawings"));
        Assert.Equal(
            @"D:\drawings\pdfs\sheet1.pdf",
            UnderlayResolution.ResolveCandidatePath(@"..\pdfs\sheet1.pdf", @"D:\drawings\sub"));
    }

    [Fact]
    public void ForwardSlashes_ArePathSeparators()
    {
        Assert.Equal(
            @"D:\drawings\pdf\sheet1.pdf",
            UnderlayResolution.ResolveCandidatePath("pdf/sheet1.pdf", @"D:\drawings"));
    }

    [Fact]
    public void MissingInputs_AreUnresolvable()
    {
        Assert.Null(UnderlayResolution.ResolveCandidatePath(null, @"D:\drawings"));
        Assert.Null(UnderlayResolution.ResolveCandidatePath("  ", @"D:\drawings"));
        Assert.Null(UnderlayResolution.ResolveCandidatePath(@"rel\file.pdf", null));
    }

    [Fact]
    public void MissingUnderlays_ReportsOnlyUnresolvable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "d26-" + Guid.NewGuid().ToString("N"));
        var exists = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(dir, "a.pdf") };
        var missing = UnderlayResolution.MissingUnderlays(
            [
                new UnderlayFileRef("pdf", "Page 1", "a.pdf"),
                new UnderlayFileRef("pdf", "Page 2", "b.pdf"),
            ],
            dir,
            exists.Contains);

        var single = Assert.Single(missing);
        Assert.Equal("Page 2", single.Name);
        Assert.Equal("b.pdf", single.Path);
    }

    [Fact]
    public void DxfSniff_FindsUnderlayDefinitionsInObjects()
    {
        const string dxf =
            "  0\nSECTION\n  2\nOBJECTS\n"
            + "  0\nDICTIONARY\n  5\nC\n330\n0\n100\nAcDbDictionary\n281\n1\n  3\nACAD_PDFDEFINITIONS\n350\nD\n  3\nACAD_IMAGE_DICT\n350\nF\n"
            + "  0\nDICTIONARY\n  5\nD\n330\nC\n100\nAcDbDictionary\n281\n1\n  3\nSheet 1\n350\nE\n"
            + "  0\nDICTIONARY\n  5\nF\n330\nC\n100\nAcDbDictionary\n281\n1\n  3\nImage A\n350\nG\n"
            + "  0\nPDFDEFINITION\n  5\nE\n330\nD\n100\nAcDbUnderlayDefinition\n  1\nwork\\pdf-source\\sheet1.pdf\n  2\nSheet 1\n"
            + "  0\nIMAGEDEF\n  5\nG\n330\nF\n100\nAcDbRasterImageDef\n 90\n0\n  1\nD:\\imgs\\a.tif\n  2\nImage A\n"
            + "  0\nENDSEC\n  0\nEOF\n";

        var defs = DxfUnderlaySniff.Read(dxf);

        Assert.Equal(2, defs.Count);
        Assert.Equal("pdf", defs[0].Kind);
        Assert.Equal("Sheet 1", defs[0].Name);
        Assert.Equal(@"work\pdf-source\sheet1.pdf", defs[0].Path);
        Assert.Equal("image", defs[1].Kind);
        Assert.Equal("Image A", defs[1].Name);
        Assert.Equal(@"D:\imgs\a.tif", defs[1].Path);
    }

    [Fact]
    public void DxfSniff_IgnoresNonObjectSections()
    {
        const string dxf =
            "  0\nSECTION\n  2\nENTITIES\n  0\nPDFDEFINITION\n  5\nE\n  1\nx.pdf\n  0\nENDSEC\n  0\nEOF\n";

        Assert.Empty(DxfUnderlaySniff.Read(dxf));
    }
}

public sealed class D26UnderlayPreflightSourceGuards
{
    private static string PluginSource(string file) =>
        File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), file));

    [Fact]
    public void PlotPreflightEnumeratesAllUnderlayKinds()
    {
        var source = PluginSource("PluginCommandProcessor.Plot.cs");
        foreach (var dict in new[] { "ACAD_PDFDEFINITIONS", "ACAD_DWFDEFINITIONS", "ACAD_DGNDEFINITIONS", "ACAD_IMAGE_DICT" })
        {
            Assert.Contains($"\"{dict}\"", source, StringComparison.Ordinal);
        }

        Assert.Contains("plot_underlay_unresolved", source, StringComparison.Ordinal);
        Assert.Contains("EnumerateUnderlayDefinitions", source, StringComparison.Ordinal);
        Assert.Contains("IsLoaded", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UnderlayDefinitionId_NeverSerializes()
    {
        // Review round 2 F1: a live ObjectId in a ForgeResult payload kills System.Text.Json
        // with a reference-cycle exception — including the plot_underlay_unresolved failure
        // itself. UnderlayEntry.DefinitionId must stay JsonIgnore'd.
        var source = PluginSource("PluginCommandProcessor.Plot.cs");
        Assert.Contains("JsonIgnore] ObjectId DefinitionId", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishPathRunsTheSamePreflight()
    {
        var source = PluginSource("PluginCommandProcessor.cs");
        Assert.Contains("EnumerateUnderlayDefinitions", source, StringComparison.Ordinal);
        Assert.Contains("UnderlayPreflightFailure", source, StringComparison.Ordinal);
    }
}
