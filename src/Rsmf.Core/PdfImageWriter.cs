using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Rsmf.Core;

/// <summary>
/// Writes a PDF whose pages are full-page pictures (Flate-compressed; full colour, or a colour table when the page has few colours). Pages are written as
/// they are added, so memory use does not grow with the page count. No fonts, scripts, links,
/// attachments or document information (title, author, producer, dates) are ever written: each page
/// is one image and nothing else.
/// </summary>
public sealed class PdfImageWriter : IDisposable
{
    private readonly Stream _out;
    private readonly List<long> _offsets = new() { 0, 0, 0 };   // object 0 unused; 1 catalog, 2 pages
    private readonly List<int> _pageObjects = new();
    private long _position;
    private bool _finished;

    public PdfImageWriter(Stream output)
    {
        _out = output;
        Raw("%PDF-1.7\n");
        WriteBytes(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });   // marks the file as binary
    }

    public int PageCount => _pageObjects.Count;

    /// <summary>Adds one page showing the picture edge to edge. <paramref name="rgb"/> holds width*height*3 bytes, top row first.</summary>
    public void AddPage(double widthPoints, double heightPoints, int pixelWidth, int pixelHeight, ReadOnlySpan<byte> rgb)
    {
        if (_finished) throw new InvalidOperationException("The PDF is already finished.");
        if (pixelWidth <= 0 || pixelHeight <= 0 || widthPoints <= 0 || heightPoints <= 0) throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        if (rgb.Length != (long)pixelWidth * pixelHeight * 3) throw new ArgumentException("The pixel data does not match the picture size.", nameof(rgb));

        // Pages of text and flat colour use few distinct colours: those are stored with a colour table
        // (one byte per pixel), which is several times smaller. Pages with photographs stay full colour.
        var indexed = TryIndex(rgb, out var palette);
        byte[] packed;
        using (var buffer = new MemoryStream())
        {
            using (var z = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            {
                if (indexed is not null) z.Write(indexed); else z.Write(rgb);
            }
            packed = buffer.ToArray();
        }
        var colourSpace = indexed is null
            ? "/DeviceRGB"
            : $"[/Indexed /DeviceRGB {N(palette.Length / 3 - 1)} <{Convert.ToHexString(palette)}>]";

        var image = Begin();
        Raw($"<< /Type /XObject /Subtype /Image /Width {N(pixelWidth)} /Height {N(pixelHeight)} /ColorSpace {colourSpace} /BitsPerComponent 8 /Filter /FlateDecode /Length {N(packed.Length)} >>\nstream\n");
        WriteBytes(packed);
        Raw("\nendstream\nendobj\n");

        var content = Encoding.ASCII.GetBytes($"q {D(widthPoints)} 0 0 {D(heightPoints)} 0 0 cm /Im0 Do Q\n");
        var contents = Begin();
        Raw($"<< /Length {N(content.Length)} >>\nstream\n");
        WriteBytes(content);
        Raw("endstream\nendobj\n");

        var page = Begin();
        Raw($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {D(widthPoints)} {D(heightPoints)}] /Resources << /XObject << /Im0 {N(image)} 0 R >> >> /Contents {N(contents)} 0 R >>\nendobj\n");
        _pageObjects.Add(page);
    }

    /// <summary>Writes the page list, document information and cross-reference table. Call once, after the last page.</summary>
    public void Finish()
    {
        if (_finished) return;
        if (_pageObjects.Count == 0) throw new InvalidOperationException("A PDF needs at least one page.");
        _finished = true;

        _offsets[1] = _position;
        Raw("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        _offsets[2] = _position;
        Raw("2 0 obj\n<< /Type /Pages /Count " + N(_pageObjects.Count) + " /Kids [");
        for (var i = 0; i < _pageObjects.Count; i++) Raw((i % 12 == 0 ? "\n" : " ") + N(_pageObjects[i]) + " 0 R");
        Raw("\n] >>\nendobj\n");

        var xref = _position;
        Raw("xref\n0 " + N(_offsets.Count) + "\n0000000000 65535 f \n");
        for (var i = 1; i < _offsets.Count; i++) Raw(_offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Raw("trailer\n<< /Size " + N(_offsets.Count) + " /Root 1 0 R >>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
        _out.Flush();
    }

    /// <summary>
    /// Maps the picture onto a table of at most 256 colours, or returns null when that would change it
    /// visibly. Colours are grouped by their top five bits per channel and each group is shown as its
    /// average (a change of a few levels out of 255 at most). A page with up to 256 groups always
    /// qualifies. A page with up to 1,024 groups qualifies when the groups beyond the 256 most common
    /// cover no more than 0.5% of the pixels (stray anti-aliasing shades); those take the nearest table
    /// colour. Anything richer, such as a photograph, is left in full colour.
    /// </summary>
    internal static byte[]? TryIndex(ReadOnlySpan<byte> rgb, out byte[] palette)
    {
        palette = Array.Empty<byte>();
        const int Groups = 32768, MaxGroups = 1024, Table = 256;
        var counts = new int[Groups];
        var used = 0;
        var pixels = rgb.Length / 3;
        for (var i = 0; i < pixels * 3; i += 3)
        {
            var key = ((rgb[i] >> 3) << 10) | ((rgb[i + 1] >> 3) << 5) | (rgb[i + 2] >> 3);
            if (counts[key]++ == 0 && ++used > MaxGroups) return null;
        }

        var keys = new int[used];
        for (int key = 0, n = 0; key < Groups; key++) if (counts[key] > 0) keys[n++] = key;
        Array.Sort(keys, (x, y) => counts[y] != counts[x] ? counts[y].CompareTo(counts[x]) : x.CompareTo(y));   // most common first

        var size = Math.Min(used, Table);
        long stray = 0;
        for (var n = size; n < used; n++) stray += counts[keys[n]];
        if (stray * 200 > pixels) return null;

        var slot = new short[Groups];
        for (var n = 0; n < size; n++) slot[keys[n]] = (short)n;
        for (var n = size; n < used; n++)
        {
            // Nearest table group, compared at the centre of each five-bit group.
            int r = keys[n] >> 10, g = (keys[n] >> 5) & 31, b = keys[n] & 31, best = 0, bestDistance = int.MaxValue;
            for (var t = 0; t < size; t++)
            {
                int dr = (keys[t] >> 10) - r, dg = ((keys[t] >> 5) & 31) - g, db = (keys[t] & 31) - b;
                var d = dr * dr + dg * dg + db * db;
                if (d < bestDistance) { bestDistance = d; best = t; }
            }
            slot[keys[n]] = (short)best;
        }

        var sums = new long[size * 3];
        var members = new int[size];
        var index = new byte[pixels];
        for (int i = 0, p = 0; p < pixels; i += 3, p++)
        {
            var key = ((rgb[i] >> 3) << 10) | ((rgb[i + 1] >> 3) << 5) | (rgb[i + 2] >> 3);
            int n = slot[key];
            index[p] = (byte)n;
            if (n >= size || keys[n] != key) continue;       // stray pixels do not shift the table colour
            members[n]++;
            sums[n * 3] += rgb[i];
            sums[n * 3 + 1] += rgb[i + 1];
            sums[n * 3 + 2] += rgb[i + 2];
        }
        palette = new byte[size * 3];
        for (var n = 0; n < size; n++)
            for (var c = 0; c < 3; c++)
                palette[n * 3 + c] = (byte)((sums[n * 3 + c] + members[n] / 2) / members[n]);
        return index;
    }

    public void Dispose() { }

    private int Begin()
    {
        var number = _offsets.Count;
        _offsets.Add(_position);
        Raw(N(number) + " 0 obj\n");
        return number;
    }

    private void Raw(string ascii) => WriteBytes(Encoding.ASCII.GetBytes(ascii));

    private void WriteBytes(byte[] bytes)
    {
        _out.Write(bytes, 0, bytes.Length);
        _position += bytes.Length;
    }

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
    private static string D(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
}
