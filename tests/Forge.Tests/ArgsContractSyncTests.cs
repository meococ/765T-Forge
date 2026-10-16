using System.Text.RegularExpressions;

namespace Forge.Tests;

/// <summary>
/// Source-scan contract between the MCP server and the AutoCAD plugin. Every tool in
/// ForgeMcpTools.cs forwards an anonymous object (<c>runner.InvokeAsync("forge_x", new { a, b = .. })</c>);
/// the plugin binds it with <c>Args&lt;T&gt;(command)</c> (System.Text.Json, case-insensitive). A key
/// with no matching property on T is silently dropped — that is exactly how degree/startTangent,
/// hasArrowHead, title, … were lost. This test fails on any such key.
/// </summary>
public sealed class ArgsContractSyncTests
{
    /// <summary>
    /// Keys that are intentionally not bound to the Args record (consumed generically). Keep tiny
    /// and explain every entry. Format: "tool:key" or "*:key".
    /// </summary>
    private static readonly HashSet<string> Allowlist = new(StringComparer.OrdinalIgnoreCase)
    {
    };

    [Fact]
    public void ServerArgsAreBoundByPluginArgsRecords()
    {
        var analysis = ArgsContractAnalyzer.Analyze(
            File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Server"), "ForgeMcpTools.cs")),
            Directory.GetFiles(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), "*.cs").Select(File.ReadAllText).ToArray(),
            Directory.GetFiles(PluginDispatchSyncTests.RepoPath("src", "Forge.Shared"), "*.cs").Select(File.ReadAllText).ToArray());

        var dropped = analysis.Dropped
            .Where(d => !Allowlist.Contains($"{d.Tool}:{d.Key}") && !Allowlist.Contains($"*:{d.Key}"))
            .Select(d => $"{d.Tool}: '{d.Key}' not on {d.ArgsTypes}")
            .ToArray();

        // Sanity: the scan must reach every plugin-bound tool with arguments, and leave none unmapped.
        Assert.True(analysis.Unmapped.Count == 0, $"Plugin handlers with args that could not be mapped to an Args<T> record: {string.Join(", ", analysis.Unmapped)}");
        Assert.True(analysis.CheckedTools.Count >= 75, $"Only {analysis.CheckedTools.Count} tools were checked; the source scan is probably broken.");
        Assert.True(dropped.Length == 0, "Server args silently dropped by the plugin:\n" + string.Join("\n", dropped));
    }

    [Fact]
    public void DrawStepDtoMatchesPluginDrawBatchStep()
    {
        var serverSource = File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Server"), "ForgeMcpTools.cs"));
        var pluginSources = Directory.GetFiles(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), "*.cs").Select(File.ReadAllText).ToArray();

        var server = ArgsContractAnalyzer.RecordProperties(new[] { serverSource }, "DrawStepDto");
        var plugin = ArgsContractAnalyzer.RecordProperties(pluginSources, "DrawBatchStep");
        Assert.NotNull(server);
        Assert.NotNull(plugin);
        Assert.NotEmpty(server!);

        var serverOnly = server!.Where(p => !plugin!.Contains(p)).OrderBy(p => p).ToArray();
        var pluginOnly = plugin!.Where(p => !server.Contains(p)).OrderBy(p => p).ToArray();
        Assert.True(
            serverOnly.Length == 0 && pluginOnly.Length == 0,
            $"DrawStepDto vs DrawBatchStep differ. Server-only (dropped by plugin): [{string.Join(", ", serverOnly)}]; plugin-only (unreachable from MCP): [{string.Join(", ", pluginOnly)}]");
    }

    [Fact]
    public void AnalyzerParsesAnonymousObjectsAndRecords()
    {
        const string server = """
            public static Task<ForgeResult> A(ForgeToolRunner runner, string name, double[]? p1 = null)
                => runner.InvokeAsync("forge_a", new { name, p1, size = p1?.Length ?? 0, nested = new { x = 1, y = "a,b" }, args.Kept }, dryRun);
            public static Task<ForgeResult> B(ForgeToolRunner runner)
                => runner.InvokeAsync(
                    "forge_b",
                    new { lonely = $"{string.Join(", ", new[] { "q" })}" },
                    cancellationToken: ct);
            """;
        const string plugin = """
            ForgeResult Dispatch() => command.Tool.ToLowerInvariant() switch
            {
                "forge_a" => HandleA(command),
                "forge_b" => HandleB(command),
            };
            private static ForgeResult HandleA(ForgeCommand command)
            {
                var args = Args<AArgs>(command);
                if (x) { return Inner(command); }
                return ForgeResult.Success(command.Id, $"{args.Name} {{ }}");
            }
            private static ForgeResult HandleB(ForgeCommand command) => Shared(command, 1);
            private static ForgeResult Shared(ForgeCommand command, int n)
            {
                var a = Args<BArgs>(command);
                return null!;
            }
            private sealed record AArgs { public string? Name { get; init; } public double[]? P1 { get; init; } public Dictionary<string, string>? Nested { get; init; } public string? Kept { get; init; } }
            private sealed record BArgs(string? Other, int Count = 2);
            """;

        var analysis = ArgsContractAnalyzer.Analyze(server, new[] { plugin }, Array.Empty<string>());
        Assert.Equal(new[] { "forge_a", "forge_b" }, analysis.CheckedTools.OrderBy(t => t).ToArray());
        var dropped = analysis.Dropped.Select(d => $"{d.Tool}:{d.Key}").OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "forge_a:size", "forge_b:lonely" }, dropped);
    }
}

/// <summary>Regex + brace-matching scanner over C# sources (no Roslyn dependency).</summary>
internal static class ArgsContractAnalyzer
{
    internal sealed record DroppedKey(string Tool, string Key, string ArgsTypes);

    internal sealed record Result(IReadOnlyList<string> CheckedTools, IReadOnlyList<string> Unmapped, IReadOnlyList<DroppedKey> Dropped);

    private static readonly Regex InvokeCall = new(@"InvokeAsync\(\s*""(forge_[a-z0-9_]+)""\s*,\s*new\s*\{", RegexOptions.Compiled);
    private static readonly Regex DispatchArm = new(@"""(forge_[a-z0-9_]+)""\s*=>\s*(\w+)\(\s*command\s*\)", RegexOptions.Compiled);
    // Args<T>(command), RunLinework<T>(command), ForgeJson.FromElement<T>(command.Args), …
    private static readonly Regex ArgsUse = new(@"\b\w+<(\w+)>\(\s*command\b", RegexOptions.Compiled);
    private static readonly Regex TryGetKey = new(@"TryGetProperty\(\s*""(\w+)""", RegexOptions.Compiled);
    private static readonly Regex CommandCall = new(@"\b([A-Z]\w*)\(\s*command\b", RegexOptions.Compiled);
    private static readonly Regex NamedMember = new(@"^\s*@?(\w+)\s*=(?![=>])", RegexOptions.Compiled);
    private static readonly Regex ProjectedMember = new(@"^\s*(?:@?\w+[?!]?\.)*@?(\w+)!?\s*$", RegexOptions.Compiled);
    private static readonly Regex PropertyDecl = new(@"\bpublic\s+(?:required\s+)?[^;{}()=]*?\b(\w+)\s*\{\s*get\b", RegexOptions.Compiled);

    public static Result Analyze(string serverSource, IReadOnlyList<string> pluginSources, IReadOnlyList<string> sharedSources)
    {
        var plugin = string.Join("\n", pluginSources);
        var arms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in DispatchArm.Matches(plugin))
        {
            arms.TryAdd(m.Groups[1].Value, m.Groups[2].Value); // first arm wins = the dispatch switch
        }

        var typeSources = pluginSources.Concat(sharedSources).ToArray();
        var checkedTools = new List<string>();
        var unmapped = new List<string>();
        var dropped = new List<DroppedKey>();

        foreach (var (tool, keys) in ServerCalls(serverSource))
        {
            if (!arms.TryGetValue(tool, out var handler))
            {
                continue; // server-only tool (resolved in ForgeToolRunner)
            }

            var argsTypes = new SortedSet<string>(StringComparer.Ordinal);
            var genericKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectArgsTypes(plugin, handler, argsTypes, genericKeys, depth: 0, new HashSet<string>(StringComparer.Ordinal));

            var properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resolvedAny = false;
            foreach (var type in argsTypes)
            {
                var props = RecordProperties(typeSources, type);
                if (props is not null)
                {
                    resolvedAny = true;
                    properties.UnionWith(props);
                }
            }

            if (!resolvedAny && genericKeys.Count == 0)
            {
                if (keys.Count > 0)
                {
                    unmapped.Add($"{tool}->{handler}");
                }

                continue;
            }

            checkedTools.Add(tool);
            foreach (var key in keys.Where(k => !properties.Contains(k) && !genericKeys.Contains(k)))
            {
                dropped.Add(new DroppedKey(tool, key, argsTypes.Count == 0 ? "(TryGetProperty only)" : string.Join("+", argsTypes)));
            }
        }

        return new Result(checkedTools, unmapped, dropped);
    }

    /// <summary>tool → top-level property names of the anonymous object passed to InvokeAsync.</summary>
    public static IEnumerable<(string Tool, IReadOnlyList<string> Keys)> ServerCalls(string source)
    {
        foreach (Match m in InvokeCall.Matches(source))
        {
            var open = m.Index + m.Length - 1;
            var close = MatchBrace(source, open);
            if (close < 0)
            {
                continue;
            }

            var keys = new List<string>();
            foreach (var member in SplitTopLevel(source.Substring(open + 1, close - open - 1)))
            {
                if (string.IsNullOrWhiteSpace(member))
                {
                    continue;
                }

                var named = NamedMember.Match(member);
                if (named.Success)
                {
                    keys.Add(named.Groups[1].Value);
                    continue;
                }

                var projected = ProjectedMember.Match(member);
                if (projected.Success)
                {
                    keys.Add(projected.Groups[1].Value);
                }
            }

            yield return (m.Groups[1].Value, keys);
        }
    }

    /// <summary>Property names of a record/class (positional parameters + { get; } properties); null when not found.</summary>
    public static HashSet<string>? RecordProperties(IReadOnlyList<string> sources, string typeName, int depth = 0)
    {
        // Primitive/BCL names (e.g. "string" from a stray generic) never match a declaration → null.
        var decl = new Regex(@"\b(?:record|class|struct)\s+" + Regex.Escape(typeName) + @"\b");
        foreach (var source in sources)
        {
            var m = decl.Match(source);
            if (!m.Success)
            {
                continue;
            }

            var props = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var i = m.Index + m.Length;
            while (i < source.Length && char.IsWhiteSpace(source[i]))
            {
                i++;
            }

            if (i < source.Length && source[i] == '(')
            {
                var closeParen = MatchBracket(source, i, '(', ')');
                foreach (var parameter in SplitTopLevel(source.Substring(i + 1, closeParen - i - 1)))
                {
                    var head = parameter.Split('=')[0].Trim();
                    var name = Regex.Match(head, @"(\w+)\s*$");
                    if (name.Success)
                    {
                        props.Add(name.Groups[1].Value);
                    }
                }

                i = closeParen + 1;
            }

            // Inherited members: "record Derived : Base" (or "Base(...)") — union the base's properties.
            var inheritance = new Regex(@"\G\s*:\s*(\w+)").Match(source, i);
            if (inheritance.Success && depth < 5 && RecordProperties(sources, inheritance.Groups[1].Value, depth + 1) is { } inherited)
            {
                props.UnionWith(inherited);
            }

            var semicolon = source.IndexOf(';', i);
            var brace = source.IndexOf('{', i);
            if (brace >= 0 && (semicolon < 0 || brace < semicolon))
            {
                var close = MatchBrace(source, brace);
                foreach (Match p in PropertyDecl.Matches(source.Substring(brace + 1, close - brace - 1)))
                {
                    props.Add(p.Groups[1].Value);
                }
            }

            return props;
        }

        return null;
    }

    private static void CollectArgsTypes(string plugin, string method, ISet<string> types, ISet<string> genericKeys, int depth, ISet<string> visited)
    {
        if (depth > 2 || !visited.Add(method))
        {
            return;
        }

        var body = MethodBody(plugin, method);
        if (body is null)
        {
            return;
        }

        foreach (Match m in ArgsUse.Matches(body))
        {
            types.Add(m.Groups[1].Value);
        }

        foreach (Match m in TryGetKey.Matches(body))
        {
            genericKeys.Add(m.Groups[1].Value);
        }

        if (types.Count > 0)
        {
            return;
        }

        // The handler delegates (e.g. "return Shared(command, ...)") — follow the call one level down.
        foreach (Match m in CommandCall.Matches(body))
        {
            var callee = m.Groups[1].Value;
            if (callee is not ("Args" or "DryRun" or "Failure" or "Success" or "RejectControlChars"))
            {
                CollectArgsTypes(plugin, callee, types, genericKeys, depth + 1, visited);
            }
        }
    }

    private static string? MethodBody(string source, string method)
    {
        var signature = new Regex(@"\bForgeResult\??\s+" + Regex.Escape(method) + @"\s*\([^)]*\bForgeCommand\s+command\b[^)]*\)");
        var m = signature.Match(source);
        if (!m.Success)
        {
            return null;
        }

        var i = m.Index + m.Length;
        while (i < source.Length && char.IsWhiteSpace(source[i]))
        {
            i++;
        }

        if (i + 1 < source.Length && source[i] == '=' && source[i + 1] == '>')
        {
            var end = source.IndexOf(";\n", i, StringComparison.Ordinal);
            var endCr = source.IndexOf(";\r\n", i, StringComparison.Ordinal);
            var stop = new[] { end, endCr }.Where(x => x >= 0).DefaultIfEmpty(source.Length - 1).Min();
            return source.Substring(i, stop - i + 1);
        }

        if (i < source.Length && source[i] == '{')
        {
            var close = MatchBrace(source, i);
            return close < 0 ? null : source.Substring(i, close - i + 1);
        }

        return null;
    }

    /// <summary>Splits on commas at nesting depth 0 ((), [], {}, strings and char literals skipped).</summary>
    private static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '"':
                    i = SkipString(text, i);
                    break;
                case '\'':
                    i = SkipChar(text, i);
                    break;
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(text.Substring(start, i - start));
                    start = i + 1;
                    break;
            }
        }

        parts.Add(text.Substring(start));
        return parts;
    }

    private static int MatchBrace(string s, int open) => MatchBracket(s, open, '{', '}');

    private static int MatchBracket(string s, int open, char openChar, char closeChar)
    {
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                i = s.IndexOf('\n', i);
                if (i < 0)
                {
                    return -1;
                }

                continue;
            }

            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                i = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (i < 0)
                {
                    return -1;
                }

                i++;
                continue;
            }

            if (c == '"')
            {
                i = SkipString(s, i);
                continue;
            }

            if (c == '\'')
            {
                i = SkipChar(s, i);
                continue;
            }

            if (c == openChar)
            {
                depth++;
            }
            else if (c == closeChar)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>Index of the closing quote. Quotes inside interpolation holes come in pairs, so naive toggling stays balanced.</summary>
    private static int SkipString(string s, int i)
    {
        if (i + 2 < s.Length && s[i + 1] == '"' && s[i + 2] == '"')
        {
            var raw = s.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
            return raw < 0 ? s.Length - 1 : raw + 2;
        }

        var verbatim = (i > 0 && s[i - 1] == '@') || (i > 1 && s[i - 1] == '$' && s[i - 2] == '@');
        for (var j = i + 1; j < s.Length; j++)
        {
            if (verbatim)
            {
                if (s[j] == '"')
                {
                    if (j + 1 < s.Length && s[j + 1] == '"')
                    {
                        j++;
                        continue;
                    }

                    return j;
                }

                continue;
            }

            if (s[j] == '\\')
            {
                j++;
                continue;
            }

            if (s[j] == '"' || s[j] == '\n')
            {
                return j;
            }
        }

        return s.Length - 1;
    }

    private static int SkipChar(string s, int i)
    {
        var j = i + 1;
        if (j < s.Length && s[j] == '\\')
        {
            j += 2;
        }
        else
        {
            j++;
        }

        var limit = Math.Min(s.Length, i + 10);
        while (j < limit && s[j] != '\'')
        {
            j++;
        }

        return j < limit ? j : i; // not a char literal (e.g. apostrophe) → treat as plain character
    }
}
