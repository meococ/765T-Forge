using System.ComponentModel;
using System.Text.Json;
using Forge.Shared;
using ModelContextProtocol.Server;

namespace Forge.Server;

[McpServerResourceType]
public sealed class ForgeMcpResources
{
    [McpServerResource(UriTemplate = "forge://docs/capability-matrix", Name = "CapabilityMatrix", MimeType = "text/markdown")]
    [Description("Honest shipped capability matrix for 765T-Forge tools.")]
    public static string CapabilityMatrix()
        => TryReadRepoFile("docs/capability-matrix.md")
           ?? "# Capability matrix\nSee repository docs/capability-matrix.md";

    [McpServerResource(UriTemplate = "forge://profiles/{name}", Name = "ToolProfile", MimeType = "application/json")]
    [Description("JSON allowlist of tools for profile core|plot|qa.")]
    public static string ToolProfile(string name)
    {
        if (!ToolProfiles.TryGet(name, out var tools))
        {
            return JsonSerializer.Serialize(new { error = "unknown_profile", known = ToolProfiles.Names }, ForgeJson.Options);
        }

        return JsonSerializer.Serialize(new { name, tools }, ForgeJson.Options);
    }

    [McpServerResource(UriTemplate = "forge://profiles", Name = "ToolProfiles", MimeType = "application/json")]
    [Description("List available tool profiles.")]
    public static string ToolProfileIndex()
        => JsonSerializer.Serialize(new { profiles = ToolProfiles.Names }, ForgeJson.Options);

    [McpServerResource(UriTemplate = "forge://safety", Name = "SafetyOverview", MimeType = "text/markdown")]
    [Description("Agent-facing safety overview.")]
    public static string Safety()
        => TryReadRepoFile("docs/safety.md")
           ?? "# Safety\nSee repository docs/safety.md";

    private static string? TryReadRepoFile(string relative)
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            // Prefer docs next to the published exe (Release zip), then walk up for repo checkouts.
            for (var i = 0; i < 8; i++)
            {
                var candidate = Path.GetFullPath(Path.Combine(baseDir, relative));
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }

                var parent = Directory.GetParent(baseDir);
                if (parent is null)
                {
                    break;
                }

                baseDir = parent.FullName;
            }
        }
        catch
        {
            // Best effort for MCP resource.
        }

        return null;
    }
}
