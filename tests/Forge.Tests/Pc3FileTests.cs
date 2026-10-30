using System.IO.Compression;
using System.Text;
using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// forge_plot_to_pdf viewer suppression: the PDF driver's PC3 custom property View_New_File=TRUE
/// makes AutoCAD open the PDF in a viewer (an OpenWith dialog hangs the plot on a PC without a PDF
/// app). Forge plots through a clone with the property set to FALSE.
/// </summary>
public sealed class Pc3FileTests
{
    private const string Header = "PIAFILEVERSION_2.0,PC3VER1,compress\r\npmzlibcodec";

    private const string SampleText =
        "meta{\n description=\"\n driver_pathname=\"pdfplot16.hdi\n}\n" +
        "mod{\n media{\n  abilities=\"500005500500505555000005550000\n }\n custom{\n" +
        "  0{\n   name=\"Vector_Resolution\n   value=4800\n  }\n" +
        "  1{\n   name=\"View_New_File\n   value=TRUE\n  }\n" +
        "  2{\n   name=\"Include_Layer\n   value=TRUE\n  }\n }\n}\n";

    [Fact]
    public void HeaderIsTheFortyEightBytePiaPrefix()
    {
        Assert.Equal(48, Header.Length);
    }

    [Fact]
    public void WriteThenReadRoundTripsTextAndHeaderFields()
    {
        var bytes = Pc3File.Write(SampleText, Header);
        Assert.True(Pc3File.TryRead(bytes, out var text, out var header));
        Assert.Equal(SampleText, text);
        Assert.Equal(Header, header);

        var compressed = bytes[60..];
        Assert.Equal(Pc3File.Adler32(compressed), BitConverter.ToUInt32(bytes, 48));
        Assert.Equal((uint)(SampleText.Length + 1), BitConverter.ToUInt32(bytes, 52)); // NUL-terminated body
        Assert.Equal((uint)compressed.Length, BitConverter.ToUInt32(bytes, 56));

        using var z = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
        using var output = new MemoryStream();
        z.CopyTo(output);
        Assert.Equal(0, output.ToArray()[^1]);
    }

    [Fact]
    public void Adler32MatchesReferenceValue()
    {
        // Reference value from the zlib specification example.
        Assert.Equal(0x11E60398u, Pc3File.Adler32(Encoding.ASCII.GetBytes("Wikipedia")));
        Assert.Equal(1u, Pc3File.Adler32([]));
    }

    [Fact]
    public void ViewerPropertyIsReadAndForcedOff()
    {
        Assert.Equal("TRUE", Pc3File.GetCustomValue(SampleText, Pc3File.ViewerProperty));
        Assert.True(Pc3File.OpensViewer(SampleText));

        var clone = Pc3File.SetCustomValue(SampleText, Pc3File.ViewerProperty, "FALSE", out var changed);
        Assert.True(changed);
        Assert.False(Pc3File.OpensViewer(clone));
        Assert.Equal("FALSE", Pc3File.GetCustomValue(clone, Pc3File.ViewerProperty));

        // Only the viewer property changes; neighbouring TRUE values stay.
        Assert.Equal("TRUE", Pc3File.GetCustomValue(clone, "Include_Layer"));
        Assert.Equal(SampleText.Length + 1, clone.Length);

        Pc3File.SetCustomValue(clone, Pc3File.ViewerProperty, "FALSE", out var changedAgain);
        Assert.False(changedAgain);
    }

    [Fact]
    public void ClonedContainerStillReadsBack()
    {
        var clone = Pc3File.SetCustomValue(SampleText, Pc3File.ViewerProperty, "FALSE", out _);
        Assert.True(Pc3File.TryRead(Pc3File.Write(clone, Header), out var text, out _));
        Assert.False(Pc3File.OpensViewer(text));
    }

    [Fact]
    public void MissingPropertyIsReportedAsAbsent()
    {
        const string text = "mod{\n custom{\n  0{\n   name=\"Vector_Resolution\n   value=4800\n  }\n }\n}\n";
        Assert.Null(Pc3File.GetCustomValue(text, Pc3File.ViewerProperty));
        Assert.False(Pc3File.OpensViewer(text));
        Assert.Equal(text, Pc3File.SetCustomValue(text, Pc3File.ViewerProperty, "FALSE", out var changed));
        Assert.False(changed);
    }

    [Fact]
    public void CrLfConfigurationTextIsHandled()
    {
        var crlf = SampleText.Replace("\n", "\r\n");
        Assert.True(Pc3File.OpensViewer(crlf));
        var clone = Pc3File.SetCustomValue(crlf, Pc3File.ViewerProperty, "FALSE", out var changed);
        Assert.True(changed);
        Assert.Equal("FALSE", Pc3File.GetCustomValue(clone, Pc3File.ViewerProperty));
    }

    [Fact]
    public void NonPiaBytesAreRejected()
    {
        Assert.False(Pc3File.TryRead(Encoding.ASCII.GetBytes("not a pc3 file at all, just some text bytes here........"), out _, out _));
        Assert.False(Pc3File.TryRead([], out _, out _));

        // A PIA header over a body that is not a zlib stream is refused, not thrown.
        var bogus = Encoding.ASCII.GetBytes(Header).Concat(new byte[12]).Concat(Encoding.ASCII.GetBytes("plain text, not zlib")).ToArray();
        Assert.False(Pc3File.TryRead(bogus, out _, out _));
    }

    [Fact]
    public void WriteRejectsWrongHeaderLength()
    {
        Assert.Throws<ArgumentException>(() => Pc3File.Write(SampleText, "PIAFILEVERSION_2.0"));
    }

    [Theory]
    [InlineData("DWG To PDF.pc3", "765T-Forge DWG To PDF (no viewer).pc3")]
    [InlineData("DWG To PDF", "765T-Forge DWG To PDF (no viewer).pc3")]
    [InlineData(" AutoCAD PDF (High Quality Print).pc3 ", "765T-Forge AutoCAD PDF (High Quality Print) (no viewer).pc3")]
    public void CloneNameIsDeterministicAndOwned(string device, string expected)
    {
        Assert.Equal(expected, Pc3File.NoViewerCloneName(device));
    }

    /// <summary>
    /// review-wp2-r2 Q3: PrepareNoViewerDevice runs before the media check (required ordering),
    /// so a typed media failure may already have written the clone — the failure data must name
    /// it via pc3Prepared, but ONLY when this call actually created the file.
    /// </summary>
    [Theory]
    [InlineData("clone", "created", @"C:\Plotters\765T-Forge DWG To PDF (no viewer).pc3", @"C:\Plotters\765T-Forge DWG To PDF (no viewer).pc3")]
    [InlineData("clone", "reused", @"C:\Plotters\clone.pc3", null)] // existed already — nothing written
    [InlineData("would_clone", "Dry run: the plot would use the no-viewer clone.", @"C:\Plotters\clone.pc3", null)]
    [InlineData("already_off", null, null, null)]
    [InlineData("property_absent", null, null, null)]
    [InlineData("not_pc3", null, null, null)]
    [InlineData("failed", "cannot write", @"C:\Plotters\clone.pc3", null)] // write failed — nothing to report
    [InlineData("clone", "created", null, null)] // defensive: no path means nothing to name
    public void PreparedClonePathNamesOnlyAFileThisCallWrote(string state, string? note, string? clonePath, string? expected)
    {
        Assert.Equal(expected, Pc3File.PreparedClonePath(state, note, clonePath));
    }
}
