using System.Globalization;

namespace Rsmf.Core;

/// <summary>Converts the size threshold between gigabytes (what people type) and bytes (what is checked and reported).</summary>
public static class SizeUnits
{
    /// <summary>One gigabyte as Windows counts it: 1,024 x 1,024 x 1,024 bytes. With this, 2 GB is the default 2,147,483,648 bytes.</summary>
    public const long BytesPerGb = 1_073_741_824L;

    /// <summary>Largest value accepted. Guards against an old byte figure being typed where GB is now expected.</summary>
    public const double MaxGb = 100_000;

    /// <summary>Parses a size in GB such as "2", "1.5" or "2 GB". Returns false for anything else, zero, or a value over <see cref="MaxGb"/>.</summary>
    public static bool TryParseGb(string? text, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.EndsWith("GB", StringComparison.OrdinalIgnoreCase)) s = s[..^2].TrimEnd();
        if (!double.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var gb)) return false;
        if (double.IsNaN(gb) || gb <= 0 || gb > MaxGb) return false;
        bytes = (long)Math.Round(gb * BytesPerGb, MidpointRounding.AwayFromZero);
        return bytes > 0;
    }

    /// <summary>True when the text is a number too large to be a size in GB (most likely a byte count from an older version).</summary>
    public static bool LooksLikeBytes(string? text) =>
        double.TryParse(text?.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) && n > MaxGb;

    /// <summary>Formats bytes as GB with no trailing zeros, e.g. 2147483648 gives "2" and 1610612736 gives "1.5".</summary>
    public static string FormatGb(long bytes) =>
        ((double)bytes / BytesPerGb).ToString("0.#########", CultureInfo.InvariantCulture);
}
