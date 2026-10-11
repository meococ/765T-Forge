using Forge.Shared;
using Xunit;

namespace Forge.Tests;

public class HostYearPolicyTests
{
    [Theory]
    [InlineData("25.0.171.0.0", 2025)]
    [InlineData("R25.0 (LMS Tech)", 2025)]
    [InlineData("25.1.0.0", 2026)]
    [InlineData("24.3.1.0", 2024)]
    public void TryMapHostYear_MapsKnownVersions(string versionText, int expected)
    {
        var ok = HostYearPolicy.TryMapHostYear(versionText, out var year);

        Assert.True(ok);
        Assert.Equal(expected, year);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("AutoCAD")]
    [InlineData("26.0")]
    public void TryMapHostYear_RefusesUnmappedVersions(string versionText)
    {
        Assert.False(HostYearPolicy.TryMapHostYear(versionText, out _));
    }

    [Fact]
    public void SameYear_Allows()
    {
        var (allowed, code, _) = HostYearPolicy.Check(2025, 2025, "25.0.171.0.0");

        Assert.True(allowed);
        Assert.Null(code);
    }

    [Fact]
    public void Mismatch_RefusesNamed_BothYearsInMessage()
    {
        var (allowed, code, message) = HostYearPolicy.Check(2026, 2025, "25.0.171.0.0");

        Assert.False(allowed);
        Assert.Equal(HostYearPolicy.MismatchCode, code);
        Assert.Contains("2026", message);
        Assert.Contains("2025", message);
    }

    [Fact]
    public void MissingStamp_RefusesNamed()
    {
        var (allowed, code, _) = HostYearPolicy.Check(null, 2025, "25.0.171.0.0");

        Assert.False(allowed);
        Assert.Equal(HostYearPolicy.StampMissingCode, code);
    }

    [Fact]
    public void UnknownHostYear_RefusesNamed_WithRawVersion()
    {
        var (allowed, code, message) = HostYearPolicy.Check(2025, null, "some-future-build");

        Assert.False(allowed);
        Assert.Equal(HostYearPolicy.HostYearUnknownCode, code);
        Assert.Contains("some-future-build", message);
    }

    [Fact]
    public void ProvenPair_Allows()
    {
        var (allowed, code, _) = HostYearPolicy.Check(
            2026, 2025, "25.0.171.0.0", new[] { (Target: 2026, Host: 2025) });

        Assert.True(allowed);
        Assert.Null(code);
    }
}
