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

public sealed class D13ZeroDocumentAnswerTests
{
    [Fact]
    public void HealthAnswersOkWithZeroDocumentsAndAHint()
    {
        var result = DocumentContextPolicy.ZeroDocumentAnswer("cmd-1", "forge_system_health");
        Assert.NotNull(result);
        Assert.True(result!.Ok);
        Assert.Null(result.Error);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Data);
        Assert.Contains("\"documents\":0", json, StringComparison.Ordinal);
        Assert.Contains("forge_doc_new", json, StringComparison.Ordinal);
        Assert.Contains("forge_doc_open", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DocListOpenAnswersWithAnEmptyDocumentList()
    {
        var result = DocumentContextPolicy.ZeroDocumentAnswer("cmd-2", "forge_doc_list_open");
        Assert.NotNull(result);
        Assert.True(result!.Ok);
        Assert.Contains("\"documents\":[]", System.Text.Json.JsonSerializer.Serialize(result.Data), StringComparison.Ordinal);
    }

    [Fact]
    public void VersionStillRidesTheIdleMarshal()
    {
        Assert.Null(DocumentContextPolicy.ZeroDocumentAnswer("cmd-3", "forge_system_version"));
    }

    [Fact]
    public void DocumentToolsNeverGetASynthesizedAnswer()
    {
        Assert.Null(DocumentContextPolicy.ZeroDocumentAnswer("cmd-4", "forge_draw_line"));
        Assert.Null(DocumentContextPolicy.ZeroDocumentAnswer("cmd-5", "forge_plot_to_pdf"));
    }
}

/// <summary>D13 plugin wiring guards.</summary>
public sealed class D13ZeroDocumentSourceGuards
{
    private static string ServerSource()
        => File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), "NamedPipePluginServer.cs"));

    [Fact]
    public void TheDispatchRoutesThroughTheSharedPolicy()
    {
        var source = ServerSource();
        Assert.Contains("DocumentContextPolicy.Route(", source, StringComparison.Ordinal);
        Assert.Contains("DocumentContextPolicy.ZeroDocumentAnswer(", source, StringComparison.Ordinal);
        Assert.Contains("no_active_document", source, StringComparison.Ordinal);
        Assert.Contains("no_command_context", source, StringComparison.Ordinal);
        // The decision must be taken before any marshal — never inside the command context.
        Assert.DoesNotContain("DocumentFreeTools = new", source, StringComparison.Ordinal);
    }
}
