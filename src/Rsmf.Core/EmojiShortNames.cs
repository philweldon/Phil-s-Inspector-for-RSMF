using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Rsmf.Core;

/// <summary>
/// RSMF files write emoji as short names between colons (":smile:", ":+1:") in message text, and as a
/// short name in a reaction's value ("thumbsup"). This turns the names it knows into the emoji they
/// stand for, for display only; the file's own text is never changed, and a name that is not known is
/// left exactly as written.
/// </summary>
public static class EmojiShortNames
{
    private static readonly Regex InText = new(@":([A-Za-z0-9_+\-]{1,64}):", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Lazy<Dictionary<string, string>> Names = new(() =>
    {
        var map = new Dictionary<string, string>(6000, StringComparer.Ordinal);
        using var stream = typeof(EmojiShortNames).GetTypeInfo().Assembly.GetManifestResourceStream("emoji_short_names.txt");
        if (stream is null) return map;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var tab = line.IndexOf('\t');
            if (tab > 0 && tab < line.Length - 1) map[line[..tab]] = line[(tab + 1)..];
        }
        return map;
    });

    /// <summary>How many short names are known.</summary>
    public static int Count => Names.Value.Count;

    /// <summary>Looks up one short name, with or without its colons, in any letter case.</summary>
    public static bool TryGet(string? shortName, out string emoji)
    {
        emoji = "";
        if (string.IsNullOrWhiteSpace(shortName)) return false;
        var name = shortName.Trim();
        if (name.Length >= 3 && name[0] == ':' && name[^1] == ':') name = name[1..^1];
        if (!Names.Value.TryGetValue(name.ToLowerInvariant(), out var found)) return false;
        emoji = found;
        return true;
    }

    /// <summary>Message text for display: every known ":name:" becomes its emoji; everything else is untouched.</summary>
    public static string Expand(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        if (text.IndexOf(':') < 0) return text;
        return InText.Replace(text, m => Names.Value.TryGetValue(m.Groups[1].Value.ToLowerInvariant(), out var emoji) ? emoji : m.Value);
    }

    /// <summary>A reaction's value for display: the emoji when the whole value is a known short name, otherwise the value as written.</summary>
    public static string ExpandReaction(string? value) => TryGet(value, out var emoji) ? emoji : value ?? "";
}
