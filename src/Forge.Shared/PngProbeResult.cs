namespace Forge.Shared;

/// <summary>
/// TOOL-GAP #27: output verification for raster plots — the PNG analogue of
/// PdfProbeResult: existence, non-empty, 8-byte PNG signature, and freshness
/// (mtime &gt;= plot start).
/// </summary>
public sealed class PngProbeResult
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public bool Exists { get; init; }
    public long Bytes { get; init; }
    public bool Valid { get; init; }
    public bool Passed { get; init; }
    public string? Message { get; init; }

    public int? WidthPx { get; init; }
    public int? HeightPx { get; init; }

    /// <summary>
    /// Probe the PNG output. When expectedW/H are given, the IHDR dimensions must match
    /// exactly — a wrong-size or transposed PNG is not the plotted sheet.
    /// </summary>
    public static PngProbeResult Probe(string path, DateTimeOffset? notBeforeUtc = null, int? expectedW = null, int? expectedH = null)
    {
        if (!File.Exists(path))
        {
            return new PngProbeResult { Exists = false, Passed = false, Message = "PNG output does not exist." };
        }

        var info = new FileInfo(path);
        var bytes = info.Length;
        if (bytes <= 0)
        {
            return new PngProbeResult { Exists = true, Bytes = 0, Passed = false, Message = "PNG exists but is zero bytes." };
        }

        if (notBeforeUtc.HasValue && info.LastWriteTimeUtc < notBeforeUtc.Value.UtcDateTime.AddSeconds(-1))
        {
            return new PngProbeResult
            {
                Exists = true,
                Bytes = bytes,
                Passed = false,
                Message = $"PNG output is stale: last-write time ({info.LastWriteTimeUtc:O}) precedes plot start ({notBeforeUtc.Value.UtcDateTime:O})."
            };
        }

        // Signature (8B) + first chunk length (4B) + "IHDR" (4B) + width/height (4B each).
        var header = new byte[24];
        try
        {
            using var fs = File.OpenRead(path);
            var read = 0;
            while (read < header.Length)
            {
                var n = fs.Read(header, read, header.Length - read);
                if (n <= 0)
                {
                    break;
                }

                read += n;
            }

            if (read < header.Length || !header.AsSpan(0, Signature.Length).SequenceEqual(Signature))
            {
                return new PngProbeResult
                {
                    Exists = true,
                    Bytes = bytes,
                    Valid = false,
                    Passed = false,
                    Message = "Output file is not a valid PNG (bad 8-byte signature)."
                };
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new PngProbeResult
            {
                Exists = true,
                Bytes = bytes,
                Valid = false,
                Passed = false,
                Message = $"PNG output could not be read: {ex.Message}"
            };
        }

        var wPx = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
        var hPx = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
        if (expectedW.HasValue && expectedH.HasValue && (wPx != expectedW.Value || hPx != expectedH.Value))
        {
            return new PngProbeResult
            {
                Exists = true,
                Bytes = bytes,
                Valid = true,
                WidthPx = wPx,
                HeightPx = hPx,
                Passed = false,
                Message = $"PNG dimensions {wPx}x{hPx} do not match the requested medium {expectedW}x{expectedH}."
            };
        }

        return new PngProbeResult { Exists = true, Bytes = bytes, Valid = true, WidthPx = wPx, HeightPx = hPx, Passed = true, Message = "PNG probe passed." };
    }
}
