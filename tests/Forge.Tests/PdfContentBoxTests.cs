using System.IO.Compression;
using System.Text;
using Forge.Shared;

namespace Forge.Tests;

public sealed class PdfContentBoxTests
{
    private static readonly double[] Sheet = [0, 0, 1000, 1000];

    // ---- Real AutoCAD 2025 "DWG To PDF" output ----

    [Fact]
    public void StaleExtentsFixture_IsTinyPatchNearRightEdge()
    {
        var r = PdfContentBox.Read(FindFixture("plot-r1-model-stale-extents.pdf"));

        Assert.Equal(PdfInkStatus.Ok, r.Status);
        Assert.Equal(new double[] { 0, 0, 2384, 1684 }, r.MediaBox);
        Near(0.0133, r.FillX, 0.003);
        Near(0.0357, r.FillY, 0.003);
        Assert.True(r.LooksTiny);
        Assert.InRange(r.InkBox![0], 2300, 2384);
    }

    [Fact]
    public void WindowA3Fixture_IsNotTiny()
    {
        var r = PdfContentBox.Read(FindFixture("plot-r1-window-a3.pdf"));

        Assert.Equal(PdfInkStatus.Ok, r.Status);
        Assert.Equal(new double[] { 0, 0, 1191, 842 }, r.MediaBox);
        Near(0.5911, r.FillX, 0.003);
        Near(0.4427, r.FillY, 0.003);
        Assert.False(r.LooksTiny);
    }

    // ---- Content streams ----

    [Fact]
    public void ClipPath_IsNotInk()
    {
        var r = Content("0 0 1000 1000 re W n 10 10 m 20 20 l S");

        AssertBox(r, 9.5, 9.5, 20.5, 20.5);
        Assert.True(r.LooksTiny);
    }

    [Fact]
    public void SaveRestoreAndCm_UnionBothLines()
    {
        // The 2x CTM doubles the half line width to 1; the second line sticks out to -0.5 and is clipped to the sheet.
        var r = Content("q 2 0 0 2 100 100 cm 0 0 m 10 10 l S Q 0 0 m 1 1 l S");

        AssertBox(r, 0, 0, 121, 121);
    }

    [Fact]
    public void FilledRectangle_HasNoStrokeExpansion()
    {
        AssertBox(Content("100 200 300 400 re f"), 100, 200, 400, 600);
    }

    [Fact]
    public void LineWidth_ExpandsByHalfWidth()
    {
        AssertBox(Content("10 w 100 100 m 200 100 l S"), 95, 95, 205, 105);
    }

    [Fact]
    public void SquareCapsAndMiterJoins_ExpandFurther()
    {
        double half = 5 * Math.Sqrt(2);
        AssertBox(Content("10 w 2 J 100 100 m 200 100 l S"), 100 - half, 100 - half, 200 + half, 100 + half);
        AssertBox(Content("10 w 0 j 4 M 100 100 m 200 100 l 200 200 l S"), 80, 80, 220, 220);
        AssertBox(Content("10 w 1 j 100 100 m 200 100 l 200 200 l S"), 95, 95, 205, 205);
    }

    [Fact]
    public void Text_BoxIsOneEmPerByte()
    {
        AssertBox(Content("BT /F1 10 Tf 1 0 0 1 100 100 Tm (ABC) Tj ET"), 100, 97.5, 130, 110);
    }

    [Fact]
    public void Text_SuccessiveShowsAndKerningAccumulate()
    {
        // (AB) reaches 120; C 130; -1000 moves the pen 10 right; D reaches 150.
        AssertBox(Content("BT /F1 10 Tf 100 100 Td (AB) Tj [(C) -1000 (D)] TJ ET"), 100, 97.5, 150, 110);
    }

    [Fact]
    public void FormXObject_BBoxThroughMatrixAndCtm()
    {
        var xobjects = new Dictionary<string, PdfXObjectInfo>
        {
            ["Fm1"] = new(false, [0, 0, 50, 50], [2, 0, 0, 2, 10, 10]),
        };

        AssertBox(Content("/Fm1 Do", xobjects), 10, 10, 110, 110);
    }

    [Fact]
    public void ImageXObject_IsUnitSquareThroughCtm()
    {
        var xobjects = new Dictionary<string, PdfXObjectInfo> { ["Im1"] = new(true, null, null) };

        AssertBox(Content("q 200 0 0 100 50 60 cm /Im1 Do Q", xobjects), 50, 60, 250, 160);
    }

    [Fact]
    public void InlineImage_SkipsDataThatLooksLikeEI()
    {
        // 2x2 gray, 8 bits: 4 data bytes, which here spell "\nEI ".
        var stream = Concat(
            Encoding.ASCII.GetBytes("q 30 0 0 40 5 6 cm BI /W 2 /H 2 /BPC 8 /CS /G ID "),
            [0x0A, (byte)'E', (byte)'I', 0x20],
            Encoding.ASCII.GetBytes(" EI Q"));

        AssertBox(PdfContentBox.ReadContent(stream, Sheet), 5, 6, 35, 46);
    }

    [Fact]
    public void InlineImage_WithFilter_FindsEI()
    {
        AssertBox(Content("q 30 0 0 40 5 6 cm BI /W 2 /H 2 /BPC 8 /CS /G /F /AHx ID 00FF00FF> EI Q"), 5, 6, 35, 46);
    }

    [Fact]
    public void Shading_IsUnavailable()
    {
        var r = Content("10 10 m 20 20 l S /Sh1 sh");

        Assert.Equal(PdfInkStatus.Unavailable, r.Status);
        Assert.Contains("sh", r.Reason);
        Assert.Null(r.InkBox);
        Assert.Null(r.LooksTiny);
    }

    [Fact]
    public void UnknownOperator_IsUnavailable()
    {
        var r = Content("10 10 m 20 20 l S 1 2 zz");

        Assert.Equal(PdfInkStatus.Unavailable, r.Status);
        Assert.Contains("'zz'", r.Reason);
    }

    [Fact]
    public void MissingXObject_IsUnavailable()
    {
        var r = Content("/Im9 Do");

        Assert.Equal(PdfInkStatus.Unavailable, r.Status);
        Assert.Contains("Im9", r.Reason);
    }

    [Fact]
    public void WrongOperandCount_IsUnavailable()
    {
        Assert.Equal(PdfInkStatus.Unavailable, Content("1 2 3 m 4 5 l S").Status);
    }

    [Fact]
    public void KnownNonPaintingOperators_DoNotBlock()
    {
        const string noOps =
            "0 J 0 j 10 M [3 2] 0 d /RI ri 1 i /GS1 gs /CS0 CS /CS0 cs 0.5 SC 0.5 SCN 0.5 sc /P1 scn "
            + "0 G 0 g 1 0 0 RG 1 0 0 rg 0 0 0 1 K 0 0 0 1 k /Tag BMC /Tag <</MCID 0>> BDC EMC "
            + "/Tag MP /Tag <</A 1>> DP BX EX 1000 0 d0 1000 0 0 0 750 750 d1 % comment\n";

        AssertBox(Content(noOps + "100 100 m 200 200 l S"), 99.5, 99.5, 200.5, 200.5);
    }

    [Fact]
    public void NothingPainted_IsEmpty()
    {
        foreach (var stream in new[] { "", "0 0 1000 1000 re W n", "q 1 0 0 1 5 5 cm Q BT ET" })
        {
            var r = Content(stream);

            Assert.Equal(PdfInkStatus.Empty, r.Status);
            Assert.Null(r.InkBox);
            Assert.Equal(0, r.FillX);
            Assert.Equal(0, r.FillY);
            Assert.True(r.LooksTiny);
        }
    }

    [Fact]
    public void InkOnlyOffTheSheet_IsEmpty()
    {
        Assert.Equal(PdfInkStatus.Empty, Content("2000 2000 m 2100 2100 l S").Status);
    }

    [Fact]
    public void LargeDrawing_IsNotTiny()
    {
        var r = Content("10 10 m 990 10 l 990 600 l S");

        Assert.Equal(PdfInkStatus.Ok, r.Status);
        Assert.False(r.LooksTiny);
    }

    // ---- Whole PDF files ----

    [Fact]
    public void Pdf_InheritedMediaBox_StrokedLine()
    {
        var r = PdfContentBox.Read(BuildPdf(Encoding.ASCII.GetBytes("10 w 100 100 m 300 400 l S")));

        Assert.Equal(new double[] { 0, 0, 595, 842 }, r.MediaBox);
        AssertBox(r, 95, 95, 305, 405);
        Near(210.0 / 595, r.FillX, 1e-9);
        Near(310.0 / 842, r.FillY, 1e-9);
    }

    [Fact]
    public void Pdf_FlateContent_IsDecoded()
    {
        var r = PdfContentBox.Read(BuildPdf(Deflate("100 100 m 300 400 l S"), "/Filter /FlateDecode"));

        AssertBox(r, 99.5, 99.5, 300.5, 400.5);
    }

    [Fact]
    public void Pdf_LzwFilter_IsUnavailable()
    {
        var r = PdfContentBox.Read(BuildPdf(Encoding.ASCII.GetBytes("100 100 m 300 400 l S"), "/Filter /LZWDecode"));

        Assert.Equal(PdfInkStatus.Unavailable, r.Status);
        Assert.Contains("LZWDecode", r.Reason);
        Assert.Equal(new double[] { 0, 0, 595, 842 }, r.MediaBox);
    }

    [Fact]
    public void Pdf_TruncatedFlate_IsUnavailable()
    {
        var text = new StringBuilder();
        for (int i = 0; i < 2000; i++)
        {
            text.Append(i % 500).Append(' ').Append(i * 7 % 800).Append(" m ").Append(i * 13 % 590).Append(" 800 l S\n");
        }

        var full = Deflate(text.ToString());
        var r = PdfContentBox.Read(BuildPdf(full[..(full.Length / 2)], "/Filter /FlateDecode"));

        Assert.Equal(PdfInkStatus.Unavailable, r.Status);
    }

    [Fact]
    public void Pdf_IndirectLength_IsResolved()
    {
        // The data holds "endstream" inside a string, so only the /Length finds the real end.
        var content = Encoding.ASCII.GetBytes("100 100 m 300 400 l S BT /F1 10 Tf 400 700 Td (\nendstream) Tj ET");

        var r = PdfContentBox.Read(BuildPdf(content, indirectLength: true));

        AssertBox(r, 99.5, 99.5, 500, 710);
    }

    [Fact]
    public void Pdf_ResourcesResolveXObjectsAndExtGState()
    {
        const string resources = "<< /XObject << /Fm1 5 0 R /Im1 6 0 R >> /ExtGState << /G1 << /LW 20 >> >> >>";
        var content = Encoding.ASCII.GetBytes("/Fm1 Do q 100 0 0 50 300 300 cm /Im1 Do Q /G1 gs 400 500 m 500 500 l S");

        var r = PdfContentBox.Read(BuildPdf(
            content,
            resources: resources,
            extraObjects:
            [
                "<< /Type /XObject /Subtype /Form /BBox [0 0 50 50] /Matrix [2 0 0 2 10 10] /Length 0 >>\nstream\n\nendstream",
                "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceGray /Length 1 >>\nstream\nA\nendstream",
            ]));

        AssertBox(r, 10, 10, 510, 510);
    }

    [Fact]
    public void Pdf_Type3FontText_IsUnavailable()
    {
        const string resources = "<< /Font << /F1 << /Type /Font /Subtype /Type3 >> >> >>";

        var r = PdfContentBox.Read(BuildPdf(Encoding.ASCII.GetBytes("BT /F1 10 Tf 100 100 Td (A) Tj ET"), resources: resources));

        Assert.Equal(PdfInkStatus.Unavailable, r.Status);
        Assert.Contains("Type3", r.Reason);
    }

    [Fact]
    public void Pdf_PrintableAnnotationCounts_NonPrintingDoesNot()
    {
        const string annots =
            "/Annots [<< /Type /Annot /Subtype /Square /Rect [550 700 500 800] /F 4 >> "
            + "<< /Type /Annot /Subtype /Square /Rect [0 0 10 10] /F 64 >>]";

        var r = PdfContentBox.Read(BuildPdf(Encoding.ASCII.GetBytes("10 w 100 100 m 300 400 l S"), pageExtra: annots));

        AssertBox(r, 95, 95, 550, 800);
    }

    [Fact]
    public void Pdf_PageInsideObjectStream_IsUnavailable()
    {
        const string pdf =
            "%PDF-1.5\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"
            + "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>\nendobj\n"
            + "5 0 obj\n<< /Type /ObjStm /N 1 /First 4 /Length 4 >>\nstream\n3 0 \nendstream\nendobj\n"
            + "trailer\n<< /Size 6 /Root 1 0 R >>\n%%EOF\n";

        var r = PdfContentBox.Read(Encoding.ASCII.GetBytes(pdf));

        Assert.Equal(PdfInkStatus.Unavailable, r.Status);
        Assert.Contains("object stream", r.Reason);
    }

    [Fact]
    public void BadInput_IsUnavailableAndDoesNotThrow()
    {
        Assert.Equal(PdfInkStatus.Unavailable, PdfContentBox.Read(Array.Empty<byte>()).Status);
        Assert.Equal(PdfInkStatus.Unavailable, PdfContentBox.Read(Encoding.ASCII.GetBytes("not a pdf at all")).Status);
        Assert.Equal(PdfInkStatus.Unavailable, PdfContentBox.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf")).Status);
        Assert.Equal(PdfInkStatus.Unavailable, PdfContentBox.ReadContent(Encoding.ASCII.GetBytes("0 0 m"), [0, 0, 0, 0]).Status);
    }

    // ---- Helpers ----

    private static PdfInkResult Content(string stream, IReadOnlyDictionary<string, PdfXObjectInfo>? xobjects = null) =>
        PdfContentBox.ReadContent(Encoding.Latin1.GetBytes(stream), Sheet, xobjects);

    private static void AssertBox(PdfInkResult r, double x0, double y0, double x1, double y1)
    {
        Assert.True(r.Status == PdfInkStatus.Ok, $"status {r.Status}: {r.Reason}");
        Assert.NotNull(r.InkBox);
        Near(x0, r.InkBox![0], 1e-9);
        Near(y0, r.InkBox[1], 1e-9);
        Near(x1, r.InkBox[2], 1e-9);
        Near(y1, r.InkBox[3], 1e-9);
    }

    private static void Near(double expected, double? actual, double tolerance)
    {
        Assert.NotNull(actual);
        Assert.InRange(actual!.Value, expected - tolerance, expected + tolerance);
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static byte[] Deflate(string text)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(Encoding.ASCII.GetBytes(text));
        }

        return output.ToArray();
    }

    /// <summary>
    /// One-page PDF; the MediaBox [0 0 595 842] sits on /Pages so the page inherits it. The content
    /// stream is object 4; <paramref name="extraObjects"/> become objects 5, 6, ... With
    /// <paramref name="indirectLength"/> the stream /Length is a reference to one more object at the end.
    /// </summary>
    private static byte[] BuildPdf(
        byte[] content,
        string streamDictExtra = "",
        string pageExtra = "",
        string resources = "<< >>",
        bool indirectLength = false,
        params string[] extraObjects)
    {
        using var pdf = new MemoryStream();
        var offsets = new List<long>();
        void Write(string text) => pdf.Write(Encoding.ASCII.GetBytes(text));
        void AddObject(string body)
        {
            offsets.Add(pdf.Length);
            Write($"{offsets.Count} 0 obj\n{body}\nendobj\n");
        }

        int lengthObject = 5 + extraObjects.Length;
        Write("%PDF-1.4\n");
        AddObject("<< /Type /Catalog /Pages 2 0 R >>");
        AddObject("<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 595 842] >>");
        AddObject($"<< /Type /Page /Parent 2 0 R /Resources {resources} /Contents 4 0 R {pageExtra} >>");
        offsets.Add(pdf.Length);
        Write($"4 0 obj\n<< /Length {(indirectLength ? $"{lengthObject} 0 R" : content.Length)} {streamDictExtra} >>\nstream\n");
        pdf.Write(content);
        Write("\nendstream\nendobj\n");
        foreach (var body in extraObjects)
        {
            AddObject(body);
        }

        if (indirectLength)
        {
            AddObject(content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        long xref = pdf.Length;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write($"{offset:D10} 00000 n \n");
        }

        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return pdf.ToArray();
    }

    private static string FindFixture(string name)
    {
        var relative = Path.Combine("tests", "Forge.Tests", "Fixtures", name);
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Fixture not found above " + AppContext.BaseDirectory, relative);
    }
}
