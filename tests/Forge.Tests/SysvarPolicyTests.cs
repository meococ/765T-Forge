using Forge.Shared;

namespace Forge.Tests;

public sealed class SysvarPolicyTests
{
    [Theory]
    [InlineData("SECURELOAD")]
    [InlineData("secureload")]
    [InlineData("TRUSTEDPATHS")]
    [InlineData("TRUSTEDDOMAINS")]
    [InlineData("LEGACYCODESEARCH")]
    [InlineData("ACADLSPASDOC")]
    [InlineData("SAFEMODE")]
    [InlineData("TEXTEVAL")]
    [InlineData("DEMANDLOAD")]
    [InlineData("APPAUTOLOAD")]
    [InlineData("AUTOLOAD")]
    [InlineData("EXPERT")]
    public void TrustSysvarsAreDenied(string name)
    {
        var result = SysvarPolicy.Reject("cmd", name);
        Assert.NotNull(result);
        Assert.False(result!.Ok);
        Assert.Equal("deny_sysvar", result.Error!.Code);
    }

    [Fact]
    public void FilediaStaysSettable()
    {
        Assert.Null(SysvarPolicy.Reject("cmd", "FILEDIA"));
    }

    [Fact]
    public void SafetyPolicyDoesNotDenylistSysvarNames()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Forge.Shared", "SafetyPolicy.cs"));
        Assert.DoesNotContain("SECURELOAD", source, StringComparison.Ordinal);
        Assert.DoesNotContain("deny_sysvar", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SetVarSourceRejectsBeforeSetSystemVariable()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Forge.Plugin", "PluginCommandProcessor.cs"));
        var start = source.IndexOf("private static ForgeResult SetVar", StringComparison.Ordinal);
        var end = source.IndexOf("private static object? ConvertSystemVariableValue", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var body = source[start..end];
        var reject = body.IndexOf("SysvarPolicy.Reject", StringComparison.Ordinal);
        var set = body.IndexOf("SetSystemVariable", StringComparison.Ordinal);
        Assert.True(reject >= 0 && set > reject);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "765T-Forge.ServerOnly.slnf")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
