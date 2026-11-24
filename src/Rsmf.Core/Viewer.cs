using System.Text.Json;
using NodaTime;
using static Rsmf.Core.ManifestIndex;

namespace Rsmf.Core;

public sealed record AttachmentView(string Id, string Display, long? DeclaredSize, bool InArchive, bool IsInlineImage);

public sealed record ReactionView(string Value, int? Count)
{
    /// <summary>The reaction as shown: a known short name such as "thumbsup" appears as its emoji.</summary>
    public string Display => Count is > 1 ? $"{EmojiShortNames.ExpandReaction(Value)} \u00D7{Count}" : EmojiShortNames.ExpandReaction(Value);
}

/// <summary>One event prepared for display. All values are plain text.</summary>
public sealed class MessageView
{
    public required int EventIndex { get; init; }
    public required string RecordId { get; init; }
    public required string Type { get; init; }
    public required string SenderName { get; init; }
    public required string SenderId { get; init; }
    /// <summary>Timestamp in the selected zone, or the raw text when it cannot be parsed.</summary>
    public required string TimestampText { get; init; }
    public required string RawTimestamp { get; init; }
    public Instant? Instant { get; init; }
    public required string Body { get; init; }
    public bool Deleted { get; init; }
    public string ParentId { get; init; } = "";
    public IReadOnlyList<AttachmentView> Attachments { get; init; } = Array.Empty<AttachmentView>();
    public string Location => $"$.events[{EventIndex}]";

    /// <summary>True for events of type "message"; other events (join, leave, ...) are shown as notices.</summary>
    public bool IsMessage => Type == "message";
    /// <summary>Position of the sender in the manifest's participant list, or -1 when unknown. Used to give each sender a stable colour.</summary>
    public int SenderIndex { get; init; } = -1;
    /// <summary>Up to two initials of the sender's display name.</summary>
    public string SenderInitials { get; init; } = "?";
    /// <summary>ZIP member name of the sender's avatar when it is in the archive and named as an image; otherwise empty.</summary>
    public string SenderAvatar { get; init; } = "";
    /// <summary>Calendar day in the selected zone, e.g. "Tuesday, 3 March 2026"; empty without a usable timestamp.</summary>
    public string DayText { get; init; } = "";
    /// <summary>Time of day in the selected zone, e.g. "09:05:00"; empty without a usable timestamp.</summary>
    public string TimeText { get; init; } = "";
    public string Direction { get; init; } = "";
    /// <summary>True when the event is outgoing, or (with no direction) was sent by the conversation's custodian.</summary>
    public bool AlignRight { get; init; }
    public IReadOnlyList<ReactionView> Reactions { get; init; } = Array.Empty<ReactionView>();
    public int EditCount { get; init; }

    /// <summary>Number of occurrences of the search text in the body, sender name and attachment names.</summary>
    public int HitCount(string term) =>
        TextSearch.Count(Body, term) + TextSearch.Count(SenderName, term)
        + Attachments.Sum(a => Math.Max(TextSearch.Count(a.Display, term), TextSearch.Count(a.Id, term)));

    public bool Matches(string term) =>
        Body.Contains(term, StringComparison.OrdinalIgnoreCase)
        || SenderName.Contains(term, StringComparison.OrdinalIgnoreCase)
        || Attachments.Any(a => a.Display.Contains(term, StringComparison.OrdinalIgnoreCase) || a.Id.Contains(term, StringComparison.OrdinalIgnoreCase));
}

public sealed class ConversationView
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Platform { get; init; } = "";
    public string Type { get; init; } = "";
    /// <summary>Events in chronological order; events without a usable timestamp come last in file order.</summary>
    public List<MessageView> Messages { get; } = new();
    public string Summary => $"{Title}  ({Messages.Count} events)";
    /// <summary>Second line for lists: platform, type and event count.</summary>
    public string Detail => string.Join("  \u00B7  ",
        new[] { Platform, Type, $"{Messages.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} events" }.Where(s => s.Length > 0));
}

/// <summary>
/// A file opened for viewing: analysis result plus display-ready conversations. Shows whatever parsed
/// safely; <see cref="Unavailable"/> states what could not be read. Keeps the source open read-only until disposed.
/// </summary>
public sealed class ViewerDocument : IDisposable
{
    public const long MaxInlineImageBytes = 25L * 1024 * 1024;
    public const int MaxDisplayedTextChars = 5_000_000;
    public const string OrphanConversationId = "\u0000orphans";

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif" };

    private readonly RsmfDocument _doc;
    private readonly object _zipGate = new();

    public string Path => _doc.Path;
    public FileResult Result { get; }
    public IReadOnlyList<HeaderEntry> Headers => _doc.Headers;
    public string ExtractedText { get; }
    public string ManifestText { get; }
    public List<ConversationView> Conversations { get; } = new();
    /// <summary>Plain statements of what is unavailable for this file. Empty when everything parsed.</summary>
    public List<string> Unavailable { get; } = new();
    public string ZoneId { get; }

    private ViewerDocument(RsmfDocument doc, FileResult result, DateTimeZone zone)
    {
        _doc = doc;
        Result = result;
        ZoneId = zone.Id;
        ExtractedText = Limit(doc.BodyText ?? "", "extracted text");
        ManifestText = Limit(doc.ManifestText ?? "", "manifest");

        if (!doc.Readable) Unavailable.Add("The file could not be opened, so nothing is available.");
        else if (!doc.MimeParsed) Unavailable.Add("The file is not a parseable MIME message: headers, extracted text, manifest and conversations are unavailable.");
        else
        {
            if (doc.BodyText is null) Unavailable.Add("No extracted text (message body) could be read.");
            if (!doc.ZipOpened) Unavailable.Add("rsmf.zip could not be opened: the manifest, conversations and attachments are unavailable.");
            else if (doc.Manifest is null)
                Unavailable.Add(doc.ManifestText is null
                    ? "rsmf_manifest.json could not be read: conversations are unavailable."
                    : "rsmf_manifest.json is not valid JSON: conversations are unavailable. The raw text is shown on the manifest tab.");
        }

        if (doc.Manifest is not null) BuildConversations(doc.Manifest.RootElement, zone);
    }

    private string Limit(string text, string what)
    {
        if (text.Length <= MaxDisplayedTextChars) return text;
        Unavailable.Add($"The {what} is {text.Length:N0} characters long; only the first {MaxDisplayedTextChars:N0} are shown.");
        return text[..MaxDisplayedTextChars];
    }

    /// <summary>Opens and analyses a file for viewing. Call off the UI thread.</summary>
    public static ViewerDocument Load(string path, InspectionOptions options, CancellationToken ct)
    {
        using var culture = InvariantScope.Enter();
        var doc = RsmfDocument.Open(path, options, loadText: true, ct);
        try
        {
            var result = new FileResult { Path = path, RelativePath = System.IO.Path.GetFileName(path) };
            FileInspector.Analyze(doc, result, options, ct);
            return new ViewerDocument(doc, result, options.ResolveZone());
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    private void BuildConversations(JsonElement root, DateTimeZone zone)
    {
        var idx = Build(root);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in idx.Participants)
        {
            if (p.Id is null || names.ContainsKey(p.Id)) continue;
            var display = Str(p.El, "display");
            names[p.Id] = string.IsNullOrWhiteSpace(display) ? p.Id : display!;
        }
        var zipNames = new HashSet<string>(_doc.ZipMembers.Select(m => m.FullName), StringComparer.Ordinal);
        var senderIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var avatars = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in idx.Participants)
        {
            if (p.Id is null || senderIndex.ContainsKey(p.Id)) continue;
            senderIndex[p.Id] = p.Index;
            var avatar = Str(p.El, "avatar");
            if (!string.IsNullOrEmpty(avatar) && zipNames.Contains(avatar) && IsImageName(avatar)) avatars[p.Id] = avatar;
        }
        var custodians = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in idx.Conversations)
        {
            var custodian = Str(c.El, "custodian");
            if (c.Id is not null && custodian is not null) custodians.TryAdd(c.Id, custodian);
        }

        var byId = new Dictionary<string, ConversationView>(StringComparer.Ordinal);
        foreach (var c in idx.Conversations)
        {
            var id = c.Id ?? $"conversations[{c.Index}]";
            var display = Str(c.El, "display");
            var view = new ConversationView
            {
                Id = id,
                Title = string.IsNullOrWhiteSpace(display) ? id : display!,
                Platform = Str(c.El, "platform") ?? "",
                Type = Str(c.El, "type") ?? "",
            };
            Conversations.Add(view);
            if (c.Id is not null) byId.TryAdd(c.Id, view);
        }

        ConversationView? orphans = null;
        foreach (var e in idx.Events)
        {
            var attachments = new List<AttachmentView>();
            foreach (var (_, a) in Items(e.El, "attachments"))
            {
                var id = Str(a, "id") ?? "";
                var display = Str(a, "display");
                if (string.IsNullOrWhiteSpace(display)) display = id;
                long? size = Prop(a, "size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var n) ? n : null;
                var inArchive = id.Length > 0 && zipNames.Contains(id);
                attachments.Add(new AttachmentView(id, display!, size, inArchive, inArchive && IsImageName(display!)));
            }

            var reactions = new List<ReactionView>();
            foreach (var (_, r) in Items(e.El, "reactions"))
            {
                var value = Str(r, "value");
                if (string.IsNullOrEmpty(value)) continue;
                int? count = Prop(r, "count", out var rc) && rc.ValueKind == JsonValueKind.Number && rc.TryGetInt32(out var n2) ? n2 : null;
                reactions.Add(new ReactionView(value, count));
            }
            var direction = Str(e.El, "direction") ?? "";
            var senderName = e.Participant is null ? "(no participant)" : names.GetValueOrDefault(e.Participant, e.Participant + " (unknown participant)");
            var local = e.TimestampState == TimestampState.Ok ? e.Timestamp.Instant.InZone(zone) : (ZonedDateTime?)null;

            var raw = Str(e.El, "timestamp") ?? "";
            var message = new MessageView
            {
                SenderIndex = e.Participant is not null && senderIndex.TryGetValue(e.Participant, out var si) ? si : -1,
                SenderInitials = Initials(e.Participant is not null && names.ContainsKey(e.Participant) ? senderName : e.Participant ?? ""),
                SenderAvatar = e.Participant is not null && avatars.TryGetValue(e.Participant, out var av) ? av : "",
                DayText = local?.ToString("dddd, d MMMM uuuu", System.Globalization.CultureInfo.InvariantCulture) ?? "",
                TimeText = local?.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) ?? "",
                Direction = direction,
                AlignRight = direction == "outgoing"
                             || (direction.Length == 0 && e.Participant is not null && e.Conversation is not null
                                 && custodians.TryGetValue(e.Conversation, out var cust) && cust == e.Participant),
                Reactions = reactions,
                EditCount = Items(e.El, "edits").Count(),
                EventIndex = e.Index,
                RecordId = e.RecordId,
                Type = e.Type ?? "(no type)",
                SenderId = e.Participant ?? "",
                SenderName = senderName,
                RawTimestamp = raw,
                Instant = e.TimestampState == TimestampState.Ok ? e.Timestamp.Instant : null,
                TimestampText = e.TimestampState switch
                {
                    TimestampState.Ok => e.Timestamp.Instant.InZone(zone).ToString("uuuu-MM-dd HH:mm:ss o<+HH:mm>", System.Globalization.CultureInfo.InvariantCulture)
                                         + (e.Timestamp.HasOffset ? "" : "  (no offset in file; read as UTC)"),
                    TimestampState.Invalid => raw + "  (unparseable)",
                    _ => "(no timestamp)",
                },
                Body = Str(e.El, "body") ?? "",
                Deleted = Prop(e.El, "deleted", out var d) && d.ValueKind == JsonValueKind.True,
                ParentId = e.Parent ?? "",
                Attachments = attachments,
            };

            if (e.Conversation is not null && byId.TryGetValue(e.Conversation, out var target)) target.Messages.Add(message);
            else
            {
                orphans ??= new ConversationView { Id = OrphanConversationId, Title = "(events without a valid conversation)" };
                orphans.Messages.Add(message);
            }
        }
        if (orphans is not null) Conversations.Add(orphans);

        foreach (var c in Conversations)
        {
            var ordered = c.Messages
                .OrderBy(m => m.Instant.HasValue ? 0 : 1)
                .ThenBy(m => m.Instant ?? NodaTime.Instant.MinValue)
                .ThenBy(m => m.EventIndex)
                .ToList();
            c.Messages.Clear();
            c.Messages.AddRange(ordered);
        }
    }

    internal static string Initials(string name)
    {
        var parts = name.Split(new[] { ' ', '.', '_', '-', '@' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => char.IsLetter(p[0])).Take(2).ToArray();
        if (parts.Length > 0) return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0])));
        return name.Any(char.IsDigit) ? "#" : "?";
    }

    /// <summary>Returns the bytes of a sender's avatar image, or null when absent, too large or not image data.</summary>
    public byte[]? ReadAvatar(string zipName)
    {
        if (string.IsNullOrEmpty(zipName) || !IsImageName(zipName)) return null;
        byte[]? bytes;
        lock (_zipGate) bytes = _doc.ReadMember(zipName, MaxInlineImageBytes);
        return bytes is not null && LooksLikeImage(bytes) ? bytes : null;
    }

    internal static bool IsImageName(string name) =>
        ImageExtensions.Any(x => name.EndsWith(x, StringComparison.OrdinalIgnoreCase));

    /// <summary>Messages of a conversation that contain the search text (body, sender or attachment name).</summary>
    public static IReadOnlyList<MessageView> Search(ConversationView conversation, string? term) =>
        string.IsNullOrWhiteSpace(term) ? conversation.Messages : conversation.Messages.Where(m => m.Matches(term.Trim())).ToList();

    /// <summary>Finds the conversation and message for a finding location such as <c>$.events[12].participant</c>.</summary>
    public (ConversationView Conversation, MessageView Message)? FindEvent(string location)
    {
        const string prefix = "$.events[";
        if (!location.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var end = location.IndexOf(']', prefix.Length);
        if (end < 0 || !int.TryParse(location.AsSpan(prefix.Length, end - prefix.Length), out var index)) return null;
        foreach (var c in Conversations)
        {
            var m = c.Messages.FirstOrDefault(x => x.EventIndex == index);
            if (m is not null) return (c, m);
        }
        return null;
    }

    /// <summary>Finds a message by the record identifier used in findings and coverage (e.g. <c>event:m2 (events[1])</c>).</summary>
    public (ConversationView Conversation, MessageView Message)? FindRecord(string recordId)
    {
        foreach (var c in Conversations)
        {
            var m = c.Messages.FirstOrDefault(x => x.RecordId == recordId);
            if (m is not null) return (c, m);
        }
        return null;
    }

    /// <summary>
    /// Returns the bytes of an inline image attachment, or null when it is absent, too large, or its
    /// content is not actually PNG, JPEG or GIF data. Attachments are never opened or executed.
    /// </summary>
    public byte[]? ReadInlineImage(AttachmentView attachment)
    {
        if (!attachment.IsInlineImage) return null;
        byte[]? bytes;
        lock (_zipGate) bytes = _doc.ReadMember(attachment.Id, MaxInlineImageBytes);
        return bytes is not null && LooksLikeImage(bytes) ? bytes : null;
    }

    internal static bool LooksLikeImage(byte[] b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return true; // PNG
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return true;                                                                                // JPEG
        if (b.Length >= 6 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38 && (b[4] == 0x37 || b[4] == 0x39) && b[5] == 0x61) return true;              // GIF87a / GIF89a
        return false;
    }

    public void Dispose()
    {
        lock (_zipGate) _doc.Dispose();
    }
}
