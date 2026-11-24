using System.Globalization;
using System.Text.Json;
using NodaTime;
using static Rsmf.Core.ManifestIndex;

namespace Rsmf.Core;

/// <summary>Runs every rule, metric and QC check against one file.</summary>
public static class FileInspector
{
    public static readonly string[] StandardRsmfHeaders =
    {
        "X-RSMF-Version", "X-RSMF-Generator", "X-RSMF-BeginDate", "X-RSMF-EndDate", "X-RSMF-EventCount",
        "X-RSMF-Application", "X-RSMF-Custodian", "X-RSMF-Participants", "X-RSMF-AttachmentCount",
        "X-RSMF-EventCollectionID",
    };

    /// <summary>Headers Relativity itself writes that are not custom (DEDUP §Deduplication for short message formats).</summary>
    public static readonly string[] OtherKnownRsmfHeaders = { "X-RSMF-DuplicatedEventCount" };

    public static readonly string[] V2OnlyHeaders =
    {
        "X-RSMF-Application", "X-RSMF-Custodian", "X-RSMF-Participants", "X-RSMF-AttachmentCount",
        "X-RSMF-EventCollectionID",
    };

    private const int MaxListed = 50;

    public static FileResult Inspect(string path, string relativePath, InspectionOptions options, CancellationToken ct)
    {
        using var culture = InvariantScope.Enter();
        var result = new FileResult { Path = path, RelativePath = relativePath };
        using var doc = RsmfDocument.Open(path, options, loadText: false, ct);
        Analyze(doc, result, options, ct);
        return result;
    }

    /// <summary>Analyses an already opened document (the GUI keeps the document for viewing).</summary>
    public static void Analyze(RsmfDocument doc, FileResult result, InspectionOptions options, CancellationToken ct)
    {
        result.Findings.AddRange(doc.Findings);
        result.SkippedChecks.AddRange(doc.SkippedChecks);
        result.SizeBytes = doc.FileSize;
        result.Metrics.SizeBytes = doc.FileSize;
        result.TimeWindow.ZoneId = options.ResolveZone().Id;
        if (!doc.Readable)
        {
            try { result.SizeBytes = result.Metrics.SizeBytes = new FileInfo(doc.Path).Length; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            result.Checked = false;
            return;
        }
        result.Checked = true;

        var ext = Path.GetExtension(doc.Path);
        if (!string.Equals(ext, ".rsmf", StringComparison.OrdinalIgnoreCase))
            result.Findings.Add(RuleCatalog.Make("F-MIME-007", ext.Length == 0 ? "(no extension)" : ext, "(file name)"));

        if (doc.FileSize > options.SizeThresholdBytes)
            result.Findings.Add(RuleCatalog.Make("L-SIZE-001", $"{doc.FileSize} bytes", "(file)",
                expected: $"<= {options.SizeThresholdBytes} bytes"));

        if (!doc.MimeParsed) return;

        var headerVersion = doc.GetHeader("X-RSMF-Version");
        result.RsmfVersion = headerVersion ?? "";
        CheckHeaders(doc, result, headerVersion);

        ManifestIndex? idx = null;
        if (doc.Manifest is not null)
        {
            ct.ThrowIfCancellationRequested();
            idx = Build(doc.Manifest.RootElement);
            if (string.IsNullOrEmpty(result.RsmfVersion)) result.RsmfVersion = idx.Version ?? "";
            CheckManifest(doc, idx, result, options, headerVersion, ct);
            ComputeMetrics(idx, result, options);
            CheckHeadersAgainstManifest(doc, idx, result);
            ct.ThrowIfCancellationRequested();
            CheckTimeWindow(idx, result, options);
        }
        else
        {
            result.TimeWindow.Reason = "manifest not available";
        }

        Coverage.Build(doc, idx, result, options, headerVersion);
    }

    // ------------------------------------------------------------------ headers

    private static void CheckHeaders(RsmfDocument doc, FileResult result, string? headerVersion)
    {
        var f = result.Findings;
        if (string.IsNullOrWhiteSpace(headerVersion))
        {
            f.Add(RuleCatalog.Make("F-MIME-002", doc.Headers.Any(h => Eq(h.Name, "X-RSMF-Version")) ? "blank" : "missing", "X-RSMF-Version"));
        }
        else
        {
            if (!SchemaValidator.IsSupported(headerVersion))
                f.Add(RuleCatalog.Make("F-MIME-003", headerVersion, "X-RSMF-Version"));
            if (!Eq(doc.Headers[0].Name, "X-RSMF-Version"))
                f.Add(RuleCatalog.Make("F-MIME-004", "first header is " + doc.Headers[0].Name, "X-RSMF-Version"));
        }

        foreach (var h in doc.Headers)
        {
            if (!h.Name.StartsWith("X-RSMF-", StringComparison.OrdinalIgnoreCase)) continue;
            if (IsKnownRsmfHeader(h.Name)) continue;
            var custom = h.Name["X-RSMF-".Length..];
            if (custom.Length >= 255)
                f.Add(RuleCatalog.Make("F-MIME-008", $"{custom.Length} characters", h.Name[..40] + "..."));
        }

        foreach (var name in new[] { "X-RSMF-EventCount", "X-RSMF-AttachmentCount" })
        {
            var v = doc.GetHeader(name);
            if (!string.IsNullOrWhiteSpace(v) && !TryWholeNumber(v, out _))
                f.Add(RuleCatalog.Make("F-HDR-001", "not a whole number", name, expected: "Non-negative whole number"));
        }
        foreach (var name in new[] { "X-RSMF-BeginDate", "X-RSMF-EndDate" })
        {
            var v = doc.GetHeader(name);
            if (!string.IsNullOrWhiteSpace(v) && !TimestampParser.TryParse(v, out _))
                f.Add(RuleCatalog.Make("F-HDR-001", "not an ISO 8601 date-time", name, expected: "ISO 8601 date-time"));
        }

        if (headerVersion == "1.0.0")
        {
            foreach (var name in V2OnlyHeaders)
                if (doc.Headers.Any(h => Eq(h.Name, name)))
                    f.Add(RuleCatalog.Make("F-HDR-005", "present", name));
        }
    }

    private static void CheckHeadersAgainstManifest(RsmfDocument doc, ManifestIndex idx, FileResult result)
    {
        var f = result.Findings;
        var countText = doc.GetHeader("X-RSMF-EventCount");
        if (TryWholeNumber(countText, out var headerCount) && headerCount != idx.Events.Count)
            f.Add(RuleCatalog.Make("F-HDR-002", $"header {headerCount}, manifest {idx.Events.Count}", "X-RSMF-EventCount",
                expected: idx.Events.Count.ToString(CultureInfo.InvariantCulture)));

        var hasBegin = TimestampParser.TryParse(doc.GetHeader("X-RSMF-BeginDate"), out var begin);
        var hasEnd = TimestampParser.TryParse(doc.GetHeader("X-RSMF-EndDate"), out var end);
        if (!hasBegin && !hasEnd) return;

        if (idx.Events.Any(e => e.TimestampState == TimestampState.Invalid))
        {
            result.SkippedChecks.Add("F-HDR-003/F-HDR-004 (header dates vs manifest): one or more event timestamps are unparseable");
            return;
        }
        var stamps = idx.Events.Where(e => e.TimestampState == TimestampState.Ok).Select(e => e.Timestamp.Instant).ToList();
        if (stamps.Count == 0)
        {
            result.SkippedChecks.Add("F-HDR-003/F-HDR-004 (header dates vs manifest): no event has a timestamp");
            return;
        }
        var min = stamps.Min();
        var max = stamps.Max();
        if (hasBegin && begin.Instant != min)
            f.Add(RuleCatalog.Make("F-HDR-003", $"header {TimestampParser.Format(begin.Instant)}, earliest event {TimestampParser.Format(min)}",
                "X-RSMF-BeginDate", expected: TimestampParser.Format(min)));
        if (hasEnd && end.Instant != max)
            f.Add(RuleCatalog.Make("F-HDR-004", $"header {TimestampParser.Format(end.Instant)}, latest event {TimestampParser.Format(max)}",
                "X-RSMF-EndDate", expected: TimestampParser.Format(max)));
    }

    internal static bool IsKnownRsmfHeader(string name) =>
        StandardRsmfHeaders.Any(s => Eq(s, name)) || OtherKnownRsmfHeaders.Any(s => Eq(s, name));

    internal static bool TryWholeNumber(string? text, out long value)
    {
        value = 0;
        return !string.IsNullOrWhiteSpace(text)
               && long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ manifest

    private static void CheckManifest(RsmfDocument doc, ManifestIndex idx, FileResult result, InspectionOptions options,
        string? headerVersion, CancellationToken ct)
    {
        var f = result.Findings;
        var root = idx.Root;
        string? Sens(Func<string> build) => options.IncludeSensitive ? build() : null;

        // version
        var versionSupported = SchemaValidator.IsSupported(idx.Version);
        if (!versionSupported)
            f.Add(RuleCatalog.Make("F-MAN-002", idx.Version ?? (Prop(root, "version", out _) ? "not a string" : "missing"), "$.version"));
        if (!string.IsNullOrWhiteSpace(headerVersion) && idx.Version is not null && headerVersion != idx.Version)
            f.Add(RuleCatalog.Make("Q-VER-001", $"header {headerVersion}, manifest {idx.Version}", "X-RSMF-Version"));

        // schema
        if (versionSupported)
        {
            foreach (var v in SchemaValidator.Validate(idx.Version!, root))
                f.Add(RuleCatalog.Make("F-MAN-003", v.Keyword, v.Path, idx.RecordIdForPath(v.Path), detail: v.Message,
                    expected: $"Valid against rsmf_schema_{idx.Version!.Replace('.', '_')}.json"));
            foreach (var (path, name) in SchemaValidator.UndefinedProperties(idx.Version!, root))
                f.Add(RuleCatalog.Make("F-MAN-004", "property \"" + Trunc(name, 60) + "\"", path + "." + Trunc(name, 60), idx.RecordIdForPath(path)));
        }
        else
        {
            result.SkippedChecks.Add("F-MAN-003/F-MAN-004 (schema validation): manifest version is not 1.0.0 or 2.0.0");
            f.Add(RuleCatalog.Make("I-PARSE-001", "manifest version is not supported", "$.version", detail: "skipped schema validation"));
        }
        ct.ThrowIfCancellationRequested();

        // minimum content
        if (idx.Participants.Count == 0) f.Add(RuleCatalog.Make("F-MAN-005", "0 participants", "$.participants"));
        if (idx.Conversations.Count == 0) f.Add(RuleCatalog.Make("F-MAN-006", "0 conversations", "$.conversations"));
        if (idx.Events.Count == 0) f.Add(RuleCatalog.Make("F-MAN-007", "0 events", "$.events"));

        // uniqueness
        foreach (var g in idx.Participants.Where(p => p.Id is not null).GroupBy(p => p.Id!).Where(g => g.Count() > 1))
            f.Add(RuleCatalog.Make("F-MAN-008", $"id used by {g.Count()} participants", $"$.participants[{g.First().Index}].id", g.First().RecordId));
        foreach (var g in idx.Conversations.Where(c => c.Id is not null).GroupBy(c => c.Id!).Where(g => g.Count() > 1))
            f.Add(RuleCatalog.Make("F-MAN-009", $"id used by {g.Count()} conversations", $"$.conversations[{g.First().Index}].id", g.First().RecordId));
        var eventIdCounts = new Dictionary<(string Conv, string Id), int>();
        foreach (var e in idx.Events)
        {
            if (e.Id is null) continue;
            var key = (e.Conversation ?? "", e.Id);
            eventIdCounts[key] = eventIdCounts.GetValueOrDefault(key) + 1;
        }
        var reported = new HashSet<(string, string)>();
        foreach (var e in idx.Events)
        {
            if (e.Id is null) continue;
            var key = (e.Conversation ?? "", e.Id);
            if (eventIdCounts[key] > 1 && reported.Add(key))
                f.Add(RuleCatalog.Make("F-MAN-010", $"id used by {eventIdCounts[key]} events in the conversation", $"$.events[{e.Index}].id", e.RecordId));
        }

        // names of files in the archive
        var zipNames = new HashSet<string>(doc.ZipMembers.Select(m => m.FullName), StringComparer.Ordinal);
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        // participants
        foreach (var p in idx.Participants)
        {
            var avatar = Str(p.El, "avatar");
            if (string.IsNullOrEmpty(avatar)) continue;
            referenced.Add(avatar);
            if (!zipNames.Contains(avatar))
                f.Add(RuleCatalog.Make("F-REF-010", "file not found in rsmf.zip: " + Trunc(avatar, 120), $"$.participants[{p.Index}].avatar", p.RecordId,
                    sensitiveDetail: Sens(() => ParticipantDetail(p.El))));
        }

        // conversations
        foreach (var c in idx.Conversations)
        {
            var path = $"$.conversations[{c.Index}]";
            if (idx.Version == "2.0.0" && !Prop(c.El, "type", out _))
                f.Add(RuleCatalog.Make("F-MAN-011", "missing", path + ".type", c.RecordId));
            var members = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (i, m) in Items(c.El, "participants"))
            {
                if (m.ValueKind != JsonValueKind.String) continue;
                var id = m.GetString()!;
                members.Add(id);
                if (!idx.ParticipantIds.Contains(id))
                    f.Add(RuleCatalog.Make("F-REF-001", "no participant with id " + Trunc(id, 120), $"{path}.participants[{i}]", c.RecordId));
            }
            var custodian = Str(c.El, "custodian");
            if (custodian is not null && !members.Contains(custodian))
                f.Add(RuleCatalog.Make("F-REF-005", "custodian " + Trunc(custodian, 120) + " is not in the conversation's participants", path + ".custodian", c.RecordId));
            var icon = Str(c.El, "icon");
            if (!string.IsNullOrEmpty(icon))
            {
                referenced.Add(icon);
                if (!zipNames.Contains(icon))
                    f.Add(RuleCatalog.Make("F-REF-011", "file not found in rsmf.zip: " + Trunc(icon, 120), path + ".icon", c.RecordId));
            }
        }

        // events
        foreach (var e in idx.Events)
        {
            if ((e.Index & 0x3FF) == 0) ct.ThrowIfCancellationRequested();
            var path = $"$.events[{e.Index}]";
            string? Detail() => Sens(() => EventDetail(e.El));

            if (Prop(e.El, "timestamp", out var ts) && ts.ValueKind == JsonValueKind.String && e.TimestampState != TimestampState.Ok)
                f.Add(RuleCatalog.Make("F-MAN-013", string.IsNullOrWhiteSpace(ts.GetString()) ? "blank" : "not parseable as ISO 8601", path + ".timestamp", e.RecordId, sensitiveDetail: Detail()));

            if (e.Participant is not null && !idx.ParticipantIds.Contains(e.Participant))
                f.Add(RuleCatalog.Make("F-REF-002", "no participant with id " + Trunc(e.Participant, 120), path + ".participant", e.RecordId, sensitiveDetail: Detail()));
            if (e.Conversation is not null && !idx.ConversationIds.Contains(e.Conversation))
                f.Add(RuleCatalog.Make("F-REF-003", "no conversation with id " + Trunc(e.Conversation, 120), path + ".conversation", e.RecordId, sensitiveDetail: Detail()));
            if (e.Parent is not null)
            {
                var n = eventIdCounts.GetValueOrDefault((e.Conversation ?? "", e.Parent));
                if (e.Parent == e.Id) n--;
                if (n < 1)
                    f.Add(RuleCatalog.Make("F-REF-004", "no other event with id " + Trunc(e.Parent, 120) + " in the same conversation", path + ".parent", e.RecordId, sensitiveDetail: Detail()));
            }

            foreach (var (i, r) in Items(e.El, "reactions"))
            {
                var value = Str(r, "value");
                if (value is not null)
                {
                    var length = value.EnumerateRunes().Count();
                    if (length > 30)
                        f.Add(RuleCatalog.Make("F-MAN-012", $"{length} characters", $"{path}.reactions[{i}].value", e.RecordId, sensitiveDetail: Detail()));
                }
                foreach (var (j, rp) in Items(r, "participants"))
                    if (rp.ValueKind == JsonValueKind.String && !idx.ParticipantIds.Contains(rp.GetString()!))
                        f.Add(RuleCatalog.Make("F-REF-006", "no participant with id " + Trunc(rp.GetString()!, 120), $"{path}.reactions[{i}].participants[{j}]", e.RecordId, sensitiveDetail: Detail()));
            }

            foreach (var (i, a) in Items(e.El, "attachments"))
            {
                var id = Str(a, "id");
                if (string.IsNullOrEmpty(id)) continue;
                referenced.Add(id);
                if (!zipNames.Contains(id))
                    f.Add(RuleCatalog.Make("F-REF-009", "file not found in rsmf.zip: " + Trunc(id, 120), $"{path}.attachments[{i}].id", e.RecordId, sensitiveDetail: Detail()));
            }

            CheckSubRecords(e, "edits", "F-REF-007", idx, f, Detail);
            CheckSubRecords(e, "read_receipts", "F-REF-008", idx, f, Detail);
        }

        // unreferenced archive members
        foreach (var m in doc.ZipMembers)
        {
            if (m.FullName == RsmfDocument.ManifestName || referenced.Contains(m.FullName)) continue;
            f.Add(RuleCatalog.Make("F-ZIP-002", "unreferenced file: " + Trunc(m.FullName, 120), "rsmf.zip/" + Trunc(m.FullName, 120)));
        }
    }

    private static void CheckSubRecords(EventRec e, string array, string refRule, ManifestIndex idx, List<Finding> f, Func<string?> detail)
    {
        foreach (var (i, item) in Items(e.El, array))
        {
            var path = $"$.events[{e.Index}].{array}[{i}]";
            var p = Str(item, "participant");
            if (p is not null && !idx.ParticipantIds.Contains(p))
                f.Add(RuleCatalog.Make(refRule, "no participant with id " + Trunc(p, 120), path + ".participant", e.RecordId, sensitiveDetail: detail()));
            if (Prop(item, "timestamp", out var ts) && ts.ValueKind == JsonValueKind.String && !TimestampParser.TryParse(ts.GetString(), out _))
                f.Add(RuleCatalog.Make("F-MAN-013", string.IsNullOrWhiteSpace(ts.GetString()) ? "blank" : "not parseable as ISO 8601", path + ".timestamp", e.RecordId, sensitiveDetail: detail()));
        }
    }

    internal static string Trunc(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    internal static string ParticipantDetail(JsonElement el) =>
        $"display={Str(el, "display")}; email={Str(el, "email")}; account_id={Str(el, "account_id")}";

    internal static string EventDetail(JsonElement el) =>
        $"participant={Str(el, "participant")}; timestamp={Str(el, "timestamp")}; body={Trunc(Str(el, "body") ?? "", 500)}";

    // ------------------------------------------------------------------ metrics and limits

    private static void ComputeMetrics(ManifestIndex idx, FileResult result, InspectionOptions options)
    {
        var m = result.Metrics;
        m.TotalEvents = idx.Events.Count;
        m.MessageEvents = idx.Events.Count(e => e.IsMessage);
        m.ConversationCount = idx.Conversations.Count;
        m.UniqueParticipantCount = idx.ParticipantIds.Count + idx.Participants.Count(p => p.Id is null);

        var byId = new Dictionary<string, ConversationMetrics>(StringComparer.Ordinal);
        var members = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var c in idx.Conversations)
        {
            var cm = new ConversationMetrics { RecordId = c.RecordId, Index = c.Index };
            m.Conversations.Add(cm);
            if (c.Id is null || byId.ContainsKey(c.Id)) continue;
            byId[c.Id] = cm;
            var set = members[c.Id] = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (_, p) in Items(c.El, "participants"))
                if (p.ValueKind == JsonValueKind.String) set.Add(p.GetString()!);
        }
        foreach (var e in idx.Events)
        {
            if (e.Conversation is null || !byId.TryGetValue(e.Conversation, out var cm)) continue;
            cm.EventCount++;
            if (e.Participant is not null) members[e.Conversation].Add(e.Participant);
            if (!e.IsMessage) continue;
            cm.MessageCount++;
            if (e.TimestampState != TimestampState.Ok) continue;
            var t = e.Timestamp.Instant;
            if (cm.Earliest is null || t < cm.Earliest) cm.Earliest = t;
            if (cm.Latest is null || t > cm.Latest) cm.Latest = t;
        }
        foreach (var (id, cm) in byId) cm.ParticipantCount = members[id].Count;

        if (idx.Events.Count > RuleCatalog.EventLimit)
            result.Findings.Add(RuleCatalog.Make("L-EVT-001", $"{idx.Events.Count} events", "$.events"));

        if (options.ParticipantWarningThreshold is int limit && m.UniqueParticipantCount > limit)
            result.Findings.Add(RuleCatalog.Make("Q-PART-001", $"{m.UniqueParticipantCount} participants", "$.participants",
                expected: $"<= {limit} participants"));

        CheckParticipantNames(idx, result, options);
    }

    /// <summary>The placeholder some export tools write when they could not work out who sent a message.</summary>
    internal const string UnspecifiedSender = "Unspecified Sender";

    /// <summary>
    /// Q-PART-002: a participant whose display name, account name or id is the "Unspecified Sender" placeholder.
    /// Q-PART-003: a participant identified by a phone number alone: no email, and nothing in the display
    /// name or account name except (at most) a phone number. One finding per participant.
    /// </summary>
    private static void CheckParticipantNames(ManifestIndex idx, FileResult result, InspectionOptions options)
    {
        foreach (var p in idx.Participants)
        {
            var display = (Str(p.El, "display") ?? "").Trim();
            var account = (Str(p.El, "account_id") ?? "").Trim();
            var email = (Str(p.El, "email") ?? "").Trim();
            var id = (p.Id ?? "").Trim();
            var path = $"$.participants[{p.Index}]";
            string? Sensitive() => options.IncludeSensitive ? ParticipantDetail(p.El) : null;

            var placeholderIn = new[] { ("display", display), ("account_id", account), ("id", id) }
                .Where(v => v.Item2.Contains(UnspecifiedSender, StringComparison.OrdinalIgnoreCase)).Select(v => v.Item1).ToList();
            if (placeholderIn.Count > 0)
            {
                result.Findings.Add(RuleCatalog.Make("Q-PART-002", $"\"{UnspecifiedSender}\" in {string.Join(", ", placeholderIn)}", path, p.RecordId,
                    sensitiveDetail: Sensitive()));
                continue;
            }

            var displayIsPhone = LooksLikePhoneNumber(display);
            var accountIsPhone = LooksLikePhoneNumber(account);
            var named = (display.Length > 0 && !displayIsPhone) || (account.Length > 0 && !accountIsPhone) || email.Length > 0;
            if (named) continue;
            var phoneIn = new List<string>();
            if (displayIsPhone) phoneIn.Add("display");
            if (accountIsPhone) phoneIn.Add("account_id");
            if (phoneIn.Count == 0 && LooksLikePhoneNumber(id)) phoneIn.Add("id");
            if (phoneIn.Count == 0) continue;       // no name and no phone number either: the schema and coverage rules cover that
            result.Findings.Add(RuleCatalog.Make("Q-PART-003", "phone number only (in " + string.Join(", ", phoneIn) + "); no name or email", path, p.RecordId,
                sensitiveDetail: Sensitive()));
        }
    }

    /// <summary>
    /// True for text that is a phone number and nothing else: 7 to 15 digits with only spaces, + - ( ) . /
    /// between them, optionally after "tel:" and optionally followed by an extension such as "x123" or "ext. 123".
    /// </summary>
    internal static bool LooksLikePhoneNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) s = s[4..].Trim();
        var ext = System.Text.RegularExpressions.Regex.Match(s, @"\s*(?:x|ext\.?|extension)\s*\d{1,6}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (ext.Success) s = s[..ext.Index];
        var digits = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c is >= '0' and <= '9') digits++;
            else if (c == '+') { if (i != 0) return false; }
            else if (c is not (' ' or '-' or '(' or ')' or '.' or '/' or '\u00A0')) return false;
        }
        return digits is >= 7 and <= 15;
    }

    // ------------------------------------------------------------------ time window

    private static void CheckTimeWindow(ManifestIndex idx, FileResult result, InspectionOptions options)
    {
        var tw = result.TimeWindow;
        var zone = options.ResolveZone();
        tw.ZoneId = zone.Id;
        tw.RuleAChecked = options.TimeWindowMode == TimeWindowMode.Both;

        var messages = idx.Events.Where(e => e.IsMessage).ToList();
        if (messages.Count == 0)
        {
            tw.Status = TimeWindowStatus.NoMessages;
            tw.Reason = "no message events";
            return;
        }

        string? firstOffsetless = null;
        var dates = new SortedSet<LocalDate>();
        foreach (var e in messages)
        {
            if (e.TimestampState != TimestampState.Ok)
            {
                tw.IndeterminateLocations.Add($"$.events[{e.Index}].timestamp ({(e.TimestampState == TimestampState.Missing ? "missing" : "unparseable")})");
                continue;
            }
            if (!e.Timestamp.HasOffset)
            {
                tw.OffsetlessCount++;
                firstOffsetless ??= $"$.events[{e.Index}].timestamp";
            }
            var t = e.Timestamp.Instant;
            if (tw.Earliest is null || t < tw.Earliest) tw.Earliest = t;
            if (tw.Latest is null || t > tw.Latest) tw.Latest = t;
            dates.Add(t.InZone(zone).Date);
        }
        foreach (var d in dates) tw.LocalDates.Add(d.ToString("uuuu-MM-dd", CultureInfo.InvariantCulture));

        if (tw.OffsetlessCount > 0)
            result.Findings.Add(RuleCatalog.Make("Q-TIME-001", $"{tw.OffsetlessCount} message timestamp(s) without an offset", firstOffsetless!));

        if (tw.IndeterminateLocations.Count > 0)
        {
            tw.Status = TimeWindowStatus.Indeterminate;
            tw.Reason = $"INDETERMINATE: {tw.IndeterminateLocations.Count} message event(s) with a missing or unparseable timestamp";
            var listed = string.Join("; ", tw.IndeterminateLocations.Take(MaxListed));
            if (tw.IndeterminateLocations.Count > MaxListed) listed += $"; (+{tw.IndeterminateLocations.Count - MaxListed} more)";
            result.Findings.Add(RuleCatalog.Make("I-TIME-001", listed, tw.IndeterminateLocations[0].Split(' ')[0],
                detail: $"{tw.IndeterminateLocations.Count} message event(s) affected"));
            return;
        }

        var reasons = new List<string>();
        if (tw.RuleAChecked && dates.Count > 1)
        {
            tw.RuleAFailed = true;
            var shown = string.Join(", ", tw.LocalDates.Take(10)) + (tw.LocalDates.Count > 10 ? ", ..." : "");
            var observed = $"{dates.Count} calendar days in {zone.Id}: {shown}";
            reasons.Add("Rule A: " + observed);
            result.Findings.Add(RuleCatalog.Make("Q-TIME-A", observed, "$.events", expected: $"One calendar day in {zone.Id}"));
        }
        var span = tw.Span!.Value;
        if (span > Duration.FromHours(24))
        {
            tw.RuleBFailed = true;
            var observed = $"span {TimestampParser.FormatDuration(span)} (h:mm:ss)";
            reasons.Add("Rule B: " + observed);
            result.Findings.Add(RuleCatalog.Make("Q-TIME-B", observed, "$.events"));
        }
        tw.Status = reasons.Count > 0 ? TimeWindowStatus.Fail : TimeWindowStatus.Pass;
        tw.Reason = string.Join(" | ", reasons);
    }
}
