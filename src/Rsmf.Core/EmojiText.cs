using System.Text;

namespace Rsmf.Core;

/// <summary>
/// Finds the stretches of a text that are emoji, so the app can draw them with the system's colour emoji
/// font. A stretch is one or more emoji in a row, including the joiners, skin tones, variation selectors,
/// keycap marks and flag letters that belong to them. Nothing in the text is changed.
/// </summary>
public static class EmojiText
{
    private const int Zwj = 0x200D, Vs16 = 0xFE0F, Vs15 = 0xFE0E, Keycap = 0x20E3;

    // Characters in the Basic Multilingual Plane that are shown as emoji without a variation selector.
    private static readonly (int From, int To)[] BmpEmoji =
    {
        (0x231A, 0x231B), (0x23E9, 0x23EC), (0x23F0, 0x23F0), (0x23F3, 0x23F3), (0x25FD, 0x25FE), (0x2614, 0x2615),
        (0x2648, 0x2653), (0x267F, 0x267F), (0x2693, 0x2693), (0x26A1, 0x26A1), (0x26AA, 0x26AB), (0x26BD, 0x26BE),
        (0x26C4, 0x26C5), (0x26CE, 0x26CE), (0x26D4, 0x26D4), (0x26EA, 0x26EA), (0x26F2, 0x26F3), (0x26F5, 0x26F5),
        (0x26FA, 0x26FA), (0x26FD, 0x26FD), (0x2705, 0x2705), (0x270A, 0x270B), (0x2728, 0x2728), (0x274C, 0x274C),
        (0x274E, 0x274E), (0x2753, 0x2755), (0x2757, 0x2757), (0x2795, 0x2797), (0x27B0, 0x27B0), (0x27BF, 0x27BF),
        (0x2B1B, 0x2B1C), (0x2B50, 0x2B50), (0x2B55, 0x2B55),
    };

    private static bool IsPictograph(int cp) =>
        cp is >= 0x1F000 and <= 0x1FAFF          // emoticons, symbols, transport, flags' letters, supplemental symbols
        || BmpEmoji.Any(r => cp >= r.From && cp <= r.To);

    private static bool IsRegionalIndicator(int cp) => cp is >= 0x1F1E6 and <= 0x1F1FF;
    private static bool IsSkinTone(int cp) => cp is >= 0x1F3FB and <= 0x1F3FF;
    private static bool IsTag(int cp) => cp is >= 0xE0020 and <= 0xE007F;
    private static bool IsKeycapBase(int cp) => cp is (>= '0' and <= '9') or '#' or '*';

    /// <summary>Characters that only ever continue an emoji that has already started.</summary>
    private static bool Continues(int cp) => cp == Zwj || cp == Vs16 || cp == Keycap || IsSkinTone(cp) || IsTag(cp);

    /// <summary>Returns each emoji stretch as (start, length) in UTF-16 units, in order and never overlapping.</summary>
    public static List<(int Start, int Length)> FindRuns(string? text)
    {
        var runs = new List<(int, int)>();
        if (string.IsNullOrEmpty(text)) return runs;

        var i = 0;
        var runStart = -1;
        while (i < text.Length)
        {
            var cp = CodePointAt(text, i, out var width);
            var next = i + width < text.Length ? CodePointAt(text, i + width, out _) : -1;

            bool starts;
            if (next == Vs15) starts = false;                                   // explicitly asked for as plain text
            else if (IsPictograph(cp)) starts = true;
            else if (next == Vs16) starts = cp > 0x7F || IsKeycapBase(cp);      // a symbol asked for as emoji, e.g. a heart, or "1" + keycap
            else starts = false;

            var inRun = runStart >= 0;
            if (starts || (inRun && Continues(cp)))
            {
                if (!inRun) runStart = i;
            }
            else if (inRun)
            {
                runs.Add((runStart, i - runStart));
                runStart = -1;
            }
            i += width;
        }
        if (runStart >= 0) runs.Add((runStart, text.Length - runStart));
        return runs;
    }

    /// <summary>True when the text contains at least one emoji.</summary>
    public static bool HasEmoji(string? text) => FindRuns(text).Count > 0;

    private static int CodePointAt(string s, int index, out int width)
    {
        if (char.IsHighSurrogate(s[index]) && index + 1 < s.Length && char.IsLowSurrogate(s[index + 1]))
        {
            width = 2;
            return char.ConvertToUtf32(s[index], s[index + 1]);
        }
        width = 1;
        return s[index];
    }
}
