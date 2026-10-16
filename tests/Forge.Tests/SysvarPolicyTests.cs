using Forge.Shared;

namespace Forge.Tests;

public sealed class SysvarPolicyTests
{
    [Theory]
    [InlineData("SECURELOAD")]
    [InlineData("secureload")]
    [InlineData("  TRUSTEDPATHS  ")]
    [InlineData("TRUSTEDDOMAINS")]
    [InlineData("LEGACYCODESEARCH")]
    [InlineData("ACADLSPASDOC")]
    [InlineData("SAFEMODE")]
    [InlineData("TEXTEVAL")]
    [InlineData("DEMANDLOAD")]
    [InlineData("APPAUTOLOAD")]
    [InlineData("AUTOLOAD")]
    [InlineData("EXPERT")]
    public void RejectsSecurityAndSessionSystemVariables(string name)
    {
        var result = SysvarPolicy.Reject("cmd-1", name);

        Assert.NotNull(result);
        Assert.False(result.Ok);
        Assert.Equal("deny_sysvar", result.Error?.Code);
    }

    [Theory]
    [InlineData("CLAYER")]
    [InlineData("DIMSCALE")]
    [InlineData("LTSCALE")]
    [InlineData("INSUNITS")]
    [InlineData("OSMODE")]
    [InlineData("ORTHOMODE")]
    public void AllowsBenignDrawingSystemVariables(string name)
    {
        Assert.Null(SysvarPolicy.Reject("cmd-1", name));
    }
}
