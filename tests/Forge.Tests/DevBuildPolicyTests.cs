using Forge.Shared;

namespace Forge.Tests;

public sealed class DevBuildPolicyTests
{
    [Fact]
    public void IsDevAssemblyRequiresTheDevSuffix()
    {
        Assert.True(DevBuildPolicy.IsDevAssembly("Forge.Plugin.Dev"));
        // Case-insensitive: a differently-cased DevAsmSuffix still marks a dev build.
        Assert.True(DevBuildPolicy.IsDevAssembly("Forge.Plugin.dev"));
        // Forge.Shared.csproj takes the same DevAsmSuffix so a dev NETLOAD resolves
        // the dev shared assembly, not the deployed one.
        Assert.True(DevBuildPolicy.IsDevAssembly("Forge.Shared.Dev"));

        Assert.False(DevBuildPolicy.IsDevAssembly("Forge.Plugin"));
        Assert.False(DevBuildPolicy.IsDevAssembly("Forge.Shared"));
        Assert.False(DevBuildPolicy.IsDevAssembly(null));
        Assert.False(DevBuildPolicy.IsDevAssembly(""));
        Assert.False(DevBuildPolicy.IsDevAssembly("Dev"));
        Assert.False(DevBuildPolicy.IsDevAssembly("Forge.Plugin.Dev2"));
    }

    [Fact]
    public void DevAssemblyHonoursForgePipeNameDev()
    {
        WithDevPipe("765T.Forge.AutoCAD.devlane", () =>
        {
            Assert.Equal(
                "765T.Forge.AutoCAD.devlane",
                DevBuildPolicy.ResolvePipeName("765T.Forge.AutoCAD", "Forge.Plugin.Dev"));
        });
    }

    [Fact]
    public void ProductionAssemblyIgnoresForgePipeNameDev()
    {
        WithDevPipe("765T.Forge.AutoCAD.devlane", () =>
        {
            // The deployed bundle must never see the dev override — post-deploy it would
            // race the dev build for the dev pipe in a session where both are loaded.
            Assert.Equal(
                "765T.Forge.AutoCAD",
                DevBuildPolicy.ResolvePipeName("765T.Forge.AutoCAD", "Forge.Plugin"));
        });
    }

    [Fact]
    public void DevAssemblyFallsBackToConfiguredPipeWhenOverrideUnsetOrBlank()
    {
        WithDevPipe(null, () =>
        {
            Assert.Equal(
                "765T.Forge.AutoCAD",
                DevBuildPolicy.ResolvePipeName("765T.Forge.AutoCAD", "Forge.Plugin.Dev"));
        });

        WithDevPipe("   ", () =>
        {
            Assert.Equal(
                "765T.Forge.AutoCAD",
                DevBuildPolicy.ResolvePipeName("765T.Forge.AutoCAD", "Forge.Plugin.Dev"));
        });
    }

    [Fact]
    public void PluginReadsDevPipeOnlyThroughThePolicy()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Forge.Plugin", "Plugin.cs"));

        Assert.Contains("DevBuildPolicy.ResolvePipeName", source);
        // The raw variable read lives inside DevBuildPolicy, never ungated in the plugin.
        Assert.DoesNotContain("GetEnvironmentVariable(\"FORGE_PIPE_NAME_DEV\")", source);
    }

    private static void WithDevPipe(string? value, Action body)
    {
        var previous = Environment.GetEnvironmentVariable("FORGE_PIPE_NAME_DEV");
        try
        {
            Environment.SetEnvironmentVariable("FORGE_PIPE_NAME_DEV", value);
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORGE_PIPE_NAME_DEV", previous);
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "765T-Forge.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
