using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rsmf.Core;

namespace Rsmf.App;

/// <summary>
/// Lays one RSMF file out as US Letter pages of chat bubbles and writes each page to a PDF as a picture.
/// Owner's rule (2026-11-19): nothing that is not in the RSMF file is added to a page. So there is no
/// page header or footer, no page number, no file name, no tool name, no notes, no day separators and
/// no counts. Timestamps are shown exactly as written in the file. The only words that do not come from
/// the file's values are RSMF's own field names (parent, deleted, reactions, edits) and event types
/// (join, leave, ...), used as labels. Text is drawn as plain text only; inline pictures are decoded
/// the same way the viewer does; no attachment is ever opened. One instance per run, on one STA thread.
/// </summary>
internal sealed class PdfRenderer
{
    public const int Dpi = 150;

    private const double PageW = 816, PageH = 1056;                 // US Letter in 1/96 inch
    private const double MarginX = 48, TopY = 48, BottomMargin = 48;
    private const double ContentW = PageW - 2 * MarginX;
    private const double ContentBottom = PageH - BottomMargin;
    private const double ContentH = ContentBottom - TopY;
    private const double BubbleMax = 560, Pad = 12, RowGap = 3;
    private const double ImageMaxW = 300, ImageMaxH = 220;

    private static readonly int PxW = (int)Math.Round(PageW * Dpi / 96.0);
    private static readonly int PxH = (int)Math.Round(PageH * Dpi / 96.0);

    private static readonly FontFamily Family = new("Segoe UI");
    private static readonly CultureInfo TextCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly Brush Ink = Solid(0x1F, 0x29, 0x33);
    private static readonly Brush Grey = Solid(0x5F, 0x6B, 0x7A);
    private static readonly Brush Rule = Solid(0xD5, 0xDA, 0xE0);
    private static readonly Pen RulePen = MakePen(Rule, 1);

    private readonly byte[] _bgra = new byte[PxW * PxH * 4];
    private readonly byte[] _rgb = new byte[PxW * PxH * 3];
    private RenderTargetBitmap? _target;

    private static Brush Solid(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Pen MakePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    // ------------------------------------------------------------------ text

    /// <summary>Plain text for drawing: control characters other than line breaks and tabs become spaces.</summary>
    private static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var chars = text.ToCharArray();
        var changed = false;
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if ((c < 0x20 && c != '\n' && c != '\t') || c == 0x7F) { chars[i] = ' '; changed = true; }
        }
        return changed ? new string(chars) : text;
    }

    private static FormattedText Ft(string text, double size, Brush brush, double maxWidth, bool bold = false, bool italic = false, bool oneLine = false)
    {
        var typeface = new Typeface(Family, italic ? FontStyles.Italic : FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
        var ft = new FormattedText(text, TextCulture, FlowDirection.LeftToRight, typeface, size, brush, 1.0)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            Trimming = oneLine ? TextTrimming.CharacterEllipsis : TextTrimming.None,
        };
        if (oneLine) ft.MaxLineCount = 1;
        EmojiPainter.Prepare(ft, text);
        return ft;
    }

    // ------------------------------------------------------------------ rows inside a bubble

    private abstract class PdfRow
    {
        public double Height { get; protected set; }
        public double Width { get; protected set; }
        /// <summary>Space above this row when it is not the first in its bubble. Zero for the continuation of one text.</summary>
        public double GapBefore { get; init; } = RowGap;
        /// <summary>Sender, timestamp and marks that come before the message text; they stay with the text that follows.</summary>
        public bool IsHead { get; init; }
        public abstract void Draw(DrawingContext dc, double x, double y);
    }

    private sealed class TextRow : PdfRow
    {
        private readonly string _text;
        private readonly double _size, _max;
        private readonly Brush _brush;
        private readonly bool _bold, _italic;

        public TextRow(string text, double size, Brush brush, double maxWidth, bool bold = false, bool italic = false)
        {
            _text = text; _size = size; _brush = brush; _max = maxWidth; _bold = bold; _italic = italic;
            var ft = Make();
            Height = Math.Ceiling(ft.Height);
            Width = Math.Min(maxWidth, Math.Ceiling(ft.WidthIncludingTrailingWhitespace));
        }

        private FormattedText Make() => Ft(_text, _size, _brush, _max, _bold, _italic);
        public override void Draw(DrawingContext dc, double x, double y) => EmojiPainter.Draw(dc, Make(), new Point(x, y));
    }

    /// <summary>Sender name in the sender's colour, then the timestamp in grey, on one line.</summary>
    private sealed class NameRow : PdfRow
    {
        private readonly string _name, _time;
        private readonly Brush _brush, _timeBrush;
        private readonly double _nameMax, _timeWidth;

        public NameRow(string name, string time, Brush brush, Brush timeBrush, double maxWidth)
        {
            _name = name; _time = time; _brush = brush; _timeBrush = timeBrush;
            var t = Ft(time, 10.5, timeBrush, maxWidth, oneLine: true);
            _timeWidth = Math.Ceiling(t.WidthIncludingTrailingWhitespace);
            _nameMax = Math.Max(60, maxWidth - _timeWidth - 12);
            var n = Ft(name, 12.5, brush, _nameMax, bold: true, oneLine: true);
            Height = Math.Ceiling(Math.Max(n.Height, t.Height));
            Width = Math.Min(maxWidth, Math.Ceiling(n.WidthIncludingTrailingWhitespace) + 12 + _timeWidth);
        }

        public override void Draw(DrawingContext dc, double x, double y)
        {
            var n = Ft(_name, 12.5, _brush, _nameMax, bold: true, oneLine: true);
            EmojiPainter.Draw(dc, n, new Point(x, y));
            EmojiPainter.Draw(dc, Ft(_time, 10.5, _timeBrush, _timeWidth + 2, oneLine: true), new Point(x + Math.Ceiling(n.WidthIncludingTrailingWhitespace) + 12, y + 2));
        }
    }

    /// <summary>An inline picture. The bytes are read again when the page is drawn, so they are not held in memory.</summary>
    private sealed class ImageRow : PdfRow
    {
        private readonly ViewerDocument _doc;
        private readonly AttachmentView _attachment;

        private ImageRow(ViewerDocument doc, AttachmentView attachment, double w, double h)
        {
            _doc = doc; _attachment = attachment; Width = w; Height = h;
        }

        public static ImageRow? TryCreate(ViewerDocument doc, AttachmentView attachment)
        {
            try
            {
                var bytes = doc.ReadInlineImage(attachment);
                if (bytes is null) return null;
                using var probe = new MemoryStream(bytes, writable: false);
                var frame = BitmapFrame.Create(probe, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                double pw = frame.PixelWidth, ph = frame.PixelHeight;
                if (pw < 1 || ph < 1) return null;
                var scale = Math.Min(1.0, Math.Min(ImageMaxW / pw, ImageMaxH / ph));
                return new ImageRow(doc, attachment, Math.Max(1, Math.Round(pw * scale)), Math.Max(1, Math.Round(ph * scale)));
            }
            catch (Exception)
            {
                return null;   // not a picture WPF can decode: the caller lists it by name instead
            }
        }

        public override void Draw(DrawingContext dc, double x, double y)
        {
            var box = new Rect(x, y, Width, Height);
            try
            {
                var bytes = _doc.ReadInlineImage(_attachment);
                if (bytes is not null)
                {
                    dc.DrawImage(ImageLoader.Decode(bytes, Math.Max(1, (int)Math.Ceiling(Width * Dpi / 96.0))), box);
                    dc.DrawRectangle(null, RulePen, box);
                    return;
                }
            }
            catch (Exception)
            {
                // fall through to the placeholder
            }
            dc.DrawRectangle(Brushes.White, RulePen, box);
        }
    }

    // ------------------------------------------------------------------ blocks on a page

    private abstract class PdfBlock
    {
        public double Height { get; protected set; }
        public abstract void Draw(DrawingContext dc, double y);
    }

    /// <summary>The conversation's display name, then its platform and type, as given in the file.</summary>
    private sealed class HeadingBlock : PdfBlock
    {
        private readonly string _title, _detail;

        public HeadingBlock(string title, string detail)
        {
            _title = title; _detail = detail;
            Height = (title.Length > 0 ? Math.Ceiling(Ft(title, 18, Ink, ContentW, bold: true).Height) + 4 : 0)
                   + (detail.Length > 0 ? Math.Ceiling(Ft(detail, 11.5, Grey, ContentW).Height) + 2 : 0)
                   + 10;
        }

        public override void Draw(DrawingContext dc, double y)
        {
            if (_title.Length > 0)
            {
                var t = Ft(_title, 18, Ink, ContentW, bold: true);
                EmojiPainter.Draw(dc, t, new Point(MarginX, y));
                y += Math.Ceiling(t.Height) + 4;
            }
            if (_detail.Length > 0)
            {
                var d = Ft(_detail, 11.5, Grey, ContentW);
                EmojiPainter.Draw(dc, d, new Point(MarginX, y));
                y += Math.Ceiling(d.Height) + 2;
            }
            y += 6;
            dc.DrawLine(RulePen, new Point(MarginX, y), new Point(PageW - MarginX, y));
        }
    }

    /// <summary>Centred small text for an event that is not a message (join, leave, disclaimer, ...).</summary>
    private sealed class CentredBlock : PdfBlock
    {
        private readonly string _text;

        public CentredBlock(string text)
        {
            _text = text;
            Height = Math.Ceiling(Make().Height) + 4;
        }

        private FormattedText Make()
        {
            var ft = Ft(_text, 11, Grey, 560, italic: true);
            ft.TextAlignment = TextAlignment.Center;
            return ft;
        }

        public override void Draw(DrawingContext dc, double y) =>
            EmojiPainter.Draw(dc, Make(), new Point(MarginX + (ContentW - 560) / 2, y + 2));
    }

    /// <summary>A message bubble, or one page's part of a message too long for a single page.</summary>
    private sealed class BubbleBlock : PdfBlock
    {
        private readonly List<PdfRow> _rows;
        private readonly bool _right;
        private readonly double _width;

        public BubbleBlock(List<PdfRow> rows, bool alignRight)
        {
            _rows = rows; _right = alignRight;
            Height = RowsHeight(rows);
            _width = Math.Min(BubbleMax, rows.Max(r => r.Width) + 2 * Pad);
        }

        public override void Draw(DrawingContext dc, double y)
        {
            var x = _right ? PageW - MarginX - _width : MarginX;
            // Chat-app look: blue on the right for the custodian's (or "outgoing") messages, green on the left for the rest.
            dc.DrawRoundedRectangle(_right ? BubblePalette.Blue : BubblePalette.Green, null, new Rect(x, y, _width, Height), 16, 16);
            var rowY = y + Pad;
            for (var i = 0; i < _rows.Count; i++)
            {
                if (i > 0) rowY += _rows[i].GapBefore;
                _rows[i].Draw(dc, x + Pad, rowY);
                rowY += _rows[i].Height;
            }
        }
    }

    private static double RowsHeight(List<PdfRow> rows)
    {
        var height = 2 * Pad;
        for (var i = 0; i < rows.Count; i++) height += rows[i].Height + (i > 0 ? rows[i].GapBefore : 0);
        return height;
    }

    private sealed class PageLayout
    {
        public List<(PdfBlock Block, double Y)> Items { get; } = new();
    }

    // ------------------------------------------------------------------ layout

    private sealed class Layout
    {
        public List<PageLayout> Pages { get; } = new();
        private double _y;
        public void StartConversation() => NewPage();

        private void NewPage()
        {
            Pages.Add(new PageLayout());
            _y = TopY;
        }

        private double Remaining(double gap) => ContentBottom - (_y > TopY ? _y + gap : _y);

        public bool Fits(double height, double gap) => height <= Remaining(gap);

        public bool AtTop => _y <= TopY;

        public void BreakPage()
        {
            if (_y > TopY) NewPage();
        }

        /// <summary>Places a block, starting a new page when it (plus <paramref name="keepWith"/>) no longer fits.</summary>
        public void Place(PdfBlock block, double gap, double keepWith = 0)
        {
            if (_y > TopY && block.Height + keepWith > Remaining(gap)) NewPage();
            if (_y > TopY) _y += gap;
            Pages[^1].Items.Add((block, _y));
            _y += block.Height;
        }
    }

    /// <summary>
    /// Cuts message text into pieces small enough to move between pages, without changing how it reads:
    /// a cut is made only at a line break that is in the text, or exactly where the text would wrap to a
    /// new line anyway. No character is dropped except the line break a cut replaces.
    /// </summary>
    private static List<string> SplitBody(string body, double width)
    {
        var pieces = new List<string>();
        if (body.Length == 0) return pieces;
        double HeightOf(string t) => Ft(t, 13, Ink, width).Height;

        // Line breaks at the end of a piece would not be drawn, so each becomes an empty piece (a blank line).
        void Add(string piece)
        {
            var text = piece.TrimEnd('\n');
            pieces.Add(text);
            for (var k = text.Length; k < piece.Length; k++) pieces.Add("");
        }

        var start = 0;
        while (start < body.Length)
        {
            var end = PdfExport.PieceLimit(body, start, 320, 8);
            if (end >= body.Length)
            {
                Add(body[start..]);
                break;
            }
            if (body[end] == '\n')
            {
                Add(body[start..end]);
                start = end + 1;
                continue;
            }

            // Mid-paragraph: find where the last line of this stretch begins and cut there.
            var stretch = body[start..end];
            var full = HeightOf(stretch);
            int low = 1, high = stretch.Length;                 // smallest length whose height reaches the full height
            while (low < high)
            {
                var mid = (low + high) / 2;
                if (HeightOf(stretch[..mid]) >= full) high = mid; else low = mid + 1;
            }
            var cut = low - 1;                                  // the character that first needed the last line
            var word = cut;
            while (word > 0 && !char.IsWhiteSpace(stretch[word - 1]) && cut - word < 200) word--;
            if (word > 0 && char.IsWhiteSpace(stretch[word - 1])) cut = word;   // the whole word moved to the new line
            if (cut > 0 && cut < stretch.Length && char.IsLowSurrogate(stretch[cut])) cut--;
            if (cut <= 0) cut = stretch.Length;                 // everything is on one line: nothing better to do
            // A cut that lands just after a line break in the text: that break is what ends the piece.
            Add(stretch[cut - 1] == '\n' ? stretch[..(cut - 1)] : stretch[..cut]);
            start += cut;
        }
        return pieces;
    }

    /// <summary>The sender as the file gives it: the participant's display name, or the participant id when the file has no name for it.</summary>
    private static string Sender(MessageView m) =>
        m.SenderName == "(no participant)" ? ""
        : m.SenderName.EndsWith(" (unknown participant)", StringComparison.Ordinal) ? m.SenderId
        : m.SenderName;

    private List<PdfRow> MessageRows(ViewerDocument doc, MessageView m)
    {
        Brush text = BubblePalette.Text, subtle = BubblePalette.Subtle;
        var inner = BubbleMax - 2 * Pad;
        var rows = new List<PdfRow>();
        var sender = Clean(Sender(m));
        var time = Clean(m.RawTimestamp);                       // exactly as written in the file
        if (sender.Length > 0 && time.Length > 0) rows.Add(new NameRow(sender, time, text, subtle, inner) { IsHead = true });
        else if (sender.Length > 0) rows.Add(new TextRow(sender, 12.5, text, inner, bold: true) { IsHead = true });
        else if (time.Length > 0) rows.Add(new TextRow(time, 10.5, subtle, inner) { IsHead = true });
        if (m.ParentId.Length > 0) rows.Add(new TextRow("parent: " + Clean(m.ParentId), 10.5, subtle, inner, italic: true) { IsHead = true });
        if (m.Deleted) rows.Add(new TextRow("deleted", 10.5, subtle, inner, italic: true) { IsHead = true });

        // Emoji short names (":smile:") are drawn as the emoji, as Relativity's viewer does.
        var body = EmojiShortNames.Expand(Clean(m.Body)).Replace("\r\n", "\n").Replace('\r', '\n');
        var firstPiece = true;
        foreach (var piece in SplitBody(body, inner))
        {
            // Pieces of one text follow each other with no extra space, so the text reads as one block.
            rows.Add(new TextRow(piece.Length > 0 ? piece : " ", 13, text, inner) { GapBefore = firstPiece ? RowGap : 0 });
            firstPiece = false;
        }

        foreach (var a in m.Attachments)
        {
            var picture = a.IsInlineImage ? ImageRow.TryCreate(doc, a) : null;
            if (picture is not null) rows.Add(picture);
            rows.Add(new TextRow(Clean(a.Display), 10.5, subtle, inner));
        }
        if (m.Reactions.Count > 0)
            rows.Add(new TextRow("reactions: " + Clean(string.Join("    ", m.Reactions.Select(r => r.Count is { } n ? EmojiShortNames.ExpandReaction(r.Value) + " " + n.ToString(CultureInfo.InvariantCulture) : EmojiShortNames.ExpandReaction(r.Value)))), 11, subtle, inner));
        if (m.EditCount > 0)
            rows.Add(new TextRow("edits: " + m.EditCount.ToString(CultureInfo.InvariantCulture), 10.5, subtle, inner, italic: true));
        if (rows.Count == 0) rows.Add(new TextRow(" ", 13, text, inner));
        return rows;
    }

    /// <summary>An event that is not a message: sender, the file's own event type, body and timestamp.</summary>
    private static string NoticeText(MessageView m)
    {
        var parts = new[] { Sender(m), m.Type, m.Body, m.RawTimestamp }.Where(p => p.Length > 0);
        var text = Clean(string.Join("  \u00B7  ", parts)).Replace('\n', ' ');
        return text.Length > 1500 ? text[..1500] + "\u2026" : text;
    }

    private Layout Build(ViewerDocument doc, CancellationToken ct)
    {
        var layout = new Layout();
        foreach (var c in doc.Conversations)
        {
            layout.StartConversation();
            // The conversation's display name, platform and type are values from the file. Events that
            // belong to no conversation in the file get no heading at all.
            if (c.Id != ViewerDocument.OrphanConversationId)
                layout.Place(new HeadingBlock(Clean(c.Title).Replace('\n', ' '), Clean(string.Join("  \u00B7  ", new[] { c.Platform, c.Type }.Where(v => v.Length > 0)))), 0);

            var count = 0;
            foreach (var m in c.Messages)
            {
                if (++count % 100 == 0) ct.ThrowIfCancellationRequested();
                if (!m.IsMessage)
                {
                    var notice = NoticeText(m);
                    if (notice.Length > 0) layout.Place(new CentredBlock(notice), 8);
                    continue;
                }

                var rows = MessageRows(doc, m);
                var total = RowsHeight(rows);
                if (layout.Fits(total, 8) || total <= ContentH)
                {
                    layout.Place(new BubbleBlock(rows, m.AlignRight), 8);
                    continue;
                }

                // Longer than a page: fill each page with as many rows as fit.
                var part = new List<PdfRow>();
                var height = 2 * Pad;
                foreach (var row in rows)
                {
                    var add = row.Height + (part.Count > 0 ? row.GapBefore : 0);
                    if (part.Count > 0 && !layout.Fits(height + add, 8) && part.All(r => r.IsHead) && !layout.AtTop)
                    {
                        layout.BreakPage();                     // keep the sender line with the text that follows
                    }
                    else if (part.Count > 0 && !layout.Fits(height + add, 8))
                    {
                        layout.Place(new BubbleBlock(part, m.AlignRight), 8);
                        layout.BreakPage();
                        part = new List<PdfRow>();
                        height = 2 * Pad;
                        add = row.Height;
                    }
                    else if (part.Count == 0 && !layout.Fits(height + add, 8))
                    {
                        layout.BreakPage();
                    }
                    part.Add(row);
                    height += add;
                }
                if (part.Count > 0) layout.Place(new BubbleBlock(part, m.AlignRight), 8);
            }
        }
        return layout;
    }

    // ------------------------------------------------------------------ drawing

    /// <summary>Renders the file to <paramref name="output"/> and returns the number of pages written.</summary>
    public int Render(ViewerDocument doc, Stream output, CancellationToken ct, Action<int>? onPage = null)
    {
        var layout = Build(doc, ct);
        using var pdf = new PdfImageWriter(output);
        var total = layout.Pages.Count;
        for (var i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            DrawPage(layout.Pages[i], pdf);
            if ((i + 1) % 5 == 0) onPage?.Invoke(i + 1);
        }
        pdf.Finish();

        // Let WPF release what the drawing used before the next file.
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        return total;
    }

    private void DrawPage(PageLayout page, PdfImageWriter pdf)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, PageW, PageH));
            dc.PushClip(new RectangleGeometry(new Rect(0, TopY - 6, PageW, ContentH + 12)));
            foreach (var (block, y) in page.Items) block.Draw(dc, y);
            dc.Pop();
        }

        _target ??= new RenderTargetBitmap(PxW, PxH, Dpi, Dpi, PixelFormats.Pbgra32);
        _target.Clear();
        _target.Render(visual);
        _target.CopyPixels(_bgra, PxW * 4, 0);
        for (int i = 0, j = 0; i < _bgra.Length; i += 4, j += 3)
        {
            _rgb[j] = _bgra[i + 2];
            _rgb[j + 1] = _bgra[i + 1];
            _rgb[j + 2] = _bgra[i];
        }
        pdf.AddPage(612, 792, PxW, PxH, _rgb);
    }
}

/// <summary>Runs "Convert RSMF folder to PDF": finds the files, renders each to its own PDF, writes the log.</summary>
internal static class PdfConverter
{
    /// <summary>Does the whole run on its own thread, so the window stays responsive.</summary>
    public static Task<PdfRun> RunAsync(string input, string outputRoot, InspectionOptions options, IProgress<PdfProgress> progress, CancellationToken ct, string folderPrefix = "pdf-")
    {
        var done = new TaskCompletionSource<PdfRun>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                done.SetResult(Run(input, outputRoot, options, progress, ct, folderPrefix));
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true, Name = "pdf-conversion" };
        thread.SetApartmentState(ApartmentState.STA);   // WPF drawing needs an STA thread
        thread.Start();
        return done.Task;
    }

    private static PdfRun Run(string input, string outputRoot, InspectionOptions options, IProgress<PdfProgress> progress, CancellationToken ct, string folderPrefix)
    {
        var zone = options.ResolveZone().Id;
        var run = new PdfRun
        {
            RunFolder = PdfExport.NewRunFolder(outputRoot, DateTime.Now, folderPrefix),
            Input = System.IO.Path.GetFullPath(input),
            ZoneId = zone,
            Dpi = PdfRenderer.Dpi,
            StartedUtc = DateTime.UtcNow,
        };
        try
        {
            try
            {
                run.Entries.AddRange(PdfExport.Discover(input, options, run.Notes, ct));
            }
            catch (OperationCanceledException)
            {
                run.Cancelled = true;
            }
            PdfExport.AssignTargets(run.RunFolder, run.Entries);

            var renderer = new PdfRenderer();
            var pagesDone = 0;
            for (var i = 0; i < run.Entries.Count; i++)
            {
                var entry = run.Entries[i];
                if (ct.IsCancellationRequested)
                {
                    run.Cancelled = true;
                    entry.Status = PdfStatus.Cancelled;
                    continue;
                }
                var index = i;
                var before = pagesDone;
                progress.Report(new PdfProgress(index, run.Entries.Count, entry.RelativePath, before));
                ConvertOne(renderer, entry, options, ct, pages => progress.Report(new PdfProgress(index, run.Entries.Count, entry.RelativePath, before + pages)));
                if (entry.Status == PdfStatus.Cancelled) run.Cancelled = true;
                pagesDone += entry.Pages;
            }
        }
        finally
        {
            PdfExport.WriteLog(run);
        }
        return run;
    }

    private static void ConvertOne(PdfRenderer renderer, PdfEntry entry, InspectionOptions options, CancellationToken ct, Action<int> onPage)
    {
        var partial = entry.PdfPath + ".partial";
        try
        {
            using var doc = ViewerDocument.Load(entry.Source, options, ct);
            entry.Conversations = doc.Conversations.Count;
            entry.Events = doc.Conversations.Sum(c => c.Messages.Count);
            if (doc.Conversations.Count == 0)
            {
                entry.Status = PdfStatus.NotConverted;
                entry.Note = doc.Unavailable.Count > 0 ? string.Join(" ", doc.Unavailable) : "The file has no conversations to show.";
                return;
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(entry.PdfPath)!);
            int pages;
            using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var buffered = new BufferedStream(file, 1 << 16))
            {
                pages = renderer.Render(doc, buffered, ct, onPage);
            }
            File.Move(partial, entry.PdfPath);
            entry.Pages = pages;
            entry.PdfBytes = new FileInfo(entry.PdfPath).Length;
            entry.Status = PdfStatus.Converted;
            if (doc.Unavailable.Count > 0) entry.Note = string.Join(" ", doc.Unavailable);
        }
        catch (OperationCanceledException)
        {
            entry.Status = PdfStatus.Cancelled;
            TryDelete(partial);
        }
        catch (Exception ex)
        {
            entry.Status = PdfStatus.Failed;
            entry.Note = "The file could not be rendered (" + ex.GetType().Name + ").";
            TryDelete(partial);
        }
    }

    /// <summary>Removes this run's own unfinished output file. Source files are never touched.</summary>
    private static void TryDelete(string partial)
    {
        try { if (File.Exists(partial)) File.Delete(partial); }
        catch (Exception) { /* left behind with its .partial name */ }
    }
}
