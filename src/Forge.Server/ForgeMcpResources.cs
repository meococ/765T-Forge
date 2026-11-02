using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Forge.Shared;
using ModelContextProtocol.Server;

namespace Forge.Server;

[McpServerResourceType]
public sealed class ForgeMcpResources
{
    [McpServerResource(UriTemplate = "forge://docs/capability-matrix", Name = "CapabilityMatrix", Title = "Capability Matrix", MimeType = "text/markdown")]
    [Description("Capability matrix generated from the Forge tool registry.")]
    public static string CapabilityMatrix()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Capability matrix");
        sb.AppendLine();
        sb.AppendLine($"Generated from the Forge tool registry ({ForgeToolRegistry.All.Count} tools).");
        sb.AppendLine("Flags: RO = read-only, Dest = destructive, Unsafe = unsafe-gated, Backup = pre-write DWG backup, AutoCAD = needs the live plugin.");
        sb.AppendLine();
        foreach (var group in ForgeToolRegistry.All.GroupBy(t => t.Group).OrderBy(g => g.Key))
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();
            sb.AppendLine("| Tool | RO | Dest | Unsafe | Backup | AutoCAD |");
            sb.AppendLine("|------|----|------|--------|--------|---------|");
            foreach (var t in group.OrderBy(t => t.Name))
            {
                sb.AppendLine($"| `{t.Name}` | {(t.ReadOnly ? "yes" : "")} | {(t.Destructive ? "yes" : "")} | {(t.Unsafe ? "yes" : "")} | {(t.RequiresBackup ? "yes" : "")} | {(t.RequiresAutoCad ? "yes" : "")} |");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    [McpServerResource(UriTemplate = "forge://profiles/{name}", Name = "ToolProfile", Title = "Tool Profile", MimeType = "application/json")]
    [Description("JSON allowlist of tools for profile core|plot|qa|linework|authoring.")]
    public static string ToolProfile(string name)
    {
        if (!ToolProfiles.TryGet(name, out var tools))
        {
            return JsonSerializer.Serialize(new { error = "unknown_profile", known = ToolProfiles.Names }, ForgeJson.Options);
        }

        return JsonSerializer.Serialize(new { name, tools }, ForgeJson.Options);
    }

    [McpServerResource(UriTemplate = "forge://profiles", Name = "ToolProfiles", Title = "Tool Profiles", MimeType = "application/json")]
    [Description("List available tool profiles.")]
    public static string ToolProfileIndex()
        => JsonSerializer.Serialize(new { profiles = ToolProfiles.Names }, ForgeJson.Options);

    [McpServerResource(UriTemplate = "forge://safety", Name = "SafetyOverview", Title = "Safety Overview", MimeType = "text/markdown")]
    [Description("Agent-facing safety overview.")]
    public static string Safety() => SafetyText;

    // ---- Live drawing resources (item 6): answer straight from the running AutoCAD
    // through ForgeToolRunner; a dead pipe still returns a typed failure body, never a throw.

    [McpServerResource(UriTemplate = "forge://drawing/summary", Name = "ActiveDrawingSummary", Title = "Active Drawing Summary", MimeType = "application/json")]
    [Description("Live summary of the active drawing (layout, entity counts, extents) via forge_drawing_summary.")]
    public static async Task<string> DrawingSummary(ForgeToolRunner runner, CancellationToken cancellationToken)
        => await LiveAsync(runner, "forge_drawing_summary", cancellationToken);

    [McpServerResource(UriTemplate = "forge://drawing/layers", Name = "ActiveDrawingLayers", Title = "Active Drawing Layers", MimeType = "application/json")]
    [Description("Live layer table of the active drawing via forge_layer_list.")]
    public static async Task<string> DrawingLayers(ForgeToolRunner runner, CancellationToken cancellationToken)
        => await LiveAsync(runner, "forge_layer_list", cancellationToken);

    [McpServerResource(UriTemplate = "forge://drawing/layouts", Name = "ActiveDrawingLayouts", Title = "Active Drawing Layouts", MimeType = "application/json")]
    [Description("Live layout list of the active drawing via forge_doc_list_layouts.")]
    public static async Task<string> DrawingLayouts(ForgeToolRunner runner, CancellationToken cancellationToken)
        => await LiveAsync(runner, "forge_doc_list_layouts", cancellationToken);

    [McpServerResource(UriTemplate = "forge://drawing/xrefs", Name = "ActiveDrawingXrefs", Title = "Active Drawing Xrefs", MimeType = "application/json")]
    [Description("Live xref table of the active drawing via forge_xref_list.")]
    public static async Task<string> DrawingXrefs(ForgeToolRunner runner, CancellationToken cancellationToken)
        => await LiveAsync(runner, "forge_xref_list", cancellationToken);

    private static async Task<string> LiveAsync(ForgeToolRunner runner, string tool, CancellationToken cancellationToken)
    {
        try
        {
            var result = await runner.InvokeAsync(tool, new { }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, ForgeJson.Options);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(
                ForgeResult.Failure("", "resource_unavailable", $"{tool} could not answer: {ex.Message}"),
                ForgeJson.Options);
        }
    }

    // Self-contained agent-facing safety text (embedded; no repo-file reads).
    private const string SafetyText = """
        # Safety

        Forge is designed for AI agents that can mutate production DWGs. Safety is mandatory, not optional.

        ## Denylist scope

        `SafetyPolicy` applies a regex denylist to **open-world** executor payloads:
        `forge_exec_command`, `forge_exec_lisp`, `forge_run_script`, `forge_exec_dotnet`.
        Blocked patterns include (non-exhaustive): `ERASE ALL`, `PURGE`, `OVERKILL`, `RECOVER`,
        `AUDIT` with fix, layer delete commands, scoped broad `ALL`/`*` selection idioms
        (not every asterisk — e.g. `ZOOM *` and layer filters are allowed), and certain
        SAVEAS/WBLOCK + broad-selection combinations.
        Typed tools' structured business values are out of denylist scope.
        Read-only tools short-circuit to allow. Unknown tools fail closed as destructive.

        ## Dual evaluation vs headless

        - Named-pipe tools: safety runs on both the server and the plugin (same `SafetyPolicy`).
        - Headless AccoreConsole (`forge_run_script`, `forge_batch_run`): server-side only.

        ## Dry-run

        Write tools accept `dryRun=true` and return what they would do without changing the
        drawing. Use dry-run before publish, save, attribute campaigns, scripts, and executors.

        ## Backup and audit

        Before non-dry-run writes whose metadata sets `RequiresBackup`, Forge copies the active
        DWG to `FORGE_BACKUP_DIR`. Operations write JSONL audit records under `FORGE_AUDIT_DIR`
        — treat them as sensitive (paths, args); correlate by `AuditId`.

        ## Token

        Set `FORGE_AUTOCAD_TOKEN` to a long random value on both the MCP server and the AutoCAD
        process. Do not run shared or production hosts on a default pipe secret.

        ## Overwrite acknowledgement

        Destructive path overwrites require explicit acknowledgement (`overwriteAcknowledged`)
        on SaveAs / PDF plot / DSD publish / pack-and-go. Silent clobber is refused.

        ## Publish preflight

        `forge_qa_preflight` (and recipes that call it) can block publish on xref, titleblock
        tag, `####` field or standards-pack failures. Pass `force=true` only with human approval.

        ## Drawing number registry

        When a sheet register is loaded (`forge_registry_load`), attribute writes that claim a
        drawing number must match the registry — agents must not invent sheet numbers.

        ## Unsafe ops

        `forge_exec_dotnet` requires both `FORGE_ENABLE_UNSAFE_OPS=true` and per-call
        `unsafeAcknowledged=true`. Leave disabled unless a human enables it for the session —
        there is no sandbox.

        ## Timeout / write races

        If the plugin response times out, a write may already have committed — read back
        (`forge_qa_*`, list tools) before retrying; the watchdog answers `plugin_request_timeout`
        while the pipe keeps serving, and refused callbacks report `plugin_request_stale`.
        Non-retryable failures carry `retryable=false` and a `hint`; do not repeat an identical
        call after a non-retryable failure — change inputs or report instead.

        ## Agent rules of thumb

        Prefer typed tools → health first → load registry/pack when available → inspect →
        dry-run → write → verify → preflight → publish. Never invent drawing numbers.
        """;
}
