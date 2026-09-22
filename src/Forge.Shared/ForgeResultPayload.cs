using System.Text.Json;
using System.Text.Json.Nodes;

namespace Forge.Shared;

public static class ForgeResultPayload
{
    /// <summary>
    /// Puts <paramref name="backupPath"/> on the same object as the payload fields.
    /// </summary>
    public static object MergeBackup(object? data, string backupPath)
    {
        var node = data is null ? null : JsonSerializer.SerializeToNode(data, ForgeJson.Options);
        if (node is JsonObject obj)
        {
            obj["backupPath"] = backupPath;
            return obj;
        }

        var wrapper = new JsonObject { ["backupPath"] = backupPath };
        if (node is not null)
        {
            wrapper["value"] = node;
        }

        return wrapper;
    }
}
