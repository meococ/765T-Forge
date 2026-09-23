namespace Forge.Shared;

public static class ForgeConstants
{
    public const string DefaultPipeName = "765T.Forge.AutoCAD";
    public const string ServerName = "765T-Forge";

    /// <summary>Product SemVer shipped in releases and CHANGELOG.</summary>
    public const string ProductVersion = "0.3.0";

    /// <summary>Named-pipe / JSON envelope contract version.</summary>
    public const string EnvelopeVersion = "forge.envelope.v1";

    /// <summary>Backward-compatible alias for ProductVersion (legacy field name).</summary>
    public const string ProtocolVersion = ProductVersion;

    /// <summary>Match timeout applied to every user/pack-supplied regex. Greppable: RegexMatchTimeout.</summary>
    public const int RegexMatchTimeoutMilliseconds = 250;

    /// <summary>Match timeout applied to every user/pack-supplied regex.</summary>
    public static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(RegexMatchTimeoutMilliseconds);

    /// <summary>Only used when FORGE_DEV_ALLOW_DEFAULT_TOKEN=true. Never for production.</summary>
    public const string DevOnlyInsecureToken = "dev-only-insecure-token";
}
