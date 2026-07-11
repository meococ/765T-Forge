using Forge.Shared;

namespace Forge.Tests;

public sealed class SafetyPolicyTests
{
    private readonly SafetyPolicy _policy = new();

    [Theory]
    [InlineData("ERASE all")]
    [InlineData("_.ERASE\nALL")]
    [InlineData("(command \"ERASE\" \"ALL\")")]
    [InlineData("-PURGE all")]
    [InlineData("OVERKILL *")]
    [InlineData("AUDIT Y")]
    public void GenericExecutorsDenyDangerousCommands(string commandText)
    {
        var command = new ForgeCommand
        {
            Tool = "forge_exec_command",
            Args = ForgeJson.ToElement(new { command = commandText })
        };

        var decision = _policy.Evaluate(command);

        Assert.False(decision.Allowed);
        Assert.StartsWith("deny_", decision.Code);
    }

    [Fact]
    public void RunScriptAllowsHarmlessAsterisks()
    {
        var decision = _policy.EvaluateText("forge_run_script", "ZOOM *\n-LAYER\nS\n0\n");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void RunScriptDeniesEraseStarSelection()
    {
        var decision = _policy.EvaluateText("forge_run_script", "ERASE\n*\n");

        Assert.False(decision.Allowed);
        Assert.Equal("deny_openworld_all_selection", decision.Code);
    }

    [Theory]
    [InlineData("LAYDEL")]
    [InlineData("(ssget \"X\") (command \"ERASE\")")]
    public void HighValueDestructivePatternsAreDenied(string commandText)
    {
        var decision = _policy.EvaluateText("forge_exec_lisp", commandText);

        Assert.False(decision.Allowed);
        Assert.StartsWith("deny_", decision.Code);
    }

    [Fact]
    public void TypedSetVarIsAllowed()
    {
        var command = new ForgeCommand
        {
            Tool = "forge_system_setvar",
            Args = ForgeJson.ToElement(new { name = "FILEDIA", value = "0" })
        };

        var decision = _policy.Evaluate(command);

        Assert.True(decision.Allowed);
    }

    [Theory]
    [InlineData("DO NOT PURGE XREFS")]
    [InlineData(@"C:\Recover\titleblock.dwg")]
    public void TypedWriteToolsDoNotScanBusinessTextAsCommands(string value)
    {
        var command = new ForgeCommand
        {
            Tool = "forge_block_set_attr",
            Args = ForgeJson.ToElement(new { tag = "NOTE", value })
        };

        var decision = _policy.Evaluate(command);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void DotnetExecutorRequiresUnsafeAcknowledgement()
    {
        var command = new ForgeCommand
        {
            Tool = "forge_exec_dotnet",
            Args = ForgeJson.ToElement(new { code = "1 + 1" })
        };

        var decision = _policy.Evaluate(command);

        Assert.False(decision.Allowed);
        Assert.Equal("unsafe_not_acknowledged", decision.Code);
    }
}
