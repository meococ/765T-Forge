using Forge.Server;
using Forge.Shared;
using ModelContextProtocol.Server;

namespace Forge.Tests;

public sealed class ToolMetadataTests
{
    [Fact]
    public void HotPathToolsAreRegisteredInSharedMetadata()
    {
        var names = ForgeToolRegistry.All.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("forge_xref_repath", names);
        Assert.Contains("forge_layer_state_restore", names);
        Assert.Contains("forge_layout_page_setup_import", names);
        Assert.Contains("forge_plot_publish", names);
        Assert.Contains("forge_qa_verify_titleblock", names);
        Assert.Contains("forge_registry_load", names);
        Assert.Contains("forge_pack_load", names);
        Assert.Contains("forge_viewport_list", names);
    }

    [Fact]
    public void McpAttributesExposeExpectedSafetyHints()
    {
        var toolAttributes = typeof(ForgeMcpTools)
            .GetMethods()
            .Select(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false).Cast<McpServerToolAttribute>().SingleOrDefault())
            .Where(a => a is not null)
            .Cast<McpServerToolAttribute>()
            .ToDictionary(a => a.Name ?? "", StringComparer.OrdinalIgnoreCase);

        Assert.True(toolAttributes["forge_system_health"].ReadOnly);
        Assert.False(toolAttributes["forge_system_health"].Destructive);
        Assert.False(toolAttributes["forge_system_health"].OpenWorld);

        Assert.False(toolAttributes["forge_exec_command"].ReadOnly);
        Assert.True(toolAttributes["forge_exec_command"].Destructive);
        Assert.True(toolAttributes["forge_exec_command"].OpenWorld);
    }

    [Fact]
    public void McpToolsMatchRegistryOneToOne()
    {
        var mcpNames = typeof(ForgeMcpTools)
            .GetMethods()
            .Select(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false).Cast<McpServerToolAttribute>().SingleOrDefault())
            .Where(a => a?.Name is not null)
            .Select(a => a!.Name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var registryNames = ForgeToolRegistry.All.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingInRegistry = mcpNames.Where(n => !registryNames.Contains(n)).OrderBy(x => x).ToArray();
        var missingInMcp = registryNames.Where(n => !mcpNames.Contains(n)).OrderBy(x => x).ToArray();

        Assert.True(missingInRegistry.Length == 0, "MCP tools missing from registry: " + string.Join(", ", missingInRegistry));
        Assert.True(missingInMcp.Length == 0, "Registry tools missing from MCP: " + string.Join(", ", missingInMcp));
    }

    [Fact]
    public void McpReadOnlyHintsAlignWithRegistry()
    {
        var toolAttributes = typeof(ForgeMcpTools)
            .GetMethods()
            .Select(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false).Cast<McpServerToolAttribute>().SingleOrDefault())
            .Where(a => a?.Name is not null)
            .Cast<McpServerToolAttribute>();

        foreach (var attr in toolAttributes)
        {
            var meta = ForgeToolRegistry.Get(attr.Name!);
            Assert.Equal(meta.ReadOnly, attr.ReadOnly);
            Assert.Equal(meta.Destructive, attr.Destructive);
            Assert.Equal(meta.Idempotent, attr.Idempotent);
            Assert.Equal(meta.OpenWorld, attr.OpenWorld);
        }
    }

    [Fact]
    public void AuthoringWaveSafetyContract()
    {
        // docs/cad-authoring-tools.md C20–C23: draw tools and block_insert are additive-only
        // (no pre-write DWG copy), every entity mutation is destructive with backup, and no
        // authoring tool is OpenWorld (typed params only — SafetyPolicy allow-lists them).
        foreach (var name in new[]
                 {
                     "forge_draw_line", "forge_draw_polyline", "forge_draw_circle", "forge_draw_arc",
                     "forge_draw_text", "forge_draw_mtext", "forge_draw_hatch", "forge_draw_dimension",
                     "forge_draw_batch", "forge_block_insert", "forge_layer_create",
                     "forge_draw_rectangle", "forge_draw_polygon", "forge_draw_ellipse", "forge_draw_spline",
                     "forge_draw_revcloud", "forge_draw_leader", "forge_draw_table",
                     "forge_linetype_load",
                     "forge_draw_grid", "forge_draw_wall"
                 })
        {
            var tool = ForgeToolRegistry.Get(name);
            Assert.False(tool.ReadOnly);
            Assert.False(tool.Destructive);
            Assert.False(tool.RequiresBackup);
            Assert.False(tool.OpenWorld);
        }

        foreach (var name in new[]
                 {
                     "forge_entity_move", "forge_entity_copy", "forge_entity_rotate", "forge_entity_scale",
                     "forge_entity_mirror", "forge_entity_offset", "forge_entity_erase", "forge_entity_set_props",
                     "forge_block_create",
                     "forge_entity_fillet", "forge_entity_chamfer", "forge_entity_trim",
                     "forge_entity_extend", "forge_entity_join", "forge_entity_explode"
                 })
        {
            var tool = ForgeToolRegistry.Get(name);
            Assert.False(tool.ReadOnly);
            Assert.True(tool.Destructive);
            Assert.True(tool.RequiresBackup);
            Assert.False(tool.OpenWorld);
        }

        Assert.True(ForgeToolRegistry.Get("forge_entity_get").ReadOnly);
        Assert.True(ForgeToolRegistry.Get("forge_entity_list").ReadOnly);
        Assert.True(ForgeToolRegistry.Get("forge_block_list").ReadOnly);
        Assert.True(ForgeToolRegistry.Get("forge_entity_intersect").ReadOnly);
        Assert.True(ForgeToolRegistry.Get("forge_drawing_summary").ReadOnly);
        Assert.True(ForgeToolRegistry.Get("forge_entity_search_text").ReadOnly);
        Assert.True(ForgeToolRegistry.Get("forge_entity_snap").ReadOnly);
        Assert.True(ForgeToolRegistry.Get("forge_layer_create").Idempotent);
        Assert.True(ForgeToolRegistry.Get("forge_linetype_load").Idempotent);

        // C24 — capture/measure/export never rewrite the drawing; array is a mutation with
        // backup; the CSV importer is additive like the other draw tools.
        Assert.True(ForgeToolRegistry.Get("forge_measure_batch").ReadOnly);
        foreach (var name in new[] { "forge_view_capture", "forge_doc_export_dxf", "forge_doc_new" })
        {
            var tool = ForgeToolRegistry.Get(name);
            Assert.False(tool.ReadOnly);
            Assert.False(tool.Destructive);
            Assert.False(tool.RequiresBackup);
        }

        var arrayTool = ForgeToolRegistry.Get("forge_entity_array");
        Assert.True(arrayTool.Destructive);
        Assert.True(arrayTool.RequiresBackup);
        var csvTool = ForgeToolRegistry.Get("forge_draw_polyline_from_csv");
        Assert.False(csvTool.Destructive);
        Assert.False(csvTool.RequiresBackup);
        Assert.True(ForgeToolRegistry.Get("forge_layer_set").Idempotent);
    }

    [Fact]
    public void StyleToolsAreDestructiveWithBackupButNeedNoAcknowledgement()
    {
        // C27: style_text_create / style_dim_create refuse an existing style (style_exists) unless
        // updateExisting=true; in update mode they restyle existing annotations, so they carry a
        // pre-write backup. Same params → same style state (idempotent). Typed params only, so
        // SafetyPolicy allows them without any extra acknowledgement.
        var policy = new SafetyPolicy();
        foreach (var name in new[] { "forge_style_text_create", "forge_style_dim_create" })
        {
            var tool = ForgeToolRegistry.Get(name);
            Assert.Equal("style", tool.Group);
            Assert.False(tool.ReadOnly);
            Assert.True(tool.Destructive);
            Assert.True(tool.RequiresBackup);
            Assert.True(tool.Idempotent);
            Assert.False(tool.OpenWorld);
            Assert.False(tool.Unsafe);

            var decision = policy.Evaluate(new ForgeCommand
            {
                Tool = name,
                Args = ForgeJson.ToElement(new { name = "Standard", updateExisting = true })
            });
            Assert.True(decision.Allowed, $"{name}: {decision.Code}");
        }
    }

    [Fact]
    public void ReliabilityWaveToolsContract()
    {
        // forge_doc_activate switches the active drawing only: it writes nothing to any DWG (no backup,
        // not destructive), repeating it lands in the same state (idempotent), typed params only.
        var activate = ForgeToolRegistry.Get("forge_doc_activate");
        Assert.Equal("document", activate.Group);
        Assert.False(activate.ReadOnly);
        Assert.False(activate.Destructive);
        Assert.True(activate.Idempotent);
        Assert.False(activate.OpenWorld);
        Assert.False(activate.RequiresBackup);
        Assert.False(activate.Unsafe);

        // forge_text_list is a pure read (block-aware text content).
        var textList = ForgeToolRegistry.Get("forge_text_list");
        Assert.True(textList.ReadOnly);
        Assert.False(textList.RequiresBackup);

        var policy = new SafetyPolicy();
        Assert.True(policy.Evaluate(new ForgeCommand { Tool = "forge_doc_activate", Args = ForgeJson.ToElement(new { name = "Plan1.dwg" }) }).Allowed);
        Assert.True(policy.Evaluate(new ForgeCommand { Tool = "forge_text_list", Args = ForgeJson.ToElement(new { includeBlocks = true }) }).Allowed);

        // exec_lisp stays behind the unsafe acknowledgement even though it now returns its value synchronously.
        Assert.True(ForgeToolRegistry.Get("forge_exec_lisp").Unsafe);
        Assert.False(policy.Evaluate(new ForgeCommand { Tool = "forge_exec_lisp", Args = ForgeJson.ToElement(new { lisp = "(+ 1 2)" }) }).Allowed);

        foreach (var profile in new[] { "core", "linework", "authoring" })
        {
            Assert.True(ToolProfiles.TryGet(profile, out var tools));
            Assert.Contains("forge_doc_activate", tools);
            Assert.Contains("forge_text_list", tools);
        }
    }

    [Fact]
    public void McpToolAnnotationsAreConsistent()
    {
        foreach (var meta in ForgeToolRegistry.All)
        {
            if (meta.ReadOnly)
            {
                Assert.False(meta.Destructive, $"{meta.Name} cannot be both ReadOnly and Destructive.");
                Assert.True(meta.Idempotent, $"{meta.Name} is ReadOnly so it must be Idempotent.");
                Assert.False(meta.RequiresBackup, $"{meta.Name} is ReadOnly so it must not require backup.");
            }
        }

        // Artifact-writing tools must not advertise ReadOnly=true.
        Assert.False(ForgeToolRegistry.Get("forge_qa_preflight").ReadOnly);
        Assert.False(ForgeToolRegistry.Get("forge_sheet_inventory_import").ReadOnly);
        foreach (var lw in new[]
                 {
                     "forge_linework_dump", "forge_linework_topology", "forge_linework_coverage",
                     "forge_linework_trace", "forge_linework_segments", "forge_linework_transform",
                     "forge_linework_compare"
                 })
        {
            Assert.False(ForgeToolRegistry.Get(lw).ReadOnly, $"{lw} writes outputPath/overlayPath/transformPath and must not be ReadOnly.");
        }
    }

    [Fact]
    public void ServerOptionsExposeClearIdentityAndInstructions()
    {
        var mcp = new ModelContextProtocol.Server.McpServerOptions();
        ForgeMcpHost.ApplyServerOptions(mcp);

        Assert.Equal(ForgeConstants.ServerName, mcp.ServerInfo?.Name);
        Assert.Equal(ForgeConstants.ProductVersion, mcp.ServerInfo?.Version);
        Assert.Contains("forge_system_health", mcp.ServerInstructions);
        Assert.Contains("dryRun", mcp.ServerInstructions);
    }

    [Fact]
    public void BusinessFailuresBecomeMcpErrorResultsWithStructuredPayload()
    {
        var failure = ForgeResult.Failure("cmd-1", "dwg_not_found", "DWG missing.");
        var callResult = new ModelContextProtocol.Protocol.CallToolResult
        {
            Content =
            [
                new ModelContextProtocol.Protocol.TextContentBlock
                {
                    Text = System.Text.Json.JsonSerializer.Serialize(failure, ForgeJson.Options)
                }
            ]
        };

        var filtered = ForgeCallToolResults.MarkBusinessFailure(callResult);
        Assert.True(filtered.IsError);

        var ok = ForgeResult.Success("cmd-2", new { status = "ok" });
        var okCallResult = new ModelContextProtocol.Protocol.CallToolResult
        {
            Content =
            [
                new ModelContextProtocol.Protocol.TextContentBlock
                {
                    Text = System.Text.Json.JsonSerializer.Serialize(ok, ForgeJson.Options)
                }
            ]
        };

        var okFiltered = ForgeCallToolResults.MarkBusinessFailure(okCallResult);
        Assert.NotEqual(true, okFiltered.IsError);
    }

    [Fact]
    public void BusinessFailureKeepsSummaryAndFullEnvelopeForTextOnlyClients()
    {
        var failure = ForgeResult.Failure(
            "cmd-3",
            "linetype_not_found_in_file",
            "Linetype(s) not defined in acadiso.lin: FOO",
            "Use standard names.",
            data: new { names = new[] { "FOO" }, loadedLinetypes = new[] { "Continuous" } });
        var callResult = new ModelContextProtocol.Protocol.CallToolResult
        {
            Content =
            [
                new ModelContextProtocol.Protocol.TextContentBlock
                {
                    Text = System.Text.Json.JsonSerializer.Serialize(failure, ForgeJson.Options)
                }
            ]
        };

        var filtered = ForgeCallToolResults.MarkBusinessFailure(callResult);
        Assert.True(filtered.IsError);
        var texts = filtered.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(b => b.Text).ToArray();
        Assert.Equal(2, texts.Length);

        // Block 1: short human summary.
        Assert.Contains("linetype_not_found_in_file", texts[0]);
        Assert.Contains("Use standard names.", texts[0]);

        // Block 2: the full envelope, data included.
        using var envelope = System.Text.Json.JsonDocument.Parse(texts[1]);
        var root = envelope.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("linetype_not_found_in_file", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("FOO", root.GetProperty("data").GetProperty("names")[0].GetString());
        Assert.Equal("Continuous", root.GetProperty("data").GetProperty("loadedLinetypes")[0].GetString());
    }

    [Fact]
    public void EveryProfileToolExistsInRegistry()
    {
        var registry = ForgeToolRegistry.All.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(ToolProfiles.Names);
        foreach (var profile in ToolProfiles.Names)
        {
            Assert.True(ToolProfiles.TryGet(profile, out var tools), $"Profile '{profile}' listed in Names but TryGet failed.");
            Assert.NotEmpty(tools);
            var unknown = tools.Where(t => !registry.Contains(t)).ToArray();
            Assert.True(unknown.Length == 0, $"Profile '{profile}' references unregistered tools: {string.Join(", ", unknown)}");
            var duplicates = tools.GroupBy(t => t, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
            Assert.True(duplicates.Length == 0, $"Profile '{profile}' lists tools twice: {string.Join(", ", duplicates)}");
        }
    }

    [Fact]
    public void RegistryCountEqualsMcpServerToolAttributesInSource()
    {
        var source = File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Server"), "ForgeMcpTools.cs"));
        var names = System.Text.RegularExpressions.Regex
            .Matches(source, @"\[McpServerTool\(\s*Name\s*=\s*""(forge_[a-z0-9_]+)""")
            .Select(m => m.Groups[1].Value)
            .ToArray();
        var attributeCount = System.Text.RegularExpressions.Regex.Matches(source, @"\[McpServerTool\(").Count;

        Assert.Equal(attributeCount, names.Length); // every attribute carries an explicit Name
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(ForgeToolRegistry.All.Count, names.Length);
        var registry = ForgeToolRegistry.All.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.True(registry.SetEquals(names), "Registry and [McpServerTool] names differ.");
    }

    private sealed class MpcOptionsProbe;
}
