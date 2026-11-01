using System.Text.Json;
using Forge.Shared;
using ModelContextProtocol.Protocol;

namespace Forge.Server;

/// <summary>
/// Live argument completions: layout names come from forge_doc_list_layouts, layer names
/// from forge_layer_list, xref names from forge_xref_list, profile names from ToolProfiles —
/// all answered through ForgeToolRunner so a running AutoCAD supplies real names and a dead
/// pipe just yields an empty list (completion must never error the client).
/// </summary>
public static class ForgeLiveCompletions
{
    /// <summary>Argument-name → completing source tool (name fragments, lowercase).</summary>
    private static readonly (string[] ArgKeys, string Tool)[] LiveArgSources =
    [
        (["layout", "layoutname"], "forge_doc_list_layouts"),
        (["layer", "layername", "layers", "layerfilter"], "forge_layer_list"),
        (["xref", "xrefname"], "forge_xref_list")
    ];

    /// <summary>Handle a completion/complete request; null → let the SDK's own path answer.</summary>
    public static async Task<CompleteResult?> TryCompleteAsync(
        ForgeToolRunner runner,
        CompleteRequestParams parameters,
        CancellationToken cancellationToken)
    {
        var argName = parameters.Argument.Name;
        var prefix = parameters.Argument.Value ?? "";

        // forge://profiles/{name} — static list, no pipe needed.
        if (parameters.Ref is ResourceTemplateReference template &&
            template.Uri.Contains("profiles/", StringComparison.OrdinalIgnoreCase) &&
            argName.Equals("name", StringComparison.OrdinalIgnoreCase))
        {
            return Result(FilterPrefix(ToolProfiles.Names, prefix));
        }

        var source = LiveArgSources.FirstOrDefault(s =>
            s.ArgKeys.Any(k => argName.Contains(k, StringComparison.OrdinalIgnoreCase)));
        if (source.Tool is null)
        {
            return null;
        }

        try
        {
            var result = await runner.InvokeAsync(
                source.Tool, new { }, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Ok || result.Data is null)
            {
                return Result([]);
            }

            return Result(FilterPrefix(ExtractNames(result.Data), prefix));
        }
        catch
        {
            return Result([]);
        }
    }

    /// <summary>Collect name-ish string values anywhere in the result payload.</summary>
    private static IEnumerable<string> ExtractNames(object data)
    {
        var json = JsonSerializer.Serialize(data, ForgeJson.Options);
        using var doc = JsonDocument.Parse(json);
        var names = new List<string>();
        Walk(doc.RootElement);
        return names;

        void Walk(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var prop in el.EnumerateObject())
                    {
                        if ((prop.Name.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                             prop.Name.Equals("layout", StringComparison.OrdinalIgnoreCase) ||
                             prop.Name.Equals("layoutName", StringComparison.OrdinalIgnoreCase) ||
                             prop.Name.Equals("layer", StringComparison.OrdinalIgnoreCase) ||
                             prop.Name.Equals("layerName", StringComparison.OrdinalIgnoreCase) ||
                             prop.Name.Equals("xref", StringComparison.OrdinalIgnoreCase) ||
                             prop.Name.Equals("xrefName", StringComparison.OrdinalIgnoreCase)) &&
                            prop.Value.ValueKind == JsonValueKind.String)
                        {
                            var value = prop.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                names.Add(value!);
                            }
                        }
                        else
                        {
                            Walk(prop.Value);
                        }
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in el.EnumerateArray())
                    {
                        Walk(item);
                    }

                    break;
            }
        }
    }

    private static IEnumerable<string> FilterPrefix(IEnumerable<string> names, string prefix)
        => names
            .Where(n => string.IsNullOrEmpty(prefix) || n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Take(100);

    private static CompleteResult Result(IEnumerable<string> values)
    {
        var list = values.ToList();
        return new CompleteResult
        {
            Completion = new Completion
            {
                Values = list,
                Total = list.Count,
                HasMore = false
            }
        };
    }
}
