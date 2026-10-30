using Forge.Shared;

namespace Forge.Tests;

/// <summary>forge_doc_activate and the per-call document target share one exact matcher that never suffix-matches.</summary>
public sealed class DocumentMatcherTests
{
    private static readonly string[] Open =
    [
        @"D:\Jobs\Villa\Plan1.dwg",
        @"D:\Jobs\Villa\plan1-rev.dwg",
        @"D:\Jobs\Tower\Plan1.dwg",
        @"D:\Jobs\Tower\Sections.dwg",
        "Drawing1.dwg"
    ];

    [Fact]
    public void FullPathMatchesExactlyOne()
    {
        Assert.Equal(new[] { 2 }, DocumentMatcher.Match(Open, @"D:\Jobs\Tower\Plan1.dwg"));
        Assert.Equal(new[] { 2 }, DocumentMatcher.Match(Open, @"d:/jobs/tower/plan1.dwg"));
    }

    [Fact]
    public void FileNameMatchesCaseInsensitively()
    {
        Assert.Equal(new[] { 3 }, DocumentMatcher.Match(Open, "sections.DWG"));
    }

    [Fact]
    public void SameFileNameInTwoFoldersIsAmbiguous()
    {
        Assert.Equal(new[] { 0, 2 }, DocumentMatcher.Match(Open, "Plan1.dwg"));
    }

    [Fact]
    public void NoSuffixOrPartialMatch()
    {
        Assert.Empty(DocumentMatcher.Match(Open, "1.dwg"));
        Assert.Empty(DocumentMatcher.Match(Open, "Plan1"));
        Assert.Empty(DocumentMatcher.Match(Open, "rev.dwg"));
        Assert.Empty(DocumentMatcher.Match(Open, @"D:\Jobs\Villa\Sections.dwg"));
    }

    [Fact]
    public void UnsavedDrawingMatchesByName()
    {
        Assert.Equal(new[] { 4 }, DocumentMatcher.Match(Open, "Drawing1.dwg"));
        Assert.Equal(new[] { 4 }, DocumentMatcher.Match(Open, " drawing1.dwg "));
    }

    [Fact]
    public void EmptyQueryMatchesNothing()
    {
        Assert.Empty(DocumentMatcher.Match(Open, null));
        Assert.Empty(DocumentMatcher.Match(Open, "  "));
        Assert.Empty(DocumentMatcher.Match([], "Plan1.dwg"));
    }
}
