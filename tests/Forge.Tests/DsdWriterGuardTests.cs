using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// Structural DSD validation: the DSD format has no escape sequence, so a layout or path
/// containing the section delimiters or key/value separator must be rejected, not escaped.
/// </summary>
public sealed class DsdWriterGuardTests
{
    [Theory]
    [InlineData("A]101")]
    [InlineData("A[101")]
    [InlineData("A=101")]
    [InlineData("A101\r\n[DWF6Sheet:Injected]")]
    [InlineData("A101\nInjected")]
    [InlineData("A101\ttab")]
    public void BuildRejectsStructuralAndControlCharactersInLayout(string layout)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => DsdWriter.Build(@"C:\lab\host.dwg", @"C:\out\set.pdf", [layout], singlePdf: true));
        Assert.Contains("layouts[0]", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\out\set]2.pdf")]
    [InlineData(@"C:\out\set=2.pdf")]
    [InlineData("C:\\out\\set\n2.pdf")]
    public void BuildRejectsStructuralAndControlCharactersInOutputPath(string outputPath)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => DsdWriter.Build(@"C:\lab\host.dwg", outputPath, ["A101"], singlePdf: true));
        Assert.Contains("outputPath", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRejectsStructuralCharactersInDwgPath()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => DsdWriter.Build(@"C:\lab\[host].dwg", @"C:\out\set.pdf", ["A101"], singlePdf: true));
        Assert.Contains("dwgPath", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateFieldIsExactAndNullTolerant()
    {
        DsdWriter.ValidateField("field", null);
        DsdWriter.ValidateField("field", "");
        DsdWriter.ValidateField("field", @"C:\out\set.pdf");

        Assert.Throws<ArgumentException>(() => DsdWriter.ValidateField("field", "]"));
        Assert.Throws<ArgumentException>(() => DsdWriter.ValidateField("field", "["));
        Assert.Throws<ArgumentException>(() => DsdWriter.ValidateField("field", "="));
        Assert.Throws<ArgumentException>(() => DsdWriter.ValidateField("field", "a\rb"));
    }

    [Fact]
    public void BuildStillProducesValidDsdForCleanInput()
    {
        var text = DsdWriter.Build(@"C:\lab\host.dwg", @"C:\out\set.pdf", ["A101"], singlePdf: true);

        Assert.Contains("[DWF6Sheet:A101]", text);
        Assert.Contains("Layout=A101", text);
        Assert.Contains(@"Path=C:\out\set.pdf", text);
    }
}
