using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Rsmf.Core;
using static Rsmf.Tests.FixtureGenerator;

namespace Rsmf.Tests;

/// <summary>One demonstration file: a valid file with a single defect that raises one rule.</summary>
public sealed record Sample(string FileName, string RuleId, string WhatIsWrong, byte[] Content)
{
    /// <summary>Other rules that unavoidably fire alongside the demonstrated one.</summary>
    public string[] AlsoExpected { get; init; } = Array.Empty<string>();
    /// <summary>Setting the sample needs, if any (shown in the index).</summary>
    public string Setting { get; init; } = "";
    public int? ParticipantThreshold { get; init; }
}

/// <summary>
/// The demonstration set: two clean files and one file per rule that a file's content can trigger.
/// The cast is borrowed from well-known cartoons and comics as a parody; every message is made up and
/// no real person appears. The story only changes names and message text, never the structure a rule checks.
/// </summary>
public static class SampleSet
{
    public const string Zone = "America/New_York";
    /// <summary>The participant warning threshold the Q-PART-001 sample is built for.</summary>
    public const int ParticipantThreshold = 5;

    private static JsonObject Ev(JsonObject m, int i) => (JsonObject)m["events"]![i]!;
    private static JsonObject Part(JsonObject m, int i) => (JsonObject)m["participants"]![i]!;
    private static JsonObject Conv(JsonObject m, int i) => (JsonObject)m["conversations"]![i]!;

    private static JsonObject Clean(string version)
    {
        var m = ValidManifest(version);
        Ev(m, 0)["body"] = "Bruce, a reporter named Clark Kent keeps asking why the money bin has a diving board.";
        Ev(m, 1)["body"] = "As your general counsel: stop swimming in the evidence.";
        Ev(m, 2)["body"] = "Sending you a photo of the vault. Strictly for liquidity purposes.";
        // The reporter is in the chat. Nobody has noticed.
        ((JsonArray)m["participants"]!).Add(Participant("P3", "Clark Kent", "clark.kent@dailyplanet.invalid"));
        ((JsonArray)Conv(m, 0)["participants"]!).Add("P3");
        return Dress(m);
    }

    // ---- the story ------------------------------------------------------------------------
    // Scrooge McDuck is accused of shady financial dealings. Bruce Wayne is his general counsel.
    // Clark Kent is the reporter. Everyone else is a witness of doubtful value.

    private static readonly string[] Cast = { "Scrooge McDuck", "Bruce Wayne", "Clark Kent", "Bugs Bunny", "Daffy Duck", "Porky Pig" };

    private static readonly string[] ScroogeLines =
    {
        "Bruce, a reporter named Clark Kent wants to know why my ledger has a chapter called Creative Nonfiction.",
        "For the record, the money bin is a savings account. It merely happens to have a diving board.",
        "Daffy Duck says he will testify. He is asking what the going rate is for the truth.",
        "Wile E. Coyote sold me an ACME audit kit. It exploded on first contact with the books.",
        "Porky Pig tried to read my disclosures aloud. We adjourned two hours into the first sentence.",
        "Is it insider trading if the insider is a duck and the trade was a sandwich?",
        "Foghorn Leghorn offered to be a character witness. I say, I say, I declined.",
        "Tweety thought he saw an auditor. He did. He did see an auditor.",
        "I asked Speedy Gonzales to move the funds quickly. I did not say offshore.",
        "Marvin the Martian wants his deposit back. He calls the fee structure earth-shattering.",
        "Elmer Fudd has been hunting for the missing wecords all season.",
        "I prefer the term fowl play. It tests better with juries.",
    };

    private static readonly string[] BruceLines =
    {
        "As your general counsel, I advise you to stop putting things in writing. Starting one message ago.",
        "A diving board is not a recognised accounting instrument. I checked. Twice. At night.",
        "Please stop telling people your assets are liquid. Kent has started bringing a snorkel.",
        "I asked Bugs Bunny for the records. He asked what was up, then left through a hole in the floor.",
        "We will plead the fifth. Sylvester has already pleaded the fifth, sixth and seventh, wetly.",
        "No comment. That is also my comment on your last comment.",
        "Kent keeps taking his glasses off and asking the same question. I do not see how that helps him.",
        "The Tasmanian Devil shredded the files. Nobody instructed him to. It is simply how he is.",
        "Yosemite Sam is not the right choice for a calm, measured press statement.",
        "Pepe Le Pew says the whole deal stinks. For once I find his testimony credible.",
        "I keep a cave full of files on this. Do not ask where the cave is.",
        "My billing rate doubles every time you say the word quack.",
    };

    private static readonly Regex Placeholder = new(@"^(?:msg|Message) (\d+)$", RegexOptions.CultureInvariant);

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>
    /// Gives a manifest its cast and its lines: the fixture generator's placeholder names, addresses and
    /// "Message 1" bodies are replaced. Ids, timestamps, counts and every other field are left alone, and
    /// values a rule depends on (a phone number as a name, "Unspecified Sender") are not placeholders, so
    /// they stay. Running it twice changes nothing more.
    /// </summary>
    private static JsonObject Dress(JsonObject m)
    {
        if (m["participants"] is JsonArray people)
            foreach (var p in people.OfType<JsonObject>())
            {
                var display = Text(p["display"]);
                if (display == DisplayMarker) p["display"] = Cast[0];
                else if (display == "Blake Sample") p["display"] = Cast[1];
                else if (display == "Second P2") p["display"] = "Bruce Wayne (listed twice)";
                else if (display is not null && display.StartsWith("Person ", StringComparison.Ordinal)
                         && int.TryParse(display[7..], out var n) && n >= 1 && n <= Cast.Length) p["display"] = Cast[n - 1];

                var email = Text(p["email"]);
                if (email == EmailMarker) p["email"] = "scrooge.mcduck@moneybin.invalid";
                else if (email == "blake.sample@test.invalid") p["email"] = "bruce.wayne@waynelegal.invalid";

                if (p["custom"] is JsonArray custom)
                    foreach (var c in custom.OfType<JsonObject>())
                        if (Text(c["value"]) == "Engineering") c["value"] = "Money Bin Operations";
            }

        if (m["conversations"] is JsonArray conversations)
            foreach (var c in conversations.OfType<JsonObject>())
                if (Text(c["display"]) is { } name && name.StartsWith("Chat ", StringComparison.Ordinal))
                    c["display"] = "The McDuck matter (privileged)";

        if (m["events"] is JsonArray events)
            foreach (var e in events.OfType<JsonObject>())
            {
                if (Text(e["body"]) is not { } body) continue;
                var match = Placeholder.Match(body);
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out var number)) continue;
                var lines = Text(e["participant"]) == "P2" ? BruceLines : ScroogeLines;
                e["body"] = lines[number / 2 % lines.Length];
            }
        return m;
    }

    /// <summary>The fixture generator's wrapper, with the story's cast in the manifest and the outer headers.</summary>
    private static RsmfBuilder Wrap(JsonObject manifest, (string Begin, string End)? headerDates = null, params (string Name, byte[] Data)[] files) =>
        FixtureGenerator.Wrap(Dress(manifest), headerDates, files)
            .SetHeader("From", "Scrooge McDuck <scrooge.mcduck@moneybin.invalid>")
            .SetHeader("To", "Bruce Wayne <bruce.wayne@waynelegal.invalid>")
            .SetHeader("Subject", "Privileged and confidential (do not forward to Kent)");

    private static RsmfBuilder Good(string version) => Wrap(Clean(version), ValidDates, ("photo.png", Png), ("avatar.png", Png));

    /// <summary>The clean 2.0 file with one change to its manifest.</summary>
    private static byte[] Manifest20(Action<JsonObject> change, params (string Name, byte[] Data)[] files)
    {
        var m = Clean("2.0.0");
        change(m);
        return Wrap(m, ValidDates, files.Length > 0 ? files : new[] { ("photo.png", Png), ("avatar.png", Png) }).Build();
    }

    /// <summary>The clean 2.0 file with one change to its outer headers or MIME structure.</summary>
    private static byte[] Outer(Action<RsmfBuilder> change, string version = "2.0.0")
    {
        var b = Good(version);
        change(b);
        return b.Build();
    }

    private static byte[] Timed(params string?[] timestamps) =>
        Wrap(Manifest("2.0.0", timestamps.Select((t, i) => Message("C1", i % 2 == 0 ? "P1" : "P2", t, "Message " + (i + 1))))).Build();

    public static List<Sample> Build()
    {
        var s = new List<Sample>
        {
            new("00-good-rsmf-2.0.rsmf", "", "Nothing. A valid RSMF 2.0 file: no findings.", Good("2.0.0").Build()),
            new("00-good-rsmf-1.0.rsmf", "", "Nothing. A valid RSMF 1.0 file: no findings.", Good("1.0.0").Build()),
        };
        void Add(string rule, string slug, string what, byte[] content, string[]? also = null, string setting = "", int? participants = null, string ext = ".rsmf") =>
            s.Add(new Sample($"{rule}-{slug}{ext}", rule, what, content) { AlsoExpected = also ?? Array.Empty<string>(), Setting = setting, ParticipantThreshold = participants });

        // ---- outer file -------------------------------------------------------------------
        var rnd = new Random(7);
        var garbage = new byte[1024];
        rnd.NextBytes(garbage);
        garbage[0] = 0;
        Add("F-MIME-001", "not-a-mime-message", "The file is random bytes, not a MIME message.", garbage, new[] { "I-PARSE-001" });
        Add("F-MIME-002", "no-version-header", "The X-RSMF-Version header is missing.", Outer(b => b.RemoveHeader("X-RSMF-Version")));
        Add("F-MIME-003", "unsupported-version", "X-RSMF-Version is 3.0.0, which is not a supported version.", Outer(b => b.SetHeader("X-RSMF-Version", "3.0.0")), new[] { "Q-VER-001" });
        Add("F-MIME-004", "version-header-not-first", "X-RSMF-Version is present but is not the first header line.", Outer(b => b.RemoveHeader("X-RSMF-Version").Header("X-RSMF-Version", "2.0.0")));
        Add("F-MIME-005", "second-attachment", "The file has two attachments; only rsmf.zip is allowed.", Outer(b => b.ExtraAttachments.Add(("other.zip", RsmfBuilder.MakeZip(null, ("readme.txt", Encoding.UTF8.GetBytes("extra")))))));
        Add("F-MIME-006", "truncated-zip", "The rsmf.zip attachment is cut off half-way, so it is not a readable ZIP.", Outer(b => b.Zip = b.Zip!.Take(b.Zip!.Length / 2).ToArray()), new[] { "I-PARSE-001" });
        Add("F-MIME-007", "wrong-extension", "A valid RSMF saved with an .eml extension. A folder scan lists it as \"not an RSMF candidate\"; use Open RSMF File to see the finding.", Good("2.0.0").Build(), ext: ".eml");
        Add("F-MIME-008", "custom-header-name-too-long", "A custom X-RSMF-* header whose name is 255 characters long.", Outer(b => b.Header("X-RSMF-" + new string('a', 255), "x")));
        Add("F-HDR-001", "event-count-not-a-number", "X-RSMF-EventCount is \"three\" instead of a whole number.", Outer(b => b.SetHeader("X-RSMF-EventCount", "three")));
        Add("F-HDR-002", "event-count-mismatch", "X-RSMF-EventCount says 99 but the manifest has 3 events.", Outer(b => b.SetHeader("X-RSMF-EventCount", "99")));
        Add("F-HDR-003", "begin-date-mismatch", "X-RSMF-BeginDate is an hour earlier than the earliest event.", Outer(b => b.SetHeader("X-RSMF-BeginDate", "2026-03-03T13:00:00Z")));
        Add("F-HDR-004", "end-date-mismatch", "X-RSMF-EndDate is an hour later than the latest event.", Outer(b => b.SetHeader("X-RSMF-EndDate", "2026-03-03T16:30:00Z")));
        Add("F-HDR-005", "v2-header-in-v1-file", "An RSMF 1.0 file carrying the 2.0-only X-RSMF-Custodian header.", Outer(b => b.Header("X-RSMF-Custodian", "Scrooge McDuck"), "1.0.0"));

        // ---- ZIP and manifest -------------------------------------------------------------
        Add("F-ZIP-001", "no-manifest-in-zip", "rsmf.zip contains attachments but no rsmf_manifest.json.", Outer(b => b.Zip = RsmfBuilder.MakeZip(null, ("photo.png", Png))), new[] { "I-PARSE-001" });
        Add("F-ZIP-002", "unreferenced-file-in-zip", "rsmf.zip contains stray.txt, which nothing in the manifest refers to.", Manifest20(_ => { }, ("photo.png", Png), ("avatar.png", Png), ("stray.txt", Encoding.UTF8.GetBytes("left over"))));
        Add("F-MAN-001", "manifest-not-valid-json", "rsmf_manifest.json is cut off and is not valid JSON.", Outer(b => b.Zip = RsmfBuilder.MakeZip("{ \"version\": \"2.0.0\", \"participants\": [", ("photo.png", Png), ("avatar.png", Png))), new[] { "I-PARSE-001" });
        Add("F-MAN-002", "manifest-version-unsupported", "The manifest's version is 9.9.9.", Outer(b =>
        {
            var m = Clean("2.0.0");
            m["version"] = "9.9.9";
            b.Zip = RsmfBuilder.MakeZip(m.ToJsonString(), ("photo.png", Png), ("avatar.png", Png));
        }), new[] { "Q-VER-001", "I-PARSE-001" });
        Add("F-MAN-003", "schema-violation", "The third event has no \"type\", which the schema requires.", Manifest20(m => Ev(m, 2).Remove("type")));
        Add("F-MAN-004", "undefined-property", "The first event has a property \"attachment\" (misspelt; the schema defines \"attachments\").", Manifest20(m => Ev(m, 0)["attachment"] = "photo.png"));

        var noParticipants = Manifest("2.0.0",
            new[] { new JsonObject { ["type"] = "message", ["body"] = "Message 1", ["conversation"] = "C1", ["timestamp"] = "2026-03-03T14:00:00Z" } },
            participants: Array.Empty<JsonObject>(),
            conversations: new[] { new JsonObject { ["id"] = "C1", ["display"] = "Chat C1", ["platform"] = "SMS", ["type"] = "direct", ["participants"] = new JsonArray() } });
        Add("F-MAN-005", "no-participants", "The manifest's participants list is empty.", Wrap(noParticipants).Build());

        var noConversations = Manifest("2.0.0",
            new[] { new JsonObject { ["type"] = "message", ["body"] = "Message 1", ["participant"] = "P1", ["timestamp"] = "2026-03-03T14:00:00Z" } },
            conversations: Array.Empty<JsonObject>());
        Add("F-MAN-006", "no-conversations", "The manifest's conversations list is empty.", Wrap(noConversations).Build());
        Add("F-MAN-007", "no-events", "The manifest's events list is empty.", Wrap(Manifest("2.0.0", Array.Empty<JsonObject>())).Build());

        Add("F-MAN-008", "duplicate-participant-id", "Two participants share the id P2.", Manifest20(m => ((JsonArray)m["participants"]!).Add(Participant("P2", "Second P2"))));
        Add("F-MAN-009", "duplicate-conversation-id", "Two conversations share the id C1.", Manifest20(m => ((JsonArray)m["conversations"]!).Add(Conversation("C1", "2.0.0", "P1", "P2"))));
        Add("F-MAN-010", "duplicate-event-id", "Two events in the same conversation share the id m1.", Manifest20(m => Ev(m, 2)["id"] = "m1"));
        Add("F-MAN-011", "conversation-without-type", "The conversation has no \"type\" (direct or channel).", Manifest20(m => Conv(m, 0).Remove("type")));
        Add("F-MAN-012", "reaction-too-long", "A reaction value is 31 characters long; the limit is 30.", Manifest20(m => ((JsonObject)Ev(m, 1)["reactions"]![0]!)["value"] = new string('x', 31)));
        Add("F-MAN-013", "timestamp-not-iso-8601", "An edit's timestamp is \"yesterday at noon\".", Manifest20(m => ((JsonObject)Ev(m, 2)["edits"]![0]!)["timestamp"] = "yesterday at noon"));

        // ---- references -------------------------------------------------------------------
        Add("F-REF-001", "conversation-participant-unknown", "The conversation lists participant P9, who is not defined.", Manifest20(m => ((JsonArray)Conv(m, 0)["participants"]!).Add("P9")));
        Add("F-REF-002", "event-participant-unknown", "The third event's participant is P9, who is not defined.", Manifest20(m => Ev(m, 2)["participant"] = "P9"));
        Add("F-REF-003", "event-conversation-unknown", "The third event's conversation is C9, which is not defined.", Manifest20(m => Ev(m, 2)["conversation"] = "C9"));
        Add("F-REF-004", "event-parent-unknown", "The third event's parent is an event id that does not exist.", Manifest20(m => Ev(m, 2)["parent"] = "no-such-event"));
        Add("F-REF-005", "custodian-not-in-conversation", "The conversation's custodian is P9, who is not one of its participants.", Manifest20(m => Conv(m, 0)["custodian"] = "P9"));
        Add("F-REF-006", "reaction-participant-unknown", "A reaction is attributed to participant P9, who is not defined.", Manifest20(m => ((JsonObject)Ev(m, 1)["reactions"]![0]!)["participants"] = new JsonArray("P9")));
        Add("F-REF-007", "edit-participant-unknown", "An edit is attributed to participant P9, who is not defined.", Manifest20(m => ((JsonObject)Ev(m, 2)["edits"]![0]!)["participant"] = "P9"));
        Add("F-REF-008", "read-receipt-participant-unknown", "A read receipt is attributed to participant P9, who is not defined.", Manifest20(m => ((JsonObject)Ev(m, 0)["read_receipts"]![0]!)["participant"] = "P9"));
        Add("F-REF-009", "attachment-missing-from-zip", "The third event refers to photo.png, but rsmf.zip does not contain it.", Manifest20(_ => { }, ("avatar.png", Png)));
        Add("F-REF-010", "avatar-missing-from-zip", "A participant's avatar is avatar.png, but rsmf.zip does not contain it.", Manifest20(_ => { }, ("photo.png", Png)));
        Add("F-REF-011", "icon-missing-from-zip", "The conversation's icon is icon.png, but rsmf.zip does not contain it.", Manifest20(m => Conv(m, 0)["icon"] = "icon.png"));

        // ---- limits -----------------------------------------------------------------------
        Add("L-EVT-001", "10001-events", "The file has 10,001 events; the viewer limit is 10,000.",
            Wrap(Manifest("2.0.0", Messages(10_001, new DateTimeOffset(2026, 3, 3, 14, 0, 0, TimeSpan.Zero), TimeSpan.FromSeconds(1)))).Build());

        // ---- local QC ---------------------------------------------------------------------
        Add("Q-TIME-A", "messages-on-two-calendar-days", "Two messages 20 minutes apart either side of midnight in New York: two calendar days, short span.",
            Timed("2026-03-03T23:50:00-05:00", "2026-03-04T00:10:00-05:00"), setting: "Time zone America/New_York");
        Add("Q-TIME-B", "span-over-24-hours", "Messages 24 h 40 min apart on the 25-hour day when clocks go back in New York: one calendar day, but over 24 hours.",
            Timed("2026-11-01T00:10:00-04:00", "2026-11-01T23:50:00-05:00"), setting: "Time zone America/New_York");
        Add("Q-TIME-001", "timestamps-without-offset", "Message timestamps have no UTC offset (read as UTC, with a warning).", Timed("2026-03-03T14:00:00", "2026-03-03T15:00:00"));
        var six = Enumerable.Range(1, 6).Select(i => Participant("P" + i, "Person " + i)).ToArray();
        Add("Q-PART-001", "six-participants", "The file has 6 participants. Only a finding when the participant warning threshold is set below 6.",
            Wrap(Manifest("2.0.0", new[] { Message("C1", "P1", "2026-03-03T14:00:00Z", "Message 1") }, six,
                new[] { Conversation("C1", "2.0.0", six.Select(p => p["id"]!.GetValue<string>()).ToArray()) })).Build(),
            setting: "Participant warning threshold 5", participants: 5);
        Add("Q-PART-002", "unspecified-sender", "One participant's display name is the placeholder \"Unspecified Sender\".",
            Wrap(Manifest("2.0.0", new[] { Message("C1", "P1", "2026-03-03T14:00:00Z", "Message 1"), Message("C1", "P2", "2026-03-03T14:05:00Z", "Message 2") },
                new[] { Participant("P1", "Scrooge McDuck"), Participant("P2", "Unspecified Sender") })).Build());
        Add("Q-PART-003", "phone-number-only", "One participant has a phone number as its display name and no account name or email.",
            Wrap(Manifest("2.0.0", new[] { Message("C1", "P1", "2026-03-03T14:00:00Z", "Message 1"), Message("C1", "P2", "2026-03-03T14:05:00Z", "Message 2") },
                new[] { Participant("P1", "Scrooge McDuck"), Participant("P2", "+1 (555) 010-0199") })).Build());
        Add("Q-VER-001", "header-and-manifest-version-differ", "X-RSMF-Version says 1.0.0 but the manifest says 2.0.0.", Outer(b => b.SetHeader("X-RSMF-Version", "1.0.0")));

        // ---- incomplete -------------------------------------------------------------------
        Add("I-TIME-001", "message-without-timestamp", "The second message has no timestamp, so the time-window check is INDETERMINATE.", Timed("2026-03-03T14:00:00Z", null));
        return s;
    }

    /// <summary>Rules with no sample file, and how to see each one.</summary>
    public static readonly (string RuleId, string HowToSee)[] NoFile =
    {
        ("L-SIZE-001", "Needs a file over the size threshold (2 GB by default). To see it, scan any sample with the size threshold set to 0.000001 GB (about 1,000 bytes)."),
        ("I-FILE-001", "Raised when a file cannot be opened. To see it, open a sample in another program that locks it (or remove your read permission) and scan."),
        ("I-SCAN-001", "Raised when a folder cannot be listed. To see it, scan a folder containing a subfolder you have no permission to open."),
        ("I-ZIP-001", "Raised when the manifest is larger than the in-memory cap (512 MB). No practical sample file."),
        ("I-PARSE-001", "Has no file of its own: it accompanies F-MIME-001, F-MIME-006, F-ZIP-001, F-MAN-001 and F-MAN-002 to say which later checks were skipped."),
        ("I-RUN-001", "Raised for files not yet checked when a scan is cancelled. To see it, cancel a scan part-way."),
    };

    public static void WriteAll(string folder)
    {
        Directory.CreateDirectory(folder);
        var samples = Build();
        foreach (var sample in samples) File.WriteAllBytes(Path.Combine(folder, sample.FileName), sample.Content);

        var sb = new StringBuilder();
        sb.AppendLine("# Sample files for Phil's Inspector for RSMF files");
        sb.AppendLine();
        sb.AppendLine("Two clean files, then one file per rule. Each rule file is the clean RSMF 2.0 file with a single");
        sb.AppendLine("defect, so it raises that rule (plus any rule listed under \"Also raised\", which cannot be avoided).");
        sb.AppendLine("Scan this folder with the time zone set to `" + Zone + "`.");
        sb.AppendLine();
        sb.AppendLine("The messages tell one running joke: Scrooge McDuck is accused of shady financial dealings, Bruce Wayne is");
        sb.AppendLine("his general counsel, and a reporter named Clark Kent is asking questions. The cast is borrowed from");
        sb.AppendLine("well-known cartoons and comics as a parody. Every message is made up, no real person appears, and the");
        sb.AppendLine("characters belong to their owners, who have nothing to do with this app.");
        sb.AppendLine();
        sb.AppendLine("| File | Rule | Category | Severity | What is wrong | Also raised | Setting needed |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var sample in samples)
        {
            var def = sample.RuleId.Length > 0 ? RuleCatalog.Get(sample.RuleId) : null;
            sb.AppendLine($"| `{sample.FileName}` | {sample.RuleId} | {def?.Category.ToString() ?? ""} | {def?.Severity.ToString().ToLowerInvariant() ?? ""} | {sample.WhatIsWrong} | {string.Join(", ", sample.AlsoExpected)} | {sample.Setting} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Rules without a sample file");
        sb.AppendLine();
        sb.AppendLine("| Rule | How to see it |");
        sb.AppendLine("|---|---|");
        foreach (var (rule, how) in NoFile) sb.AppendLine($"| {rule} | {how} |");
        File.WriteAllText(Path.Combine(folder, "INDEX.md"), sb.ToString());
    }
}
