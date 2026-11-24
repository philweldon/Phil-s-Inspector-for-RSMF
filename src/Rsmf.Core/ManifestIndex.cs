using System.Text.Json;

namespace Rsmf.Core;

internal enum TimestampState { Missing, Invalid, Ok }

internal sealed class ParticipantRec
{
    public required int Index { get; init; }
    public required JsonElement El { get; init; }
    public string? Id { get; init; }
    public string RecordId => Id is null ? $"participants[{Index}]" : $"participant:{Id}";
}

internal sealed class ConversationRec
{
    public required int Index { get; init; }
    public required JsonElement El { get; init; }
    public string? Id { get; init; }
    public string RecordId => Id is null ? $"conversations[{Index}]" : $"conversation:{Id}";
}

internal sealed class EventRec
{
    public required int Index { get; init; }
    public required JsonElement El { get; init; }
    public string? Id { get; init; }
    public string? Type { get; init; }
    public string? Conversation { get; init; }
    public string? Participant { get; init; }
    public string? Parent { get; init; }
    public TimestampState TimestampState { get; init; }
    public ParsedTimestamp Timestamp { get; init; }
    public bool IsMessage => Type == "message";
    public string RecordId => Id is null ? $"events[{Index}]" : $"event:{Id} (events[{Index}])";
}

/// <summary>Light index over the manifest JSON used by rules, metrics and coverage.</summary>
internal sealed class ManifestIndex
{
    public required JsonElement Root { get; init; }
    public string? Version { get; private set; }
    public List<ParticipantRec> Participants { get; } = new();
    public List<ConversationRec> Conversations { get; } = new();
    public List<EventRec> Events { get; } = new();
    public HashSet<string> ParticipantIds { get; } = new(StringComparer.Ordinal);
    public HashSet<string> ConversationIds { get; } = new(StringComparer.Ordinal);

    public static bool Prop(JsonElement el, string name, out JsonElement value)
    {
        value = default;
        return el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out value);
    }

    public static string? Str(JsonElement el, string name) =>
        Prop(el, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static IEnumerable<(int Index, JsonElement El)> Items(JsonElement el, string name)
    {
        if (!Prop(el, name, out var arr) || arr.ValueKind != JsonValueKind.Array) yield break;
        var i = 0;
        foreach (var item in arr.EnumerateArray()) yield return (i++, item);
    }

    public static ManifestIndex Build(JsonElement root)
    {
        var idx = new ManifestIndex { Root = root, Version = Str(root, "version") };
        foreach (var (i, el) in Items(root, "participants"))
        {
            var rec = new ParticipantRec { Index = i, El = el, Id = Str(el, "id") };
            idx.Participants.Add(rec);
            if (rec.Id is not null) idx.ParticipantIds.Add(rec.Id);
        }
        foreach (var (i, el) in Items(root, "conversations"))
        {
            var rec = new ConversationRec { Index = i, El = el, Id = Str(el, "id") };
            idx.Conversations.Add(rec);
            if (rec.Id is not null) idx.ConversationIds.Add(rec.Id);
        }
        foreach (var (i, el) in Items(root, "events"))
        {
            var state = TimestampState.Missing;
            ParsedTimestamp ts = default;
            if (Prop(el, "timestamp", out var t) && t.ValueKind != JsonValueKind.Null)
            {
                var text = t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                if (t.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(text)) state = TimestampState.Missing;
                else state = TimestampParser.TryParse(text, out ts) ? TimestampState.Ok : TimestampState.Invalid;
            }
            idx.Events.Add(new EventRec
            {
                Index = i, El = el, Id = Str(el, "id"), Type = Str(el, "type"),
                Conversation = Str(el, "conversation"), Participant = Str(el, "participant"),
                Parent = Str(el, "parent"), TimestampState = state, Timestamp = ts,
            });
        }
        return idx;
    }

    /// <summary>Maps a JSON path such as <c>$.events[12].type</c> to the record identifier.</summary>
    public string RecordIdForPath(string path)
    {
        foreach (var name in new[] { "participants", "conversations", "events" })
        {
            var prefix = "$." + name + "[";
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var end = path.IndexOf(']', prefix.Length);
            if (end < 0 || !int.TryParse(path.AsSpan(prefix.Length, end - prefix.Length), out var i)) return "";
            return name switch
            {
                "participants" => i < Participants.Count ? Participants[i].RecordId : $"participants[{i}]",
                "conversations" => i < Conversations.Count ? Conversations[i].RecordId : $"conversations[{i}]",
                _ => i < Events.Count ? Events[i].RecordId : $"events[{i}]",
            };
        }
        return "";
    }
}
