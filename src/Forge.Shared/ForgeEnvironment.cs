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
    public int PluginConnectTimeoutSeconds { get; init; } = 10;
    public int PluginResponseTimeoutSeconds { get; init; } = 120;
    public bool UsingDevDefaultToken { get; init; }

    /// <summary>
    /// Default accoreconsole root when AUTOCAD_2026_ROOT is unset: the newest installed plain
    /// "AutoCAD 20xx" directory (Program Files\Autodesk, then fixed-drive roots for non-default
    /// installs like D:\AutoCAD 2025), falling back to the documented 2026 path when nothing
    /// matches. The env var stays the manual override for whichever year is being driven.
    /// </summary>
    public static string DefaultAutoCadRoot()
    {
        try
        {
            var candidates = new List<(string Dir, int Year)>();
            CollectAutoCadRoots(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Autodesk"),
                candidates);
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                {
                    continue;
                }

                CollectAutoCadRoots(drive.Name, candidates);
            }

            return candidates
                .OrderByDescending(x => x.Year)
                .Select(x => x.Dir)
                .FirstOrDefault() ?? @"C:\Program Files\Autodesk\AutoCAD 2026";
        }
        catch
        {
            return @"C:\Program Files\Autodesk\AutoCAD 2026";
        }
    }

    private static void CollectAutoCadRoots(string directory, List<(string Dir, int Year)> candidates)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(directory, "AutoCAD 20*"))
            {
                var year = ParseAutoCadYear(Path.GetFileName(dir));
                // Only real installs count — an empty leftover "AutoCAD 20xx" folder must not
                // beat an older but working install (AutoCadRoot feeds accoreconsole resolution).
                if (year is not null && File.Exists(Path.Combine(dir, "accoreconsole.exe")))
                {
                    candidates.Add((dir, year.Value));
                }
            }
        }
        catch
        {
            // Unreadable drive or root — skip it.
        }
    }

    private static int? ParseAutoCadYear(string name)
    {
        const string prefix = "AutoCAD ";
        if (name.Length <= prefix.Length || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return int.TryParse(name[prefix.Length..], out var year) && year is >= 2004 and <= 2099
            ? year
            : null;
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
            PluginConnectTimeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("FORGE_PLUGIN_CONNECT_TIMEOUT_SECONDS"), out var connectTimeout)
                ? Math.Clamp(connectTimeout, 1, 120)
                : 10,
            PluginResponseTimeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS"), out var timeout)
                ? Math.Clamp(timeout, 5, 3600)
                : 120
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
