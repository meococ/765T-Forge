using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Forge.Shared;

/// <summary>Outcome of a <see cref="PdfContentBox"/> read.</summary>
public enum PdfInkStatus
{
    /// <summary>Ink was found and bounded.</summary>
    Ok,

    /// <summary>Nothing is painted on the sheet.</summary>
    Empty,

    /// <summary>The ink could not be bounded safely. See the reason.</summary>
    Unavailable,
}

/// <summary>
/// Ink box of page 1. <paramref name="InkBox"/> is [x0,y0,x1,y1] in points (default user space),
/// clipped to the media box. Fills are ink size / media size. For Empty the fills are 0 and
/// <paramref name="LooksTiny"/> is true.
/// </summary>
public sealed record PdfInkResult(
    PdfInkStatus Status,
    string? Reason,
    double[]? InkBox,
    double[]? MediaBox,
    double? FillX,
    double? FillY,
    bool? LooksTiny);

/// <summary>An XObject for <see cref="PdfContentBox.ReadContent"/>: an image, or a form with its BBox and optional PdfMatrix.</summary>
public sealed record PdfXObjectInfo(bool IsImage, double[]? BBox, double[]? Matrix);

/// <summary>
/// Bounds everything painted on page 1 of a PDF (the "ink box"), so a plot can be checked for
/// coming out tiny on the sheet. The box is always an over-estimate. Anything the reader cannot
/// bound safely gives <see cref="PdfInkStatus.Unavailable"/> instead of a box that may be too small.
/// </summary>
public static class PdfContentBox
{
    /// <summary>Both fills below this value mean the drawing looks tiny on the sheet.</summary>
    public const double TinyThreshold = 0.5;

    /// <summary>Reads the ink box of page 1 of the PDF at <paramref name="path"/>. Never throws.</summary>
    public static PdfInkResult Read(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            return Unavailable(ex.Message, null);
        }

        return Read(bytes);
    }

    /// <summary>Reads the ink box of page 1 of a PDF held in memory. Never throws.</summary>
    public static PdfInkResult Read(byte[] pdf)
    {
        double[]? media = null;
        try
        {
            if (pdf is null || pdf.Length == 0)
            {
                return Unavailable("PDF data is empty", null);
            }

            var file = new PdfFile(pdf);
            var page = file.FindFirstPage();
            media = file.ReadMediaBox(page);
            var resources = file.ReadResources(page);
            var content = file.ReadContents(page);
            var ink = new PdfInkBounds();
            file.AddPrintableAnnotations(page, ink);
            new PdfContentInterpreter(content, resources, ink).Run();
            return Finish(ink, media);
        }
        catch (Exception ex)
        {
            return Unavailable(ex.Message, media);
        }
    }

    /// <summary>
    /// Bounds one decoded content stream on a sheet of <paramref name="mediaBox"/>.
    /// <paramref name="xobjects"/> maps the names used by <c>Do</c> (without the slash). Never throws.
    /// </summary>
    public static PdfInkResult ReadContent(
        byte[] decodedContentStream,
        double[] mediaBox,
        IReadOnlyDictionary<string, PdfXObjectInfo>? xobjects = null)
    {
        double[]? media = null;
        try
        {
            media = PdfCos.NormalizeRect(mediaBox) ?? throw new PdfInkException("media box must be 4 finite numbers");
            if (media[2] <= media[0] || media[3] <= media[1])
            {
                throw new PdfInkException("media box has no area");
            }

            var resources = PdfPageResources.FromXObjects(xobjects);
            var ink = new PdfInkBounds();
            new PdfContentInterpreter(decodedContentStream ?? Array.Empty<byte>(), resources, ink).Run();
            return Finish(ink, media);
        }
        catch (Exception ex)
        {
            return Unavailable(ex.Message, media);
        }
    }

    private static PdfInkResult Finish(PdfInkBounds ink, double[] media)
    {
        if (!ink.Any)
        {
            return Empty(media);
        }

        double x0 = Math.Max(ink.MinX, media[0]);
        double y0 = Math.Max(ink.MinY, media[1]);
        double x1 = Math.Min(ink.MaxX, media[2]);
        double y1 = Math.Min(ink.MaxY, media[3]);
        if (x0 > x1 || y0 > y1)
        {
            return Empty(media);
        }

        double fillX = (x1 - x0) / (media[2] - media[0]);
        double fillY = (y1 - y0) / (media[3] - media[1]);
        return new PdfInkResult(
            PdfInkStatus.Ok,
            null,
            [x0, y0, x1, y1],
            media,
            fillX,
            fillY,
            fillX < TinyThreshold && fillY < TinyThreshold);
    }

    private static PdfInkResult Empty(double[] media) =>
        new(PdfInkStatus.Empty, null, null, media, 0, 0, true);

    private static PdfInkResult Unavailable(string reason, double[]? media) =>
        new(PdfInkStatus.Unavailable, reason, null, media, null, null, null);
}

/// <summary>Raised when the ink cannot be bounded safely; becomes an Unavailable result.</summary>
internal sealed class PdfInkException(string message) : Exception(message);

internal sealed record PdfName(string Value);

internal sealed record PdfRef(int Number, int Generation);

internal sealed record PdfKeyword(string Value);

internal sealed class PdfString(byte[] bytes)
{
    public byte[] Bytes { get; } = bytes;
}

internal static class PdfCos
{
    public static object? Get(Dictionary<string, object?> dict, string key) =>
        dict.TryGetValue(key, out var value) ? value : null;

    public static string? NameOf(object? value) => (value as PdfName)?.Value;

    public static bool IsType(Dictionary<string, object?> dict, string type) => NameOf(Get(dict, "Type")) == type;

    /// <summary>Returns [minX, minY, maxX, maxY], or null unless there are 4 finite numbers.</summary>
    public static double[]? NormalizeRect(IReadOnlyList<double>? r)
    {
        if (r is null || r.Count != 4)
        {
            return null;
        }

        for (int i = 0; i < 4; i++)
        {
            if (!double.IsFinite(r[i]))
            {
                return null;
            }
        }

        return [Math.Min(r[0], r[2]), Math.Min(r[1], r[3]), Math.Max(r[0], r[2]), Math.Max(r[1], r[3])];
    }
}

/// <summary>Running bounding box of painted marks, in default user space.</summary>
internal sealed class PdfInkBounds
{
    public bool Any { get; private set; }

    public double MinX { get; private set; } = double.PositiveInfinity;

    public double MinY { get; private set; } = double.PositiveInfinity;

    public double MaxX { get; private set; } = double.NegativeInfinity;

    public double MaxY { get; private set; } = double.NegativeInfinity;

    public void Add(double x0, double y0, double x1, double y1)
    {
        if (!double.IsFinite(x0) || !double.IsFinite(y0) || !double.IsFinite(x1) || !double.IsFinite(y1))
        {
            throw new PdfInkException("coordinates are out of range");
        }

        Any = true;
        MinX = Math.Min(MinX, Math.Min(x0, x1));
        MinY = Math.Min(MinY, Math.Min(y0, y1));
        MaxX = Math.Max(MaxX, Math.Max(x0, x1));
        MaxY = Math.Max(MaxY, Math.Max(y0, y1));
    }
}

/// <summary>Affine matrices [a b c d e f], row-vector convention as in the PDF spec.</summary>
internal static class PdfMatrix
{
    public static readonly double[] Identity = [1, 0, 0, 1, 0, 0];

    /// <summary>Returns m × t (apply m first, then t).</summary>
    public static double[] Multiply(double[] m, double[] t) =>
    [
        m[0] * t[0] + m[1] * t[2],
        m[0] * t[1] + m[1] * t[3],
        m[2] * t[0] + m[3] * t[2],
        m[2] * t[1] + m[3] * t[3],
        m[4] * t[0] + m[5] * t[2] + t[4],
        m[4] * t[1] + m[5] * t[3] + t[5],
    ];

    public static (double X, double Y) Apply(double[] m, double x, double y) =>
        (m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5]);

    /// <summary>Largest factor by which the matrix stretches any length (largest singular value).</summary>
    public static double MaxStretch(double[] m)
    {
        double sum = m[0] * m[0] + m[1] * m[1] + m[2] * m[2] + m[3] * m[3];
        double det = m[0] * m[3] - m[1] * m[2];
        double root = Math.Sqrt(Math.Max(0, sum * sum - 4 * det * det));
        return Math.Sqrt((sum + root) / 2);
    }
}

/// <summary>Tokenizer and object parser shared by file-level objects and content streams.</summary>
internal sealed class PdfLexer
{
    private const int MaxDepth = 64;

    private readonly byte[] _b;
    private readonly int _end;

    public PdfLexer(byte[] buffer, int start, int end)
    {
        _b = buffer;
        Pos = start;
        _end = end;
    }

    public int Pos { get; set; }

    /// <summary>Every operator the content interpreter knows.</summary>
    public static HashSet<string> KnownOperators { get; } = new(StringComparer.Ordinal)
    {
        "q", "Q", "cm", "w", "J", "j", "M", "d", "ri", "i", "gs",
        "m", "l", "c", "v", "y", "re", "h",
        "S", "s", "f", "F", "f*", "B", "B*", "b", "b*", "n", "W", "W*",
        "BT", "ET", "Tc", "Tw", "Tz", "TL", "Tf", "Tr", "Ts", "Td", "TD", "Tm", "T*", "Tj", "TJ", "'", "\"",
        "d0", "d1", "CS", "cs", "SC", "SCN", "sc", "scn", "G", "g", "RG", "rg", "K", "k",
        "sh", "BI", "ID", "EI", "Do", "MP", "DP", "BMC", "BDC", "EMC", "BX", "EX",
    };

    // Declared after KnownOperators: static initializers run in source order.
    private static readonly Dictionary<long, string> Interned = BuildInterned();

    public static bool IsWhite(byte c) => c is 0 or 9 or 10 or 12 or 13 or 32;

    public static bool IsDelimiter(byte c) =>
        c is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
            or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    public static bool IsRegular(byte c) => !IsWhite(c) && !IsDelimiter(c);

    /// <summary>Skips whitespace and comments. Returns false at the end of the data.</summary>
    public bool SkipWhitespace()
    {
        while (Pos < _end)
        {
            byte c = _b[Pos];
            if (IsWhite(c))
            {
                Pos++;
            }
            else if (c == (byte)'%')
            {
                while (Pos < _end && _b[Pos] != 10 && _b[Pos] != 13)
                {
                    Pos++;
                }
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the bytes at the current position are <paramref name="word"/> as a whole token.</summary>
    public bool IsWord(string word)
    {
        if (Pos + word.Length > _end)
        {
            return false;
        }

        for (int i = 0; i < word.Length; i++)
        {
            if (_b[Pos + i] != word[i])
            {
                return false;
            }
        }

        return Pos + word.Length == _end || !IsRegular(_b[Pos + word.Length]);
    }

    /// <summary>
    /// Reads one object: number (double), PdfName, PdfString, List, Dictionary, bool, null,
    /// PdfRef (when <paramref name="allowRefs"/>), or a PdfKeyword for anything else.
    /// </summary>
    public object? ReadObject(bool allowRefs, int depth = 0)
    {
        if (depth > MaxDepth)
        {
            throw new PdfInkException("objects are nested too deeply");
        }

        if (!SkipWhitespace())
        {
            throw new PdfInkException("unexpected end of data");
        }

        byte c = _b[Pos];
        switch (c)
        {
            case (byte)'/':
                return ReadName();
            case (byte)'(':
                return new PdfString(ReadLiteralString());
            case (byte)'<':
                return Pos + 1 < _end && _b[Pos + 1] == (byte)'<'
                    ? ReadDictionary(allowRefs, depth)
                    : new PdfString(ReadHexString());
            case (byte)'[':
                return ReadArray(allowRefs, depth);
            case (byte)']' or (byte)'>' or (byte)')' or (byte)'{' or (byte)'}':
                throw new PdfInkException($"unexpected '{(char)c}'");
        }

        int start = Pos;
        while (Pos < _end && IsRegular(_b[Pos]))
        {
            Pos++;
        }

        var token = new ReadOnlySpan<byte>(_b, start, Pos - start);
        if (c is (byte)'+' or (byte)'-' or (byte)'.' || (c >= (byte)'0' && c <= (byte)'9'))
        {
            double value = ParseNumber(token);
            if (allowRefs && IsUnsignedInteger(token) && value <= int.MaxValue)
            {
                var reference = TryReadReferenceTail((int)value);
                if (reference is not null)
                {
                    return reference;
                }
            }

            return value;
        }

        string word = Keyword(token);
        return word switch
        {
            "true" => true,
            "false" => false,
            "null" => null,
            _ => new PdfKeyword(word),
        };
    }

    private PdfRef? TryReadReferenceTail(int number)
    {
        int saved = Pos;
        if (SkipWhitespace())
        {
            int start = Pos;
            while (Pos < _end && IsRegular(_b[Pos]))
            {
                Pos++;
            }

            var gen = new ReadOnlySpan<byte>(_b, start, Pos - start);
            if (IsUnsignedInteger(gen) && gen.Length <= 5 && SkipWhitespace() && IsWord("R"))
            {
                Pos++;
                return new PdfRef(number, (int)ParseNumber(gen));
            }
        }

        Pos = saved;
        return null;
    }

    private List<object?> ReadArray(bool allowRefs, int depth)
    {
        Pos++;
        var list = new List<object?>();
        while (true)
        {
            if (!SkipWhitespace())
            {
                throw new PdfInkException("unterminated array");
            }

            if (_b[Pos] == (byte)']')
            {
                Pos++;
                return list;
            }

            var item = ReadObject(allowRefs, depth + 1);
            if (item is PdfKeyword keyword)
            {
                throw new PdfInkException($"unexpected '{keyword.Value}' in an array");
            }

            list.Add(item);
        }
    }

    private Dictionary<string, object?> ReadDictionary(bool allowRefs, int depth)
    {
        Pos += 2;
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        while (true)
        {
            if (!SkipWhitespace())
            {
                throw new PdfInkException("unterminated dictionary");
            }

            if (_b[Pos] == (byte)'>' && Pos + 1 < _end && _b[Pos + 1] == (byte)'>')
            {
                Pos += 2;
                return dict;
            }

            if (ReadObject(allowRefs, depth + 1) is not PdfName key)
            {
                throw new PdfInkException("dictionary key is not a name");
            }

            var value = ReadObject(allowRefs, depth + 1);
            if (value is PdfKeyword keyword)
            {
                throw new PdfInkException($"unexpected '{keyword.Value}' in a dictionary");
            }

            dict[key.Value] = value;
        }
    }

    private PdfName ReadName()
    {
        Pos++;
        int start = Pos;
        while (Pos < _end && IsRegular(_b[Pos]))
        {
            Pos++;
        }

        var raw = new ReadOnlySpan<byte>(_b, start, Pos - start);
        if (raw.IndexOf((byte)'#') < 0)
        {
            return new PdfName(Encoding.Latin1.GetString(raw));
        }

        var bytes = new List<byte>(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == (byte)'#' && i + 2 < raw.Length && HexValue(raw[i + 1]) >= 0 && HexValue(raw[i + 2]) >= 0)
            {
                bytes.Add((byte)(HexValue(raw[i + 1]) * 16 + HexValue(raw[i + 2])));
                i += 2;
            }
            else
            {
                bytes.Add(raw[i]);
            }
        }

        return new PdfName(Encoding.Latin1.GetString(bytes.ToArray()));
    }

    private byte[] ReadLiteralString()
    {
        Pos++;
        var bytes = new List<byte>();
        int nesting = 1;
        while (Pos < _end)
        {
            byte c = _b[Pos++];
            if (c == (byte)'\\')
            {
                if (Pos >= _end)
                {
                    break;
                }

                byte e = _b[Pos++];
                switch (e)
                {
                    case (byte)'n': bytes.Add(10); break;
                    case (byte)'r': bytes.Add(13); break;
                    case (byte)'t': bytes.Add(9); break;
                    case (byte)'b': bytes.Add(8); break;
                    case (byte)'f': bytes.Add(12); break;
                    case 13:
                        if (Pos < _end && _b[Pos] == 10)
                        {
                            Pos++;
                        }

                        break;
                    case 10:
                        break;
                    case >= (byte)'0' and <= (byte)'7':
                        int value = e - '0';
                        for (int k = 0; k < 2 && Pos < _end && _b[Pos] >= (byte)'0' && _b[Pos] <= (byte)'7'; k++)
                        {
                            value = value * 8 + (_b[Pos++] - '0');
                        }

                        bytes.Add((byte)(value & 0xFF));
                        break;
                    default:
                        bytes.Add(e);
                        break;
                }
            }
            else if (c == (byte)'(')
            {
                nesting++;
                bytes.Add(c);
            }
            else if (c == (byte)')')
            {
                if (--nesting == 0)
                {
                    return bytes.ToArray();
                }

                bytes.Add(c);
            }
            else if (c == 13)
            {
                if (Pos < _end && _b[Pos] == 10)
                {
                    Pos++;
                }

                bytes.Add(10);
            }
            else
            {
                bytes.Add(c);
            }
        }

        throw new PdfInkException("unterminated string");
    }

    private byte[] ReadHexString()
    {
        Pos++;
        var bytes = new List<byte>();
        int high = -1;
        while (Pos < _end)
        {
            byte c = _b[Pos++];
            if (c == (byte)'>')
            {
                if (high >= 0)
                {
                    bytes.Add((byte)(high * 16));
                }

                return bytes.ToArray();
            }

            if (IsWhite(c))
            {
                continue;
            }

            int v = HexValue(c);
            if (v < 0)
            {
                throw new PdfInkException("bad hex string");
            }

            if (high < 0)
            {
                high = v;
            }
            else
            {
                bytes.Add((byte)(high * 16 + v));
                high = -1;
            }
        }

        throw new PdfInkException("unterminated hex string");
    }

    private static int HexValue(byte c) => c switch
    {
        >= (byte)'0' and <= (byte)'9' => c - '0',
        >= (byte)'a' and <= (byte)'f' => c - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => c - 'A' + 10,
        _ => -1,
    };

    private static double ParseNumber(ReadOnlySpan<byte> token)
    {
        if (!double.TryParse(
                token,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out double value)
            || !double.IsFinite(value))
        {
            throw new PdfInkException($"bad number '{Encoding.Latin1.GetString(token[..Math.Min(token.Length, 24)])}'");
        }

        return value;
    }

    private static bool IsUnsignedInteger(ReadOnlySpan<byte> token)
    {
        if (token.Length == 0 || token.Length > 10)
        {
            return false;
        }

        foreach (byte c in token)
        {
            if (c < (byte)'0' || c > (byte)'9')
            {
                return false;
            }
        }

        return true;
    }

    private static string Keyword(ReadOnlySpan<byte> token)
    {
        if (token.Length is > 0 and <= 7 && Interned.TryGetValue(KeyOf(token), out var known))
        {
            return known;
        }

        return Encoding.Latin1.GetString(token);
    }

    private static long KeyOf(ReadOnlySpan<byte> token)
    {
        long key = token.Length;
        for (int i = 0; i < token.Length; i++)
        {
            key |= (long)token[i] << (8 * (i + 1));
        }

        return key;
    }

    private static Dictionary<long, string> BuildInterned()
    {
        var map = new Dictionary<long, string>();
        foreach (var word in KnownOperators.Concat(["true", "false", "null", "R", "obj", "endobj", "stream"]))
        {
            map[KeyOf(Encoding.ASCII.GetBytes(word))] = word;
        }

        return map;
    }
}

/// <summary>An indirect object found in the file; streams keep the position of their raw data.</summary>
internal sealed class PdfObject(object? value)
{
    public object? Value { get; } = value;

    public int DataStart { get; set; } = -1;

    public int DataLength { get; set; }

    public bool IsStream => DataStart >= 0;
}

/// <summary>
/// File-level reader: finds objects by scanning for "N G obj" (no xref parsing), the page tree,
/// and the decoded content of page 1.
/// </summary>
internal sealed class PdfFile
{
    private const long MaxContentBytes = 512L * 1024 * 1024;

    private readonly byte[] _b;
    private readonly Dictionary<int, PdfObject> _objects = new();
    private readonly Dictionary<int, List<int>> _headerEnds = new();
    private readonly List<(int Offset, Dictionary<string, object?> Dict)> _trailers = new();
    private bool _hasObjectStreams;

    public PdfFile(byte[] bytes)
    {
        _b = bytes;
        var headers = FindObjectHeaders();
        foreach (var header in headers)
        {
            if (!_headerEnds.TryGetValue(header.Number, out var ends))
            {
                _headerEnds[header.Number] = ends = new List<int>();
            }

            ends.Add(header.End);
        }

        int skipUntil = 0;
        foreach (var header in headers)
        {
            if (header.Start < skipUntil)
            {
                continue;
            }

            var obj = TryParseObjectAt(header.End, out int next);
            if (obj is null)
            {
                continue;
            }

            _objects[header.Number] = obj;
            skipUntil = next;
            if (obj.Value is Dictionary<string, object?> dict)
            {
                var type = PdfCos.NameOf(PdfCos.Get(dict, "Type"));
                if (type == "ObjStm")
                {
                    _hasObjectStreams = true;
                }
                else if (type == "XRef")
                {
                    _trailers.Add((header.Start, dict));
                }
            }
        }

        FindTrailers();
        _trailers.Sort((x, y) => x.Offset.CompareTo(y.Offset));
        foreach (var trailer in _trailers)
        {
            if (PdfCos.Get(trailer.Dict, "Encrypt") is not null)
            {
                throw new PdfInkException("encrypted PDF is not supported");
            }
        }
    }

    public object? Resolve(object? value)
    {
        for (int i = 0; i < 32; i++)
        {
            if (value is not PdfRef reference)
            {
                return value;
            }

            value = _objects.TryGetValue(reference.Number, out var obj) ? obj.Value : null;
        }

        throw new PdfInkException("reference chain is too long");
    }

    /// <summary>Page 1 from the page tree; falls back to the only /Type /Page object.</summary>
    public Dictionary<string, object?> FindFirstPage()
    {
        for (int i = _trailers.Count - 1; i >= 0; i--)
        {
            if (Resolve(PdfCos.Get(_trailers[i].Dict, "Root")) is not Dictionary<string, object?> root)
            {
                continue;
            }

            if (Resolve(PdfCos.Get(root, "Pages")) is Dictionary<string, object?> pages)
            {
                var page = FirstLeaf(pages, 0);
                if (page is not null)
                {
                    return page;
                }
            }

            break;
        }

        Dictionary<string, object?>? only = null;
        int count = 0;
        foreach (var obj in _objects.Values)
        {
            if (obj.Value is Dictionary<string, object?> dict && PdfCos.IsType(dict, "Page"))
            {
                only = dict;
                count++;
            }
        }

        return count switch
        {
            1 => only!,
            0 => throw new PdfInkException(_hasObjectStreams
                ? "page 1 is inside a compressed object stream (not supported)"
                : "no page object found"),
            _ => throw new PdfInkException("cannot tell which page is page 1 (no readable page tree)"),
        };
    }

    public double[] ReadMediaBox(Dictionary<string, object?> page)
    {
        var box = ReadRect(Inherited(page, "MediaBox")) ?? throw new PdfInkException("page 1 has no valid /MediaBox");
        if (box[2] <= box[0] || box[3] <= box[1])
        {
            throw new PdfInkException("page 1 /MediaBox has no area");
        }

        return box;
    }

    public PdfPageResources ReadResources(Dictionary<string, object?> page)
    {
        var res = new PdfPageResources();
        if (Inherited(page, "Resources") is not Dictionary<string, object?> dict)
        {
            return res;
        }

        if (Resolve(PdfCos.Get(dict, "XObject")) is Dictionary<string, object?> xobjects)
        {
            foreach (var (name, value) in xobjects)
            {
                if (value is PdfRef reference && !_objects.ContainsKey(reference.Number))
                {
                    res.BadXObjects[name] = Missing(reference);
                    continue;
                }

                if (Resolve(value) is not Dictionary<string, object?> xo)
                {
                    res.BadXObjects[name] = "not a stream";
                    continue;
                }

                var subtype = PdfCos.NameOf(Resolve(PdfCos.Get(xo, "Subtype")));
                if (subtype == "Image")
                {
                    res.XObjects[name] = new PdfXObjectInfo(true, null, null);
                }
                else if (subtype == "Form")
                {
                    var bbox = ReadRect(PdfCos.Get(xo, "BBox"));
                    var matrix = ReadNumbers(PdfCos.Get(xo, "Matrix"));
                    if (bbox is null)
                    {
                        res.BadXObjects[name] = "form XObject without a valid /BBox";
                    }
                    else if (PdfCos.Get(xo, "Matrix") is not null && matrix is not { Length: 6 })
                    {
                        res.BadXObjects[name] = "form XObject with a bad /Matrix";
                    }
                    else
                    {
                        res.XObjects[name] = new PdfXObjectInfo(false, bbox, matrix);
                    }
                }
                else
                {
                    res.BadXObjects[name] = $"unsupported XObject subtype /{subtype}";
                }
            }
        }

        if (Resolve(PdfCos.Get(dict, "ExtGState")) is Dictionary<string, object?> states)
        {
            foreach (var (name, value) in states)
            {
                if (Resolve(value) is Dictionary<string, object?> gs)
                {
                    res.ExtGStates[name] = ReadExtGState(gs);
                }
            }
        }

        if (Resolve(PdfCos.Get(dict, "Font")) is Dictionary<string, object?> fonts)
        {
            foreach (var (name, value) in fonts)
            {
                var problem = FontProblem(Resolve(value) as Dictionary<string, object?>);
                if (problem is not null)
                {
                    res.BadFonts[name] = problem;
                }
            }
        }

        return res;
    }

    /// <summary>Concatenated, decoded /Contents of the page (empty when there is none).</summary>
    public byte[] ReadContents(Dictionary<string, object?> page)
    {
        var streams = new List<PdfObject>();
        var contents = PdfCos.Get(page, "Contents");
        if (contents is PdfRef reference)
        {
            var obj = Require(reference);
            if (obj.IsStream)
            {
                streams.Add(obj);
            }
            else if (obj.Value is List<object?> list)
            {
                AddStreams(list, streams);
            }
            else if (obj.Value is not null)
            {
                throw new PdfInkException("page /Contents is not a stream");
            }
        }
        else if (contents is List<object?> list)
        {
            AddStreams(list, streams);
        }
        else if (contents is not null)
        {
            throw new PdfInkException("page /Contents is not a stream");
        }

        using var all = new MemoryStream();
        foreach (var stream in streams)
        {
            all.Write(Decode(stream));
            all.WriteByte(10);
            if (all.Length > MaxContentBytes)
            {
                throw new PdfInkException("page content is too large");
            }
        }

        return all.ToArray();
    }

    /// <summary>Adds the /Rect of annotations that print (Print flag set, not Hidden).</summary>
    public void AddPrintableAnnotations(Dictionary<string, object?> page, PdfInkBounds ink)
    {
        if (Resolve(PdfCos.Get(page, "Annots")) is not List<object?> annots)
        {
            return;
        }

        foreach (var item in annots)
        {
            if (Resolve(item) is not Dictionary<string, object?> annot)
            {
                continue;
            }

            long flags = Resolve(PdfCos.Get(annot, "F")) is double f ? (long)f : 0;
            var subtype = PdfCos.NameOf(Resolve(PdfCos.Get(annot, "Subtype")));
            if ((flags & 4) == 0 || (flags & 2) != 0 || subtype is "Link" or "Popup")
            {
                continue;
            }

            var rect = ReadRect(PdfCos.Get(annot, "Rect")) ?? throw new PdfInkException("printable annotation without a valid /Rect");
            ink.Add(rect[0], rect[1], rect[2], rect[3]);
        }
    }

    private List<(int Start, int End, int Number)> FindObjectHeaders()
    {
        var headers = new List<(int Start, int End, int Number)>();
        var span = _b.AsSpan();
        int from = 0;
        while (from < span.Length)
        {
            int k = span[from..].IndexOf("obj"u8);
            if (k < 0)
            {
                break;
            }

            k += from;
            from = k + 3;
            if (k + 3 < span.Length && PdfLexer.IsRegular(span[k + 3]))
            {
                continue;
            }

            int p = k - 1;
            if (!SkipBackWhite(span, ref p) || !SkipBackDigits(span, ref p, out int genStart, out int genEnd))
            {
                continue;
            }

            if (!SkipBackWhite(span, ref p) || !SkipBackDigits(span, ref p, out int numStart, out int numEnd))
            {
                continue;
            }

            if (p >= 0 && PdfLexer.IsRegular(span[p]))
            {
                continue;
            }

            if (numEnd - numStart > 10 || genEnd - genStart > 5
                || !int.TryParse(span[numStart..numEnd], NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            {
                continue;
            }

            headers.Add((numStart, k + 3, number));
        }

        return headers;
    }

    private static bool SkipBackWhite(ReadOnlySpan<byte> span, ref int p)
    {
        int start = p;
        while (p >= 0 && PdfLexer.IsWhite(span[p]))
        {
            p--;
        }

        return p < start && p >= 0;
    }

    private static bool SkipBackDigits(ReadOnlySpan<byte> span, ref int p, out int start, out int end)
    {
        end = p + 1;
        while (p >= 0 && span[p] >= (byte)'0' && span[p] <= (byte)'9')
        {
            p--;
        }

        start = p + 1;
        return start < end;
    }

    private PdfObject? TryParseObjectAt(int position, out int next)
    {
        next = position;
        try
        {
            var lex = new PdfLexer(_b, position, _b.Length);
            var value = lex.ReadObject(allowRefs: true);
            var obj = new PdfObject(value is PdfKeyword ? null : value);
            next = lex.Pos;
            if (value is Dictionary<string, object?> dict && lex.SkipWhitespace() && lex.IsWord("stream"))
            {
                next = LocateStreamData(dict, lex.Pos + 6, obj);
            }

            return obj;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private int LocateStreamData(Dictionary<string, object?> dict, int start, PdfObject obj)
    {
        int n = _b.Length;
        if (start < n && _b[start] == 13)
        {
            start++;
            if (start < n && _b[start] == 10)
            {
                start++;
            }
        }
        else if (start < n && _b[start] == 10)
        {
            start++;
        }

        long length = LengthOf(PdfCos.Get(dict, "Length"));
        if (length >= 0 && start + length <= n)
        {
            int p = (int)(start + length);
            while (p < n && PdfLexer.IsWhite(_b[p]))
            {
                p++;
            }

            if (_b.AsSpan(p).StartsWith("endstream"u8))
            {
                obj.DataStart = start;
                obj.DataLength = (int)length;
                return p + 9;
            }
        }

        int e = _b.AsSpan(start).IndexOf("endstream"u8);
        if (e < 0)
        {
            throw new PdfInkException("stream without endstream");
        }

        e += start;
        int end = e;
        if (end > start && _b[end - 1] == 10)
        {
            end--;
        }

        if (end > start && _b[end - 1] == 13)
        {
            end--;
        }

        obj.DataStart = start;
        obj.DataLength = end - start;
        return e + 9;
    }

    /// <summary>The stream /Length, direct or indirect; -1 when unknown.</summary>
    private long LengthOf(object? value)
    {
        if (value is PdfRef reference)
        {
            value = null;
            if (_objects.TryGetValue(reference.Number, out var known))
            {
                value = known.Value;
            }
            else if (_headerEnds.TryGetValue(reference.Number, out var ends))
            {
                for (int i = ends.Count - 1; i >= 0 && value is null; i--)
                {
                    try
                    {
                        value = new PdfLexer(_b, ends[i], _b.Length).ReadObject(allowRefs: false) as double?;
                    }
                    catch (PdfInkException)
                    {
                        value = null;
                    }
                }
            }
        }

        return value is double d && d >= 0 && d <= int.MaxValue && d == Math.Floor(d) ? (long)d : -1;
    }

    private void FindTrailers()
    {
        var span = _b.AsSpan();
        int from = 0;
        while (from < span.Length)
        {
            int k = span[from..].IndexOf("trailer"u8);
            if (k < 0)
            {
                break;
            }

            k += from;
            from = k + 7;
            try
            {
                if (new PdfLexer(_b, k + 7, _b.Length).ReadObject(allowRefs: true) is Dictionary<string, object?> dict)
                {
                    _trailers.Add((k, dict));
                }
            }
            catch (PdfInkException)
            {
                // Not a trailer after all (e.g. the word inside other data).
            }
        }
    }

    private Dictionary<string, object?>? FirstLeaf(Dictionary<string, object?> node, int depth)
    {
        if (depth > 64)
        {
            throw new PdfInkException("page tree is too deep");
        }

        if (PdfCos.IsType(node, "Page"))
        {
            return node;
        }

        if (Resolve(PdfCos.Get(node, "Kids")) is not List<object?> kids)
        {
            return null;
        }

        foreach (var kid in kids)
        {
            if (kid is PdfRef reference && !_objects.ContainsKey(reference.Number))
            {
                throw new PdfInkException("page tree " + Missing(reference));
            }

            if (Resolve(kid) is Dictionary<string, object?> child)
            {
                var leaf = FirstLeaf(child, depth + 1);
                if (leaf is not null)
                {
                    return leaf;
                }
            }
        }

        return null;
    }

    private object? Inherited(Dictionary<string, object?> page, string key)
    {
        var node = page;
        for (int i = 0; node is not null && i < 64; i++)
        {
            if (PdfCos.Get(node, key) is { } value)
            {
                return Resolve(value);
            }

            node = Resolve(PdfCos.Get(node, "Parent")) as Dictionary<string, object?>;
        }

        return null;
    }

    private PdfObject Require(PdfRef reference) =>
        _objects.TryGetValue(reference.Number, out var obj) ? obj : throw new PdfInkException(Missing(reference));

    private string Missing(PdfRef reference) =>
        $"object {reference.Number} not found"
        + (_hasObjectStreams ? " (it may be inside a compressed object stream, which is not supported)" : string.Empty);

    private void AddStreams(List<object?> list, List<PdfObject> streams)
    {
        foreach (var item in list)
        {
            if (item is not PdfRef reference)
            {
                throw new PdfInkException("page /Contents array holds a non-reference");
            }

            var obj = Require(reference);
            if (!obj.IsStream)
            {
                throw new PdfInkException($"page /Contents object {reference.Number} is not a stream");
            }

            streams.Add(obj);
        }
    }

    private byte[] Decode(PdfObject stream)
    {
        var dict = (Dictionary<string, object?>)stream.Value!;
        if (PdfCos.Get(dict, "F") is not null)
        {
            throw new PdfInkException("content stream with external data (/F) is not supported");
        }

        var filters = new List<string>();
        var filter = Resolve(PdfCos.Get(dict, "Filter"));
        if (filter is PdfName single)
        {
            filters.Add(single.Value);
        }
        else if (filter is List<object?> list)
        {
            foreach (var item in list)
            {
                filters.Add(PdfCos.NameOf(Resolve(item)) ?? throw new PdfInkException("bad /Filter"));
            }
        }
        else if (filter is not null)
        {
            throw new PdfInkException("bad /Filter");
        }

        var raw = new ReadOnlySpan<byte>(_b, stream.DataStart, stream.DataLength);
        if (filters.Count == 0)
        {
            return raw.ToArray();
        }

        if (filters.Count > 1 || filters[0] != "FlateDecode")
        {
            throw new PdfInkException("unsupported content filter " + string.Join(" ", filters.Select(x => "/" + x)));
        }

        var parms = Resolve(PdfCos.Get(dict, "DecodeParms"));
        if (parms is List<object?> parmList)
        {
            parms = parmList.Count > 0 ? Resolve(parmList[0]) : null;
        }

        if (parms is Dictionary<string, object?> parmDict && Resolve(PdfCos.Get(parmDict, "Predictor")) is double predictor && predictor > 1)
        {
            throw new PdfInkException("FlateDecode predictors are not supported for content streams");
        }

        return Inflate(stream.DataStart, stream.DataLength);
    }

    private byte[] Inflate(int start, int length)
    {
        using var input = new MemoryStream(_b, start, length, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = zlib.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            if (output.Length > MaxContentBytes)
            {
                throw new PdfInkException("page content is too large");
            }
        }

        var data = output.ToArray();

        // A truncated deflate stream decodes without error, so check the zlib Adler-32 trailer.
        uint adler = Adler32(data);
        var tail = new ReadOnlySpan<byte>(_b, start, length);
        tail = tail[Math.Max(0, tail.Length - 16)..];
        ReadOnlySpan<byte> expected = [(byte)(adler >> 24), (byte)(adler >> 16), (byte)(adler >> 8), (byte)adler];
        if (tail.IndexOf(expected) < 0)
        {
            throw new PdfInkException("FlateDecode content stream is truncated or corrupt (checksum mismatch)");
        }

        return data;
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        int i = 0;
        while (i < data.Length)
        {
            int end = Math.Min(data.Length, i + 5552);
            for (; i < end; i++)
            {
                a += data[i];
                b += a;
            }

            a %= 65521;
            b %= 65521;
        }

        return (b << 16) | a;
    }

    private double[]? ReadRect(object? value)
    {
        var numbers = ReadNumbers(value);
        return numbers is { Length: 4 } ? PdfCos.NormalizeRect(numbers) : null;
    }

    private double[]? ReadNumbers(object? value)
    {
        if (Resolve(value) is not List<object?> list)
        {
            return null;
        }

        var numbers = new double[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            if (Resolve(list[i]) is not double d)
            {
                return null;
            }

            numbers[i] = d;
        }

        return numbers;
    }

    private PdfGsParams ReadExtGState(Dictionary<string, object?> gs)
    {
        double? Number(string key) => Resolve(PdfCos.Get(gs, key)) as double?;

        double? fontSize = null;
        string? fontProblem = null;
        if (Resolve(PdfCos.Get(gs, "Font")) is List<object?> { Count: 2 } font)
        {
            fontProblem = FontProblem(Resolve(font[0]) as Dictionary<string, object?>);
            fontSize = Resolve(font[1]) as double?;
        }

        return new PdfGsParams(
            Number("LW"),
            Number("LC") is double cap ? (int)Math.Clamp(cap, -1, 99) : null,
            Number("LJ") is double join ? (int)Math.Clamp(join, -1, 99) : null,
            Number("ML"),
            fontSize,
            fontProblem);
    }

    /// <summary>Why text in this font cannot be bounded by the em-box model, or null if it can.</summary>
    private string? FontProblem(Dictionary<string, object?>? font)
    {
        if (font is null)
        {
            return null;
        }

        var subtype = PdfCos.NameOf(Resolve(PdfCos.Get(font, "Subtype")));
        if (subtype == "Type3")
        {
            return "Type3 font (glyph size is not known)";
        }

        var encoding = Resolve(PdfCos.Get(font, "Encoding"));
        if (subtype == "Type0"
            && ((PdfCos.NameOf(encoding)?.EndsWith("-V", StringComparison.Ordinal) ?? false)
                || (encoding is Dictionary<string, object?> cmap && Resolve(PdfCos.Get(cmap, "WMode")) is double mode && mode != 0)))
        {
            return "vertical-writing font";
        }

        return null;
    }
}

internal sealed record PdfGsParams(double? LineWidth, int? LineCap, int? LineJoin, double? MiterLimit, double? FontSize, string? FontProblem);

/// <summary>What the interpreter knows about the page resources.</summary>
internal sealed class PdfPageResources
{
    public Dictionary<string, PdfXObjectInfo> XObjects { get; } = new(StringComparer.Ordinal);

    /// <summary>XObject names that cannot be bounded, with the reason.</summary>
    public Dictionary<string, string> BadXObjects { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, PdfGsParams> ExtGStates { get; } = new(StringComparer.Ordinal);

    /// <summary>Font names whose text cannot be bounded, with the reason.</summary>
    public Dictionary<string, string> BadFonts { get; } = new(StringComparer.Ordinal);

    public static PdfPageResources FromXObjects(IReadOnlyDictionary<string, PdfXObjectInfo>? xobjects)
    {
        var res = new PdfPageResources();
        if (xobjects is null)
        {
            return res;
        }

        foreach (var (name, info) in xobjects)
        {
            if (info is null)
            {
                res.BadXObjects[name] = "no XObject information";
            }
            else if (info.IsImage)
            {
                res.XObjects[name] = info;
            }
            else if (info.BBox is not { Length: 4 } || info.BBox.Any(v => !double.IsFinite(v)))
            {
                res.BadXObjects[name] = "form XObject without a valid /BBox";
            }
            else if (info.Matrix is not null && (info.Matrix.Length != 6 || info.Matrix.Any(v => !double.IsFinite(v))))
            {
                res.BadXObjects[name] = "form XObject with a bad /Matrix";
            }
            else
            {
                res.XObjects[name] = info;
            }
        }

        return res;
    }
}

/// <summary>
/// Walks a content stream and adds a conservative box for every painting operator.
/// Clip paths (<c>W n</c>) are not ink, and ink is never intersected with the clip.
/// </summary>
internal sealed class PdfContentInterpreter
{
    private readonly byte[] _data;
    private readonly PdfLexer _lex;
    private readonly PdfPageResources _res;
    private readonly PdfInkBounds _ink;
    private readonly Stack<GState> _saved = new();
    private GState _g = new();

    private bool _hasPath;
    private double _pathMinX, _pathMinY, _pathMaxX, _pathMaxY;
    private int _subpathSegments;
    private bool _pathHasJoin;

    private double[] _tm = PdfMatrix.Identity;
    private double[] _tlm = PdfMatrix.Identity;

    // The pen is somewhere in [_penLo, _penHi] along the text-space x axis of _tm.
    private double _penLo, _penHi;

    public PdfContentInterpreter(byte[] data, PdfPageResources resources, PdfInkBounds ink)
    {
        _data = data;
        _lex = new PdfLexer(data, 0, data.Length);
        _res = resources;
        _ink = ink;
    }

    public void Run()
    {
        var operands = new List<object?>(8);
        while (_lex.SkipWhitespace())
        {
            var token = _lex.ReadObject(allowRefs: false);
            if (token is PdfKeyword keyword)
            {
                Execute(keyword.Value, operands);
                operands.Clear();
            }
            else
            {
                operands.Add(token);
            }
        }
    }

    private void Execute(string op, List<object?> a)
    {
        switch (op)
        {
            // Graphics state.
            case "q":
                _saved.Push(_g.Clone());
                break;
            case "Q":
                if (_saved.Count > 0)
                {
                    _g = _saved.Pop();
                }

                break;
            case "cm":
                _g.Ctm = PdfMatrix.Multiply(Numbers(a, 6, op), _g.Ctm);
                break;
            case "w":
                _g.LineWidth = Math.Abs(Numbers(a, 1, op)[0]);
                break;
            case "J":
                _g.LineCap = (int)Math.Clamp(Numbers(a, 1, op)[0], -1, 99);
                break;
            case "j":
                _g.LineJoin = (int)Math.Clamp(Numbers(a, 1, op)[0], -1, 99);
                break;
            case "M":
                _g.MiterLimit = Numbers(a, 1, op)[0];
                break;
            case "gs":
                ApplyExtGState(a);
                break;

            // Never paint: dash, intent, flatness, marked content, compatibility, Type 3 metrics, clip.
            case "d" or "ri" or "i":
            case "BMC" or "BDC" or "EMC" or "MP" or "DP" or "BX" or "EX":
            case "d0" or "d1" or "W" or "W*":
                break;

            // Colour setters — TOOL-GAP #30b: only paints proven pure white are excluded (the
            // DWG-to-PDF driver's full-page '1 1 1 rg … re f' background pushed the ink box x-max
            // to the 2592-pt page edge while the PNG had no ink past 35.51"). Colour-space
            // selection resets the colour to unproven (it is not itself a colour).
            case "g":
                _g.FillColor = Numbers(a, 1, op);
                _g.FillSpace = "DeviceGray";
                break;
            case "G":
                _g.StrokeColor = Numbers(a, 1, op);
                _g.StrokeSpace = "DeviceGray";
                break;
            case "rg":
                _g.FillColor = Numbers(a, 3, op);
                _g.FillSpace = "DeviceRGB";
                break;
            case "RG":
                _g.StrokeColor = Numbers(a, 3, op);
                _g.StrokeSpace = "DeviceRGB";
                break;
            case "k":
                _g.FillColor = Numbers(a, 4, op);
                _g.FillSpace = "DeviceCMYK";
                break;
            case "K":
                _g.StrokeColor = Numbers(a, 4, op);
                _g.StrokeSpace = "DeviceCMYK";
                break;
            case "sc" or "scn":
                _g.FillColor = ScColor(a);
                break;
            case "SC" or "SCN":
                _g.StrokeColor = ScColor(a);
                break;
            case "cs":
                // Device-space name → channel semantics stay provable; anything else (Indexed,
                // Separation, ICCBased, unresolvable) → channels are not gray/RGB/CMYK (F7).
                _g.FillSpace = a.Count == 1 && a[0] is PdfName fillSpace ? fillSpace.Value : null;
                _g.FillColor = null;
                break;
            case "CS":
                _g.StrokeSpace = a.Count == 1 && a[0] is PdfName strokeSpace ? strokeSpace.Value : null;
                _g.StrokeColor = null;
                break;

            // Path construction.
            case "m":
            {
                var p = Numbers(a, 2, op);
                _subpathSegments = 0;
                AddPathPoint(p[0], p[1]);
                break;
            }

            case "l":
            {
                var p = Numbers(a, 2, op);
                AddPathPoint(p[0], p[1]);
                AddSegment();
                break;
            }

            case "c":
            {
                var p = Numbers(a, 6, op);
                AddPathPoint(p[0], p[1]);
                AddPathPoint(p[2], p[3]);
                AddPathPoint(p[4], p[5]);
                AddSegment();
                break;
            }

            case "v" or "y":
            {
                var p = Numbers(a, 4, op);
                AddPathPoint(p[0], p[1]);
                AddPathPoint(p[2], p[3]);
                AddSegment();
                break;
            }

            case "re":
            {
                var p = Numbers(a, 4, op);
                AddPathPoint(p[0], p[1]);
                AddPathPoint(p[0] + p[2], p[1]);
                AddPathPoint(p[0] + p[2], p[1] + p[3]);
                AddPathPoint(p[0], p[1] + p[3]);
                _pathHasJoin = true;
                _subpathSegments = 0;
                break;
            }

            case "h":
                NoOperands(a, op);
                ClosePath();
                break;

            // Painting.
            case "S":
                NoOperands(a, op);
                PaintPath(stroke: true, fill: false);
                break;
            case "B" or "B*":
                NoOperands(a, op);
                PaintPath(stroke: true, fill: true);
                break;
            case "s":
                NoOperands(a, op);
                ClosePath();
                PaintPath(stroke: true, fill: false);
                break;
            case "b" or "b*":
                NoOperands(a, op);
                ClosePath();
                PaintPath(stroke: true, fill: true);
                break;
            case "f" or "F" or "f*":
                NoOperands(a, op);
                PaintPath(stroke: false, fill: true);
                break;
            case "n":
                ClearPath();
                break;
            case "sh":
                throw new PdfInkException("shading operator 'sh' cannot be bounded");

            // Text.
            case "BT":
                _tm = _tlm = PdfMatrix.Identity;
                _penLo = _penHi = 0;
                break;
            case "ET":
                break;
            case "Tc":
                _g.CharSpacing = Numbers(a, 1, op)[0];
                break;
            case "Tw":
                _g.WordSpacing = Numbers(a, 1, op)[0];
                break;
            case "Tz":
                _g.HorizontalScale = Numbers(a, 1, op)[0] / 100;
                break;
            case "TL":
                _g.Leading = Numbers(a, 1, op)[0];
                break;
            case "Ts":
                _g.Rise = Numbers(a, 1, op)[0];
                break;
            case "Tr":
                _g.RenderMode = (int)Math.Clamp(Numbers(a, 1, op)[0], -1, 99);
                break;
            case "Tf":
                if (a.Count != 2 || a[0] is not PdfName font || a[1] is not double size)
                {
                    throw BadOperands(op, a);
                }

                SetFont(font.Value, size);
                break;
            case "Td":
            {
                var p = Numbers(a, 2, op);
                NextLine(p[0], p[1]);
                break;
            }

            case "TD":
            {
                var p = Numbers(a, 2, op);
                _g.Leading = -p[1];
                NextLine(p[0], p[1]);
                break;
            }

            case "Tm":
                _tm = _tlm = Numbers(a, 6, op);
                _penLo = _penHi = 0;
                break;
            case "T*":
                NoOperands(a, op);
                NextLine(0, -_g.Leading);
                break;
            case "Tj":
                ShowText([a.Count == 1 && a[0] is PdfString ? a[0] : throw BadOperands(op, a)]);
                break;
            case "TJ":
                ShowText(a.Count == 1 && a[0] is List<object?> items ? items : throw BadOperands(op, a));
                break;
            case "'":
                if (a.Count != 1 || a[0] is not PdfString)
                {
                    throw BadOperands(op, a);
                }

                NextLine(0, -_g.Leading);
                ShowText([a[0]]);
                break;
            case "\"":
                if (a.Count != 3 || a[0] is not double wordSpacing || a[1] is not double charSpacing || a[2] is not PdfString)
                {
                    throw BadOperands(op, a);
                }

                _g.WordSpacing = wordSpacing;
                _g.CharSpacing = charSpacing;
                NextLine(0, -_g.Leading);
                ShowText([a[2]]);
                break;

            // Images and forms.
            case "Do":
                if (a.Count != 1 || a[0] is not PdfName xobject)
                {
                    throw BadOperands(op, a);
                }

                PaintXObject(xobject.Value);
                break;
            case "BI":
                InlineImage();
                break;
            case "ID" or "EI":
                throw new PdfInkException($"'{op}' outside an inline image");

            default:
                throw new PdfInkException($"unknown operator '{op}'");
        }
    }

    private static double[] Numbers(List<object?> a, int count, string op)
    {
        if (a.Count != count)
        {
            throw BadOperands(op, a);
        }

        var numbers = new double[count];
        for (int i = 0; i < count; i++)
        {
            numbers[i] = a[i] is double d ? d : throw BadOperands(op, a);
        }

        return numbers;
    }

    /// <summary>
    /// sc/scn/SC/SCN set the colour inside the current space: the channel vector is the
    /// all-numeric operands; a trailing pattern name makes the colour unproven → treated as ink.
    /// </summary>
    private static double[]? ScColor(List<object?> a)
    {
        var channels = new double[a.Count];
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] is not double d)
            {
                return null;
            }

            channels[i] = d;
        }

        return channels;
    }

    /// <summary>
    /// Proven-white only: one channel ≥1 (gray), three channels all ≥1 (RGB), or four channels
    /// all ≤0 (CMYK — zero ink coverage). Anything else (unset, pattern, 2 or 5+ channels) is
    /// unproven and counts as ink. The channel vector only means gray/RGB/CMYK in a device
    /// colour space — in Indexed/Separation/ICCBased a single channel is a palette index or
    /// tint, so space must be DeviceGray/DeviceRGB/DeviceCMYK for the white rules to apply (F7).
    /// </summary>
    private static bool IsWhite(double[]? color, string? space)
    {
        if (color is null || space is not ("DeviceGray" or "DeviceRGB" or "DeviceCMYK"))
        {
            return false;
        }

        return color.Length switch
        {
            1 => color[0] >= 1.0,
            3 => color[0] >= 1.0 && color[1] >= 1.0 && color[2] >= 1.0,
            4 => color[0] <= 0.0 && color[1] <= 0.0 && color[2] <= 0.0 && color[3] <= 0.0,
            _ => false,
        };
    }

    private static void NoOperands(List<object?> a, string op)
    {
        if (a.Count != 0)
        {
            throw BadOperands(op, a);
        }
    }

    private static PdfInkException BadOperands(string op, List<object?> a) =>
        new($"bad operands for '{op}' ({a.Count} given)");

    private void AddPathPoint(double x, double y)
    {
        var (px, py) = PdfMatrix.Apply(_g.Ctm, x, y);
        if (!double.IsFinite(px) || !double.IsFinite(py))
        {
            throw new PdfInkException("coordinates are out of range");
        }

        if (!_hasPath)
        {
            _pathMinX = _pathMaxX = px;
            _pathMinY = _pathMaxY = py;
            _hasPath = true;
            return;
        }

        _pathMinX = Math.Min(_pathMinX, px);
        _pathMaxX = Math.Max(_pathMaxX, px);
        _pathMinY = Math.Min(_pathMinY, py);
        _pathMaxY = Math.Max(_pathMaxY, py);
    }

    private void AddSegment()
    {
        if (++_subpathSegments >= 2)
        {
            _pathHasJoin = true;
        }
    }

    private void ClosePath()
    {
        if (_subpathSegments >= 1)
        {
            _pathHasJoin = true;
        }

        _subpathSegments = 0;
    }

    /// <summary>
    /// TOOL-GAP #30b: a paint counts as ink only when at least one channel actually shows — a
    /// proven-white fill or stroke paints nothing. An unset/unproven colour still counts as ink
    /// (over-estimate, same as before this colour tracking existed).
    /// </summary>
    private void PaintPath(bool stroke, bool fill)
    {
        var strokePaints = stroke && !IsWhite(_g.StrokeColor, _g.StrokeSpace);
        var fillPaints = fill && !IsWhite(_g.FillColor, _g.FillSpace);
        if (_hasPath && (strokePaints || fillPaints))
        {
            double r = strokePaints ? StrokeRadius(_pathHasJoin) : 0;
            _ink.Add(_pathMinX - r, _pathMinY - r, _pathMaxX + r, _pathMaxY + r);
        }

        ClearPath();
    }

    private void ClearPath()
    {
        _hasPath = false;
        _subpathSegments = 0;
        _pathHasJoin = false;
    }

    /// <summary>
    /// How far a stroke can reach past its path, in default user space: half the line width,
    /// times √2 for square caps, times the miter limit when miter joins are present, times the CTM stretch.
    /// </summary>
    private double StrokeRadius(bool hasJoins)
    {
        double factor = _g.LineCap is 0 or 1 ? 1 : Math.Sqrt(2);
        if (hasJoins && _g.LineJoin is not (1 or 2))
        {
            factor = Math.Max(factor, Math.Max(1, _g.MiterLimit));
        }

        return _g.LineWidth / 2 * factor * PdfMatrix.MaxStretch(_g.Ctm);
    }

    private void AddBox(double[] m, double x0, double y0, double x1, double y1, double r)
    {
        var (ax, ay) = PdfMatrix.Apply(m, x0, y0);
        var (bx, by) = PdfMatrix.Apply(m, x1, y0);
        var (cx, cy) = PdfMatrix.Apply(m, x0, y1);
        var (dx, dy) = PdfMatrix.Apply(m, x1, y1);
        _ink.Add(
            Math.Min(Math.Min(ax, bx), Math.Min(cx, dx)) - r,
            Math.Min(Math.Min(ay, by), Math.Min(cy, dy)) - r,
            Math.Max(Math.Max(ax, bx), Math.Max(cx, dx)) + r,
            Math.Max(Math.Max(ay, by), Math.Max(cy, dy)) + r);
    }

    private void ApplyExtGState(List<object?> a)
    {
        if (a.Count != 1 || a[0] is not PdfName name || !_res.ExtGStates.TryGetValue(name.Value, out var gs))
        {
            return;
        }

        if (gs.FontProblem is not null)
        {
            throw new PdfInkException($"ExtGState /{name.Value}: {gs.FontProblem}");
        }

        if (gs.LineWidth is double lw)
        {
            _g.LineWidth = Math.Abs(lw);
        }

        if (gs.LineCap is int cap)
        {
            _g.LineCap = cap;
        }

        if (gs.LineJoin is int join)
        {
            _g.LineJoin = join;
        }

        if (gs.MiterLimit is double miter)
        {
            _g.MiterLimit = miter;
        }

        if (gs.FontSize is double size)
        {
            _g.FontSize = size;
            _g.HasFont = true;
        }
    }

    private void SetFont(string name, double size)
    {
        if (_res.BadFonts.TryGetValue(name, out var problem))
        {
            throw new PdfInkException($"font /{name}: {problem}");
        }

        _g.FontSize = size;
        _g.HasFont = true;
    }

    private void NextLine(double tx, double ty)
    {
        _tm = _tlm = PdfMatrix.Multiply([1, 0, 0, 1, tx, ty], _tlm);
        _penLo = _penHi = 0;
    }

    /// <summary>
    /// Boxes a text run: each glyph byte is up to 1 em wide (−0.25..1 em high), and each advance is
    /// anywhere between 0 and (1 em + Tc + Tw) scaled by Tz, so narrow glyphs and multi-byte codes stay inside.
    /// </summary>
    private void ShowText(List<object?> items)
    {
        if (!_g.HasFont)
        {
            throw new PdfInkException("text is shown before a font size is set");
        }

        double size = _g.FontSize, th = _g.HorizontalScale;
        double glyphLo = Math.Min(0, size * th), glyphHi = Math.Max(0, size * th);
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        foreach (var item in items)
        {
            if (item is double adjust)
            {
                double shift = -adjust / 1000 * size * th;
                _penLo += shift;
                _penHi += shift;
                continue;
            }

            if (item is not PdfString text)
            {
                throw new PdfInkException("bad TJ array");
            }

            foreach (byte code in text.Bytes)
            {
                minX = Math.Min(minX, _penLo + glyphLo);
                maxX = Math.Max(maxX, _penHi + glyphHi);
                double word = code == 32 ? _g.WordSpacing : 0;
                double c0 = _g.CharSpacing * th;
                double c1 = (size + _g.CharSpacing) * th;
                double c2 = (_g.CharSpacing + word) * th;
                double c3 = (size + _g.CharSpacing + word) * th;
                _penLo += Math.Min(0, Math.Min(Math.Min(c0, c1), Math.Min(c2, c3)));
                _penHi += Math.Max(0, Math.Max(Math.Max(c0, c1), Math.Max(c2, c3)));
            }
        }

        if (minX > maxX)
        {
            return;
        }

        double y0 = _g.Rise + Math.Min(-0.25 * size, size);
        double y1 = _g.Rise + Math.Max(-0.25 * size, size);
        // Render modes: 0,2,4,6 fill; 1,2,5,6 stroke; 3/7 paint nothing. Proven-white channels paint nothing either.
        var fillPaints = _g.RenderMode is not (1 or 3 or 5 or 7) && !IsWhite(_g.FillColor, _g.FillSpace);
        var strokePaints = _g.RenderMode is not (0 or 3 or 4 or 7) && !IsWhite(_g.StrokeColor, _g.StrokeSpace);
        if (!fillPaints && !strokePaints)
        {
            return;
        }

        AddBox(PdfMatrix.Multiply(_tm, _g.Ctm), minX, y0, maxX, y1, strokePaints ? StrokeRadius(hasJoins: true) : 0);
    }

    private void PaintXObject(string name)
    {
        if (_res.XObjects.TryGetValue(name, out var xobject))
        {
            if (xobject.IsImage)
            {
                AddBox(_g.Ctm, 0, 0, 1, 1, 0);
            }
            else
            {
                var m = xobject.Matrix is null ? _g.Ctm : PdfMatrix.Multiply(xobject.Matrix, _g.Ctm);
                var box = xobject.BBox!;
                AddBox(m, box[0], box[1], box[2], box[3], 0);
            }

            return;
        }

        if (_res.BadXObjects.TryGetValue(name, out var problem))
        {
            throw new PdfInkException($"XObject /{name}: {problem}");
        }

        throw new PdfInkException($"XObject /{name} is not in the page resources");
    }

    private void InlineImage()
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        while (true)
        {
            if (!_lex.SkipWhitespace())
            {
                throw new PdfInkException("inline image without ID");
            }

            var key = _lex.ReadObject(allowRefs: false);
            if (key is PdfKeyword { Value: "ID" })
            {
                break;
            }

            if (key is not PdfName name)
            {
                throw new PdfInkException("bad inline image dictionary");
            }

            var value = _lex.ReadObject(allowRefs: false);
            if (value is PdfKeyword)
            {
                throw new PdfInkException("bad inline image dictionary");
            }

            dict[name.Value] = value;
        }

        int dataStart = _lex.Pos;
        if (dataStart < _data.Length && PdfLexer.IsWhite(_data[dataStart]))
        {
            dataStart++;
        }

        _lex.Pos = FindInlineImageEnd(dict, dataStart);
        AddBox(_g.Ctm, 0, 0, 1, 1, 0);
    }

    /// <summary>Position just after the EI that ends the inline image data.</summary>
    private int FindInlineImageEnd(Dictionary<string, object?> dict, int dataStart)
    {
        long? length = InlineImageLength(dict);
        if (length is long n && dataStart + n <= _data.Length)
        {
            int p = (int)(dataStart + n);
            while (p < _data.Length && PdfLexer.IsWhite(_data[p]))
            {
                p++;
            }

            if (IsEndImage(p, allowDelimiter: true))
            {
                return p + 2;
            }
        }

        for (int i = dataStart; i + 1 < _data.Length; i++)
        {
            if (i > 0 && PdfLexer.IsWhite(_data[i - 1]) && IsEndImage(i, allowDelimiter: false) && LooksLikeContent(i + 2))
            {
                return i + 2;
            }
        }

        throw new PdfInkException("inline image without EI");
    }

    private bool IsEndImage(int p, bool allowDelimiter) =>
        p + 1 < _data.Length
        && _data[p] == (byte)'E'
        && _data[p + 1] == (byte)'I'
        && (p + 2 == _data.Length
            || PdfLexer.IsWhite(_data[p + 2])
            || (allowDelimiter && PdfLexer.IsDelimiter(_data[p + 2])));

    /// <summary>True when the bytes after a candidate EI parse as operands and a known operator.</summary>
    private bool LooksLikeContent(int p)
    {
        try
        {
            var lex = new PdfLexer(_data, p, _data.Length);
            for (int i = 0; i < 16; i++)
            {
                if (!lex.SkipWhitespace())
                {
                    return true;
                }

                if (lex.ReadObject(allowRefs: false) is PdfKeyword keyword)
                {
                    return PdfLexer.KnownOperators.Contains(keyword.Value);
                }
            }

            return true;
        }
        catch (PdfInkException)
        {
            return false;
        }
    }

    /// <summary>Byte length of unfiltered inline image data, or null when it cannot be computed.</summary>
    private static long? InlineImageLength(Dictionary<string, object?> dict)
    {
        object? Pick(string shortKey, string longKey) =>
            dict.TryGetValue(shortKey, out var v) ? v : dict.TryGetValue(longKey, out v) ? v : null;

        var filter = Pick("F", "Filter");
        if (filter is PdfName || filter is List<object?> { Count: > 0 })
        {
            return null;
        }

        if (Pick("W", "Width") is not double width || Pick("H", "Height") is not double height)
        {
            return null;
        }

        double bpc;
        int components;
        if (Pick("IM", "ImageMask") is true)
        {
            bpc = 1;
            components = 1;
        }
        else
        {
            if (Pick("BPC", "BitsPerComponent") is not double bits)
            {
                return null;
            }

            bpc = bits;
            var cs = Pick("CS", "ColorSpace");
            var csName = cs is List<object?> { Count: > 0 } list ? PdfCos.NameOf(list[0]) : PdfCos.NameOf(cs);
            components = csName switch
            {
                "G" or "DeviceGray" or "CalGray" or "I" or "Indexed" => 1,
                "RGB" or "DeviceRGB" or "CalRGB" => 3,
                "CMYK" or "DeviceCMYK" => 4,
                _ => 0,
            };
        }

        bool validBpc = bpc == 1 || bpc == 2 || bpc == 4 || bpc == 8 || bpc == 16;
        if (components == 0 || !validBpc || width < 1 || height < 1 || width > 1e6 || height > 1e6
            || width != Math.Floor(width) || height != Math.Floor(height))
        {
            return null;
        }

        long rowBytes = ((long)width * components * (long)bpc + 7) / 8;
        return rowBytes * (long)height;
    }

    private sealed class GState
    {
        public double[] Ctm { get; set; } = PdfMatrix.Identity;

        public double LineWidth { get; set; } = 1;

        public int LineCap { get; set; }

        public int LineJoin { get; set; }

        public double MiterLimit { get; set; } = 10;

        public bool HasFont { get; set; }

        public double FontSize { get; set; }

        public double CharSpacing { get; set; }

        public double WordSpacing { get; set; }

        public double HorizontalScale { get; set; } = 1;

        public double Leading { get; set; }

        public double Rise { get; set; }

        public int RenderMode { get; set; }

        /// <summary>Stroking colour channels (last G/RG/K/SC/SCN); null = default black or unproven (pattern/space).</summary>
        public double[]? StrokeColor { get; set; }

        /// <summary>Non-stroking colour channels (last g/rg/k/sc/scn); null = default black or unproven.</summary>
        public double[]? FillColor { get; set; }

        /// <summary>Stroking colour space (last CS); null = non-device/unproven space. PDF default is DeviceGray.</summary>
        public string? StrokeSpace { get; set; } = "DeviceGray";

        /// <summary>Non-stroking colour space (last cs); null = non-device/unproven space. PDF default is DeviceGray.</summary>
        public string? FillSpace { get; set; } = "DeviceGray";

        public GState Clone() => (GState)MemberwiseClone();
    }
}
