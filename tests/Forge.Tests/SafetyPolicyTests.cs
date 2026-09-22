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

    [Theory]
    [InlineData("E ALL")]
    [InlineData("_.E\nALL")]
    [InlineData("E\n*")]
    [InlineData("(ssget \"_X\")\n(entdel e)")]
    [InlineData("(ssget \"_A\")\n(vla-erase e)")]
    [InlineData("(vla-delete e)\n(ssget \"_X\")")]
    [InlineData("(ssget \"_X\")\n(command \"_.ERASE\")")]
    [InlineData("(strcat \"ER\" \"ASE\")\nALL")]
    [InlineData("(eval payload)\n(ssget \"_X\")")]
    [InlineData("SAVEAS\nC:\\out\\sheet.dwg")]
    [InlineData("QSAVE")]
    [InlineData("WBLOCK\nmyblock\n*")]
    [InlineData("SAVE")]
    [InlineData("NETLOAD plugin.dll")]
    [InlineData("_.APPLOAD")]
    [InlineData("ARXLOAD helper.arx")]
    [InlineData("(load \"helper.lsp\")")]
    [InlineData("SCRIPT other.scr")]
    [InlineData("SHELL")]
    [InlineData("_.SH")]
    [InlineData("(command \"SH\")")]
    public void AddedDenylistHolesAreDenied(string commandText)
    {
        var decision = _policy.EvaluateText("forge_exec_command", commandText);

        Assert.False(decision.Allowed);
        Assert.StartsWith("deny_", decision.Code);
    }

    [Theory]
    [InlineData("DELETE ALL")]
    [InlineData("_.DELETE\nALL")]
    [InlineData("DELETE\n*")]
    [InlineData("-DELETE\nALL")]
    [InlineData("(command \"DELETE\" \"ALL\")")]
    [InlineData("(command \"_.DELETE\" \"*\")")]
    public void DeleteAllIsDenied(string commandText)
    {
        var decision = _policy.EvaluateText("forge_exec_command", commandText);
        Assert.False(decision.Allowed);
        Assert.Equal("deny_delete_all", decision.Code);
    }

    [Theory]
    [InlineData("ZOOM *\n-LAYER\nS\n0\n")]
    [InlineData("SHAPE")]
    [InlineData("FINISH")]
    [InlineData("OPEN\nC:\\SH\\file.dwg")]
    [InlineData("OPEN\nC:\\Save\\a.dwg")]
    [InlineData("OPEN\nC:\\Shell\\a.dwg")]
    [InlineData("(strcat \"A\" \"B\")")]
    [InlineData("(ssget \"_X\")")]
    [InlineData("OPEN\nC:\\Delete\\a.dwg")]
    public void HarmlessExecutorTextStaysAllowed(string commandText)
    {
        var decision = _policy.EvaluateText("forge_exec_command", commandText);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void BatchToolDoesNotScanPathsAsCommands()
    {
        var command = new ForgeCommand
        {
            Tool = "forge_batch_run",
            Args = ForgeJson.ToElement(new
            {
                jobs = new[]
                {
                    new { dwgPath = @"C:\NETLOAD\a.dwg", scriptPath = @"C:\SH\a.scr" }
                }
            })
        };

        var decision = _policy.Evaluate(command);

        Assert.True(decision.Allowed);
        Assert.False(ForgeToolRegistry.Get("forge_batch_run").OpenWorld);
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
