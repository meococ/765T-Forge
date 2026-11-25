using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// V2 Big Update regression &amp; conformance suite:
/// - EnableUnsafeOps stays opt-in: it is false unless FORGE_ENABLE_UNSAFE_OPS parses as true (still gated per-call by unsafeAcknowledged=true).
/// - acedEvaluateLisp returns BOOL TRUE (1) on success in AutoCAD 2025/2026 (R25.x) as well as RTNORM (5100).
/// - acad_command_active is annotated retryable=true so agents wait/retry instead of aborting.
/// - All 8 v2 Zero-LISP tools (133 total tools) are registered with strict safety, toolset, and profile parity.
/// </summary>
public sealed class V2BigUpdateTests
{
    [Fact]
    public void EnableUnsafeOpsDefaultsToFalseUnlessExplicitlyEnabled()
    {
        var prevUnsafe = Environment.GetEnvironmentVariable("FORGE_ENABLE_UNSAFE_OPS");
        var prevToken = Environment.GetEnvironmentVariable("FORGE_AUTOCAD_TOKEN");
        var prevMcp = Environment.GetEnvironmentVariable("MCP_AUTOCAD_TOKEN");
        var prevDev = Environment.GetEnvironmentVariable("FORGE_DEV_ALLOW_DEFAULT_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("FORGE_AUTOCAD_TOKEN", null);
            Environment.SetEnvironmentVariable("MCP_AUTOCAD_TOKEN", null);
            Environment.SetEnvironmentVariable("FORGE_DEV_ALLOW_DEFAULT_TOKEN", "true");

            Environment.SetEnvironmentVariable("FORGE_ENABLE_UNSAFE_OPS", null);
            Assert.False(ForgeEnvironment.FromProcess().EnableUnsafeOps);

            Environment.SetEnvironmentVariable("FORGE_ENABLE_UNSAFE_OPS", "");
            Assert.False(ForgeEnvironment.FromProcess().EnableUnsafeOps);

            Environment.SetEnvironmentVariable("FORGE_ENABLE_UNSAFE_OPS", "true");
            Assert.True(ForgeEnvironment.FromProcess().EnableUnsafeOps);

            Environment.SetEnvironmentVariable("FORGE_ENABLE_UNSAFE_OPS", "false");
            Assert.False(ForgeEnvironment.FromProcess().EnableUnsafeOps);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORGE_ENABLE_UNSAFE_OPS", prevUnsafe);
            Environment.SetEnvironmentVariable("FORGE_AUTOCAD_TOKEN", prevToken);
            Environment.SetEnvironmentVariable("MCP_AUTOCAD_TOKEN", prevMcp);
            Environment.SetEnvironmentVariable("FORGE_DEV_ALLOW_DEFAULT_TOKEN", prevDev);
        }
    }

    [Fact]
    public void LispCodesRecognizesAcCoreBoolTrueAndRtNorm()
    {
        // Disassembled accore.dll (AutoCAD 2026): ?acedEvaluateLisp@@YAHPEB_WAEAPEAUresbuf@@@Z
        // compares internal RTNORM (5100 / 0x13EC) and returns BOOL TRUE = 1 on success, 0 on failure.
        Assert.True(LispCodes.IsSuccess(LispCodes.EvalTrue));
        Assert.True(LispCodes.IsSuccess(LispCodes.RtNorm));
        Assert.False(LispCodes.IsSuccess(LispCodes.EvalFalse));
        Assert.False(LispCodes.IsSuccess(LispCodes.RtError));
        Assert.False(LispCodes.IsSuccess(LispCodes.RtCancel));

        Assert.Equal("TRUE", LispCodes.StatusName(LispCodes.EvalTrue));
        Assert.Equal("FALSE", LispCodes.StatusName(LispCodes.EvalFalse));
        Assert.Equal("RTNORM", LispCodes.StatusName(LispCodes.RtNorm));
    }

    [Fact]
    public void AcadCommandActiveIsRetryableWithGuidance()
    {
        var error = ForgeFailureHint.Annotate(new ForgeError("acad_command_active", "AutoCAD has an active command: MOVE"))!;
        Assert.True(error.Retryable);
        Assert.NotNull(error.Hint);
        Assert.Contains("ESC", error.Hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EightNewZeroLispToolsAreRegisteredAndProfiled()
    {
        Assert.Equal(133, ForgeToolRegistry.All.Count);

        var newTools = new[]
        {
            "forge_viewport_set",
            "forge_layout_manage",
            "forge_entity_draworder",
            "forge_draw_wipeout",
            "forge_image_list",
            "forge_image_attach",
            "forge_image_repath",
            "forge_curve_station"
        };

        Assert.True(ToolProfiles.TryGet("authoring", out var authoring));
        foreach (var name in newTools)
        {
            var meta = ForgeToolRegistry.Get(name);
            Assert.NotEqual("unknown", meta.Group);
            Assert.False(meta.Unsafe, $"{name} must be a typed safe tool, not unsafe.");
            Assert.False(meta.OpenWorld, $"{name} must be closed-world typed.");
            Assert.Contains(name, authoring);
            Assert.NotNull(ForgeToolsets.ToolsetForTool(name));
        }

        Assert.True(ForgeToolRegistry.Get("forge_image_list").ReadOnly);
        Assert.True(ForgeToolRegistry.Get("forge_curve_station").ReadOnly);
        Assert.True(ForgeToolRegistry.Get("forge_layout_manage").Destructive);
        Assert.True(ForgeToolRegistry.Get("forge_layout_manage").RequiresBackup);
        Assert.False(ForgeToolRegistry.Get("forge_image_attach").Destructive);
        Assert.False(ForgeToolRegistry.Get("forge_draw_wipeout").Destructive);
    }

    [Fact]
    public void PluginBundlesRoslynScriptingAndIdleWakeupPump()
    {
        var pluginCsproj = File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), "Forge.Plugin.csproj"));
        Assert.Contains("<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>", pluginCsproj);

        var pluginInit = File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), "Plugin.cs"));
        Assert.Contains("AssemblyLoadContext.Default.Resolving", pluginInit);

        var pipeServer = File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), "NamedPipePluginServer.cs"));
        Assert.Contains("PostMessage", pipeServer);
        Assert.Contains("WmNull", pipeServer);
        Assert.Contains("_mainThreadGate", pipeServer);
    }
}
