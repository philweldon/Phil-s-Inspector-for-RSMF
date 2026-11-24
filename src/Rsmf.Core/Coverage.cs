using System.Text.Json;
using static Rsmf.Core.ManifestIndex;

namespace Rsmf.Core;

internal enum FieldKind { String, Array, Bool, WholeNumber, Timestamp, Enum, Email, Version }

/// <summary>An expected field. Req10/Req20: 'R' required, 'O' optional, '-' not part of that version (SPEC tables).</summary>
internal sealed record FieldSpec(string Name, FieldKind Kind, char Req10, char Req20, string[]? Values = null, string[]? Values20 = null);

/// <summary>Metadata coverage: missing / blank / populated / invalid per expected field.</summary>
internal static class Coverage
{
    private const int MaxIds = 25;

    private static readonly string[] EventTypes10 = { "message", "disclaimer", "join", "leave" };
    private static readonly string[] EventTypes20 = { "message", "disclaimer", "join", "leave", "history", "unknown" };

    internal static readonly FieldSpec[] FileFields =
    {
        new("version", FieldKind.Version, 'R', 'R'),
        new("participants", FieldKind.Array, 'R', 'R'),
        new("conversations", FieldKind.Array, 'R', 'R'),
        new("events", FieldKind.Array, 'R', 'R'),
        new("eventcollectionid", FieldKind.String, '-', 'O'),
    };

    internal static readonly FieldSpec[] ParticipantFields =
    {
        new("id", FieldKind.String, 'R', 'R'),
        new("display", FieldKind.String, 'R', 'O'),
        new("avatar", FieldKind.String, 'O', 'O'),
        new("email", FieldKind.Email, 'O', 'O'),
        new("account_id", FieldKind.String, '-', 'O'),
        new("custom", FieldKind.Array, 'O', 'O'),
    };

    internal static readonly FieldSpec[] ConversationFields =
    {
        new("id", FieldKind.String, 'R', 'R'),
        new("display", FieldKind.String, 'R', 'O'),
        new("platform", FieldKind.String, 'R', 'R'),
        new("type", FieldKind.Enum, 'R', 'R', new[] { "direct", "channel" }),
        new("participants", FieldKind.Array, 'R', 'R'),
        new("custodian", FieldKind.String, '-', 'O'),
        new("custom", FieldKind.Array, 'O', 'O'),
        new("icon", FieldKind.String, '-', 'O'),
    };

    internal static readonly FieldSpec[] EventFields =
    {
        new("id", FieldKind.String, 'O', 'O'),
        new("type", FieldKind.Enum, 'R', 'R', EventTypes10, EventTypes20),
        new("parent", FieldKind.String, 'O', 'O'),
        new("body", FieldKind.String, 'O', 'O'),
        new("participant", FieldKind.String, 'R', 'O'),
        new("timestamp", FieldKind.Timestamp, 'R', 'O'),
        new("conversation", FieldKind.String, 'R', 'O'),
        new("deleted", FieldKind.Bool, 'O', 'O'),
        new("importance", FieldKind.Enum, 'O', 'O', new[] { "normal", "high" }),
        new("reactions", FieldKind.Array, 'O', 'O'),
        new("attachments", FieldKind.Array, 'O', 'O'),
        new("edits", FieldKind.Array, 'O', 'O'),
        new("custom", FieldKind.Array, 'O', 'O'),
        new("direction", FieldKind.Enum, '-', 'O', new[] { "incoming", "outgoing" }),
        new("read_receipts", FieldKind.Array, '-', 'O'),
    };

    private static readonly (string Name, FieldKind Kind, char Req10, char Req20)[] HeaderFields =
    {
        ("X-RSMF-Version", FieldKind.Version, 'R', 'R'),
        ("X-RSMF-Generator", FieldKind.String, 'O', 'O'),
        ("X-RSMF-BeginDate", FieldKind.Timestamp, 'O', 'O'),
        ("X-RSMF-EndDate", FieldKind.Timestamp, 'O', 'O'),
        ("X-RSMF-EventCount", FieldKind.WholeNumber, 'O', 'O'),
        ("X-RSMF-Application", FieldKind.String, '-', 'O'),
        ("X-RSMF-Custodian", FieldKind.String, '-', 'O'),
        ("X-RSMF-Participants", FieldKind.String, '-', 'O'),
        ("X-RSMF-AttachmentCount", FieldKind.WholeNumber, '-', 'O'),
        ("X-RSMF-EventCollectionID", FieldKind.String, '-', 'O'),
        ("From", FieldKind.String, 'O', 'O'),
        ("To", FieldKind.String, 'O', 'O'),
        ("Subject", FieldKind.String, 'O', 'O'),
        ("Date", FieldKind.String, 'O', 'O'),
    };

    public static void Build(RsmfDocument doc, ManifestIndex? idx, FileResult result, InspectionOptions options, string? headerVersion)
    {
        var version = SchemaValidator.IsSupported(idx?.Version) ? idx!.Version!
            : SchemaValidator.IsSupported(headerVersion) ? headerVersion! : "2.0.0";
        var v1 = version == "1.0.0";
        var rows = result.Coverage;

        // outer headers
        foreach (var (name, kind, r10, r20) in HeaderFields)
        {
            var row = new CoverageRow { Level = "header", Field = name, Requirement = Requirement(v1 ? r10 : r20), Records = 1 };
            var present = doc.Headers.Where(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (present.Count == 0) { row.Missing = 1; }
            else
            {
                var value = present[0].Value;
                if (string.IsNullOrWhiteSpace(value)) row.Blank = 1;
                else if (!HeaderValid(kind, value)) row.Invalid = 1;
                else row.Populated = 1;
                if (options.IncludeSensitive && !string.IsNullOrWhiteSpace(value)) row.SampleValues.Add(FileInspector.Trunc(value, 80));
            }
            rows.Add(row);
        }
        foreach (var h in doc.Headers.Where(h => h.Name.StartsWith("X-RSMF-", StringComparison.OrdinalIgnoreCase) && !FileInspector.IsKnownRsmfHeader(h.Name))
                     .GroupBy(h => h.Name, StringComparer.OrdinalIgnoreCase))
        {
            var blank = string.IsNullOrWhiteSpace(h.First().Value);
            var row = new CoverageRow
            {
                Level = "header", Field = FileInspector.Trunc(h.Key, 80), Requirement = "custom", IsCustom = true, Records = 1,
                Blank = blank ? 1 : 0, Populated = blank ? 0 : 1,
            };
            if (options.IncludeSensitive && !blank) row.SampleValues.Add(FileInspector.Trunc(h.First().Value, 80));
            rows.Add(row);
        }

        if (idx is null) return;

        AddLevel(rows, "file", FileFields, new[] { ("manifest", idx.Root) }, v1, options);
        AddLevel(rows, "participant", ParticipantFields, idx.Participants.Select(p => (p.RecordId, p.El)), v1, options);
        AddLevel(rows, "conversation", ConversationFields, idx.Conversations.Select(c => (c.RecordId, c.El)), v1, options);
        AddLevel(rows, "event", EventFields, idx.Events.Select(e => (e.RecordId, e.El)), v1, options);
    }

    private static string Requirement(char c) => c switch { 'R' => "required", 'O' => "optional", _ => "not in this version" };

    private static bool HeaderValid(FieldKind kind, string value) => kind switch
    {
        FieldKind.Version => SchemaValidator.IsSupported(value),
        FieldKind.Timestamp => TimestampParser.TryParse(value, out _),
        FieldKind.WholeNumber => FileInspector.TryWholeNumber(value, out _),
        _ => true,
    };

    private static void AddLevel(List<CoverageRow> rows, string level, FieldSpec[] specs,
        IEnumerable<(string RecordId, JsonElement El)> records, bool v1, InspectionOptions options)
    {
        var levelRows = specs.Select(s => new CoverageRow { Level = level, Field = s.Name, Requirement = Requirement(v1 ? s.Req10 : s.Req20) }).ToArray();
        var custom = new Dictionary<string, CoverageRow>(StringComparer.Ordinal);

        foreach (var (recordId, el) in records)
        {
            for (var i = 0; i < specs.Length; i++)
            {
                var spec = specs[i];
                var row = levelRows[i];
                row.Records++;
                if (!Prop(el, spec.Name, out var value))
                {
                    row.Missing++;
                    AddId(row.MissingIds, recordId);
                    continue;
                }
                if (IsBlank(value))
                {
                    row.Blank++;
                    AddId(row.BlankIds, recordId);
                }
                else if (!IsValid(spec, value, v1))
                {
                    row.Invalid++;
                    AddId(row.InvalidIds, recordId);
                }
                else
                {
                    row.Populated++;
                    if (options.IncludeSensitive && row.SampleValues.Count < 3 && value.ValueKind == JsonValueKind.String)
                    {
                        var sample = FileInspector.Trunc(value.GetString()!, 80);
                        if (!row.SampleValues.Contains(sample)) row.SampleValues.Add(sample);
                    }
                }
            }

            foreach (var (_, c) in Items(el, "custom"))
            {
                var name = Str(c, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                var key = FileInspector.Trunc(name, 80);
                if (!custom.TryGetValue(key, out var row))
                    custom[key] = row = new CoverageRow { Level = level, Field = "custom:" + key, Requirement = "custom", IsCustom = true };
                row.Records++;
                if (!Prop(c, "value", out var v)) { row.Missing++; AddId(row.MissingIds, recordId); }
                else if (IsBlank(v)) { row.Blank++; AddId(row.BlankIds, recordId); }
                else if (v.ValueKind != JsonValueKind.String) { row.Invalid++; AddId(row.InvalidIds, recordId); }
                else
                {
                    row.Populated++;
                    if (options.IncludeSensitive && row.SampleValues.Count < 3)
                    {
                        var sample = FileInspector.Trunc(v.GetString()!, 80);
                        if (!row.SampleValues.Contains(sample)) row.SampleValues.Add(sample);
                    }
                }
            }
        }
        rows.AddRange(levelRows);
        rows.AddRange(custom.Values.OrderBy(r => r.Field, StringComparer.Ordinal));
    }

    private static void AddId(List<string> ids, string id)
    {
        // The list is capped; the count columns carry the full number.
        if (ids.Count < MaxIds) ids.Add(id);
    }

    /// <summary>Blank: null, empty or whitespace-only string, or empty collection. Zero and false are populated.</summary>
    internal static bool IsBlank(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Null => true,
        JsonValueKind.String => string.IsNullOrWhiteSpace(v.GetString()),
        JsonValueKind.Array => v.GetArrayLength() == 0,
        JsonValueKind.Object => !v.EnumerateObject().Any(),
        _ => false,
    };

    internal static bool IsValid(FieldSpec spec, JsonElement v, bool v1)
    {
        switch (spec.Kind)
        {
            case FieldKind.Array: return v.ValueKind == JsonValueKind.Array;
            case FieldKind.Bool: return v.ValueKind is JsonValueKind.True or JsonValueKind.False;
            case FieldKind.WholeNumber: return v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) && n >= 0;
        }
        if (v.ValueKind != JsonValueKind.String) return false;
        var s = v.GetString()!;
        return spec.Kind switch
        {
            FieldKind.Version => SchemaValidator.IsSupported(s),
            FieldKind.Timestamp => TimestampParser.TryParse(s, out _),
            FieldKind.Enum => ((v1 ? spec.Values : spec.Values20 ?? spec.Values) ?? Array.Empty<string>()).Contains(s),
            FieldKind.Email => LooksLikeEmail(s),
            _ => true,
        };
    }

    private static bool LooksLikeEmail(string s)
    {
        var at = s.IndexOf('@');
        return at > 0 && at < s.Length - 1 && at == s.LastIndexOf('@') && !s.Any(char.IsWhiteSpace);
    }
}
