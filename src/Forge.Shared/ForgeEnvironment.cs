namespace Forge.Shared;

public sealed record ForgeEnvironment
{
    public string PipeName { get; init; } = ForgeConstants.DefaultPipeName;
    public string Token { get; init; } = "";
    public string BackupDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "765T-Forge",
        "backups");
    public string AuditDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "765T-Forge",
        "audit");
    public string AutoCadRoot { get; init; } = DefaultAutoCadRoot();
    public bool EnableUnsafeOps { get; init; }
    public int PluginResponseTimeoutSeconds { get; init; } = 120;
    public bool UsingDevDefaultToken { get; init; }

    /// <summary>When set, tools/list is limited to this profile. Unset means the full catalog.</summary>
    public string? ToolProfile { get; init; }

    public static string DefaultAutoCadRoot()
    {
        return @"C:\Program Files\Autodesk\AutoCAD 2026";
    }

    public static ForgeEnvironment FromProcess()
    {
        var token = FirstNonEmpty(
            Environment.GetEnvironmentVariable("FORGE_AUTOCAD_TOKEN"),
            Environment.GetEnvironmentVariable("MCP_AUTOCAD_TOKEN"));
        var allowDevDefault = bool.TryParse(
            Environment.GetEnvironmentVariable("FORGE_DEV_ALLOW_DEFAULT_TOKEN"),
            out var enabled) && enabled;

        if (string.IsNullOrWhiteSpace(token))
        {
            if (!allowDevDefault)
            {
                throw new InvalidOperationException(
                    "FORGE_AUTOCAD_TOKEN (or MCP_AUTOCAD_TOKEN) must be set. " +
                    "For local development only, set FORGE_DEV_ALLOW_DEFAULT_TOKEN=true.");
            }

            token = ForgeConstants.DevOnlyInsecureToken;
        }

        var toolProfile = Environment.GetEnvironmentVariable("FORGE_TOOL_PROFILE");
        if (!string.IsNullOrWhiteSpace(toolProfile) && !ToolProfiles.TryGet(toolProfile, out _))
        {
            throw new InvalidOperationException(
                $"FORGE_TOOL_PROFILE '{toolProfile}' is unknown. Known: {string.Join(", ", ToolProfiles.Names)}.");
        }

        return new ForgeEnvironment
        {
            PipeName = Get("FORGE_PIPE_NAME", ForgeConstants.DefaultPipeName),
            Token = token!,
            UsingDevDefaultToken = string.Equals(token, ForgeConstants.DevOnlyInsecureToken, StringComparison.Ordinal),
            BackupDirectory = Get("FORGE_BACKUP_DIR", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "765T-Forge",
                "backups")),
            AuditDirectory = Get("FORGE_AUDIT_DIR", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "765T-Forge",
                "audit")),
            AutoCadRoot = Get("AUTOCAD_2026_ROOT", DefaultAutoCadRoot()),
            EnableUnsafeOps = bool.TryParse(Environment.GetEnvironmentVariable("FORGE_ENABLE_UNSAFE_OPS"), out var unsafeOps) && unsafeOps,
            PluginResponseTimeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS"), out var timeout)
                ? Math.Clamp(timeout, 5, 3600)
                : 120,
            ToolProfile = string.IsNullOrWhiteSpace(toolProfile) ? null : toolProfile
        };
    }

    private static string Get(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
