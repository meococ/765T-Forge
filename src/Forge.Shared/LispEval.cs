using System.Globalization;
using System.Text;

namespace Forge.Shared;

/// <summary>One resbuf node as seen from managed code: the ObjectARX result type code plus a plain value.</summary>
/// <remarks>
/// The plugin maps AutoCAD types before handing atoms over: ObjectId → handle string, SelectionSet →
/// string[] of handles, Point2d/Point3d → double[]. Everything else is a primitive (short/int/long/
/// double/string) or null. Keeping the converter free of AutoCAD types makes it unit-testable.
/// </remarks>
public readonly record struct LispAtom(int Code, object? Value);

/// <summary>ObjectARX result type and status codes (adscodes.h) used by forge_exec_lisp.</summary>
public static class LispCodes
{
    public const int RtNone = 5000;
    public const int RtReal = 5001;
    public const int RtPoint = 5002;
    public const int RtShort = 5003;
    public const int RtAngle = 5004;
    public const int RtString = 5005;
    public const int RtEname = 5006;
    public const int RtPickSet = 5007;
    public const int RtOrient = 5008;
    public const int Rt3dPoint = 5009;
    public const int RtLong = 5010;
    public const int RtVoid = 5014;
    public const int RtListBegin = 5016;
    public const int RtListEnd = 5017;
    public const int RtDottedPair = 5018;
    public const int RtNil = 5019;
    public const int RtT = 5021;
    public const int RtInt64 = 5031;

    public const int RtNorm = 5100;
    public const int RtError = -5001;
    public const int RtCancel = -5002;
    public const int RtReject = -5003;
    public const int RtFail = -5004;
    public const int RtKeyword = -5005;
    public const int RtInputTruncated = -5008;

    public static string StatusName(int status) => status switch
    {
        RtNorm => "RTNORM",
        RtError => "RTERROR",
        RtCancel => "RTCAN",
        RtReject => "RTREJ",
        RtFail => "RTFAIL",
        RtKeyword => "RTKWORD",
        RtInputTruncated => "RTINPUTTRUNCATED",
        _ => $"status_{status.ToString(CultureInfo.InvariantCulture)}"
    };
}

/// <summary>Result of <see cref="LispEval.Prepare"/>: a single-line, comment-free, balanced expression or a typed refusal.</summary>
public sealed record LispPrepared(bool Ok, string Normalized, string? ErrorCode, string? ErrorMessage);

/// <summary>Decoded outcome of one synchronous evaluation (after <see cref="LispEval.Unwrap"/>).</summary>
public sealed record LispEvalOutcome
{
    /// <summary>True when the evaluator returned the Forge envelope (the expression ran to completion or raised a caught LISP error).</summary>
    public bool EnvelopeFound { get; init; }

    /// <summary>True when the expression raised an AutoLISP error (caught by vl-catch-all-apply).</summary>
    public bool LispError { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>JSON-friendly value (null, bool, long, double, string, double[], List, Dictionary).</summary>
    public object? Value { get; init; }

    /// <summary>AutoLISP printed form of the value, e.g. <c>(1 2.0 "a")</c>.</summary>
    public string Text { get; init; } = "nil";

    /// <summary>Coarse type name of the value: nil, T, int, real, string, point, ename, pickset, list, dotted, void, unknown.</summary>
    public string Type { get; init; } = "nil";
}

/// <summary>
/// Synchronous AutoLISP evaluation contract for forge_exec_lisp (pure logic — the plugin owns the
/// native acedEvaluateLisp call). Root cause this replaces: the old sync path passed the whole
/// expression to Editor.Command as a single command token, which AutoCAD rejects with
/// eInvalidInput for every LISP expression, so every call fell back to SendStringToExecute and no
/// value could ever be returned.
/// </summary>
public static class LispEval
{
    public const string OkMarker = "765T-FORGE-OK";
    public const string ErrorMarker = "765T-FORGE-ERR";
    public const int MaxExpressionChars = 64 * 1024;

    /// <summary>
    /// Strips ; and ;| |; comments, folds whitespace outside strings to single spaces, turns raw
    /// CR/LF/TAB inside string literals into \r \n \t escapes, and checks that parentheses and
    /// string literals balance. A malformed expression is refused before anything is evaluated,
    /// so the evaluator envelope can never be broken open by a stray ')'.
    /// </summary>
    public static LispPrepared Prepare(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Fail("lisp_empty", "lisp is empty.");
        }

        if (raw.Length > MaxExpressionChars)
        {
            return Fail("lisp_too_long", $"lisp exceeds {MaxExpressionChars} characters; use forge_run_script with a .lsp file for long programs.");
        }

        var sb = new StringBuilder(raw.Length);
        var depth = 0;
        var inString = false;
        var escaped = false;
        var pendingSpace = false;
        var i = 0;
        while (i < raw.Length)
        {
            var ch = raw[i];
            if (inString)
            {
                if (escaped)
                {
                    sb.Append(ch);
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    sb.Append(ch);
                    escaped = true;
                }
                else if (ch == '"')
                {
                    sb.Append(ch);
                    inString = false;
                }
                else if (ch == '\n')
                {
                    sb.Append("\\n");
                }
                else if (ch == '\r')
                {
                    sb.Append("\\r");
                }
                else if (ch == '\t')
                {
                    sb.Append("\\t");
                }
                else
                {
                    sb.Append(ch);
                }

                i++;
                continue;
            }

            if (ch == ';')
            {
                if (i + 1 < raw.Length && raw[i + 1] == '|')
                {
                    var close = raw.IndexOf("|;", i + 2, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        return Fail("lisp_unterminated_comment", "Block comment ;| ... |; is not closed.");
                    }

                    i = close + 2;
                }
                else
                {
                    while (i < raw.Length && raw[i] != '\n' && raw[i] != '\r')
                    {
                        i++;
                    }
                }

                pendingSpace = true;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = true;
                i++;
                continue;
            }

            if (pendingSpace && sb.Length > 0)
            {
                sb.Append(' ');
            }

            pendingSpace = false;
            if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')')
            {
                depth--;
                if (depth < 0)
                {
                    return Fail("lisp_unbalanced", "Unbalanced parentheses: a ')' closes more than was opened.");
                }
            }
            else if (ch == '"')
            {
                inString = true;
            }

            sb.Append(ch);
            i++;
        }

        if (inString)
        {
            return Fail("lisp_unterminated_string", "A string literal is not closed.");
        }

        if (depth != 0)
        {
            return Fail("lisp_unbalanced", $"Unbalanced parentheses: {depth} '(' not closed.");
        }

        var normalized = sb.ToString().Trim();
        return normalized.Length == 0
            ? Fail("lisp_empty", "lisp contains only comments/whitespace.")
            : new LispPrepared(true, normalized, null, null);

        static LispPrepared Fail(string code, string message) => new(false, "", code, message);
    }

    /// <summary>
    /// Wraps a prepared expression (one or more forms) so a single acedEvaluateLisp call returns
    /// either <c>("765T-FORGE-OK" value)</c> or <c>("765T-FORGE-ERR" "message")</c>. Multiple
    /// top-level forms run in order inside the lambda body; the value of the last one is returned.
    /// </summary>
    public static string Wrap(string normalized)
    {
        return "((lambda (forge-r) (if (vl-catch-all-error-p forge-r) (list \"" + ErrorMarker +
               "\" (vl-catch-all-error-message forge-r)) (list \"" + OkMarker +
               "\" forge-r))) (vl-catch-all-apply (function (lambda () " + normalized + " )) nil))";
    }

    /// <summary>Decodes the evaluator's result buffer and strips the Forge envelope.</summary>
    public static LispEvalOutcome Unwrap(IReadOnlyList<LispAtom>? atoms)
    {
        if (atoms is null || atoms.Count == 0)
        {
            return new LispEvalOutcome { EnvelopeFound = false };
        }

        var parsed = LispValueConverter.Parse(atoms);
        if (parsed is LispNode.ListNode list && list.Items.Count == 2 && list.Tail is null &&
            list.Items[0] is LispNode.Atom { Value: string marker })
        {
            var payload = list.Items[1];
            if (marker == OkMarker)
            {
                return new LispEvalOutcome
                {
                    EnvelopeFound = true,
                    Value = LispValueConverter.ToJsonValue(payload),
                    Text = LispValueConverter.ToLispText(payload),
                    Type = LispValueConverter.TypeName(payload)
                };
            }

            if (marker == ErrorMarker)
            {
                var message = payload is LispNode.Atom { Value: string m } ? m : LispValueConverter.ToLispText(payload);
                return new LispEvalOutcome
                {
                    EnvelopeFound = true,
                    LispError = true,
                    ErrorMessage = message,
                    Text = LispValueConverter.ToLispText(payload),
                    Type = "error"
                };
            }
        }

        // Not our envelope (should not happen) — still report what came back, honestly flagged.
        return new LispEvalOutcome
        {
            EnvelopeFound = false,
            Value = LispValueConverter.ToJsonValue(parsed),
            Text = LispValueConverter.ToLispText(parsed),
            Type = LispValueConverter.TypeName(parsed)
        };
    }
}

/// <summary>Parsed AutoLISP value tree built from a flat resbuf chain.</summary>
public abstract record LispNode
{
    private LispNode()
    {
    }

    public sealed record Atom(int Code, object? Value) : LispNode;

    /// <summary>A proper list (Tail null) or a dotted list (Items . Tail).</summary>
    public sealed record ListNode(IReadOnlyList<LispNode> Items, LispNode? Tail) : LispNode;
}

/// <summary>resbuf chain → value tree → JSON-friendly object / AutoLISP printed text.</summary>
public static class LispValueConverter
{
    public const int MaxPickSetHandles = 1000;

    public static LispNode Parse(IReadOnlyList<LispAtom> atoms)
    {
        var index = 0;
        var first = ParseOne(atoms, ref index);
        if (index >= atoms.Count)
        {
            return first;
        }

        // A bare sequence of several values at top level is returned as an implicit list.
        var items = new List<LispNode> { first };
        while (index < atoms.Count)
        {
            items.Add(ParseOne(atoms, ref index));
        }

        return new LispNode.ListNode(items, null);
    }

    private static LispNode ParseOne(IReadOnlyList<LispAtom> atoms, ref int index)
    {
        var atom = atoms[index];
        if (atom.Code == LispCodes.RtListBegin)
        {
            index++;
            var items = new List<LispNode>();
            while (index < atoms.Count)
            {
                var code = atoms[index].Code;
                if (code == LispCodes.RtListEnd)
                {
                    index++;
                    return new LispNode.ListNode(items, null);
                }

                if (code == LispCodes.RtDottedPair)
                {
                    index++;
                    if (items.Count >= 2)
                    {
                        // AutoCAD's own encoding: RTLB car cdr RTDOTE — RTDOTE closes the dotted list.
                        var tail = items[^1];
                        items.RemoveAt(items.Count - 1);
                        return new LispNode.ListNode(items, tail);
                    }

                    // acutBuildList style: RTLB car RTDOTE cdr RTLE.
                    LispNode? cdr = null;
                    if (index < atoms.Count && atoms[index].Code != LispCodes.RtListEnd)
                    {
                        cdr = ParseOne(atoms, ref index);
                    }

                    if (index < atoms.Count && atoms[index].Code == LispCodes.RtListEnd)
                    {
                        index++;
                    }

                    return cdr is null ? new LispNode.ListNode(items, null) : new LispNode.ListNode(items, cdr);
                }

                items.Add(ParseOne(atoms, ref index));
            }

            // Missing RTLE: tolerate the truncated chain.
            return new LispNode.ListNode(items, null);
        }

        index++;
        return new LispNode.Atom(atom.Code, atom.Value);
    }

    public static object? ToJsonValue(LispNode node)
    {
        switch (node)
        {
            case LispNode.ListNode { Tail: null } list:
                return list.Items.Count == 0 ? null : list.Items.Select(ToJsonValue).ToList();
            case LispNode.ListNode list when list.Items.Count == 1:
                return new Dictionary<string, object?> { ["car"] = ToJsonValue(list.Items[0]), ["cdr"] = ToJsonValue(list.Tail!) };
            case LispNode.ListNode list:
                return new Dictionary<string, object?> { ["items"] = list.Items.Select(ToJsonValue).ToList(), ["cdr"] = ToJsonValue(list.Tail!) };
            case LispNode.Atom atom:
                return AtomToJson(atom);
            default:
                return null;
        }
    }

    private static object? AtomToJson(LispNode.Atom atom)
    {
        switch (atom.Code)
        {
            case LispCodes.RtNil:
            case LispCodes.RtVoid:
            case LispCodes.RtNone:
                return null;
            case LispCodes.RtT:
                return true;
            case LispCodes.RtShort:
            case LispCodes.RtLong:
            case LispCodes.RtInt64:
                return ToLong(atom.Value);
            case LispCodes.RtReal:
            case LispCodes.RtAngle:
            case LispCodes.RtOrient:
                return SafeDouble(ToDouble(atom.Value));
            case LispCodes.RtString:
                return atom.Value?.ToString() ?? "";
            case LispCodes.RtPoint:
            case LispCodes.Rt3dPoint:
                return atom.Value is double[] pt ? pt.Select(SafeDouble).ToArray() : atom.Value;
            case LispCodes.RtEname:
                return new Dictionary<string, object?> { ["ename"] = atom.Value?.ToString() };
            case LispCodes.RtPickSet:
            {
                var handles = atom.Value as string[] ?? [];
                return new Dictionary<string, object?>
                {
                    ["pickset"] = handles.Length,
                    ["handles"] = handles.Take(MaxPickSetHandles).ToArray(),
                    ["truncated"] = handles.Length > MaxPickSetHandles
                };
            }
            default:
                return new Dictionary<string, object?> { ["code"] = atom.Code, ["value"] = atom.Value?.ToString() };
        }
    }

    public static string ToLispText(LispNode node)
    {
        var sb = new StringBuilder();
        Write(sb, node);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, LispNode node)
    {
        switch (node)
        {
            case LispNode.ListNode list:
                if (list.Items.Count == 0 && list.Tail is null)
                {
                    sb.Append("nil");
                    return;
                }

                sb.Append('(');
                for (var i = 0; i < list.Items.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(' ');
                    }

                    Write(sb, list.Items[i]);
                }

                if (list.Tail is not null)
                {
                    sb.Append(" . ");
                    Write(sb, list.Tail);
                }

                sb.Append(')');
                return;
            case LispNode.Atom atom:
                WriteAtom(sb, atom);
                return;
        }
    }

    private static void WriteAtom(StringBuilder sb, LispNode.Atom atom)
    {
        switch (atom.Code)
        {
            case LispCodes.RtNil:
                sb.Append("nil");
                break;
            case LispCodes.RtVoid:
            case LispCodes.RtNone:
                sb.Append("nil");
                break;
            case LispCodes.RtT:
                sb.Append('T');
                break;
            case LispCodes.RtShort:
            case LispCodes.RtLong:
            case LispCodes.RtInt64:
                sb.Append(ToLong(atom.Value).ToString(CultureInfo.InvariantCulture));
                break;
            case LispCodes.RtReal:
            case LispCodes.RtAngle:
            case LispCodes.RtOrient:
                sb.Append(RealText(ToDouble(atom.Value)));
                break;
            case LispCodes.RtString:
                sb.Append('"').Append(EscapeLispString(atom.Value?.ToString() ?? "")).Append('"');
                break;
            case LispCodes.RtPoint:
            case LispCodes.Rt3dPoint:
                if (atom.Value is double[] pt)
                {
                    sb.Append('(').Append(string.Join(' ', pt.Select(RealText))).Append(')');
                }
                else
                {
                    sb.Append("nil");
                }

                break;
            case LispCodes.RtEname:
                sb.Append("<Entity handle: ").Append(atom.Value).Append('>');
                break;
            case LispCodes.RtPickSet:
                sb.Append("<Selection set: ").Append((atom.Value as string[])?.Length ?? 0).Append(" objects>");
                break;
            default:
                sb.Append("<code ").Append(atom.Code).Append(": ").Append(atom.Value).Append('>');
                break;
        }
    }

    public static string TypeName(LispNode node) => node switch
    {
        LispNode.ListNode { Items.Count: 0, Tail: null } => "nil",
        LispNode.ListNode { Tail: null } => "list",
        LispNode.ListNode => "dotted",
        LispNode.Atom a => a.Code switch
        {
            LispCodes.RtNil => "nil",
            LispCodes.RtVoid or LispCodes.RtNone => "void",
            LispCodes.RtT => "T",
            LispCodes.RtShort or LispCodes.RtLong or LispCodes.RtInt64 => "int",
            LispCodes.RtReal or LispCodes.RtAngle or LispCodes.RtOrient => "real",
            LispCodes.RtString => "string",
            LispCodes.RtPoint or LispCodes.Rt3dPoint => "point",
            LispCodes.RtEname => "ename",
            LispCodes.RtPickSet => "pickset",
            _ => "unknown"
        },
        _ => "unknown"
    };

    private static string EscapeLispString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    private static string RealText(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') || text.Contains('e') ? text : text + ".0";
    }

    private static long ToLong(object? value) => value switch
    {
        null => 0,
        long l => l,
        int i => i,
        short s => s,
        double d => (long)d,
        _ => long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0
    };

    private static double ToDouble(object? value) => value switch
    {
        null => 0,
        double d => d,
        float f => f,
        long l => l,
        int i => i,
        short s => s,
        _ => double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : double.NaN
    };

    /// <summary>System.Text.Json refuses NaN/Infinity by default; surface them as strings instead of failing the envelope.</summary>
    private static object SafeDouble(double value)
        => double.IsNaN(value) || double.IsInfinity(value) ? value.ToString(CultureInfo.InvariantCulture) : value;
}
