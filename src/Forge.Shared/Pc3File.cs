using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Forge.Shared;

/// <summary>
/// Reader/writer for AutoCAD PIA plotter-configuration containers (.pc3/.ctb/.stb):
/// a text header line <c>PIAFILEVERSION_2.0,PC3VER1,compress\r\npmzlibcodec</c> (48 bytes),
/// then three little-endian uint32 fields — Adler-32 of the compressed body, uncompressed length,
/// compressed length — then a zlib stream holding the configuration text (NUL-terminated).
/// <para>
/// Used by forge_plot_to_pdf: the PDF drivers carry a <c>custom{ … name="View_New_File" value=TRUE }</c>
/// property ("open in viewer when done"). On a PC with no default PDF app, Windows answers that
/// with an OpenWith dialog and AutoCAD stalls on "Sending sheet to plotter" until the tool times
/// out. Forge plots through a clone of the requested PC3 with the property forced to FALSE.
/// </para>
/// </summary>
public static class Pc3File
{
    public const string ViewerProperty = "View_New_File";
    private const int HeaderLength = 48;
    private const int PrefixLength = HeaderLength + 12;

    /// <summary>Decodes the configuration text. Returns false for anything that is not a PIA zlib container.</summary>
    public static bool TryRead(byte[] bytes, out string text, out string header)
    {
        text = "";
        header = "";
        if (bytes is null || bytes.Length < PrefixLength + 2)
        {
            return false;
        }

        header = Encoding.ASCII.GetString(bytes, 0, HeaderLength);
        if (!header.StartsWith("PIAFILEVERSION_", StringComparison.Ordinal) || !header.EndsWith("pmzlibcodec", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var input = new MemoryStream(bytes, PrefixLength, bytes.Length - PrefixLength);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            z.CopyTo(output);
            var raw = output.ToArray();
            var length = raw.Length;
            while (length > 0 && raw[length - 1] == 0)
            {
                length--;
            }

            text = Encoding.Latin1.GetString(raw, 0, length);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>Encodes configuration text back into a PIA container using the given 48-byte header.</summary>
    public static byte[] Write(string text, string header)
    {
        if (header is null || header.Length != HeaderLength)
        {
            throw new ArgumentException($"PIA header must be exactly {HeaderLength} characters.", nameof(header));
        }

        var body = Encoding.Latin1.GetBytes(text + "\0");
        byte[] compressed;
        using (var output = new MemoryStream())
        {
            using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(body, 0, body.Length);
            }

            compressed = output.ToArray();
        }

        var result = new byte[PrefixLength + compressed.Length];
        Encoding.ASCII.GetBytes(header, 0, HeaderLength, result, 0);
        BitConverter.GetBytes(Adler32(compressed)).CopyTo(result, HeaderLength);
        BitConverter.GetBytes((uint)body.Length).CopyTo(result, HeaderLength + 4);
        BitConverter.GetBytes((uint)compressed.Length).CopyTo(result, HeaderLength + 8);
        compressed.CopyTo(result, PrefixLength);
        return result;
    }

    public static uint Adler32(byte[] data)
    {
        const uint Mod = 65521;
        uint a = 1, b = 0;
        foreach (var d in data)
        {
            a = (a + d) % Mod;
            b = (b + a) % Mod;
        }

        return (b << 16) | a;
    }

    /// <summary>Value of a custom{} property (e.g. View_New_File → "TRUE"), or null when the property is absent.</summary>
    public static string? GetCustomValue(string text, string name)
    {
        var match = CustomProperty(name).Match(text);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    /// <summary>Returns the text with the custom{} property set to <paramref name="value"/>; unchanged when the property is absent.</summary>
    public static string SetCustomValue(string text, string name, string value, out bool changed)
    {
        var match = CustomProperty(name).Match(text);
        if (!match.Success)
        {
            changed = false;
            return text;
        }

        var group = match.Groups["value"];
        changed = !string.Equals(group.Value.Trim(), value, StringComparison.Ordinal);
        return text[..group.Index] + value + text[(group.Index + group.Length)..];
    }

    /// <summary>True when the driver would open the plotted file in a viewer when done.</summary>
    public static bool OpensViewer(string text)
        => string.Equals(GetCustomValue(text, ViewerProperty), "TRUE", StringComparison.OrdinalIgnoreCase);

    /// <summary>Deterministic, clearly-owned file name for the no-viewer clone of a device, e.g. "765T-Forge DWG To PDF (no viewer).pc3".</summary>
    public static string NoViewerCloneName(string device)
    {
        var stem = Path.GetFileNameWithoutExtension(device.Trim());
        return $"765T-Forge {stem} (no viewer).pc3";
    }

    /// <summary>
    /// review-wp2-r2 Q3: forge_plot_to_pdf prepares the no-viewer clone BEFORE the media check —
    /// required ordering, since the device must exist for SetPlotConfigurationName — so a typed
    /// media failure can already have written a file. This is the path to report as
    /// `pc3Prepared` in failure data, and only when THIS call created the clone (state "clone",
    /// note "created"): a reused clone, a dry run, an already-clean PC3 or a non-PC3 device
    /// wrote nothing and must report null.
    /// </summary>
    public static string? PreparedClonePath(string state, string? note, string? clonePath)
        => state == "clone" && note == "created" ? clonePath : null;

    private static Regex CustomProperty(string name) => new(
        "name=\"" + Regex.Escape(name) + @"\s*[\r\n]+\s*value=(?<value>[^\r\n]*)",
        RegexOptions.CultureInvariant);
}
