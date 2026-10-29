using System.Xml.Linq;

namespace Forge.Tests;

public sealed class ProjectConfigurationTests
{
    [Fact]
    public void ModelContextProtocolIsPinnedTo130AndAspNetCoreIsAbsent()
    {
        var root = RepoRoot();
        var packages = XDocument.Load(Path.Combine(root, "Directory.Packages.props"));
        var packageVersions = packages.Descendants("PackageVersion").ToDictionary(
            x => x.Attribute("Include")?.Value ?? "",
            x => x.Attribute("Version")?.Value ?? "");

        Assert.Equal("1.3.0", packageVersions["ModelContextProtocol"]);

        var serverProject = XDocument.Load(Path.Combine(root, "src", "Forge.Server", "Forge.Server.csproj"));
        var packageRefs = serverProject.Descendants("PackageReference")
            .Select(x => x.Attribute("Include")?.Value)
            .Where(x => x is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("ModelContextProtocol", packageRefs);
        Assert.DoesNotContain("ModelContextProtocol.AspNetCore", packageRefs);
    }

    [Fact]
    public void PluginTargetsNet8WindowsAndUsesAutoCadRootProperty()
    {
        var root = RepoRoot();
        var pluginProjectPath = Path.Combine(root, "src", "Forge.Plugin", "Forge.Plugin.csproj");
        var pluginProject = XDocument.Load(pluginProjectPath);
        var target = pluginProject.Descendants("TargetFramework").Single().Value;
        var text = File.ReadAllText(pluginProjectPath);

        Assert.Equal("net8.0-windows", target);
        Assert.Contains("$(AutoCadRoot)\\AcCoreMgd.dll", text);
        Assert.Contains("AUTOCAD_2026_ROOT", text);
        Assert.Contains("<Private>false</Private>", text);
        Assert.DoesNotContain("net9.0", text);
    }

    [Fact]
    public void NamedPipeServerUsesAclForCurrentUser()
    {
        var root = RepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Forge.Plugin", "NamedPipePluginServer.cs"));

        Assert.Contains("NamedPipeServerStreamAcl.Create", source);
        Assert.Contains("WindowsIdentity.GetCurrent", source);
        Assert.Contains("PipeAccessRule", source);
    }

    [Fact]
    public void PluginProcessorDoesNotAwaitBeforeAutoCadApiSwitch()
    {
        var root = RepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Forge.Plugin", "PluginCommandProcessor.cs"));

        Assert.Contains("public ForgeResult Process(ForgeCommand command)", source);
        Assert.DoesNotContain("ConfigureAwait(false)", source);
        Assert.DoesNotContain("public async Task<ForgeResult> ProcessAsync", source);
    }

    [Fact]
    public void PipeTimeoutWarnsAgainstBlindRetry()
    {
        var root = RepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Forge.Server", "ForgePipeClient.cs"));

        Assert.Contains("plugin_response_timeout", source);
        Assert.Contains("Do not blindly retry non-idempotent writes", source);
    }

    [Fact]
    public void DocumentResolutionIsExactWithAmbiguityGuardAndScopedSwitch()
    {
        var root = RepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Forge.Plugin", "NamedPipePluginServer.cs"));

        // No loose suffix matching (would let "1.dwg" resolve "plan1.dwg").
        Assert.DoesNotContain("EndsWith(document", source);
        // Fail closed when more than one open document matches.
        Assert.Contains("ambiguous_document", source);
        // Only switch the active document when it genuinely changes, and restore it afterwards.
        Assert.Contains("ReferenceEquals(doc, current)", source);
    }

    [Fact]
    public void CommandDispatchedToolsRejectControlCharacters()
    {
        var root = RepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Forge.Plugin", "PluginCommandProcessor.cs"));

        Assert.Contains("illegal_control_character", source);
        Assert.Contains("RejectControlChars", source);
    }

    [Fact]
    public void ExecLispEvaluatesSynchronouslyAndKeepsTheQueuedPathAsFallbackOnly()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Forge.Plugin", "PluginCommandProcessor.Lisp.cs"));

        // acedEvaluateLisp returns the value; Editor.Command(lisp) always threw eInvalidInput and fell back to queueing.
        Assert.Contains("acedEvaluateLisp", source);
        Assert.Contains("LispEval.Wrap", source);
        Assert.DoesNotContain("ActiveEditor.Command(lisp", source);
        Assert.DoesNotContain(".Command(normalized", source);
        // The queued path survives only for a missing export, and says so.
        Assert.Contains("queued_fallback", source);
        Assert.Contains("EntryPointNotFoundException", source);
        Assert.Contains("unsafe_not_acknowledged", source);
    }

    [Fact]
    public void PlotNeverRunsWithTheViewerDeviceAndRefreshesTheDeviceList()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Forge.Plugin", "PluginCommandProcessor.Plot.cs"));

        Assert.Contains("PrepareNoViewerDevice", source);
        Assert.Contains("Pc3File.ViewerProperty", source);
        Assert.Contains("plot_viewer_suppression_failed", source);
        Assert.Contains("RefreshList", source);
        // Background plotting would return before the PDF exists; it is forced off and restored.
        Assert.Contains("BACKGROUNDPLOT", source);
        Assert.Contains("plot_overwrite_not_acknowledged", source);
    }

    [Fact]
    public void DocActivateRunsInApplicationContextWithReadBack()
    {
        var root = RepoRoot();
        var pipe = File.ReadAllText(Path.Combine(root, "src", "Forge.Plugin", "NamedPipePluginServer.cs"));
        var tool = File.ReadAllText(Path.Combine(root, "src", "Forge.Plugin", "PluginCommandProcessor.DocActivate.cs"));

        Assert.Contains("DocActivateTool", pipe);
        Assert.Contains("ExecuteDocActivateWhenIdleAsync", pipe);
        Assert.Contains("DocumentMatcher.Match", pipe);
        Assert.Contains("DocumentMatcher.Match", tool);
        Assert.Contains("ReadBackActiveDocument", tool);
        Assert.Contains("doc_activate_unconfirmed", tool);
        Assert.Contains("ambiguous_document", tool);
    }

    [Fact]
    public void RunScriptDrivesLispThroughAGeneratedLoaderAndCleansOutput()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Forge.Server", "HeadlessAccoreConsoleRunner.cs"));

        Assert.Contains("AccoreScriptPlan.TryGetKind", source);
        Assert.Contains("AccoreScriptPlan.LoaderScriptBytes", source);
        Assert.Contains("CleanForReport", source);
        Assert.Contains("unsupported_script_type", source);
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
