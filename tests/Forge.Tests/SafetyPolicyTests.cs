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
    [InlineData("(command \"_.ERASE\" (ssget \"_X\") \"\")")]
    [InlineData("(command \"_.ERASE\" (ssget \"_A\") \"\")")]
    [InlineData("(setq s (ssget \"_X\")) (entdel (ssname s 0))")]
    [InlineData("E\nALL\n")]
    [InlineData("PU\nA\n*\nN\n")]
    [InlineData("(setvar \"SECURELOAD\" 0)")]
    [InlineData("(setvar \"TRUSTEDPATHS\" \"C:\\\\temp\")")]
    [InlineData("(startapp \"cmd.exe\" \"/c whoami\")")]
    [InlineData("(vlax-create-object \"WScript.Shell\")")]
    [InlineData("SHELL whoami")]
    [InlineData("(command \"_.SHELL\" \"whoami\")")]
    [InlineData("NETLOAD C:\\x\\evil.dll")]
    [InlineData("(command \"_.NETLOAD\" \"C:\\\\x\\\\evil.dll\")")]
    [InlineData("(load \"C:\\\\temp\\\\x.lsp\")")]
    [InlineData("(open \"...acaddoc.lsp\" \"w\") (write-line \"x\")")]
    [InlineData("(vl-file-copy \"x.lsp\" \"...\\\\acaddoc.lsp\")")]
    [InlineData("(command \"_.SAVEAS\" \"\" \"D:\\\\...\\\\A-101.dwg\")")]
    [InlineData("(command \"_.QUIT\" \"_Y\")")]
    [InlineData("(vlax-invoke o 'Delete)")]
    [InlineData("(vla-getinterfaceobject acad \"WScript.Shell\")")]
    [InlineData("(eval (read (strcat \"(st\" \"artapp ...\")))")]
    [InlineData("(setvar (strcat \"SECURE\" \"LOAD\") 0)")]
    public void HighValueDestructivePatternsAreDenied(string commandText)
    {
        var decision = _policy.EvaluateText("forge_exec_lisp", commandText);

        Assert.False(decision.Allowed, $"Expected '{commandText}' to be denied, but it was allowed.");
        Assert.StartsWith("deny_", decision.Code);
    }

    [Theory]
    [InlineData("-LAYER M PU")]
    [InlineData("-INSERT PU-VALVE")]
    [InlineData("text \"E ALL PIPES\"")]
    public void AliasRegexDoesNotFalsePositiveOnLayerBlockOrText(string commandText)
    {
        var decision = _policy.EvaluateText("forge_exec_command", commandText);

        Assert.True(decision.Allowed, $"Expected '{commandText}' to be allowed, but got {decision.Code}: {decision.Message}");
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
    [InlineData("forge_exec_dotnet", "1 + 1")]
    [InlineData("forge_exec_command", "REGEN")]
    [InlineData("forge_exec_lisp", "(+ 1 2)")]
    public void AllGenericExecutorsRequireUnsafeAcknowledgement(string tool, string payload)
    {
        var unacknowledged = new ForgeCommand
        {
            Tool = tool,
            UnsafeAcknowledged = false,
            Args = ForgeJson.ToElement(new { code = payload, command = payload })
        };

        var denied = _policy.Evaluate(unacknowledged);
        Assert.False(denied.Allowed);
        Assert.Equal("unsafe_not_acknowledged", denied.Code);

        var acknowledged = unacknowledged with { UnsafeAcknowledged = true };
        var allowed = _policy.Evaluate(acknowledged);
        Assert.True(allowed.Allowed, $"Expected acknowledged benign {tool} call to be allowed, got {allowed.Code}");
    }
}
