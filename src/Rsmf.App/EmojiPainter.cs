using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Rsmf.Core;

namespace Rsmf.App;

/// <summary>
/// Draws text with colour emoji. WPF itself draws emoji as black outlines, so this reads the colour layers
/// (the COLR and CPAL tables) from Windows' own "Segoe UI Emoji" font file and paints each emoji as its
/// stack of coloured shapes. Nothing is bundled and nothing is downloaded; on a PC without that font, or
/// if the font cannot be read, text is drawn the ordinary way with outline emoji.
/// </summary>
internal static class EmojiPainter
{
    public const string EmojiFamilyName = "Segoe UI Emoji";
    private static readonly FontFamily EmojiFamily = new(EmojiFamilyName);

    /// <summary>The colour layers of one font file: for each emoji glyph, the glyphs to stack and the colour of each.</summary>
    private sealed class ColourTable
    {
        public required Dictionary<ushort, (int First, int Count)> Glyphs { get; init; }
        public required (ushort Glyph, ushort Colour)[] Layers { get; init; }
        public required Color[] Palette { get; init; }
    }

    private static readonly ConcurrentDictionary<string, ColourTable?> Tables = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<Color, Brush> Brushes = new();

    private static readonly Lazy<bool> Available = new(() =>
    {
        try
        {
            return new Typeface(EmojiFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out var face)
                   && TableFor(face) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    });

    /// <summary>True when this PC has the colour emoji font and its colour layers could be read.</summary>
    public static bool IsAvailable => Available.Value;

    /// <summary>Asks for the emoji font on every emoji stretch of the text, so each emoji is shaped as one unit.</summary>
    public static void Prepare(FormattedText formatted, string text)
    {
        if (!IsAvailable) return;
        foreach (var (start, length) in EmojiText.FindRuns(text))
            formatted.SetFontFamily(EmojiFamily, start, length);
    }

    /// <summary>Draws the text at the given point, painting emoji in colour when that is possible.</summary>
    public static void Draw(DrawingContext dc, FormattedText formatted, Point origin)
    {
        if (!IsAvailable)
        {
            dc.DrawText(formatted, origin);
            return;
        }
        // Let WPF lay the text out, then replay what it would have drawn, swapping in colour for emoji glyphs.
        var recorded = new DrawingGroup();
        using (var recorder = recorded.Open()) recorder.DrawText(formatted, origin);
        Replay(dc, recorded);
    }

    private static void Replay(DrawingContext dc, Drawing drawing)
    {
        if (drawing is DrawingGroup group)
        {
            var pushed = 0;
            if (group.Transform is not null) { dc.PushTransform(group.Transform); pushed++; }
            if (group.ClipGeometry is not null) { dc.PushClip(group.ClipGeometry); pushed++; }
            if (group.Opacity < 1) { dc.PushOpacity(group.Opacity); pushed++; }
            foreach (var child in group.Children) Replay(dc, child);
            for (; pushed > 0; pushed--) dc.Pop();
            return;
        }
        if (drawing is GlyphRunDrawing run && run.GlyphRun is { } glyphs && (glyphs.BidiLevel & 1) == 0 && !glyphs.IsSideways
            && TableFor(glyphs.GlyphTypeface) is { } table && glyphs.GlyphIndices.Any(table.Glyphs.ContainsKey))
        {
            DrawColourRun(dc, glyphs, run.ForegroundBrush, table);
            return;
        }
        dc.DrawDrawing(drawing);
    }

    private static void DrawColourRun(DrawingContext dc, GlyphRun run, Brush foreground, ColourTable table)
    {
        var face = run.GlyphTypeface;
        var size = run.FontRenderingEmSize;
        var x = run.BaselineOrigin.X;
        var y = run.BaselineOrigin.Y;
        for (var i = 0; i < run.GlyphIndices.Count; i++)
        {
            var glyph = run.GlyphIndices[i];
            var offset = run.GlyphOffsets is { Count: > 0 } offsets && i < offsets.Count ? offsets[i] : new Point(0, 0);
            dc.PushTransform(new TranslateTransform(x + offset.X, y - offset.Y));
            if (table.Glyphs.TryGetValue(glyph, out var entry))
            {
                for (var layer = entry.First; layer < entry.First + entry.Count && layer < table.Layers.Length; layer++)
                {
                    var (layerGlyph, colour) = table.Layers[layer];
                    var brush = colour == 0xFFFF || colour >= table.Palette.Length ? foreground : BrushFor(table.Palette[colour]);
                    dc.DrawGeometry(brush, null, face.GetGlyphOutline(layerGlyph, size, size));
                }
            }
            else
            {
                dc.DrawGeometry(foreground, null, face.GetGlyphOutline(glyph, size, size));
            }
            dc.Pop();
            x += i < run.AdvanceWidths.Count ? run.AdvanceWidths[i] : 0;
        }
    }

    private static Brush BrushFor(Color colour) => Brushes.GetOrAdd(colour, c =>
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    });

    private static ColourTable? TableFor(GlyphTypeface? face)
    {
        if (face?.FontUri is not { IsFile: true } uri) return null;
        return Tables.GetOrAdd(uri.LocalPath, path =>
        {
            try
            {
                return Read(File.ReadAllBytes(path));
            }
            catch (Exception)
            {
                return null;   // unreadable or not the layout expected: fall back to outline emoji
            }
        });
    }

    // ------------------------------------------------------------------ font file parsing (OpenType COLR version 0 + CPAL)

    private static ushort U16(byte[] b, long at) => (ushort)((b[at] << 8) | b[at + 1]);
    private static uint U32(byte[] b, long at) => ((uint)b[at] << 24) | ((uint)b[at + 1] << 16) | ((uint)b[at + 2] << 8) | b[at + 3];

    private static ColourTable? Read(byte[] font)
    {
        if (font.Length < 12) return null;
        long colr = -1, cpal = -1;
        int tables = U16(font, 4);
        for (var i = 0; i < tables; i++)
        {
            long record = 12 + 16L * i;
            if (record + 16 > font.Length) return null;
            var tag = System.Text.Encoding.ASCII.GetString(font, (int)record, 4);
            if (tag == "COLR") colr = U32(font, record + 8);
            else if (tag == "CPAL") cpal = U32(font, record + 8);
        }
        if (colr < 0 || cpal < 0 || colr + 14 > font.Length || cpal + 12 > font.Length) return null;

        // COLR: base glyph records (glyph, first layer, layer count) and layer records (glyph, palette entry).
        int baseCount = U16(font, colr + 2);
        long baseAt = colr + U32(font, colr + 4);
        long layerAt = colr + U32(font, colr + 8);
        int layerCount = U16(font, colr + 12);
        if (baseCount == 0 || baseAt + 6L * baseCount > font.Length || layerAt + 4L * layerCount > font.Length) return null;

        var glyphs = new Dictionary<ushort, (int, int)>(baseCount);
        for (var i = 0; i < baseCount; i++)
        {
            var at = baseAt + 6L * i;
            glyphs[U16(font, at)] = (U16(font, at + 2), U16(font, at + 4));
        }
        var layers = new (ushort, ushort)[layerCount];
        for (var i = 0; i < layerCount; i++)
        {
            var at = layerAt + 4L * i;
            layers[i] = (U16(font, at), U16(font, at + 2));
        }

        // CPAL: the first palette's colours, stored as blue, green, red, alpha.
        int entries = U16(font, cpal + 2);
        int palettes = U16(font, cpal + 4);
        int records = U16(font, cpal + 6);
        long recordsAt = cpal + U32(font, cpal + 8);
        if (palettes == 0 || cpal + 12 + 2 > font.Length) return null;
        int first = U16(font, cpal + 12);
        if (recordsAt + 4L * records > font.Length || first + entries > records) return null;
        var palette = new Color[entries];
        for (var i = 0; i < entries; i++)
        {
            var at = recordsAt + 4L * (first + i);
            palette[i] = Color.FromArgb(font[at + 3], font[at + 2], font[at + 1], font[at]);
        }
        return new ColourTable { Glyphs = glyphs, Layers = layers, Palette = palette };
    }

    // ------------------------------------------------------------------ self-check

    /// <summary>Reports what was found on this PC and how some sample emoji come out (self-check only).</summary>
    public static string SelfTest()
    {
        if (!IsAvailable) return "available=False";
        new Typeface(EmojiFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out var face);
        var table = TableFor(face)!;
        var samples = new (string Name, string Text)[]
        {
            ("grin", "😀"), ("thumb-tone", "👍🏽"), ("family", "👨‍👩‍👧"),
            ("flag", "🇺🇸"), ("keycap", "1️⃣"), ("heart", "❤️"),
        };
        var parts = new List<string>();
        foreach (var (name, text) in samples)
        {
            var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 13, System.Windows.Media.Brushes.Black, 1.0);
            Prepare(formatted, text);
            var recorded = new DrawingGroup();
            using (var recorder = recorded.Open()) recorder.DrawText(formatted, new Point(0, 0));
            int glyphCount = 0, coloured = 0;
            void Count(Drawing d)
            {
                if (d is DrawingGroup g) { foreach (var c in g.Children) Count(c); }
                else if (d is GlyphRunDrawing r && r.GlyphRun is { } gr)
                {
                    glyphCount += gr.GlyphIndices.Count;
                    if (TableFor(gr.GlyphTypeface) is { } t) coloured += gr.GlyphIndices.Count(t.Glyphs.ContainsKey);
                }
            }
            Count(recorded);
            parts.Add($"{name}={coloured}/{glyphCount}");
        }
        return $"available=True file={Path.GetFileName(face!.FontUri.LocalPath)} colour-glyphs={table.Glyphs.Count} layers={table.Layers.Length} palette={table.Palette.Length} " +
               "coloured/glyphs: " + string.Join(" ", parts);
    }
}

/// <summary>
/// Text for the viewer: wraps like a TextBlock (or stays on one line), highlights search hits, and shows emoji in colour.
/// The text is drawn as plain text; nothing in it is interpreted.
/// </summary>
public sealed class ChatText : FrameworkElement
{
    private static readonly Brush HitBackground = MakeBrush(0xFF, 0xE0, 0x66);

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(ChatText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TermProperty = DependencyProperty.Register(nameof(Term), typeof(string), typeof(ChatText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(ChatText),
        new FrameworkPropertyMetadata(System.Windows.Media.Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>One line only, cut with an ellipsis when it does not fit (for names and titles).</summary>
    public static readonly DependencyProperty SingleLineProperty = DependencyProperty.Register(nameof(SingleLine), typeof(bool), typeof(ChatText),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Centres each line within the element (for notices).</summary>
    public static readonly DependencyProperty CenteredProperty = DependencyProperty.Register(nameof(Centered), typeof(bool), typeof(ChatText),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool SingleLine { get => (bool)GetValue(SingleLineProperty); set => SetValue(SingleLineProperty, value); }
    public bool Centered { get => (bool)GetValue(CenteredProperty); set => SetValue(CenteredProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Term { get => (string)GetValue(TermProperty); set => SetValue(TermProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    private static Brush MakeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private FormattedText Build(double width)
    {
        var text = Text ?? "";
        var typeface = new Typeface(System.Windows.Documents.TextElement.GetFontFamily(this), System.Windows.Documents.TextElement.GetFontStyle(this),
            System.Windows.Documents.TextElement.GetFontWeight(this), FontStretches.Normal);
        var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight, typeface,
            System.Windows.Documents.TextElement.GetFontSize(this), Foreground ?? System.Windows.Media.Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (SingleLine)
        {
            formatted.MaxLineCount = 1;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }
        else
        {
            formatted.Trimming = TextTrimming.None;      // wrap, never cut text short with an ellipsis
        }
        if (!double.IsInfinity(width) && width > 0) formatted.MaxTextWidth = Math.Min(width, 3_000_000);
        EmojiPainter.Prepare(formatted, text);
        return formatted;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (string.IsNullOrEmpty(Text)) return new Size(0, 0);
        var formatted = Build(availableSize.Width);
        var width = Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
        if (!double.IsInfinity(availableSize.Width)) width = Math.Min(width, availableSize.Width);
        return new Size(width, Math.Ceiling(formatted.Height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var text = Text ?? "";
        if (text.Length == 0) return;
        var formatted = Build(Math.Max(1, RenderSize.Width));
        if (Centered) formatted.TextAlignment = TextAlignment.Center;
        foreach (var (start, length) in Rsmf.Core.TextSearch.FindMatches(text, Term ?? "", 200))
        {
            if (formatted.BuildHighlightGeometry(new Point(0, 0), start, length) is { } box) dc.DrawGeometry(HitBackground, null, box);
            formatted.SetForegroundBrush(BubblePalette.HitText, start, length);
        }
        EmojiPainter.Draw(dc, formatted, new Point(0, 0));
    }
}
