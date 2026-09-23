using Forge.Shared;

namespace Forge.Tests;

public sealed class DsdWriterTests
{
    [Fact]
    public void BuildEmitsMultiSheetTypeAndLayouts()
    {
        var text = DsdWriter.Build(@"C:\lab\host.dwg", @"C:\out\set.pdf", ["A101", "A102"], singlePdf: true);

        Assert.Contains("Type=6", text);
        Assert.Contains("[DWF6Sheet:A101]", text);
        Assert.Contains("[DWF6Sheet:A102]", text);
        Assert.Contains(@"Dwg=C:\lab\host.dwg", text);
        Assert.Contains(@"Path=C:\out\set.pdf", text);
    }

    [Fact]
    public void BuildRequiresLayouts()
    {
        Assert.Throws<ArgumentException>(() => DsdWriter.Build("a.dwg", "b.pdf", Array.Empty<string>(), true));
    }

    [Fact]
    public void WriteFileUsesUnicode()
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
