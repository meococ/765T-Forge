using Forge.Shared;

namespace Forge.Tests;

public sealed class DsdWriterTests
{
    [Fact]
    public void Build_EmitsMultiSheetTypeAndLayouts()
    {
        var text = DsdWriter.Build(@"C:\lab\host.dwg", @"C:\out\set.pdf", ["A101", "A102"], singlePdf: true);

        Assert.Contains("Type=6", text);
        Assert.Contains("[DWF6Sheet:A101]", text);
        Assert.Contains("[DWF6Sheet:A102]", text);
        Assert.Contains(@"Dwg=C:\lab\host.dwg", text);
        Assert.Contains(@"DWF=C:\out\set.pdf", text);
        Assert.Contains(@"OUT=C:\out", text);
        Assert.Contains("[SheetSet Properties]", text);

        var multiFileText = DsdWriter.Build(@"C:\lab\host.dwg", @"C:\out\set.pdf", ["A101"], singlePdf: false);
        Assert.Contains("Type=5", multiFileText);
    }

    [Fact]
    public void Build_RequiresLayouts()
    {
        Assert.Throws<ArgumentException>(() => DsdWriter.Build("a.dwg", "b.pdf", Array.Empty<string>(), true));
    }

    [Fact]
    public void WriteFile_UsesUnicode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge-dsd-{Guid.NewGuid():N}.dsd");
        try
        {
            DsdWriter.WriteFile(path, "host.dwg", "out.pdf", ["L1"], true);
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length >= 2);
            // UTF-16 LE BOM
            Assert.Equal(0xFF, bytes[0]);
            Assert.Equal(0xFE, bytes[1]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
