using NodaTime;
using NodaTime.Text;

namespace Rsmf.Core;

/// <summary>A parsed ISO 8601 date-time. Offset-less values are interpreted as UTC.</summary>
public readonly record struct ParsedTimestamp(Instant Instant, bool HasOffset, Offset Offset);

public static class TimestampParser
{
    private static readonly OffsetDateTimePattern[] OffsetPatterns =
    {
        OffsetDateTimePattern.ExtendedIso,
        OffsetDateTimePattern.Rfc3339,
        OffsetDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mmo<G>"),
        OffsetDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm':'ss;FFFFFFFFFo<+HHmm>"),
    };

    private static readonly LocalDateTimePattern[] LocalPatterns =
    {
        LocalDateTimePattern.ExtendedIso,
        LocalDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm"),
    };

    /// <summary>Parses an ISO 8601 extended date-time with optional fraction and optional Z/offset.</summary>
    public static bool TryParse(string? text, out ParsedTimestamp result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.Length < 16 || s.Length > 40) return false;
        // Accept a lower-case 't' / 'z' as RFC 3339 permits.
        if (s[10] == 't') s = s[..10] + "T" + s[11..];
        if (s[^1] == 'z') s = s[..^1] + "Z";

        foreach (var p in OffsetPatterns)
        {
            var r = p.Parse(s);
            if (r.Success)
            {
                result = new ParsedTimestamp(r.Value.ToInstant(), true, r.Value.Offset);
                return true;
            }
        }
        foreach (var p in LocalPatterns)
        {
            var r = p.Parse(s);
            if (r.Success)
            {
                result = new ParsedTimestamp(r.Value.InUtc().ToInstant(), false, Offset.Zero);
                return true;
            }
        }
        return false;
    }

    public static string Format(Instant instant) => InstantPattern.ExtendedIso.Format(instant);

    public static string FormatDuration(Duration d)
    {
        var neg = d < Duration.Zero;
        if (neg) d = -d;
        var totalHours = (long)Math.Floor(d.TotalHours);
        var s = $"{totalHours}:{d.Minutes:00}:{d.Seconds:00}";
        if (d.SubsecondNanoseconds != 0) s += "." + d.SubsecondNanoseconds.ToString("000000000").TrimEnd('0');
        return neg ? "-" + s : s;
    }
}
