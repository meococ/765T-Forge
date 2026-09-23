namespace Forge.Plugin;

/// <summary>
/// Single-line named-pipe protocol helpers with identical observable behaviour on both TFMs:
/// a read or write that does not complete within the configured plugin response timeout throws a
/// determinate <see cref="TimeoutException"/>, and cancellation surfaces as
/// <see cref="OperationCanceledException"/>.
/// net462 has no <c>ReadLineAsync(CancellationToken)</c> or <c>WriteLineAsync(ReadOnlyMemory&lt;char&gt;, CancellationToken)</c>,
/// so those paths use the parameterless overloads inside the same timeout wrapper.
/// </summary>
internal static class PipeProtocol
{
    public static async Task<string?> ReadLineAsync(
        StreamReader reader,
        int timeoutSeconds,
        string pipeName,
        CancellationToken cancellationToken)
    {
#if NETFRAMEWORK
        var readTask = reader.ReadLineAsync();
#else
        var readTask = reader.ReadLineAsync(cancellationToken).AsTask();
#endif
        var delayTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
        if (!ReferenceEquals(await Task.WhenAny(readTask, delayTask).ConfigureAwait(false), readTask))
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"No request line was received on pipe '{pipeName}' within {timeoutSeconds} seconds.");
        }

        return await readTask.ConfigureAwait(false);
    }

    public static async Task WriteLineAsync(
        StreamWriter writer,
        string line,
        int timeoutSeconds,
        string pipeName,
        CancellationToken cancellationToken)
    {
#if NETFRAMEWORK
        var writeTask = writer.WriteLineAsync(line);
#else
        var writeTask = writer.WriteLineAsync(line.AsMemory(), cancellationToken);
#endif
        var delayTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
        if (!ReferenceEquals(await Task.WhenAny(writeTask, delayTask).ConfigureAwait(false), writeTask))
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"Could not write the response to pipe '{pipeName}' within {timeoutSeconds} seconds.");
        }

        await writeTask.ConfigureAwait(false);
    }
}
