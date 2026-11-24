namespace Rsmf.Core;

public enum FindingCategory { FORMAT, LIMIT, LOCAL, INCOMPLETE }

public enum Severity { Info = 0, Warning = 1, Error = 2 }

/// <summary>Definition of one rule in docs/RULES.md.</summary>
public sealed record RuleDefinition(
    string Id, FindingCategory Category, Severity Severity, string Title, string Expected, string Source);

/// <summary>One finding raised against a file (or against the scan itself).</summary>
public sealed record Finding
{
    public required string RuleId { get; init; }
    public required FindingCategory Category { get; init; }
    public required Severity Severity { get; init; }
    public required string Message { get; init; }
    /// <summary>Observed value. Never contains message bodies or personal metadata values.</summary>
    public required string Observed { get; init; }
    public required string Expected { get; init; }
    public required string Source { get; init; }
    /// <summary>Header name or JSON path, e.g. <c>$.events[12].participant</c>.</summary>
    public required string Location { get; init; }
    /// <summary>Identifier of the affected record, e.g. <c>event:m2</c> or <c>events[12]</c>.</summary>
    public string RecordId { get; init; } = "";
    /// <summary>Personal context for the record. Only written to reports when explicitly requested.</summary>
    public string? SensitiveDetail { get; init; }
}

/// <summary>The rule catalogue. Must stay in step with docs/RULES.md (a test enforces this).</summary>
public static class RuleCatalog
{
    public const string RulesetVersion = "1.2.0";
    public const int EventLimit = 10_000;
    public const long DefaultSizeThresholdBytes = 2_147_483_648L;

    private const FindingCategory F = FindingCategory.FORMAT, L = FindingCategory.LIMIT,
        Q = FindingCategory.LOCAL, I = FindingCategory.INCOMPLETE;
    private const Severity E = Severity.Error, W = Severity.Warning, N = Severity.Info;

    public static readonly IReadOnlyList<RuleDefinition> All = new RuleDefinition[]
    {
        new("F-MIME-001", F, E, "File parses as an Internet Message Format / MIME message", "Parseable headers and MIME structure", "CREATE §RSMF file support in Processing; SPEC §Overview"),
        new("F-MIME-002", F, E, "X-RSMF-Version header present", "Present, non-blank", "SPEC §Header; CREATE §support (2); PROC §Checklist"),
        new("F-MIME-003", F, E, "X-RSMF-Version value is a supported version", "1.0.0 or 2.0.0", "SPEC §Header, §Example; VALID schemas"),
        new("F-MIME-004", F, W, "X-RSMF-Version is the first header line", "First header in the file", "SPEC §Header; CREATE §support (2)"),
        new("F-MIME-005", F, E, "Exactly one base64 attachment with filename rsmf.zip", "Exactly one attachment: base64, Content-Disposition attachment, filename=\"rsmf.zip\"", "CREATE §support (3); VALID PDF Sample 3"),
        new("F-MIME-006", F, E, "rsmf.zip decodes from base64 and opens as a ZIP archive", "Valid base64, readable ZIP", "CREATE §support (3); SPEC §RSMF structure"),
        new("F-MIME-007", F, W, "File name extension", ".rsmf", "CREATE §support (1)"),
        new("F-MIME-008", F, W, "Custom X-RSMF-* header name length", "Fewer than 255 characters", "PROC §RSMF Header"),
        new("F-HDR-001", F, W, "Optional count and date headers are parseable", "Whole number / ISO 8601 date-time", "SPEC §Header; PROC §RSMF Header"),
        new("F-HDR-002", F, W, "X-RSMF-EventCount matches the number of events in the manifest", "Equal", "SPEC §Header; GEN RSMFGenerator.cs"),
        new("F-HDR-003", F, W, "X-RSMF-BeginDate matches the earliest event timestamp", "Same instant", "SPEC §Header; GEN RSMFGenerator.cs"),
        new("F-HDR-004", F, W, "X-RSMF-EndDate matches the latest event timestamp", "Same instant", "SPEC §Header; GEN RSMFGenerator.cs"),
        new("F-HDR-005", F, W, "RSMF 2.0-only header in a 1.0.0 file", "Absent in 1.0 files", "SPEC §Header table"),
        new("F-ZIP-001", F, E, "rsmf_manifest.json exists at the ZIP root", "Present at root", "SPEC §RSMF structure"),
        new("F-ZIP-002", F, W, "ZIP member is not referenced by the manifest", "Every member referenced", "PROC §RSMF.zip, §Troubleshooting attachments"),
        new("F-MAN-001", F, E, "Manifest is well-formed JSON with an object root", "Parseable JSON object", "SPEC §rsmf_manifest.json"),
        new("F-MAN-002", F, E, "Manifest version is a supported version", "1.0.0 or 2.0.0", "SPEC §rsmf_manifest.json; VALID schemas"),
        new("F-MAN-003", F, E, "Manifest validates against the bundled schema for its version", "No schema violations", "VALID rsmf_schema_1_0_0.json / rsmf_schema_2_0_0.json; VALID PDF"),
        new("F-MAN-004", F, W, "Property is not defined by the schema for this version", "Only defined properties", "VALID PDF Sample 4; SPEC field tables"),
        new("F-MAN-005", F, E, "At least one participant", ">= 1", "SPEC §Participants"),
        new("F-MAN-006", F, E, "At least one conversation", ">= 1", "SPEC §Conversations"),
        new("F-MAN-007", F, E, "At least one event", ">= 1", "SPEC §Events"),
        new("F-MAN-008", F, E, "Participant ids are unique", "No duplicates", "SPEC §Participants (id)"),
        new("F-MAN-009", F, E, "Conversation ids are unique", "No duplicates", "SPEC §Conversations (id)"),
        new("F-MAN-010", F, E, "Event ids are unique within their conversation", "No duplicates", "SPEC §Events (id)"),
        new("F-MAN-011", F, W, "Conversation type present in a 2.0.0 manifest", "direct or channel", "SPEC §Conversations (type)"),
        new("F-MAN-012", F, E, "Reaction value length", "<= 30 characters", "SPEC §Reaction (value)"),
        new("F-MAN-013", F, E, "Timestamp is an ISO 8601 date-time", "Parseable ISO 8601 date-time", "SPEC §Events, §Edits, §Read_receipts; VALID schemas"),
        new("F-REF-001", F, W, "Conversation participant maps to a root participant", "Id of a root participant", "SPEC §Conversations; VALID PDF Sample 4"),
        new("F-REF-002", F, W, "Event participant maps to a root participant", "Id of a root participant", "SPEC §Events; VALID PDF Sample 4"),
        new("F-REF-003", F, E, "Event conversation maps to a conversation", "Id of a conversation", "SPEC §Events (conversation)"),
        new("F-REF-004", F, W, "Event parent maps to another event in the same conversation", "Id of another event in the same conversation", "SPEC §Events (parent); VALID schema 2.0.0; SLICE §Orphaned messages"),
        new("F-REF-005", F, W, "Conversation custodian is one of the conversation's participants", "A participant of the conversation", "SPEC §Conversations (custodian)"),
        new("F-REF-006", F, W, "Reaction participant maps to a root participant", "Id of a root participant", "SPEC §Reaction"),
        new("F-REF-007", F, W, "Edit participant maps to a root participant", "Id of a root participant", "SPEC §Edits"),
        new("F-REF-008", F, W, "Read-receipt participant maps to a root participant", "Id of a root participant", "SPEC §Read_receipts"),
        new("F-REF-009", F, E, "Attachment id resolves to a file in rsmf.zip", "Name of a file in rsmf.zip", "SPEC §Attachment; PROC §Troubleshooting attachments"),
        new("F-REF-010", F, W, "Participant avatar resolves to a file in rsmf.zip", "Name of a file in rsmf.zip", "SPEC §Participants; VALID PDF Sample 2"),
        new("F-REF-011", F, W, "Conversation icon resolves to a file in rsmf.zip", "Name of a file in rsmf.zip", "SPEC §Conversations (icon)"),
        new("L-SIZE-001", L, E, "File size is within the processing size threshold", "Size <= threshold", "PROC §Checklist, §Troubleshooting; SLICE §Splitting to meet RSMF file limits; VLRW §RSMF; BRIEF"),
        new("L-EVT-001", L, E, "Total events are within the viewer limit", "<= 10000 events", "PROC §Checklist; SLICE §Splitting to meet RSMF file limits; CREATE; VLRW §RSMF"),
        new("Q-TIME-A", Q, E, "All message timestamps fall on one calendar day in the selected time zone", "One calendar day", "BRIEF Phase 1 Time-window QC, Rule A"),
        new("Q-TIME-B", Q, E, "Elapsed span between earliest and latest message", "<= 24 hours", "BRIEF Phase 1 Time-window QC, Rule B"),
        new("Q-TIME-001", Q, W, "Message timestamp has no UTC offset; treated as UTC", "Explicit offset or Z", "BRIEF Phase 1 Time-window QC"),
        new("Q-PART-001", Q, W, "Unique participant count is within the user-set threshold", "<= threshold", "BRIEF Phase 1 Limits; VLRW §RSMF"),
        new("Q-PART-002", Q, W, "No participant is the placeholder \"Unspecified Sender\"", "A real participant name", "Owner request 2026-11-19"),
        new("Q-PART-003", Q, W, "Every participant has a name, not only a phone number", "A display name, account name or email address", "Owner request 2026-11-19"),
        new("Q-VER-001", Q, W, "X-RSMF-Version header equals manifest version", "Equal", "BRIEF Phase 1 Parsing; GEN RSMFGenerator.cs"),
        new("I-FILE-001", I, E, "File could not be opened or read", "Readable", "BRIEF Phase 1 Folder scan"),
        new("I-SCAN-001", I, E, "Directory could not be listed; scan coverage incomplete", "Listable", "BRIEF Phase 1 Folder scan"),
        new("I-ZIP-001", I, W, "Content exceeded the in-memory size cap; dependent checks not run", "Within cap", "BRIEF Phase 1 Folder scan"),
        new("I-PARSE-001", I, N, "Checks skipped because an earlier layer failed", "All layers parse", "BRIEF Phase 1 Folder scan, Audit output"),
        new("I-TIME-001", I, W, "Time-window check is INDETERMINATE", "All message timestamps parseable", "BRIEF Phase 1 Time-window QC"),
        new("I-RUN-001", I, N, "Scan was cancelled before this file was checked", "Run completes", "BRIEF Phase 1 Folder scan"),
    };

    private static readonly Dictionary<string, RuleDefinition> ById = All.ToDictionary(r => r.Id);

    /// <summary>
    /// The number each rule is known by in the app and the reports ("004"). Numbers are permanent: a rule
    /// keeps its number for ever, a new rule takes the next free number, and a number is never reused.
    /// The letter codes are the original identifiers; they stay in the data for anyone comparing old reports.
    /// </summary>
    private static readonly (int Number, string Code)[] Numbers =
    {
        (1, "F-MIME-001"), (2, "F-MIME-002"), (3, "F-MIME-003"), (4, "F-MIME-004"), (5, "F-MIME-005"), (6, "F-MIME-006"),
        (7, "F-MIME-007"), (8, "F-MIME-008"), (9, "F-HDR-001"), (10, "F-HDR-002"), (11, "F-HDR-003"), (12, "F-HDR-004"),
        (13, "F-HDR-005"), (14, "F-ZIP-001"), (15, "F-ZIP-002"), (16, "F-MAN-001"), (17, "F-MAN-002"), (18, "F-MAN-003"),
        (19, "F-MAN-004"), (20, "F-MAN-005"), (21, "F-MAN-006"), (22, "F-MAN-007"), (23, "F-MAN-008"), (24, "F-MAN-009"),
        (25, "F-MAN-010"), (26, "F-MAN-011"), (27, "F-MAN-012"), (28, "F-MAN-013"), (29, "F-REF-001"), (30, "F-REF-002"),
        (31, "F-REF-003"), (32, "F-REF-004"), (33, "F-REF-005"), (34, "F-REF-006"), (35, "F-REF-007"), (36, "F-REF-008"),
        (37, "F-REF-009"), (38, "F-REF-010"), (39, "F-REF-011"), (40, "L-SIZE-001"), (41, "L-EVT-001"), (42, "Q-TIME-A"),
        (43, "Q-TIME-B"), (44, "Q-TIME-001"), (45, "Q-PART-001"), (46, "Q-PART-002"), (47, "Q-PART-003"), (48, "Q-VER-001"),
        (49, "I-FILE-001"), (50, "I-SCAN-001"), (51, "I-ZIP-001"), (52, "I-PARSE-001"), (53, "I-TIME-001"), (54, "I-RUN-001"),
    };

    private static readonly Dictionary<string, int> NumberByCode = Numbers.ToDictionary(n => n.Code, n => n.Number, StringComparer.Ordinal);
    private static readonly Dictionary<int, string> CodeByNumber = Numbers.ToDictionary(n => n.Number, n => n.Code);

    /// <summary>The rule's number, or 0 for an unknown code.</summary>
    public static int Number(string code) => NumberByCode.TryGetValue(code, out var n) ? n : 0;

    /// <summary>The rule's number as shown to people: three digits, for example "004".</summary>
    public static string Label(string code) => Number(code).ToString("000", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Finds a rule from what someone typed: its number ("4", "004", "rule 4") or its letter code in any case.</summary>
    public static bool TryResolve(string? text, out RuleDefinition rule)
    {
        rule = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (t.StartsWith("rule", StringComparison.OrdinalIgnoreCase)) t = t[4..].Trim();
        t = t.TrimStart('#');
        if (int.TryParse(t, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
            && CodeByNumber.TryGetValue(number, out var code))
        {
            rule = ById[code];
            return true;
        }
        return ById.TryGetValue(t.ToUpperInvariant(), out rule!);
    }

    public static RuleDefinition Get(string id) => ById[id];

    /// <summary>Creates a finding for a rule. <paramref name="detail"/> is appended to the rule title.</summary>
    public static Finding Make(string ruleId, string observed, string location, string recordId = "",
        string? detail = null, string? expected = null, string? sensitiveDetail = null)
    {
        var r = ById[ruleId];
        return new Finding
        {
            RuleId = r.Id,
            Category = r.Category,
            Severity = r.Severity,
            Message = detail is null ? r.Title : $"{r.Title}: {detail}",
            Observed = observed,
            Expected = expected ?? r.Expected,
            Source = r.Source,
            Location = location,
            RecordId = recordId,
            SensitiveDetail = sensitiveDetail,
        };
    }
}
