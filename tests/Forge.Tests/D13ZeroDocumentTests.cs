using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// smoke r2 D13: with 0 open documents every document-context tool must fail fast with
/// no_active_document (never wait the request timeout on a command context that cannot
/// exist), forge_system_health must answer ok with documents: 0 + a hint, and
/// no_command_context is kept only for "documents exist but none is active".
/// The registry iteration test makes sure a tool added later cannot slip through.
/// </summary>
public sealed class D13ZeroDocumentPolicyTests
{
    private const string DocTool = "forge_draw_line";
    private const string Health = "forge_system_health";
    private const string DocList = "forge_doc_list_open";
    private const string Version = "forge_system_version";

    [Fact]
    public void ApplicationContextToolsAlwaysRideTheApplicationLane()
    {
        foreach (var tool in DocumentContextPolicy.ApplicationContextTools)
        {
            Assert.Equal(DocDispatchDecision.ApplicationLane, DocumentContextPolicy.Route(tool, 0, false));
            Assert.Equal(DocDispatchDecision.ApplicationLane, DocumentContextPolicy.Route(tool, 3, false));
            Assert.Equal(DocDispatchDecision.ApplicationLane, DocumentContextPolicy.Route(tool, 3, true));
        }
    }

    [Fact]
    public void ADocumentToolFailsFastWhenNoDocumentIsOpen()
    {
        Assert.Equal(DocDispatchDecision.FailNoActiveDocument, DocumentContextPolicy.Route(DocTool, 0, false));
    }

    [Fact]
    public void ADocumentToolGetsNoCommandContextWhenDocumentsExistButNoneIsActive()
    {
        Assert.Equal(DocDispatchDecision.FailNoCommandContext, DocumentContextPolicy.Route(DocTool, 2, false));
    }

    [Fact]
    public void ADocumentToolRunsInTheCommandContextWithAnActiveDocument()
    {
        Assert.Equal(DocDispatchDecision.CommandContext, DocumentContextPolicy.Route(DocTool, 2, true));
    }

    [Fact]
    public void DocumentFreeToolsGetTheZeroDocumentLaneAtZeroDocs()
    {
        foreach (var tool in DocumentContextPolicy.ZeroDocumentTools)
        {
            Assert.Equal(DocDispatchDecision.ZeroDocumentLane, DocumentContextPolicy.Route(tool, 0, false));
            // Documents open but none active: still no command context is needed — Idle marshal as today.
            Assert.Equal(DocDispatchDecision.IdleLane, DocumentContextPolicy.Route(tool, 1, false));
            // With an active document they run in the command context like every other tool.
            Assert.Equal(DocDispatchDecision.CommandContext, DocumentContextPolicy.Route(tool, 1, true));
        }
    }

    [Fact]
    public void HealthVersionAndDocListAreTheZeroDocumentTools()
    {
        Assert.True(DocumentContextPolicy.ZeroDocumentTools.Contains(Health));
        Assert.True(DocumentContextPolicy.ZeroDocumentTools.Contains(Version));
        Assert.True(DocumentContextPolicy.ZeroDocumentTools.Contains(DocList));
        Assert.Equal(3, DocumentContextPolicy.ZeroDocumentTools.Count);
    }

    [Fact]
    public void AnUnregisteredToolFailsFastToo()
    {
        Assert.Equal(DocDispatchDecision.FailNoActiveDocument, DocumentContextPolicy.Route("forge_future_tool", 0, false));
    }

    [Fact]
    public void EveryRegisteredToolRoutesToAKnownDecisionAtZeroDocuments()
    {
        var allowed = new[]
        {
            DocDispatchDecision.ApplicationLane,
            DocDispatchDecision.ZeroDocumentLane,
            DocDispatchDecision.FailNoActiveDocument
        };

        foreach (var tool in ForgeToolRegistry.All)
        {
            var decision = DocumentContextPolicy.Route(tool.Name, documentCount: 0, hasActiveDocument: false);
            Assert.Contains(decision, allowed);

            var expected = DocumentContextPolicy.ApplicationContextTools.Contains(tool.Name)
                ? DocDispatchDecision.ApplicationLane
                : DocumentContextPolicy.ZeroDocumentTools.Contains(tool.Name)
                    ? DocDispatchDecision.ZeroDocumentLane
                    : DocDispatchDecision.FailNoActiveDocument;
            Assert.True(decision == expected, $"{tool.Name}: expected {expected}, got {decision}");
        }
    }

    [Fact]
    public void EveryRegisteredToolHasAFullDecisionRow()
    {
        foreach (var tool in ForgeToolRegistry.All)
        {
            foreach (var count in new[] { 0, 1, 3 })
            {
                foreach (var active in new[] { false, true })
                {
                    var decision = DocumentContextPolicy.Route(tool.Name, count, active);
                    Assert.True(Enum.IsDefined(decision), $"{tool.Name} count={count} active={active} -> {decision}");
                }
            }

            // With an active document nothing may fail fast on document state.
            var withActive = DocumentContextPolicy.Route(tool.Name, 1, true);
            Assert.True(
                withActive is DocDispatchDecision.ApplicationLane or DocDispatchDecision.CommandContext,
                $"{tool.Name} with an active document routed to {withActive}");
        }
    }
}

/// <summary>review-wp2-r1 finding 1 (D13): the fixed-time pipe-token gate every lane passes first.</summary>
public sealed class D13PipeAuthTests
{
    [Theory]
    [InlineData("abc", "abc", true)]
    [InlineData("abc", "abd", false)]
    [InlineData("abc", "abcd", false)]
    [InlineData(null, "expected", false)]
    [InlineData("", "expected", false)]
    [InlineData("", "", true)]
    [InlineData(null, null, true)]
    public void TokensCompareInFixedTime(string? token, string? expected, bool matches)
        => Assert.Equal(matches, PipeAuth.TokenMatches(token, expected));

    [Fact]
    public void ASharedHintNamesTheRecoveryTools()
    {
        // The Start-tab guidance the real Health() emits at 0 documents (folded into the real
        // handler so the token check, audit record and full field set apply — no synthesized answer).
        Assert.Contains("forge_doc_new", DocumentContextPolicy.ZeroDocumentHint, StringComparison.Ordinal);
        Assert.Contains("forge_doc_open", DocumentContextPolicy.ZeroDocumentHint, StringComparison.Ordinal);
    }
}

/// <summary>D13 + review-wp2-r1 findings 1–2 plugin wiring guards.</summary>
public sealed class D13ZeroDocumentSourceGuards
{
    private static string ServerSource()
        => File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), "NamedPipePluginServer.cs"));

    private static string ProcessorSource()
        => File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), "PluginCommandProcessor.cs"));

    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"method not found: {signature}");
        var end = System.Text.RegularExpressions.Regex.Match(source[start..], @"\n    \}\r?\n");
        Assert.True(end.Success, $"end of method not found: {signature}");
        return source.Substring(start, end.Index + end.Length);
    }

    [Fact]
    public void TheDispatchAuthenticatesBeforeItRoutes()
    {
        // A bad token at 0 documents must get `unauthorized` for health, doc_list_open AND a
        // doc-needing tool — never `ok` (the r1 break: the synthesized lane skipped Process).
        var body = MethodBody(ServerSource(), "private Task<ForgeResult> ExecuteOnMainThreadAsync(");
        var verify = body.IndexOf("VerifyToken(command)", StringComparison.Ordinal);
        var route = body.IndexOf("DocumentContextPolicy.Route(", StringComparison.Ordinal);
        Assert.True(verify >= 0 && route > verify, "the token check must run before DocumentContextPolicy.Route so every lane — refusals included — is authenticated");
    }

    [Fact]
    public void TheZeroDocumentLaneRunsThroughProcessOnTheIdleMarshal()
    {
        var body = MethodBody(ServerSource(), "private Task<ForgeResult> ExecuteOnMainThreadAsync(");
        // The synthesized answer is gone: the lane reaches _processor.Process via the Idle marshal.
        Assert.DoesNotContain("ZeroDocumentAnswer", body, StringComparison.Ordinal);
        var lane = body.IndexOf("DocDispatchDecision.ZeroDocumentLane", StringComparison.Ordinal);
        var idle = body.IndexOf("ExecuteWhenIdleAsync(command)", StringComparison.Ordinal);
        Assert.True(lane >= 0 && idle > lane, "the zero-document lane must marshal to the Idle lane, not answer inline");

        var idleBody = MethodBody(ServerSource(), "private Task<ForgeResult> ExecuteWhenIdleAsync(");
        Assert.Contains("_processor.Process(command)", idleBody, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessKeepsItsOwnTokenCheckAndWritesTheAuditRecord()
    {
        var body = MethodBody(ProcessorSource(), "public ForgeResult Process(");
        var verify = body.IndexOf("VerifyToken(command)", StringComparison.Ordinal);
        var audit = body.IndexOf("_auditSink.WriteBestEffort(", StringComparison.Ordinal);
        Assert.True(verify >= 0, "Process must keep its own token check (single enforcement point)");
        Assert.True(audit > verify, "the audit record is written for calls that pass the token check");
    }

    [Fact]
    public void TheRealHealthHandlerCarriesTheFullFieldSet()
    {
        // good token at 0 documents ⇒ health answers with pipe, targetYear, hostYear,
        // documents: 0 and the hint — the fields the synthesized answer dropped (finding 2).
        var body = MethodBody(ProcessorSource(), "private ForgeResult Health(");
        Assert.Contains("pipe", body, StringComparison.Ordinal);
        Assert.Contains("targetYear", body, StringComparison.Ordinal);
        Assert.Contains("hostYear", body, StringComparison.Ordinal);
        Assert.Contains("documents", body, StringComparison.Ordinal);
        Assert.Contains("DocumentContextPolicy.ZeroDocumentHint", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDispatchRoutesThroughTheSharedPolicy()
    {
        var source = ServerSource();
        Assert.Contains("DocumentContextPolicy.Route(", source, StringComparison.Ordinal);
        Assert.Contains("no_active_document", source, StringComparison.Ordinal);
        Assert.Contains("no_command_context", source, StringComparison.Ordinal);
        // The decision must be taken before any marshal — never inside the command context.
        Assert.DoesNotContain("DocumentFreeTools = new", source, StringComparison.Ordinal);
    }
}
