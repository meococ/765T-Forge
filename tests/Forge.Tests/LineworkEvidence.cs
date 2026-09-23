using Xunit;

namespace Forge.Tests;

/// <summary>
/// Locates the optional linework evidence files used by the real-data tests.
///
/// Those files come from a private customer drawing. They are deliberately not committed
/// and are never referenced by an absolute path, so no customer or project name lives in
/// this repository. Point <c>FORGE_LINEWORK_EVIDENCE_DIR</c> at a directory containing
/// <c>pl_dump.txt</c> and <c>all_pipe_segs_live.json</c> to run the real-data tests.
///
/// When the directory is not configured the dependent tests report as Skipped with an
/// explicit reason rather than passing silently, so the suite never claims coverage it did
/// not execute.
/// </summary>
internal static class LineworkEvidence
{
    public const string DumpFileName = "pl_dump.txt";
    public const string ModelFileName = "all_pipe_segs_live.json";

    public const string ConfigurationHint =
        "Set FORGE_LINEWORK_EVIDENCE_DIR to a directory containing " + DumpFileName +
        " and " + ModelFileName + " to run the real-data linework tests.";

    public static string? DumpPath => Resolve(DumpFileName);

    public static string? ModelPath => Resolve(ModelFileName);

    private static string? Resolve(string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("FORGE_LINEWORK_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var path = Path.Combine(directory, fileName);
        return File.Exists(path) ? path : null;
    }
}

/// <summary>
/// A fact that reports as Skipped with a reason when the linework dump evidence file is
/// absent, instead of returning early and counting as a pass.
/// </summary>
internal sealed class LineworkEvidenceFactAttribute : FactAttribute
{
    public LineworkEvidenceFactAttribute()
    {
        if (LineworkEvidence.DumpPath is null)
        {
            Skip = LineworkEvidence.ConfigurationHint;
        }
    }
}

/// <summary>
/// A fact that requires both the dump and the model-segments evidence files.
/// </summary>
internal sealed class LineworkEvidenceModelFactAttribute : FactAttribute
{
    public LineworkEvidenceModelFactAttribute()
    {
        if (LineworkEvidence.DumpPath is null || LineworkEvidence.ModelPath is null)
        {
            Skip = LineworkEvidence.ConfigurationHint;
        }
    }
}
