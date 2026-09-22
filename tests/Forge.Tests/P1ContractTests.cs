using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Forge.Server;
using Forge.Shared;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Forge.Tests;

public sealed class P1ContractTests
{
    [Fact]
    public void OutputSchemasArePerToolAndRequireContractFields()
    {
        var tools = typeof(ForgeMcpTools)
            .GetMethods()
            .Select(method => new
            {
                Method = method,
                Attribute = method.GetCustomAttributes(typeof(McpServerToolAttribute), false).Cast<McpServerToolAttribute>().SingleOrDefault()
            })
            .Where(item => item.Attribute?.Name is not null)
            .ToArray();

        Assert.Equal(56, tools.Length);
        var schemas = tools.Select(item => item.Attribute!.OutputSchemaType).ToArray();
        Assert.DoesNotContain(schemas, schema => schema == typeof(ForgeResult));
        Assert.Equal(schemas.Length, schemas.Distinct().Count());

        foreach (var tool in tools)
        {
            var returned = tool.Method.ReturnType.GetGenericArguments().Single();
            Assert.Equal(returned, tool.Attribute!.OutputSchemaType);
        }

        Assert.True(HasJsonRequired(typeof(ExecEnvelope<ExecCommandData>), "Queued"));
        Assert.True(HasJsonRequired(typeof(ExecEnvelope<ExecCommandData>), "Completed"));
        Assert.True(HasJsonRequired(typeof(QaEnvelope<QaCheckData>), "Passed"));
        Assert.True(HasJsonRequired(typeof(PublishEnvelope<PlotPublishData>), "VerificationPassed"));
    }

    [Fact]
    public void EveryToolParameterHasADescription()
    {
        var methods = typeof(ForgeMcpTools).GetMethods()
            .Where(method => method.GetCustomAttributes(typeof(McpServerToolAttribute), false).Length > 0);

        foreach (var method in methods)
        {
            foreach (var parameter in method.GetParameters())
            {
                if (parameter.ParameterType == typeof(ForgeToolRunner) || parameter.ParameterType == typeof(CancellationToken))
                {
                    continue;
                }

                if (parameter.ParameterType.IsGenericType && parameter.ParameterType.GetGenericTypeDefinition() == typeof(IProgress<>))
                {
                    continue;
                }

                var description = parameter.GetCustomAttribute<DescriptionAttribute>();
                Assert.False(string.IsNullOrWhiteSpace(description?.Description), $"{method.Name}.{parameter.Name}");
            }
        }

        foreach (var property in typeof(CampaignEntryDto).GetProperties().Concat(typeof(BatchJobDto).GetProperties()))
        {
            var description = property.GetCustomAttribute<DescriptionAttribute>();
            Assert.False(string.IsNullOrWhiteSpace(description?.Description), property.Name);
        }
    }

    [Fact]
    public void OverwriteToolsAdvertiseDestructiveHint()
    {
        string[] names =
        [
            "forge_system_setvar",
            "forge_xref_repath",
            "forge_xref_normalize_relative",
            "forge_layer_state_restore",
            "forge_block_set_attr",
            "forge_block_campaign",
            "forge_layout_page_setup_import",
            "forge_layout_page_setup_apply",
            "forge_plot_to_pdf",
            "forge_plot_publish",
            "forge_recipe_issue_set"
        ];

        var attributes = typeof(ForgeMcpTools).GetMethods()
            .Select(method => method.GetCustomAttributes(typeof(McpServerToolAttribute), false).Cast<McpServerToolAttribute>().SingleOrDefault())
            .Where(attribute => attribute?.Name is not null)
            .ToDictionary(attribute => attribute!.Name!, StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            Assert.True(ForgeToolRegistry.Get(name).Destructive, name);
            Assert.True(attributes[name]!.Destructive, name);
        }

        Assert.False(ForgeToolRegistry.Get("forge_batch_run").OpenWorld);
    }

    [Fact]
    public void PlotProfileIncludesHotPathReads()
    {
        Assert.True(ToolProfiles.TryGet("plot", out var tools));
        Assert.Contains("forge_doc_list_layouts", tools);
        Assert.Contains("forge_registry_lookup", tools);
        Assert.Contains("forge_block_campaign", tools);
        Assert.Contains("forge_xref_list", tools);
    }

    [Fact]
    public void UnknownProfileResourceIsAProtocolError()
    {
        var ex = Assert.Throws<McpProtocolException>(() => ForgeMcpResources.ToolProfile("nope"));
        Assert.Equal(McpErrorCode.ResourceNotFound, ex.ErrorCode);
    }

    [Fact]
    public void ExecAndQaProjectionsLiftRequiredFlags()
    {
        var queued = ForgeResult.Failure("c1", "queued_not_completed", "queued", data: new { queued = true, completed = false, mode = "queued" });
        var exec = ForgeOutputMap.Exec<ExecCommandData>(queued);
        Assert.True(exec.Queued);
        Assert.False(exec.Completed);
        Assert.False(exec.Ok);

        var gate = ForgeResult.Gate("c2", false, "qa_failed", "no", null, new { passed = false });
        var qa = ForgeOutputMap.Qa<QaCheckData>(gate);
        Assert.False(qa.Passed);
        Assert.False(qa.Ok);

        var publish = ForgeResult.Success("c3", new { receipt = new { verificationPassed = true } }, verification: new ForgeVerification { Attempted = true, Passed = true });
        var projected = ForgeOutputMap.Publish<PlotPublishData>(publish);
        Assert.True(projected.VerificationPassed);
    }

    [Fact]
    public void AuditDecisionIsNotCompletedWhenTheCallFails()
    {
        var failed = ForgeResult.Failure("id", "qa_failed", "no");
        Assert.Equal("qa_failed", AuditDecisions.For(failed, dryRun: false));
        Assert.NotEqual("completed", AuditDecisions.For(failed, dryRun: false));

        var ok = ForgeResult.Success("id", new { saved = true });
        Assert.Equal("result_recorded", AuditDecisions.For(ok, dryRun: false));
        Assert.Equal("dry_run", AuditDecisions.For(ok, dryRun: true));
        Assert.True(AuditDecisions.BlocksDispatchWhenUnwritten(ForgeToolRegistry.Get("forge_exec_command")));
        Assert.False(AuditDecisions.BlocksDispatchWhenUnwritten(ForgeToolRegistry.Get("forge_system_health")));
    }

    [Fact]
    public void SysvarBlockListDoesNotChangeTheCommandDenylist()
    {
        Assert.True(SysvarPolicy.IsForbidden("SECURELOAD"));
        Assert.True(SysvarPolicy.IsForbidden("TRUSTEDPATHS"));
        Assert.False(SysvarPolicy.IsForbidden("FILEDIA"));

        var policy = new SafetyPolicy();
        var decision = policy.Evaluate(new ForgeCommand
        {
            Tool = "forge_system_setvar",
            Args = ForgeJson.ToElement(new { name = "SECURELOAD", value = "0" })
        });
        Assert.True(decision.Allowed);

        var zoom = policy.EvaluateText("forge_exec_command", "ZOOM *");
        Assert.True(zoom.Allowed);
    }

    [Fact]
    public void PurgeAndUnsafeSuggestionsNameRealControls()
    {
        var policy = new SafetyPolicy();
        var purge = policy.EvaluateText("forge_exec_command", "PURGE");
        Assert.Equal("deny_purge", purge.Code);
        Assert.Contains("no purge tool", purge.Suggestion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("typed purge", purge.Suggestion, StringComparison.OrdinalIgnoreCase);

        var unsafeDecision = policy.Evaluate(new ForgeCommand
        {
            Tool = "forge_exec_dotnet",
            Args = ForgeJson.ToElement(new { code = "1" })
        });
        Assert.Contains("FORGE_ENABLE_UNSAFE_OPS", unsafeDecision.Suggestion, StringComparison.Ordinal);
        Assert.Contains("no enable_unsafe_ops", unsafeDecision.Suggestion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScriptTranscriptAndSheetLabelStayHonest()
    {
        Assert.True(AccoreConsoleTranscript.HasScriptError("Command: *Cancel*", ""));
        Assert.True(AccoreConsoleTranscript.HasScriptError("", "Unknown command \"FOO\""));
        Assert.False(AccoreConsoleTranscript.HasScriptError("Command: ZOOM\n*All*", ""));
        Assert.Equal("SinglePdf", DsdWriter.SheetTypeLabel(true));
        Assert.Equal("MultiPdf", DsdWriter.SheetTypeLabel(false));
        Assert.Equal(6, DsdWriter.TypeCode(true));
        Assert.Equal(7, DsdWriter.TypeCode(false));
    }

    [Fact]
    public void TitleblockGateFailsClosed()
    {
        var findings = TitleblockPreflight.Evaluate(
            [
                new TitleblockSample("1A", "DWG_NO", "MTR-1"),
                new TitleblockSample("2B", "DWG_NO", "####")
            ],
            ["DWG_NO"]);
        Assert.Contains(findings, finding => finding.Code == "titleblock_tag_empty" && finding.Severity == "error");

        var missing = TitleblockPreflight.MissingRequirements(hasCallerTags: false, hasPackTags: false);
        Assert.Equal("preflight_no_titleblock_requirements", missing?.Code);
        Assert.Null(TitleblockPreflight.MissingRequirements(true, false));
    }

    [Fact]
    public void PackCompareTreatsMissingDeviceAsMismatchAndMissingLayerAsError()
    {
        var pack = new StandardsPack { PackId = "p", PlotDevice = "DWG To PDF.pc3", Layers = ["A-ANNO"] };
        var unbound = pack.EvaluatePlotBindings(null, null, null, backgroundPlot: 0);
        Assert.Contains(unbound, finding => finding.Code == "pack_plot_device_mismatch" && finding.Severity == "error");

        var matched = pack.EvaluatePlotBindings("DWG To PDF.pc3", null, null, 0);
        Assert.DoesNotContain(matched, finding => finding.Code == "pack_plot_device_mismatch");

        var layers = pack.EvaluateLayers(["0"]);
        Assert.Contains(layers, finding => finding.Code == "pack_layer_missing" && finding.Severity == "error");
    }

    [Fact]
    public void HostMismatchUsesSeriesNotTheConstantYearAlone()
    {
        Assert.False(AutoCadHostInfo.IsMismatch("25.1.0.0", "2026"));
        Assert.True(AutoCadHostInfo.IsMismatch("24.3", "2026"));
        Assert.False(AutoCadHostInfo.IsMismatch("AutoCAD 2026", "2026"));
    }

    [Fact]
    public async Task AuditWriteReportsSuccess()
    {
        var dir = Path.Combine(Path.GetTempPath(), "765T-Forge-Tests", Guid.NewGuid().ToString("N"));
        var sink = new FileAuditSink(dir);
        var write = await sink.WriteAsync(new AuditRecord
        {
            Source = "test",
            Tool = "forge_system_health",
            CommandId = "1",
            Allowed = true,
            Args = ForgeJson.ToElement(new { })
        });
        Assert.True(write.Written);
        Assert.False(string.IsNullOrWhiteSpace(write.AuditId));
    }

    private static bool HasJsonRequired(Type type, string propertyName)
    {
        var property = type.GetProperty(propertyName);
        Assert.NotNull(property);
        return property!.GetCustomAttributes(typeof(JsonRequiredAttribute), inherit: true).Length > 0;
    }
}
