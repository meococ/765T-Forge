using System.Text;

namespace Forge.Shared;

/// <summary>
/// readiness-20261118 gap 8: tokenization and the unbounded point-loop decision for
/// forge_exec_command. Editor.Command waits for a running command to complete — a sequence
/// that runs out of input mid-prompt ("_.LINE 0,0 10,10") parks on "next point" and wedges
/// the serialized lane. A trailing Enter is consumed inside a point loop and ends it, but a
/// sequence that already terminated must NOT get one — the Enter would land on the
/// "Command:" prompt and just repeat the previous command.
/// </summary>
public static class AutoCadCommandSequence
{
    /// <summary>Commands whose input loop cannot end while args remain: one trailing Enter ends the point loop cleanly.</summary>
    public static readonly string[] UnboundedPointLoopCommands = ["LINE", "PLINE", "MLINE", "LWPOLYLINE"];

    /// <summary>
    /// True when the token stream still feeds an unbounded point-loop command — the last token
    /// is a coordinate (or the bare command itself), so an extra Enter lands inside the loop.
    /// Commands that self-terminate mid-input (e.g. PLINE with a Close option) are excluded.
    /// </summary>
    public static bool EndsInUnterminatedPointLoop(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return false;
        }

        var first = tokens[0].TrimStart('_', '.', '-', '(');
        if (!UnboundedPointLoopCommands.Contains(first, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return tokens.Count == 1 || tokens[^1].Contains(',');
    }

    /// <summary>
    /// True for a bare point-loop command name with no input at all ("_.LINE" alone): no
    /// sequence of Enters can finish it — it parks on the first-point prompt until ESC, so
    /// dispatching it would only wedge the lane for the whole watchdog window. Refuse it.
    /// </summary>
    public static bool IsUnfinishable(IReadOnlyList<string> tokens)
    {
        if (tokens.Count != 1)
        {
            return false;
        }

        var first = tokens[0].TrimStart('_', '.', '-', '(');
        return UnboundedPointLoopCommands.Contains(first, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Splits a command line into Editor.Command args — whitespace splits, double quotes group.</summary>
    public static string[] Tokenize(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in command.Trim())
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens.ToArray();
    }
}
