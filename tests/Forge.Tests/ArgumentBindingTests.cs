using System.Text.Json;
using Forge.Shared;
using Xunit;

namespace Forge.Tests;

/// <summary>
/// Locks in the exact argument-binding contract. A malformed payload must fail
/// determinately instead of being silently replaced by a default instance.
/// </summary>
public sealed class ArgumentBindingTests
{
    private sealed record SampleArgs
    {
        public string? Name { get; init; }

        public int Count { get; init; } = 7;
    }

    [Fact]
    public void AbsentArgsYieldsDefaults()
    {
        var args = ForgeJson.ArgsOrDefault<SampleArgs>(default);

        Assert.Null(args.Name);
        Assert.Equal(7, args.Count);
    }

    [Fact]
    public void ExplicitJsonNullYieldsDefaults()
    {
        using var document = JsonDocument.Parse("null");

        var args = ForgeJson.ArgsOrDefault<SampleArgs>(document.RootElement);

        Assert.Null(args.Name);
        Assert.Equal(7, args.Count);
    }

    [Fact]
    public void WellFormedObjectBindsExactly()
    {
        using var document = JsonDocument.Parse("""{"name":"sheet-1","count":3}""");

        var args = ForgeJson.ArgsOrDefault<SampleArgs>(document.RootElement);

        Assert.Equal("sheet-1", args.Name);
        Assert.Equal(3, args.Count);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    public void WrongJsonKindThrowsInsteadOfDefaulting(string payload)
    {
        using var document = JsonDocument.Parse(payload);

        Assert.ThrowsAny<JsonException>(() => ForgeJson.ArgsOrDefault<SampleArgs>(document.RootElement));
    }

    [Fact]
    public void WrongPropertyTypeThrowsInsteadOfDefaulting()
    {
        using var document = JsonDocument.Parse("""{"count":"not-a-number"}""");

        Assert.ThrowsAny<JsonException>(() => ForgeJson.ArgsOrDefault<SampleArgs>(document.RootElement));
    }
}
