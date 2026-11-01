using System.Text.Json;
using Forge.Shared;

namespace Forge.Plugin;

/// <summary>
/// Writes forge_progress lines onto the named pipe while a command is still running.
/// The pipe protocol is one request line in, one reply line out — progress lines are extra
/// output lines the Forge server skips while reading the reply and forwards as MCP
/// notifications/progress. Every write is best-effort: a dead pipe must never kill a plot
/// or pack that is mid-flight.
/// </summary>
public sealed class PipeProgressReporter
{
    private readonly StreamWriter _writer;
    private readonly string _commandId;
    private readonly object _gate = new();

    public PipeProgressReporter(StreamWriter writer, string commandId)
    {
        _writer = writer;
        _commandId = commandId;
    }

    public void Report(double progress, double? total = null, string? message = null)
    {
        try
        {
            var line = JsonSerializer.Serialize(
                ForgeProgressMessage.For(_commandId, progress, total, message), ForgeJson.Options);
            lock (_gate)
            {
                _writer.WriteLine(line);
                _writer.Flush();
            }
        }
        catch
        {
            // Pipe dropped mid-op — progress is informational only.
        }
    }
}
