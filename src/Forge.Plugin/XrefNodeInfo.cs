namespace Forge.Plugin;

/// <summary>
/// Typed xref row shared by every xref call site (list / QA / closure walk / pack-and-go).
/// Replaces the previous anonymous types read back through reflection, so a renamed member is
/// now a compile error instead of a silent default.
/// <para>
/// <see cref="TabOrder"/> is -1 for xref block records: the AutoCAD API exposes
/// <c>TabOrder</c> only on <c>Layout</c>, never on <c>BlockTableRecord</c>. The value is an
/// explicit "not applicable" marker, not a guessed ordering.
/// </para>
/// </summary>
public sealed record XrefNodeInfo(
    string Name,
    string Path,
    bool IsOverlay,
    bool IsUnloaded,
    string? Status,
    int TabOrder,
    int Depth,
    string? ParentName)
{
    /// <summary>Explicit marker: xref rows have no layout tab order in the AutoCAD API.</summary>
    public const int NoTabOrder = -1;

    /// <summary>
    /// Exact file-system readback for <see cref="Path"/> resolved by the caller. Set explicitly
    /// where a path could be resolved; never defaulted to a value that reads as "healthy".
    /// </summary>
    public bool PathExists { get; init; }
}
