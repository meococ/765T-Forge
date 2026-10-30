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
    public void DgnPendingRunMirrorIsASetDrainedPerRunAndAtDispose()
    {
        var root = RepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Forge.Plugin", "NamedPipePluginServer.cs"));

        // review-d14-r2 M1/N2: a watchdog-timeout retry can leave two DGN runs pending at
        // once, so the mirror is a set; each finish path removes only its own run (a shared
        // unconditional clear would let the first finishing lane drain another lane's
        // mirror slot and skip its Terminate drain entirely).
        Assert.Contains("HashSet<PluginCommandProcessor.DgnImportRun>", source);
        Assert.Contains("_pendingDgnRuns.Add(run)", source);
        Assert.Contains("_pendingDgnRuns.Remove(timedOutRun)", source);
        Assert.Contains("_pendingDgnRuns.Remove(finishedRun)", source);
        Assert.Contains("_pendingDgnRuns.Clear()", source);
        Assert.DoesNotContain("_pendingDgnRun = null", source);
    }

    [Fact]
    public void DgnCloseRunsOnlyOnSettledIdleTicksAfterContextRestored()
    {
        var root = RepoRoot();
        var dgn = File.ReadAllText(Path.Combine(root, "src", "Forge.Plugin", "PluginCommandProcessor.Dgn.cs"));
        var pipe = File.ReadAllText(Path.Combine(root, "src", "Forge.Plugin", "NamedPipePluginServer.cs"));

        // fail-evidence-20261030: an Idle-lane CloseAndDiscard mid-teardown wedged
        // quiescence. fail-evidence-r3-20261031: the same close inside the queued
        // app-context callback (and on the tick that first observes ContextRestored)
        // throws silently — the temp document leaked. r4 split: the queued callback
        // holds ONLY the ordering proof + sysvar/scope restore; the document close is
        // attempted by the Idle lane on ticks AFTER a full quiescent cycle, retried
        // until the deadline, and reported as dgn_temp_document_leaked when it never lands.
        var cbStart = dgn.IndexOf("Documents.ExecuteInApplicationContext(_ =>", StringComparison.Ordinal);
        Assert.True(cbStart >= 0, "the leg must queue the app-context ordering proof");
        var cbEnd = dgn.IndexOf("}, null);", cbStart, StringComparison.Ordinal);
        Assert.True(cbEnd > cbStart, "the queued callback region must be locatable");
        var callback = dgn.Substring(cbStart, cbEnd - cbStart);
        Assert.Contains("RestoreDgnSysvars(run)", callback);
        Assert.Contains("run.ContextRestored = true", callback);
        Assert.DoesNotContain("CloseAndDiscard", callback);
        Assert.DoesNotContain("MdiActiveDocument", callback);
        Assert.DoesNotContain("TryRestoreDgnDocument", callback);

        // The leg runs the synchronous command — no nested async session survives the
        // callback to be orphaned by the close — and a late Abandoned return must not
        // queue a stale restore.
        Assert.Contains("run.TempDoc.Editor.Command(", dgn);
        Assert.DoesNotContain("CommandAsync(", dgn);
        Assert.Contains("if (!run.Abandoned)", dgn);

        // The document-restore entry point exists for the settled lane ticks.
        Assert.Contains("internal bool TryRestoreDgnDocument(DgnImportRun run)", dgn);

        // Honest reporting: a leftover temp document is a distinct failure code plus a
        // truth field on every DGN result, not ok:true with a footnote.
        Assert.Contains("dgn_temp_document_leaked", dgn);
        Assert.Contains("tempDocumentLeaked", dgn);
        Assert.Contains("tempDocumentName", dgn);
        Assert.Contains("dgn_context_restore_timeout", dgn);

        // Lane ordering: wait for ContextRestored, arm one full quiescent cycle, then
        // attempt the restore with the nested-raise guard, retrying until the deadline,
        // then answer through EndDgnImport.
        var waitIndex = pipe.IndexOf("!dgnRun.ContextRestored", StringComparison.Ordinal);
        var armIndex = pipe.IndexOf("dgnRun.SettleArmed = true", StringComparison.Ordinal);
        var closeIndex = pipe.IndexOf("_processor.TryRestoreDgnDocument(dgnRun)", StringComparison.Ordinal);
        var endIndex = pipe.IndexOf("EndDgnImport(command, finishedRun)", StringComparison.Ordinal);
        Assert.True(waitIndex >= 0, "the Idle lane must wait on ContextRestored before finishing a DGN run");
        Assert.True(armIndex > waitIndex, "the settle arm must come after the ContextRestored wait");
        Assert.True(closeIndex > armIndex, "the first close attempt must come on a tick after the arm");
        Assert.True(endIndex > closeIndex, "the answer must follow the settled close attempt");
        Assert.Contains("dgnRun.RestoreInFlight", pipe);
        Assert.Contains("restoreTimedOut: true", pipe);
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

        Assert.Contains("DocumentContextPolicy.Route(", pipe); // fix-smoke-20261029 D1/r2-D13: doc_activate/new/open share the Idle lane via the Shared decision table
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
