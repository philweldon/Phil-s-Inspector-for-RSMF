using NodaTime;

namespace Rsmf.Core;

public sealed class ConversationMetrics
{
    public required string RecordId { get; init; }
    public required int Index { get; init; }
    public int EventCount { get; set; }
    public int MessageCount { get; set; }
    public int ParticipantCount { get; set; }
    /// <summary>Earliest and latest message-event timestamps in the conversation.</summary>
    public Instant? Earliest { get; set; }
    public Instant? Latest { get; set; }
    public Duration? Span => Earliest.HasValue && Latest.HasValue ? Latest.Value - Earliest.Value : null;
}

public sealed class FileMetrics
{
    public long SizeBytes { get; set; }
    public int? TotalEvents { get; set; }
    public int? MessageEvents { get; set; }
    public int? ConversationCount { get; set; }
    public int? UniqueParticipantCount { get; set; }
    public List<ConversationMetrics> Conversations { get; } = new();
}

public enum TimeWindowStatus { NotEvaluated, NoMessages, Pass, Fail, Indeterminate }

public sealed class TimeWindowResult
{
    public TimeWindowStatus Status { get; set; } = TimeWindowStatus.NotEvaluated;
    public bool RuleAChecked { get; set; }
    public bool RuleAFailed { get; set; }
    public bool RuleBFailed { get; set; }
    public Instant? Earliest { get; set; }
    public Instant? Latest { get; set; }
    public Duration? Span => Earliest.HasValue && Latest.HasValue ? Latest.Value - Earliest.Value : null;
    public string ZoneId { get; set; } = "";
    public List<string> LocalDates { get; } = new();
    public int OffsetlessCount { get; set; }
    public List<string> IndeterminateLocations { get; } = new();
    public string Reason { get; set; } = "";
}

public sealed class CoverageRow
{
    public required string Level { get; init; }
    public required string Field { get; init; }
    public required string Requirement { get; init; }
    public bool IsCustom { get; init; }
    public int Records { get; set; }
    public int Missing { get; set; }
    public int Blank { get; set; }
    public int Populated { get; set; }
    public int Invalid { get; set; }
    public List<string> MissingIds { get; } = new();
    public List<string> BlankIds { get; } = new();
    public List<string> InvalidIds { get; } = new();
    /// <summary>Example values; collected only when sensitive output was requested.</summary>
    public List<string> SampleValues { get; } = new();
}

public enum FileKind { RsmfCandidate, NotCandidate, SkippedLink }

public enum FileStatus { Ok, Warnings, Errors, Unreadable, NotChecked, NotCandidate, Skipped }

public sealed class FileResult
{
    public required string Path { get; init; }
    public required string RelativePath { get; init; }
    public FileKind Kind { get; init; } = FileKind.RsmfCandidate;
    public long SizeBytes { get; set; }
    public string RsmfVersion { get; set; } = "";
    public bool Checked { get; set; }
    public List<Finding> Findings { get; } = new();
    public FileMetrics Metrics { get; } = new();
    public TimeWindowResult TimeWindow { get; } = new();
    public List<CoverageRow> Coverage { get; } = new();
    public List<string> SkippedChecks { get; } = new();

    public int Count(Severity s) => Findings.Count(f => f.Severity == s);
    public bool Incomplete => Findings.Any(f => f.Category == FindingCategory.INCOMPLETE);

    public FileStatus Status
    {
        get
        {
            if (Kind == FileKind.NotCandidate) return FileStatus.NotCandidate;
            if (Kind == FileKind.SkippedLink) return FileStatus.Skipped;
            if (!Checked) return Findings.Any(f => f.RuleId == "I-FILE-001") ? FileStatus.Unreadable : FileStatus.NotChecked;
            if (Findings.Any(f => f.RuleId == "I-FILE-001")) return FileStatus.Unreadable;
            if (Count(Severity.Error) > 0) return FileStatus.Errors;
            if (Count(Severity.Warning) > 0) return FileStatus.Warnings;
            return FileStatus.Ok;
        }
    }
}
