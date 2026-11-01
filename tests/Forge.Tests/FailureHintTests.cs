using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// B3: every failure carries retryable + hint. Only transport codes that prove the call never
/// reached the plugin are retryable; plugin_exception and validation-class codes are not, and
/// their hint says an identical call fails again.
/// </summary>
public sealed class FailureHintTests
{
    [Fact]
    public void PluginExceptionIsNonRetryable()
    {
        var error = ForgeFailureHint.Annotate(new ForgeError("plugin_exception", "eNotInDatabase"))!;
        Assert.False(error.Retryable);
        Assert.Contains("same arguments", error.Hint, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("missing_layout")]
    [InlineData("invalid_args")]
    [InlineData("layout_not_found")]
    [InlineData("layer_in_use")]
    [InlineData("discard_not_acknowledged")]
    [InlineData("tool_not_enabled")]
    [InlineData("unknown_tool")]
    public void ValidationClassCodesAreNonRetryable(string code)
    {
        var error = ForgeFailureHint.Annotate(new ForgeError(code, "x"))!;
        Assert.False(error.Retryable);
        Assert.NotNull(error.Hint);
    }

    [Theory]
    [InlineData("plugin_connect_timeout")]
    [InlineData("plugin_unavailable")]
    public void TransportCodesAreRetryable(string code)
    {
        var error = ForgeFailureHint.Annotate(new ForgeError(code, "x"))!;
        Assert.True(error.Retryable);
        Assert.NotNull(error.Hint);
    }

    [Theory]
    [InlineData("plugin_request_timeout")]
    [InlineData("plugin_response_timeout")]
    [InlineData("plugin_request_stale")]
    public void TimeoutCodesAreNonRetryableWithReadbackHint(string code)
    {
        var error = ForgeFailureHint.Annotate(new ForgeError(code, "x"))!;
        Assert.False(error.Retryable);
        Assert.Contains("readback", error.Hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SuccessResultIsUntouched()
    {
        var result = ForgeResult.Success("id", new { ok = 1 });
        Assert.Same(result.Error, ForgeFailureHint.Annotate(result).Error);
    }

    [Fact]
    public void ExplicitFlagsArePreserved()
    {
        var error = new ForgeError("custom_code", "x", Retryable: true, Hint: "custom hint");
        var annotated = ForgeFailureHint.Annotate(error)!;
        Assert.True(annotated.Retryable);
        Assert.Equal("custom hint", annotated.Hint);
    }
}
