using System.Text;
using Forge.Server;

namespace Forge.Tests;

/// <summary>
/// accoreconsole.exe writes UTF-16LE to a redirected stdout. Read as UTF-8 every ASCII char is
/// followed by NUL ("U\0n\0k\0..."), which used to defeat the literal abort-token search.
/// </summary>
public sealed class AccoreConsoleOutputTests
{
    [Fact]
    public void ScriptCheckDetectsUtf16DecodedAsUtf8()
    {
        const string garbled = "U\0n\0k\0n\0o\0w\0n\0 \0c\0o\0m\0m\0a\0n\0d\0";
        Assert.True(AccoreConsoleScriptCheck.HasScriptError(garbled, ""));
        Assert.True(AccoreConsoleScriptCheck.HasScriptError("", "*\0C\0a\0n\0c\0e\0l\0*\0"));
        Assert.False(AccoreConsoleScriptCheck.HasScriptError("C\0o\0m\0m\0a\0n\0d\0:\0 \0_\0Q\0S\0A\0V\0E\0", ""));
    }

    [Fact]
    public void ScriptCheckNormalizesWhitespace()
    {
        Assert.True(AccoreConsoleScriptCheck.HasScriptError("Unknown\r\n  command \"FOO\".", null));
        Assert.Equal("a b c", AccoreConsoleScriptCheck.Normalize("a\0 \r\n\tb\0\0  c"));
    }

    [Fact]
    public void DecodeOutputDetectsUtf16LeWithAndWithoutBom()
    {
        const string text = "Command: FOOBAR\r\nUnknown command \"FOOBAR\".  Press F1 for help.";
        var noBom = Encoding.Unicode.GetBytes(text);
        var withBom = Encoding.Unicode.GetPreamble().Concat(noBom).ToArray();

        Assert.Equal(text, AccoreConsoleScriptCheck.DecodeOutput(noBom));
        Assert.Equal(text, AccoreConsoleScriptCheck.DecodeOutput(withBom));
        Assert.True(AccoreConsoleScriptCheck.HasScriptError(AccoreConsoleScriptCheck.DecodeOutput(noBom), ""));
    }

    [Fact]
    public void DecodeOutputKeepsUtf8AndVietnamese()
    {
        const string text = "Bản vẽ đã lưu. Command: _QSAVE";
        Assert.Equal(text, AccoreConsoleScriptCheck.DecodeOutput(Encoding.UTF8.GetBytes(text)));
        Assert.Equal(text, AccoreConsoleScriptCheck.DecodeOutput(Encoding.Unicode.GetBytes(text)));
        Assert.Equal("", AccoreConsoleScriptCheck.DecodeOutput(null));
        Assert.Equal("", AccoreConsoleScriptCheck.DecodeOutput(Array.Empty<byte>()));
    }

    [Fact]
    public void DecodeOutputRealignsUtf16AfterOneStrayByte()
    {
        const string text = "Command: _QSAVE\r\nRegenerating model.";
        var shifted = new byte[] { (byte)'X' }.Concat(Encoding.Unicode.GetBytes(text)).ToArray();
        var decoded = AccoreConsoleScriptCheck.DecodeOutput(shifted);
        Assert.Equal("X" + text, decoded);
        Assert.DoesNotContain('\0', decoded);
    }

    [Fact]
    public void CleanForReportDropsControlCharsAndDoubledLineEnds()
    {
        var raw = "Command:\b\b\b _QSAVE\r\r\n\f\u0007Done\u007f\tok\r\r\r\n\uFFFD\uFFFD\uFFFDend";
        Assert.Equal("Command: _QSAVE\r\nDone\tok\r\n\uFFFDend", AccoreConsoleScriptCheck.CleanForReport(raw));
        Assert.Equal("", AccoreConsoleScriptCheck.CleanForReport(null));
    }

    [Fact]
    public void CleanForReportKeepsHeadAndTailOfLongOutput()
    {
        var raw = new string('a', 100) + new string('b', 100);
        var cleaned = AccoreConsoleScriptCheck.CleanForReport(raw, maxChars: 40);
        Assert.StartsWith(new string('a', 20), cleaned);
        Assert.EndsWith(new string('b', 20), cleaned);
        Assert.Contains("[160 chars omitted]", cleaned);
        Assert.Equal(raw, AccoreConsoleScriptCheck.CleanForReport(raw, maxChars: 0));
    }

    [Fact]
    public void Utf16ConsoleOutputBecomesReadableReportText()
    {
        // The full path forge_run_script now uses: decode, then clean.
        var bytes = Encoding.Unicode.GetBytes("Command: (load \"C:/t/a.lsp\")\r\r\nOK\r\r\n");
        Assert.Equal("Command: (load \"C:/t/a.lsp\")\r\nOK\r\n", AccoreConsoleScriptCheck.CleanForReport(AccoreConsoleScriptCheck.DecodeOutput(bytes)));
    }
}
