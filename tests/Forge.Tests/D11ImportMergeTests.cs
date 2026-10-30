using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// smoke r2 D11: merge mode inserted into the CURRENT space (paper when CTAB was a layout),
/// the readback scanned model space, and ok stayed true although verification failed.
/// The merge must always land in model space and ok must derive from the verification.
/// </summary>
public sealed class D11ImportMergeTests
{
    [Theory]
    [InlineData(3, true)]
    [InlineData(1, true)]
    public void MergeIsVerifiedOnlyByEntitiesReadBackInModelSpace(int created, bool expected)
        => Assert.Equal(expected, DrawingImport.ImportVerified(DrawingImportMode.Merge, created, hasInsert: false, xrefAttached: false));

    [Fact]
    public void MergeWithZeroCreatedIsNeverVerified()
    {
        // The r2 false-success: entities landed on a paper layout, model-space readback was 0.
        Assert.False(DrawingImport.ImportVerified(DrawingImportMode.Merge, 0, hasInsert: true, xrefAttached: false));
        Assert.False(DrawingImport.ImportVerified(DrawingImportMode.Merge, 0, hasInsert: false, xrefAttached: false));
    }

    [Fact]
    public void BlockIsVerifiedByDefinitionEntitiesOrAnInsertHandle()
    {
        Assert.True(DrawingImport.ImportVerified(DrawingImportMode.Block, 5, hasInsert: false, xrefAttached: false));
        Assert.True(DrawingImport.ImportVerified(DrawingImportMode.Block, 0, hasInsert: true, xrefAttached: false));
        Assert.False(DrawingImport.ImportVerified(DrawingImportMode.Block, 0, hasInsert: false, xrefAttached: false));
    }

    [Fact]
    public void XrefIsVerifiedByTheAttachedDefinitionNotByAnInsert()
    {
        // An xref with insertReference=false legitimately has no INSERT and copies 0 entities —
        // attach success (the definition handle) is what verification means. Before D11 this
        // combination reported verification.passed=false while ok stayed true.
        Assert.True(DrawingImport.ImportVerified(DrawingImportMode.Xref, 0, hasInsert: false, xrefAttached: true));
        Assert.True(DrawingImport.ImportVerified(DrawingImportMode.Xref, 0, hasInsert: true, xrefAttached: true));
        Assert.False(DrawingImport.ImportVerified(DrawingImportMode.Xref, 0, hasInsert: false, xrefAttached: false));
    }

    [Fact]
    public void ResultCompositionFailsWhenVerificationFails()
    {
        var data = new { mode = "merge", created = 0 };
        var verification = new ForgeVerification { Attempted = true, Passed = false, Message = "0 entities read back" };
        var result = DrawingImport.ImportResult(
            "cmd-1",
            verified: false,
            data,
            verification,
            "Merge read back 0 entities in model space (source had 42); the import did not land where it was verified.",
            "Check CTAB/target space and the source model space.");

        Assert.False(result.Ok);
        Assert.Equal("import_verification_failed", result.Error!.Code);
        Assert.Same(data, result.Data);
        Assert.Same(verification, result.Verification);
        Assert.False(result.Verification!.Passed);
    }

    [Fact]
    public void ResultCompositionSucceedsOnlyWhenVerified()
    {
        var verification = new ForgeVerification { Attempted = true, Passed = true };
        var result = DrawingImport.ImportResult("cmd-2", true, new { created = 12 }, verification, "unused");
        Assert.True(result.Ok);
        Assert.Null(result.Error);
        Assert.Same(verification, result.Verification);
    }
}

/// <summary>D11 plugin wiring guards: the merge inserts into model space regardless of CTAB.</summary>
public sealed class D11ImportMergeSourceGuards
{
    private static string ImportSource()
        => File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), "PluginCommandProcessor.Import.cs"));

    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"method not found: {signature}");
        var end = System.Text.RegularExpressions.Regex.Match(source[start..], @"\n    \}\r?\n");
        Assert.True(end.Success, $"end of method not found: {signature}");
        return source.Substring(start, end.Index + end.Length);
    }

    [Fact]
    public void MergeClonesIntoTheModelSpaceRecordNotTheCurrentSpace()
    {
        var body = MethodBody(ImportSource(), "private static ImportOutcome ImportMerge(");
        // db.Insert lands in the CURRENT space — with CTAB on a paper layout that is the
        // bug — and managed Database.CurrentSpaceId is read-only, so the target must be
        // the model-space block table record itself.
        Assert.DoesNotContain("db.Insert(", body, StringComparison.Ordinal);
        Assert.Contains("WblockCloneObjects", body, StringComparison.Ordinal);
        Assert.Contains("BlockTableRecord.ModelSpace", body, StringComparison.Ordinal);
        Assert.Contains("SymbolUtilityServices.GetBlockModelSpaceId", body, StringComparison.Ordinal);
    }

    [Fact]
    public void DocImportDerivesOkFromVerification()
    {
        var source = ImportSource();
        Assert.Contains("DrawingImport.ImportVerified(", source, StringComparison.Ordinal);
        Assert.Contains("DrawingImport.ImportResult(", source, StringComparison.Ordinal);
        // The old silent success is gone.
        Assert.DoesNotContain("Passed = outcome.Created > 0 || outcome.InsertHandle is not null", source);
    }
}
