using Forge.Shared;

namespace Forge.Tests;

public sealed class SafetyPolicyTests
{
    private static readonly string[] UnsafeExecutors =
    [
        "forge_exec_command",
        "forge_exec_lisp",
        "forge_run_script",
        "forge_batch_run",
        "forge_exec_dotnet"
    ];

    private readonly SafetyPolicy _policy = new();

    public static IEnumerable<object[]> UnsafeExecutorNames()
        => UnsafeExecutors.Select(name => new object[] { name });

    [Theory]
    [InlineData("ERASE all")]
    [InlineData("_.ERASE\nALL")]
    [InlineData("(command \"ERASE\" \"ALL\")")]
    [InlineData("-PURGE all")]
    [InlineData("OVERKILL *")]
    [InlineData("AUDIT Y")]
    [InlineData(@"C:\Recover\titleblock.dwg")]
    public void NonUnsafeWritableToolsAreAllowedRegardlessOfArgumentText(string value)
    {
        var command = new ForgeCommand
        {
            Tool = "forge_block_set_attr",
            Args = ForgeJson.ToElement(new { tag = "NOTE", value })
        };

        var decision = _policy.Evaluate(command, unsafeOpsEnabled: true);

        Assert.True(decision.Allowed);
    }

    [Theory]
    [MemberData(nameof(UnsafeExecutorNames))]
    public void UnsafeExecutorsDenyWhenProcessSwitchIsOff(string tool)
    {
        var decision = _policy.Evaluate(ExecutorCommand(tool, unsafeAcknowledged: true), unsafeOpsEnabled: false);

        Assert.False(decision.Allowed);
        Assert.Equal("unsafe_not_acknowledged", decision.Code);
    }

    [Theory]
    [MemberData(nameof(UnsafeExecutorNames))]
    public void UnsafeExecutorsDenyWhenCallIsNotAcknowledged(string tool)
    {
        var decision = _policy.Evaluate(ExecutorCommand(tool, unsafeAcknowledged: false), unsafeOpsEnabled: true);

        Assert.False(decision.Allowed);
        Assert.Equal("unsafe_not_acknowledged", decision.Code);
    }

    [Theory]
    [MemberData(nameof(UnsafeExecutorNames))]
    public void UnsafeExecutorsAllowOnlyWhenBothGateFlagsAreSet(string tool)
    {
        var decision = _policy.Evaluate(ExecutorCommand(tool, unsafeAcknowledged: true), unsafeOpsEnabled: true);

        Assert.True(decision.Allowed);
        Assert.Equal("allowed", decision.Code);
    }

    [Fact]
    public void ReadOnlyToolsAreAlwaysAllowed()
    {
        var command = new ForgeCommand
        {
            Tool = "forge_qa_readback",
            Args = ForgeJson.ToElement(new { query = "ERASE ALL" })
        };

        Assert.True(_policy.Evaluate(command, unsafeOpsEnabled: false).Allowed);
    }

    [Fact]
    public void TypedWritesAreAllowedWithoutUnsafeSwitch()
    {
        var command = new ForgeCommand
        {
            Tool = "forge_system_setvar",
            Args = ForgeJson.ToElement(new { name = "FILEDIA", value = "0" })
        };

        Assert.True(_policy.Evaluate(command, unsafeOpsEnabled: false).Allowed);
    }

    private static ForgeCommand ExecutorCommand(string tool, bool unsafeAcknowledged)
        => new()
        {
            Tool = tool,
            Args = ForgeJson.ToElement(new { command = "ERASE ALL" }),
            UnsafeAcknowledged = unsafeAcknowledged
        };
}
