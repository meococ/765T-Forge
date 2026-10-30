using Forge.Shared;
using Xunit;

namespace Forge.Tests;

public class DgnImportPolicyTests
{
    [Fact]
    public void MissingPaths_RefuseNamed()
    {
        var (allowed, code, _, _) = DgnImportPolicy.Validate(new DgnImportArgs());

        Assert.False(allowed);
        Assert.Equal("missing_dgn_args", code);
    }

    [Theory]
    [InlineData("meters")]
    [InlineData("Master Units")]
    [InlineData("mm")]
    public void UnknownUnits_RefuseNamed(string units)
    {
        var (allowed, code, message, suggestion) = DgnImportPolicy.Validate(
            new DgnImportArgs { DgnPath = "a.dgn", TargetDwgPath = "b.dwg", Units = units });

        Assert.False(allowed);
        Assert.Equal("dgn_units_invalid", code);
        Assert.Contains("master or sub", message);
        Assert.Contains("rescale", suggestion);
    }

    [Fact]
    public void NewlineInPathOrModel_RefuseNamed()
    {
        var (pathCode, _) = RefuseFor(new DgnImportArgs { DgnPath = "a.dgn\r\nerase all", TargetDwgPath = "b.dwg" });
        Assert.Equal("illegal_control_character", pathCode);

        var (modelCode, _) = RefuseFor(new DgnImportArgs { DgnPath = "a.dgn", TargetDwgPath = "b.dwg", ModelName = "m\no" });
        Assert.Equal("illegal_control_character", modelCode);
    }

    [Fact]
    public void ValidArgs_Allow_WithAnyKnownUnits()
    {
        foreach (var units in new string?[] { null, "", "master", "Master", "sub", "SUB" })
        {
            var (allowed, code, _, _) = DgnImportPolicy.Validate(
                new DgnImportArgs { DgnPath = "a.dgn", TargetDwgPath = "b.dwg", Units = units });
            Assert.True(allowed, $"units='{units}' should validate");
            Assert.Null(code);
        }
    }

    [Theory]
    [InlineData(null, "Master")]
    [InlineData("", "Master")]
    [InlineData("master", "Master")]
    [InlineData("MASTER", "Master")]
    [InlineData("sub", "Sub")]
    [InlineData("Sub", "Sub")]
    public void NormalizeUnits_MapsToPromptTokens(string? units, string expected)
    {
        Assert.Equal(expected, DgnImportPolicy.NormalizeUnits(units));
    }

    [Theory]
    [InlineData(new byte[] { 0x08, 0x09 }, true)]
    [InlineData(new byte[] { 0x09, 0x08 }, true)]
    [InlineData(new byte[] { 0x08, 0x09, 0x00, 0x01 }, true)]
    [InlineData(new byte[] { 0xD0, 0xCF }, false)]
    [InlineData(new byte[] { 0x08 }, false)]
    [InlineData(new byte[] { }, false)]
    public void LooksLikeV7Header_MatchesIntergraphMagicOnly(byte[] header, bool expected)
    {
        Assert.Equal(expected, DgnImportPolicy.LooksLikeV7Header(header));
    }

    [Fact]
    public void LooksLikeV7Header_NullHeader_IsNotV7()
    {
        Assert.False(DgnImportPolicy.LooksLikeV7Header(null));
    }

    [Fact]
    public void CommandArguments_MatchDashDgnImportPromptOrder()
    {
        var tokens = DgnImportPolicy.CommandArguments(@"D:\in\plan.dgn", null, "Master");

        Assert.Equal(new object[] { "-DGNIMPORT", @"D:\in\plan.dgn", "", "Master", "" }, tokens);
    }

    [Theory]
    [InlineData("Sheet 1", "Sub", new object[] { "-DGNIMPORT", "a.dgn", "Sheet 1", "Sub", "" })]
    [InlineData("", "Master", new object[] { "-DGNIMPORT", "a.dgn", "", "Master", "" })]
    public void CommandArguments_ModelAndUnitsPassThrough(string model, string units, object[] expected)
    {
        Assert.Equal(expected, DgnImportPolicy.CommandArguments("a.dgn", model, units));
    }

    [Fact]
    public void ImportIntoCurrentDrawing_IsModeOne()
    {
        // DGNIMPORTMODE 0 would create a second document inside -DGNIMPORT; 2 prefixes named
        // objects with the file name. The tool's temp-document contract requires 1.
        Assert.Equal(1, DgnImportPolicy.ImportIntoCurrentDrawing);
    }

    [Fact]
    public void Registry_TypedTool_NoBackup_NoOpenWorld()
    {
        var tool = ForgeToolRegistry.Get("forge_dgn_import");

        Assert.Equal("forge_dgn_import", tool.Name);
        Assert.Equal("dgn", tool.Group);
        Assert.False(tool.ReadOnly);
        Assert.False(tool.Destructive);
        Assert.False(tool.Idempotent);
        Assert.False(tool.OpenWorld);
        Assert.False(tool.RequiresBackup);
        Assert.True(tool.RequiresAutoCad);
    }

    private static (string? Code, string? Message) RefuseFor(DgnImportArgs args)
    {
        var (allowed, code, message, _) = DgnImportPolicy.Validate(args);
        Assert.False(allowed);
        return (code, message);
    }
}
