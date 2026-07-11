namespace Forge.Shared;

public sealed record QaFinding(
    string Code,
    string Severity,
    string Message,
    string? Suggestion = null,
    string? SuggestedTool = null);

public sealed record QaReport
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? Document { get; init; }
    public bool Passed { get; init; }
    public IReadOnlyList<QaFinding> Findings { get; init; } = Array.Empty<QaFinding>();
    public object? Context { get; init; }
    public string? ArtifactPath { get; init; }

    public static QaReport FromFindings(string? document, IEnumerable<QaFinding> findings, object? context = null)
    {
        var list = findings.ToArray();
        var passed = list.All(f => !string.Equals(f.Severity, "error", StringComparison.OrdinalIgnoreCase));
        return new QaReport
        {
            Document = document,
            Passed = passed,
            Findings = list,
            Context = context
        };
    }
}
