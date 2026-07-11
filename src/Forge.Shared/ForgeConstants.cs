namespace Forge.Shared;

public static class ForgeConstants
{
    public const string DefaultPipeName = "765T.Forge.AutoCAD";
    public const string ServerName = "765T-Forge";

    /// <summary>Product SemVer shipped in releases and CHANGELOG.</summary>
    public const string ProductVersion = "0.2.1";

    /// <summary>Named-pipe / JSON envelope contract version.</summary>
    public const string EnvelopeVersion = "forge.envelope.v1";

    /// <summary>Backward-compatible alias for ProductVersion (legacy field name).</summary>
    public const string ProtocolVersion = ProductVersion;

    public const string AutoCadVersion = "2026";

    /// <summary>Only used when FORGE_DEV_ALLOW_DEFAULT_TOKEN=true. Never for production.</summary>
    public const string DevOnlyInsecureToken = "dev-only-insecure-token";
}
