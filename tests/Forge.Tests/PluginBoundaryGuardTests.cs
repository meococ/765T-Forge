using Forge.Plugin;

namespace Forge.Tests;

/// <summary>
/// Behaviour tests for the pure plugin-boundary guards. These sources are compiled into the test
/// assembly through Directory.Build.targets because the test project deliberately never
/// references Forge.Plugin (AutoCAD references are not available to unit tests).
/// </summary>
public sealed class PluginBoundaryGuardTests
{
    [Theory]
    [InlineData("A101")]
    [InlineData("ISO A1 (841.00 x 594.00 MM)")]
    [InlineData(@"C:\out\set.pdf")]
    [InlineData("")]
    public void RequireCommandTokenAcceptsSafeValues(string value)
    {
        Assert.Equal(value, ForgeCommandTokenGuard.RequireCommandToken(value, "field"));
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("A101\"")]
    [InlineData("line\rbreak")]
    [InlineData("line\nbreak")]
    [InlineData("tab\there")]
    [InlineData("null\0char")]
    public void RequireCommandTokenRejectsQuotesAndControlCharacters(string value)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => ForgeCommandTokenGuard.RequireCommandToken(value, "setupName"));
        Assert.Contains("setupName", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireCommandTokenRejectsNull()
    {
        Assert.Throws<ArgumentException>(() => ForgeCommandTokenGuard.RequireCommandToken(null, "outputPath"));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("a")]
    [InlineData("A-b-9")]
    public void CommandIdAcceptsExactPattern(string id)
    {
        Assert.True(ForgeCommandId.IsValid(id));
        Assert.Equal(id, ForgeCommandId.RequireValid(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a\\..\\..\\..\\target")]
    [InlineData("../escape")]
    [InlineData("with space")]
    [InlineData("under_score")]
    [InlineData("a/b")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 65 chars
    public void CommandIdRejectsAnythingOutsideThePattern(string? id)
    {
        Assert.False(ForgeCommandId.IsValid(id));
        Assert.Throws<ArgumentException>(() => ForgeCommandId.RequireValid(id));
    }

    [Fact]
    public void FrameLimitIsExact()
    {
        Assert.Equal(4_000_000, ForgePipeFrames.MaxFrameCharacters);
        Assert.False(ForgePipeFrames.ExceedsLimit(new string('x', ForgePipeFrames.MaxFrameCharacters)));
        Assert.True(ForgePipeFrames.ExceedsLimit(new string('x', ForgePipeFrames.MaxFrameCharacters + 1)));
        Assert.False(ForgePipeFrames.ExceedsLimit(null));
    }

    [Fact]
    public void XrefNodeInfoHasNoTabOrderForXrefs()
    {
        Assert.Equal(-1, XrefNodeInfo.NoTabOrder);
        var node = new XrefNodeInfo("A", @"C:\a.dwg", false, false, "Resolved", XrefNodeInfo.NoTabOrder, 1, null)
        {
            PathExists = true
        };
        Assert.True(node.PathExists);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("Inches", "Inches")]
    [InlineData("inches", "Inches")]
    [InlineData("MILLIMETERS", "Millimeters")]
    [InlineData("Pixels", "Pixels")]
    public void PlotUnitsAcceptOnlyExactApiNames(string? requested, string? expected)
    {
        Assert.True(ForgePlotUnits.TryNormalize(requested, out var units));
        Assert.Equal(expected, units);
    }

    [Theory]
    [InlineData("ISO A1 (841.00 x 594.00 MM)")]
    [InlineData("Millimeter")]
    [InlineData("inch")]
    public void PlotUnitsNeverGuessFromOtherStrings(string requested)
    {
        Assert.False(ForgePlotUnits.TryNormalize(requested, out var units));
        Assert.Null(units);
    }

    [Fact]
    public void PackDestinationsDisambiguateNameCollisionsByParentFolder()
    {
        var destinations = PackDestinationResolver.Resolve(
            [@"C:\a\grid.dwg", @"C:\b\grid.dwg", @"C:\out2\plan.dwg"],
            @"C:\dest");

        Assert.Equal(@"C:\dest\grid.dwg", destinations[0]);
        Assert.Equal(@"C:\dest\grid-b.dwg", destinations[1]);
        Assert.Equal(@"C:\dest\plan.dwg", destinations[2]);
    }

    [Fact]
    public void PackDestinationsCompareCaseInsensitivelyLikeWindows()
    {
        var destinations = PackDestinationResolver.Resolve(
            [@"C:\a\GRID.dwg", @"C:\b\grid.dwg"],
            @"C:\dest");

        Assert.Equal(@"C:\dest\GRID.dwg", destinations[0]);
        Assert.Equal(@"C:\dest\grid-b.dwg", destinations[1]);
    }

    [Fact]
    public void PackDestinationsAreStableRegardlessOfInputOrder()
    {
        var first = PackDestinationResolver.Resolve(
            [@"C:\b\grid.dwg", @"C:\a\grid.dwg"],
            @"C:\dest");
        var second = PackDestinationResolver.Resolve(
            [@"C:\a\grid.dwg", @"C:\b\grid.dwg"],
            @"C:\dest");

        Assert.Equal(@"C:\dest\grid-b.dwg", first[0]);
        Assert.Equal(@"C:\dest\grid.dwg", first[1]);
        Assert.Equal(@"C:\dest\grid.dwg", second[0]);
        Assert.Equal(@"C:\dest\grid-b.dwg", second[1]);
    }

    [Fact]
    public void PackDestinationsHandleThreeWayAndRepeatedParentNames()
    {
        var threeWay = PackDestinationResolver.Resolve(
            [@"C:\a\grid.dwg", @"C:\b\grid.dwg", @"C:\c\grid.dwg"],
            @"C:\dest");
        Assert.Equal(
            [@"C:\dest\grid.dwg", @"C:\dest\grid-b.dwg", @"C:\dest\grid-c.dwg"],
            threeWay);

        var repeatedParent = PackDestinationResolver.Resolve(
            [@"C:\x\grid.dwg", @"C:\y\x\grid.dwg"],
            @"C:\dest");
        Assert.Equal(@"C:\dest\grid.dwg", repeatedParent[0]);
        Assert.Equal(@"C:\dest\grid-x.dwg", repeatedParent[1]);

        // Same parent folder name again: the suffix itself collides, so a stable counter is used.
        var sameParent = PackDestinationResolver.Resolve(
            [@"C:\x\grid.dwg", @"C:\z\x\grid.dwg"],
            @"C:\dest");
        Assert.Equal(@"C:\dest\grid.dwg", sameParent[0]);
        Assert.Equal(@"C:\dest\grid-x.dwg", sameParent[1]);
    }

    [Theory]
    [InlineData("forge_exec_dotnet", true)]
    [InlineData("FORGE_EXEC_DOTNET", true)]
    [InlineData("forge_exec_command", false)]
    [InlineData("forge_plot_to_pdf", false)]
    public void AwaitedDispatchIsAnExactToolNameGate(string tool, bool expected)
    {
        Assert.Equal(expected, ForgeAsyncDispatch.RequiresAwaitedDispatch(tool));
    }

    [Fact]
    public void PackDestinationsDoNotCollideWithAnExistingLiteralName()
    {
        var destinations = PackDestinationResolver.Resolve(
            [@"C:\a\grid.dwg", @"C:\b\grid.dwg", @"C:\other\grid-b.dwg"],
            @"C:\dest");

        Assert.Equal(3, destinations.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void PackDestinationsRequireAFileNameAndDestination()
    {
        Assert.Throws<ArgumentException>(() => PackDestinationResolver.Resolve([@"C:\a\"], @"C:\dest"));
        Assert.Throws<ArgumentException>(() => PackDestinationResolver.Resolve([@"C:\a\grid.dwg"], ""));
        Assert.Throws<ArgumentNullException>(() => PackDestinationResolver.Resolve(null!, @"C:\dest"));
    }
}
