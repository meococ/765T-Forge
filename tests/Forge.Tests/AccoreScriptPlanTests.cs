using System.Text;
using Forge.Server;

namespace Forge.Tests;

/// <summary>
/// forge_run_script .lsp support: accoreconsole /s only runs .scr, so a .lsp is driven by a generated
/// loader .scr whose (load "...") path literal must survive spaces, backslashes, quotes and Unicode.
/// </summary>
public sealed class AccoreScriptPlanTests
{
    [Theory]
    [InlineData(@"C:\work\fix.scr", AccoreScriptPlan.ScriptKind.Script)]
    [InlineData(@"C:\work\FIX.SCR", AccoreScriptPlan.ScriptKind.Script)]
    [InlineData(@"C:\work\noext", AccoreScriptPlan.ScriptKind.Script)]
    [InlineData(@"C:\work\probe.lsp", AccoreScriptPlan.ScriptKind.Lisp)]
    [InlineData(@"C:\work\PROBE.LSP", AccoreScriptPlan.ScriptKind.Lisp)]
    public void SupportedKinds(string path, AccoreScriptPlan.ScriptKind expected)
    {
        Assert.True(AccoreScriptPlan.TryGetKind(path, out var kind, out var error));
        Assert.Null(error);
        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData(@"C:\work\probe.fas")]
    [InlineData(@"C:\work\probe.vlx")]
    [InlineData(@"C:\work\probe.dll")]
    public void OtherExtensionsAreRefused(string path)
    {
        Assert.False(AccoreScriptPlan.TryGetKind(path, out _, out var error));
        Assert.Contains(".scr or .lsp", error);
    }

    [Fact]
    public void PathLiteralUsesForwardSlashesAndEscapesQuotes()
    {
        Assert.Equal("\"C:/Temp/Forge Run/a.lsp\"", AccoreScriptPlan.LispPathLiteral(@"C:\Temp\Forge Run\a.lsp"));
        Assert.Equal("\"C:/odd\\\"name.lsp\"", AccoreScriptPlan.LispPathLiteral("C:\\odd\"name.lsp"));
    }

    [Fact]
    public void LoaderScriptLoadsThenPrincs()
    {
        var text = AccoreScriptPlan.LoaderScript(@"C:\Users\me\AppData\Local\Temp\765T\run 1.lsp");
        Assert.Equal("(load \"C:/Users/me/AppData/Local/Temp/765T/run 1.lsp\")\r\n(princ)\r\n", text);
    }

    [Fact]
    public void LoaderBytesArePlainAsciiForAsciiPaths()
    {
        var bytes = AccoreScriptPlan.LoaderScriptBytes(@"C:\Temp\a.lsp");
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal(AccoreScriptPlan.LoaderScript(@"C:\Temp\a.lsp"), Encoding.ASCII.GetString(bytes));
        Assert.DoesNotContain((byte)0, bytes);
    }

    [Fact]
    public void LoaderBytesAreBomUtf8ForUnicodePaths()
    {
        const string path = @"C:\Dự án\bản vẽ.lsp";
        var bytes = AccoreScriptPlan.LoaderScriptBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal(AccoreScriptPlan.LoaderScript(path), Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
        Assert.DoesNotContain((byte)0, bytes);
    }
}
