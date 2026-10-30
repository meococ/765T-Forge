using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// forge_exec_lisp synchronous evaluation contract (pure half): expression preparation, the
/// vl-catch-all-apply envelope, and resbuf → JSON / AutoLISP text decoding.
/// </summary>
public sealed class LispEvalTests
{
    private static LispAtom A(int code, object? value = null) => new(code, value);

    [Fact]
    public void PrepareStripsCommentsAndFoldsWhitespace()
    {
        var prepared = LispEval.Prepare("; header comment\n(setq a 1) ;| block\n comment |;\n\n  (+ a\t2) ; trailing");
        Assert.True(prepared.Ok);
        Assert.Equal("(setq a 1) (+ a 2)", prepared.Normalized);
    }

    [Fact]
    public void PrepareKeepsStringContentAndEscapesRawNewlines()
    {
        var prepared = LispEval.Prepare("(princ \"a;b\n  c \\\"q\\\" ( )\")");
        Assert.True(prepared.Ok);
        Assert.Equal("(princ \"a;b\\n  c \\\"q\\\" ( )\")", prepared.Normalized);
    }

    [Theory]
    [InlineData("", "lisp_empty")]
    [InlineData("   ; only a comment", "lisp_empty")]
    [InlineData("(+ 1 2", "lisp_unbalanced")]
    [InlineData("(+ 1 2))", "lisp_unbalanced")]
    [InlineData(") (", "lisp_unbalanced")]
    [InlineData("(princ \"open)", "lisp_unterminated_string")]
    [InlineData("(+ 1 2) ;| never closed", "lisp_unterminated_comment")]
    public void PrepareRefusesMalformedExpressions(string raw, string code)
    {
        var prepared = LispEval.Prepare(raw);
        Assert.False(prepared.Ok);
        Assert.Equal(code, prepared.ErrorCode);
    }

    [Fact]
    public void PrepareRefusesOversizedExpressions()
    {
        var prepared = LispEval.Prepare("(list " + new string('1', LispEval.MaxExpressionChars) + ")");
        Assert.False(prepared.Ok);
        Assert.Equal("lisp_too_long", prepared.ErrorCode);
    }

    [Fact]
    public void WrapEmbedsBodyInCatchAllEnvelope()
    {
        var wrapped = LispEval.Wrap("(setq a 1) (+ a 2)");
        Assert.StartsWith("((lambda (forge-r)", wrapped);
        Assert.Contains("(vl-catch-all-apply (function (lambda () (setq a 1) (+ a 2) )) nil)", wrapped);
        Assert.Contains("\"765T-FORGE-OK\"", wrapped);
        Assert.Contains("\"765T-FORGE-ERR\"", wrapped);
        Assert.Contains("vl-catch-all-error-message", wrapped);
        Assert.True(LispEval.Prepare(wrapped).Ok, "The envelope itself must stay balanced.");
    }

    [Fact]
    public void UnwrapReturnsIntegerValue()
    {
        var outcome = LispEval.Unwrap([A(LispCodes.RtListBegin), A(LispCodes.RtString, LispEval.OkMarker), A(LispCodes.RtShort, (short)3), A(LispCodes.RtListEnd)]);
        Assert.True(outcome.EnvelopeFound);
        Assert.False(outcome.LispError);
        Assert.Equal(3L, outcome.Value);
        Assert.Equal("3", outcome.Text);
        Assert.Equal("int", outcome.Type);
    }

    [Fact]
    public void UnwrapReturnsNilAndT()
    {
        var nil = LispEval.Unwrap([A(LispCodes.RtListBegin), A(LispCodes.RtString, LispEval.OkMarker), A(LispCodes.RtNil), A(LispCodes.RtListEnd)]);
        Assert.Null(nil.Value);
        Assert.Equal("nil", nil.Text);
        Assert.Equal("nil", nil.Type);

        var t = LispEval.Unwrap([A(LispCodes.RtListBegin), A(LispCodes.RtString, LispEval.OkMarker), A(LispCodes.RtT), A(LispCodes.RtListEnd)]);
        Assert.Equal(true, t.Value);
        Assert.Equal("T", t.Text);
    }

    [Fact]
    public void UnwrapReturnsNestedListWithRealsStringsAndPoints()
    {
        var outcome = LispEval.Unwrap(
        [
            A(LispCodes.RtListBegin), A(LispCodes.RtString, LispEval.OkMarker),
            A(LispCodes.RtListBegin),
            A(LispCodes.RtReal, 2.0),
            A(LispCodes.RtString, "a\"b"),
            A(LispCodes.Rt3dPoint, new[] { 1.0, 2.5, 0.0 }),
            A(LispCodes.RtListBegin), A(LispCodes.RtLong, 7), A(LispCodes.RtListEnd),
            A(LispCodes.RtListEnd),
            A(LispCodes.RtListEnd)
        ]);

        Assert.Equal("list", outcome.Type);
        Assert.Equal("(2.0 \"a\\\"b\" (1.0 2.5 0.0) (7))", outcome.Text);
        var list = Assert.IsType<List<object?>>(outcome.Value);
        Assert.Equal(2.0, list[0]);
        Assert.Equal("a\"b", list[1]);
        Assert.Equal(new[] { 1.0, 2.5, 0.0 }, Assert.IsType<object[]>(list[2]).Cast<double>().ToArray());
        Assert.Equal(7L, Assert.IsType<List<object?>>(list[3])[0]);
    }

    [Fact]
    public void UnwrapDecodesDottedPairInAutoCadEncoding()
    {
        // AutoCAD returns (8 . "WALLS") as RTLB car cdr RTDOTE.
        var outcome = LispEval.Unwrap(
        [
            A(LispCodes.RtListBegin), A(LispCodes.RtString, LispEval.OkMarker),
            A(LispCodes.RtListBegin), A(LispCodes.RtShort, (short)8), A(LispCodes.RtString, "WALLS"), A(LispCodes.RtDottedPair),
            A(LispCodes.RtListEnd)
        ]);

        Assert.Equal("dotted", outcome.Type);
        Assert.Equal("(8 . \"WALLS\")", outcome.Text);
        var pair = Assert.IsType<Dictionary<string, object?>>(outcome.Value);
        Assert.Equal(8L, pair["car"]);
        Assert.Equal("WALLS", pair["cdr"]);
    }

    [Fact]
    public void UnwrapDecodesDottedPairInBuildListEncoding()
    {
        // acutBuildList style: RTLB car RTDOTE cdr RTLE.
        var node = LispValueConverter.Parse(
        [
            A(LispCodes.RtListBegin), A(LispCodes.RtShort, (short)62), A(LispCodes.RtDottedPair), A(LispCodes.RtShort, (short)1), A(LispCodes.RtListEnd)
        ]);

        Assert.Equal("(62 . 1)", LispValueConverter.ToLispText(node));
    }

    [Fact]
    public void UnwrapDecodesAssociationList()
    {
        var outcome = LispEval.Unwrap(
        [
            A(LispCodes.RtListBegin), A(LispCodes.RtString, LispEval.OkMarker),
            A(LispCodes.RtListBegin),
            A(LispCodes.RtListBegin), A(LispCodes.RtShort, (short)0), A(LispCodes.RtString, "LINE"), A(LispCodes.RtDottedPair),
            A(LispCodes.RtListBegin), A(LispCodes.RtShort, (short)8), A(LispCodes.RtString, "0"), A(LispCodes.RtDottedPair),
            A(LispCodes.RtListBegin), A(LispCodes.RtShort, (short)10), A(LispCodes.RtReal, 1.0), A(LispCodes.RtReal, 2.0), A(LispCodes.RtReal, 0.0), A(LispCodes.RtListEnd),
            A(LispCodes.RtListEnd),
            A(LispCodes.RtListEnd)
        ]);

        Assert.Equal("((0 . \"LINE\") (8 . \"0\") (10 1.0 2.0 0.0))", outcome.Text);
    }

    [Fact]
    public void UnwrapReportsCaughtLispError()
    {
        var outcome = LispEval.Unwrap(
        [
            A(LispCodes.RtListBegin), A(LispCodes.RtString, LispEval.ErrorMarker), A(LispCodes.RtString, "bad argument type: numberp: \"x\""), A(LispCodes.RtListEnd)
        ]);

        Assert.True(outcome.EnvelopeFound);
        Assert.True(outcome.LispError);
        Assert.Equal("bad argument type: numberp: \"x\"", outcome.ErrorMessage);
    }

    [Fact]
    public void UnwrapFlagsForeignResult()
    {
        var outcome = LispEval.Unwrap([A(LispCodes.RtShort, (short)5)]);
        Assert.False(outcome.EnvelopeFound);
        Assert.Equal(5L, outcome.Value);

        Assert.False(LispEval.Unwrap([]).EnvelopeFound);
        Assert.False(LispEval.Unwrap(null).EnvelopeFound);
    }

    [Fact]
    public void ConverterMapsEnameAndPickSet()
    {
        var ename = LispValueConverter.Parse([A(LispCodes.RtEname, "2A3F")]);
        Assert.Equal("ename", LispValueConverter.TypeName(ename));
        Assert.Equal("<Entity handle: 2A3F>", LispValueConverter.ToLispText(ename));
        Assert.Equal("2A3F", Assert.IsType<Dictionary<string, object?>>(LispValueConverter.ToJsonValue(ename))["ename"]);

        var handles = Enumerable.Range(1, LispValueConverter.MaxPickSetHandles + 5).Select(i => i.ToString("X")).ToArray();
        var ss = LispValueConverter.ToJsonValue(LispValueConverter.Parse([A(LispCodes.RtPickSet, handles)]));
        var dict = Assert.IsType<Dictionary<string, object?>>(ss);
        Assert.Equal(handles.Length, dict["pickset"]);
        Assert.Equal(LispValueConverter.MaxPickSetHandles, Assert.IsType<string[]>(dict["handles"]).Length);
        Assert.Equal(true, dict["truncated"]);
    }

    [Fact]
    public void ConverterKeepsNonFiniteRealsSerializable()
    {
        var node = LispValueConverter.Parse([A(LispCodes.RtReal, double.NaN)]);
        Assert.Equal("NaN", LispValueConverter.ToJsonValue(node));
        var json = System.Text.Json.JsonSerializer.Serialize(new { v = LispValueConverter.ToJsonValue(node) }, ForgeJson.Options);
        Assert.Contains("NaN", json);
    }

    [Fact]
    public void StatusNamesAreReadable()
    {
        Assert.Equal("RTNORM", LispCodes.StatusName(LispCodes.RtNorm));
        Assert.Equal("RTERROR", LispCodes.StatusName(LispCodes.RtError));
        Assert.Equal("status_42", LispCodes.StatusName(42));
    }
}
