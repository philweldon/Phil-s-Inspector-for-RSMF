using System.IO.Compression;
using System.Text;
using Rsmf.Core;
using Xunit;

namespace Rsmf.Tests;

public class PdfTests
{
    private static byte[] Picture(int w, int h, byte r, byte g, byte b)
    {
        var px = new byte[w * h * 3];
        for (var i = 0; i < px.Length; i += 3) { px[i] = r; px[i + 1] = g; px[i + 2] = b; }
        return px;
    }

    private static byte[] Write(int pages)
    {
        using var ms = new MemoryStream();
        using (var pdf = new PdfImageWriter(ms))
        {
            for (var i = 0; i < pages; i++) pdf.AddPage(612, 792, 20, 30, Picture(20, 30, (byte)(10 * i), 128, 255));
            pdf.Finish();
        }
        return ms.ToArray();
    }

    [Fact]
    public void Pdf_has_header_page_count_and_a_correct_cross_reference_table()
    {
        var bytes = Write(3);
        var text = Encoding.Latin1.GetString(bytes);
        if (Environment.GetEnvironmentVariable("RSMF_PDF_OUT") is { Length: > 0 } keep) File.WriteAllBytes(keep, bytes);   // for checking with other PDF tools

        Assert.StartsWith("%PDF-1.7\n", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains("/Type /Pages /Count 3", text);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(text, "/Type /Page /Parent").Count);

        // Every cross-reference entry must point at the start of the object it names.
        var startXref = int.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        Assert.StartsWith("xref\n0 ", text[startXref..]);
        var lines = text[startXref..].Split('\n');
        var count = int.Parse(lines[1].Split(' ')[1]);
        Assert.Equal(3 + 3 * 3, count);                      // unused, catalog, pages + three objects per page
        for (var obj = 1; obj < count; obj++)
        {
            var offset = int.Parse(lines[2 + obj][..10]);
            Assert.StartsWith($"{obj} 0 obj\n", text[offset..]);
        }
    }

    private static (string Dictionary, byte[] Data) FirstImage(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var dict = text.IndexOf("/Subtype /Image", StringComparison.Ordinal);
        var end = text.IndexOf("stream\n", dict, StringComparison.Ordinal);
        var dictionary = text[dict..end];
        var length = int.Parse(System.Text.RegularExpressions.Regex.Match(dictionary, @"/Length (\d+)").Groups[1].Value);
        using var z = new ZLibStream(new MemoryStream(pdf, end + 7, length), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        return (dictionary, raw.ToArray());
    }

    [Fact]
    public void Pdf_page_with_few_colours_is_stored_with_a_colour_table()
    {
        var (dictionary, data) = FirstImage(Write(1));
        Assert.Contains("/Width 20 /Height 30 /ColorSpace [/Indexed /DeviceRGB 0 <0080FF>] /BitsPerComponent 8", dictionary);
        Assert.Equal(new byte[20 * 30], data);               // every pixel is colour 0 of the table
        Assert.Contains("/MediaBox [0 0 612 792]", Encoding.Latin1.GetString(Write(1)));
    }

    [Fact]
    public void Pdf_page_with_many_colours_keeps_every_pixel_exactly()
    {
        var px = new byte[64 * 64 * 3];
        new Random(7).NextBytes(px);
        using var ms = new MemoryStream();
        using (var pdf = new PdfImageWriter(ms))
        {
            pdf.AddPage(612, 792, 64, 64, px);
            pdf.Finish();
        }
        var (dictionary, data) = FirstImage(ms.ToArray());
        Assert.Contains("/ColorSpace /DeviceRGB /BitsPerComponent 8", dictionary);
        Assert.Equal(px, data);
    }

    [Fact]
    public void Colour_table_changes_no_channel_by_more_than_seven_levels()
    {
        var px = new byte[300 * 3];
        for (var i = 0; i < 300; i++) { px[i * 3] = (byte)(i % 16); px[i * 3 + 1] = (byte)(200 + i % 5); px[i * 3 + 2] = (byte)(i % 2 * 255); }
        var index = PdfImageWriter.TryIndex(px, out var palette);
        Assert.NotNull(index);
        Assert.True(palette.Length / 3 <= 256);
        for (var i = 0; i < 300; i++)
            for (var c = 0; c < 3; c++)
                Assert.InRange(Math.Abs(palette[index![i] * 3 + c] - px[i * 3 + c]), 0, 7);
    }

    /// <summary>A page of one background colour plus <paramref name="groups"/> other colour groups of <paramref name="each"/> pixels.</summary>
    private static byte[] ManyGroups(int groups, int each, int background)
    {
        var px = new byte[(groups * each + background) * 3];
        var at = 0;
        for (var g = 0; g < groups; g++)
            for (var k = 0; k < each; k++) { px[at++] = (byte)((g % 32) * 8); px[at++] = (byte)((g / 32) * 8); px[at++] = 0; }
        while (at < px.Length) { px[at++] = 255; px[at++] = 255; px[at++] = 255; }
        return px;
    }

    [Fact]
    public void Colour_table_absorbs_a_few_stray_shades_but_not_a_colour_rich_page()
    {
        // 400 groups, the stray ones covering well under 0.5% of the page: stored with a table.
        var few = ManyGroups(400, 1, 200_000);
        var index = PdfImageWriter.TryIndex(few, out var palette);
        Assert.NotNull(index);
        Assert.Equal(256 * 3, palette.Length);
        Assert.Equal(new byte[] { 255, 255, 255 }, palette[..3]);            // the most common colour comes first, unchanged

        // The same 400 groups covering most of the page: left in full colour.
        Assert.Null(PdfImageWriter.TryIndex(ManyGroups(400, 500, 1_000), out _));

        // More than 1,024 groups: always full colour.
        Assert.Null(PdfImageWriter.TryIndex(ManyGroups(1_025, 1, 1_000_000), out _));
    }

    [Fact]
    public void Pdf_contains_only_pictures_no_active_content_and_no_document_information()
    {
        var text = Encoding.Latin1.GetString(Write(2));
        foreach (var forbidden in new[] { "/JavaScript", "/JS", "/OpenAction", "/AA", "/Launch", "/URI", "/EmbeddedFile", "/Font", "/Annots",
                     "/Info", "/Title", "/Author", "/Producer", "/Creator", "/CreationDate", "/Metadata", "RSMF", "Inspector" })
            Assert.DoesNotContain(forbidden, text);
    }

    [Fact]
    public void Pdf_writer_rejects_wrong_sized_pictures_and_empty_documents()
    {
        using var ms = new MemoryStream();
        using var pdf = new PdfImageWriter(ms);
        Assert.Throws<ArgumentException>(() => pdf.AddPage(612, 792, 20, 30, new byte[10]));
        Assert.Throws<InvalidOperationException>(() => pdf.Finish());
    }

    [Fact]
    public void Discover_lists_rsmf_files_in_stable_order_and_targets_mirror_the_folders()
    {
        var root = Directory.CreateTempSubdirectory("rsmf-pdf-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sub", "deep"));
            foreach (var rel in new[] { "b.rsmf", "a.RSMF", "notes.txt", Path.Combine("sub", "c.rsmf"), Path.Combine("sub", "deep", "d.rsmf") })
                File.WriteAllText(Path.Combine(root, rel), "x");

            var notes = new List<string>();
            var found = PdfExport.Discover(root, new InspectionOptions(), notes, CancellationToken.None);
            Assert.Equal(new[] { "a.RSMF", "b.rsmf", Path.Combine("sub", "c.rsmf"), Path.Combine("sub", "deep", "d.rsmf") }, found.Select(e => e.RelativePath));
            Assert.Empty(notes);

            var run = Path.Combine(root, "out");
            PdfExport.AssignTargets(run, found);
            Assert.Equal(Path.Combine(run, "a.pdf"), found[0].PdfPath);
            Assert.Equal(Path.Combine(run, "sub", "deep", "d.pdf"), found[3].PdfPath);

            var single = PdfExport.Discover(Path.Combine(root, "b.rsmf"), new InspectionOptions(), notes, CancellationToken.None);
            Assert.Equal("b.rsmf", Assert.Single(single).RelativePath);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Targets_that_would_collide_get_a_number()
    {
        var entries = new[] { "A.rsmf", "a.rsmf", "a.RSMF" }.Select(n => new PdfEntry { Source = n, RelativePath = n }).ToList();
        PdfExport.AssignTargets("out", entries);
        Assert.Equal(new[] { "A.pdf", "a-2.pdf", "a-3.pdf" }, entries.Select(e => Path.GetFileName(e.PdfPath)));
    }

    [Fact]
    public void Discover_notes_a_folder_that_cannot_be_listed_and_carries_on()
    {
        var root = Directory.CreateTempSubdirectory("rsmf-pdf-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "locked"));
            File.WriteAllText(Path.Combine(root, "ok.rsmf"), "x");
            var options = new InspectionOptions
            {
                DirectoryLister = d => d.EndsWith("locked", StringComparison.Ordinal) ? throw new UnauthorizedAccessException() : InspectionOptions.DefaultList(d),
            };
            var notes = new List<string>();
            var found = PdfExport.Discover(root, options, notes, CancellationToken.None);
            Assert.Equal("ok.rsmf", Assert.Single(found).RelativePath);
            Assert.Contains("locked", Assert.Single(notes));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Conversion_log_and_summary_are_written_without_message_content()
    {
        var root = Directory.CreateTempSubdirectory("rsmf-pdf-").FullName;
        try
        {
            var folder = PdfExport.NewRunFolder(root, new DateTime(2026, 11, 19, 7, 0, 0));
            Assert.StartsWith("pdf-20261119-070000-", Path.GetFileName(folder));
            var run = new PdfRun { RunFolder = folder, Input = root, ZoneId = "America/New_York", Dpi = 150, StartedUtc = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc) };
            run.Entries.Add(new PdfEntry { Source = "x", RelativePath = "good, one.rsmf", PdfPath = Path.Combine(folder, "good, one.pdf"), Status = PdfStatus.Converted, Pages = 3, Conversations = 1, Events = 40, PdfBytes = 1234 });
            run.Entries.Add(new PdfEntry { Source = "y", RelativePath = "bad.rsmf", Status = PdfStatus.NotConverted, Note = "rsmf.zip could not be opened" });
            PdfExport.WriteLog(run);

            var log = File.ReadAllLines(Path.Combine(folder, PdfExport.LogFileName));
            Assert.Equal("source_file,status,pdf_file,pages,conversations,events,pdf_bytes,note", log[0].TrimStart('﻿'));
            Assert.Equal("\"good, one.rsmf\",converted,\"good, one.pdf\",3,1,40,1234,", log[1]);
            Assert.Equal("bad.rsmf,not converted,,0,0,0,0,rsmf.zip could not be opened", log[2]);

            var summary = File.ReadAllText(Path.Combine(folder, PdfExport.SummaryFileName));
            Assert.Contains("Converted:            1 file(s), 3 page(s)", summary);
            Assert.Contains("Not converted:        1", summary);
            Assert.Contains("completed with exceptions", summary);
            Assert.Contains("not certified", summary);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(799)]
    [InlineData(800)]
    [InlineData(801)]
    [InlineData(25_000)]
    public void SplitForPages_keeps_every_piece_small_and_loses_nothing_but_the_cut_characters(int length)
    {
        var sb = new StringBuilder();
        var rng = new Random(length);
        while (sb.Length < length) sb.Append(rng.Next(12) switch { 0 => "\n", 1 => "😀", 2 => "longwordwithoutanyspacesatall", _ => "word " });
        var text = sb.ToString()[..length];
        if (text.Length > 0 && char.IsHighSurrogate(text[^1])) text = text[..^1];

        var pieces = PdfExport.SplitForPages(text);
        Assert.All(pieces, p =>
        {
            Assert.True(p.Length <= 800);
            Assert.True(p.Count(c => c == '\n') <= 20);
            Assert.False(p.Length > 0 && char.IsLowSurrogate(p[0]));
            Assert.False(p.Length > 0 && char.IsHighSurrogate(p[^1]));
        });
        string Squash(string s) => s.Replace("\n", "").Replace(" ", "").Replace("\r", "");
        Assert.Equal(Squash(text), Squash(string.Concat(pieces)));
    }

    [Fact]
    public void SplitForPages_breaks_text_with_many_line_breaks_by_line_count()
    {
        var text = string.Join("\n", Enumerable.Range(0, 100).Select(i => "line " + i));
        var pieces = PdfExport.SplitForPages(text);
        Assert.True(pieces.Count >= 4);
        Assert.Equal(text.Split('\n'), pieces.SelectMany(p => p.Split('\n')));
    }

    [Fact]
    public void Pdf_stress_file_loads_with_two_conversations_and_real_pictures()
    {
        var dir = Directory.CreateTempSubdirectory("rsmf-pdf-").FullName;
        try
        {
            var path = Path.Combine(dir, PdfStressFile.FileName);
            File.WriteAllBytes(path, PdfStressFile.Build());
            if (Environment.GetEnvironmentVariable("RSMF_PDF_STRESS_OUT") is { Length: > 0 } keep) File.Copy(path, keep, overwrite: true);

            using var doc = ViewerDocument.Load(path, new InspectionOptions { TimeZoneId = "America/New_York" }, CancellationToken.None);
            Assert.Empty(doc.Unavailable);
            Assert.Equal(2, doc.Conversations.Count);
            Assert.Equal(13, doc.Conversations[0].Messages.Count);
            var withPictures = doc.Conversations[0].Messages.Single(m => m.Attachments.Count == 3);
            Assert.Equal(new[] { true, true, false }, withPictures.Attachments.Select(a => a.IsInlineImage));
            Assert.NotNull(doc.ReadInlineImage(withPictures.Attachments[0]));
            Assert.Null(doc.ReadInlineImage(withPictures.Attachments[2]));       // not a picture: listed by name only
            Assert.Contains(doc.Conversations[0].Messages, m => m.Body.Length > 20_000);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void PieceLimit_stops_at_the_character_limit_or_at_the_line_break_that_is_one_too_many()
    {
        Assert.Equal(5, PdfExport.PieceLimit("hello", 0));
        Assert.Equal(800, PdfExport.PieceLimit(new string('a', 2000), 0));
        Assert.Equal(1300, PdfExport.PieceLimit(new string('a', 2000), 500));
        var lines = string.Join("\n", Enumerable.Repeat("ab", 50));       // a line break every third character
        var end = PdfExport.PieceLimit(lines, 0);
        Assert.Equal('\n', lines[end]);
        Assert.Equal(20, lines[..end].Count(c => c == '\n'));
    }
}
