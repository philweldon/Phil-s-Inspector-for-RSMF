using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace Rsmf.Tests;

/// <summary>Builds one synthetic .rsmf file byte-for-byte so tests control every layer.</summary>
public sealed class RsmfBuilder
{
    public const string Boundary = "_RSMF_Boundary_Delimiter_";

    public List<(string Name, string Value)> Headers { get; } = new();
    public string BodyText { get; set; } = "Synthetic test conversation.";
    public byte[]? Zip { get; set; }
    public bool IncludeZipPart { get; set; } = true;
    public string ZipFileName { get; set; } = "rsmf.zip";
    public string ZipEncoding { get; set; } = "base64";
    /// <summary>Replaces the base64 text of the zip part when set (to damage it).</summary>
    public string? RawZipPartText { get; set; }
    public List<(string FileName, byte[] Data)> ExtraAttachments { get; } = new();

    public RsmfBuilder Header(string name, string value)
    {
        Headers.Add((name, value));
        return this;
    }

    public RsmfBuilder RemoveHeader(string name)
    {
        Headers.RemoveAll(h => h.Name == name);
        return this;
    }

    public RsmfBuilder SetHeader(string name, string value)
    {
        var i = Headers.FindIndex(h => h.Name == name);
        if (i >= 0) Headers[i] = (name, value); else Headers.Add((name, value));
        return this;
    }

    public byte[] Build()
    {
        var sb = new StringBuilder();
        foreach (var (n, v) in Headers) sb.Append(n).Append(": ").Append(v).Append("\r\n");
        sb.Append("Content-Type: multipart/mixed; boundary=\"").Append(Boundary).Append("\"\r\n");
        sb.Append("MIME-Version: 1.0\r\n\r\n");
        sb.Append("--").Append(Boundary).Append("\r\n");
        sb.Append("Content-Type: text/plain; charset=\"utf-8\"\r\nContent-Transfer-Encoding: 7bit\r\n\r\n");
        sb.Append(BodyText).Append("\r\n");
        if (IncludeZipPart)
            AppendAttachment(sb, ZipFileName, ZipEncoding, RawZipPartText ?? Wrap(Convert.ToBase64String(Zip ?? Array.Empty<byte>())));
        foreach (var (name, data) in ExtraAttachments)
            AppendAttachment(sb, name, "base64", Wrap(Convert.ToBase64String(data)));
        sb.Append("--").Append(Boundary).Append("--\r\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void AppendAttachment(StringBuilder sb, string fileName, string encoding, string text)
    {
        sb.Append("--").Append(Boundary).Append("\r\n");
        sb.Append("Content-Type: application/zip; name=\"").Append(fileName).Append("\"\r\n");
        sb.Append("Content-Disposition: attachment; filename=\"").Append(fileName).Append("\"\r\n");
        sb.Append("Content-Transfer-Encoding: ").Append(encoding).Append("\r\n\r\n");
        sb.Append(text).Append("\r\n");
    }

    public static string Wrap(string base64)
    {
        var sb = new StringBuilder(base64.Length + base64.Length / 38);
        for (var i = 0; i < base64.Length; i += 76)
            sb.Append(base64, i, Math.Min(76, base64.Length - i)).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>Quoted-printable encoding (RFC 2045) of arbitrary bytes, with soft line breaks.</summary>
    public static string QuotedPrintable(byte[] data)
    {
        var sb = new StringBuilder(data.Length * 3);
        var column = 0;
        foreach (var b in data)
        {
            var literal = b >= 33 && b <= 126 && b != (byte)'=';
            var width = literal ? 1 : 3;
            if (column + width > 72)
            {
                sb.Append("=\r\n");
                column = 0;
            }
            if (literal) sb.Append((char)b); else sb.Append('=').Append(b.ToString("X2"));
            column += width;
        }
        return sb.Append("\r\n").ToString();
    }

    public static byte[] MakeZip(string? manifestJson, params (string Name, byte[] Data)[] files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (manifestJson is not null) Add(zip, "rsmf_manifest.json", Encoding.UTF8.GetBytes(manifestJson));
            foreach (var (name, data) in files) Add(zip, name, data);
        }
        return ms.ToArray();

        static void Add(ZipArchive zip, string name, byte[] data)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Fastest);
            using var s = e.Open();
            s.Write(data, 0, data.Length);
        }
    }
}

/// <summary>Synthetic manifests and the full fixture folder. All people and content are invented.</summary>
public static class FixtureGenerator
{
    public const string BodyMarker = "SECRET-BODY-MARKER";
    public const string EmailMarker = "avery.example@test.invalid";
    public const string DisplayMarker = "Avery Example";

    /// <summary>A minimal 1x1 PNG.</summary>
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public static JsonObject Participant(string id, string? display, string? email = null)
    {
        var p = new JsonObject { ["id"] = id };
        if (display is not null) p["display"] = display;
        if (email is not null) p["email"] = email;
        return p;
    }

    public static JsonObject Conversation(string id, string version, params string[] participants)
    {
        var c = new JsonObject
        {
            ["id"] = id, ["display"] = "Chat " + id, ["platform"] = "SMS", ["type"] = "direct",
            ["participants"] = new JsonArray(participants.Select(p => (JsonNode)p).ToArray()),
        };
        if (version == "2.0.0") c["custodian"] = participants[0];
        return c;
    }

    public static JsonObject Message(string conversation, string participant, string? timestamp, string body = BodyMarker, string? id = null)
    {
        var e = new JsonObject { ["type"] = "message", ["body"] = body, ["participant"] = participant, ["conversation"] = conversation };
        if (timestamp is not null) e["timestamp"] = timestamp;
        if (id is not null) e["id"] = id;
        return e;
    }

    public static JsonObject Manifest(string version, IEnumerable<JsonObject> events, JsonObject[]? participants = null, JsonObject[]? conversations = null)
    {
        participants ??= new[] { Participant("P1", DisplayMarker, EmailMarker), Participant("P2", "Blake Sample", "blake.sample@test.invalid") };
        conversations ??= new[] { Conversation("C1", version, "P1", "P2") };
        return new JsonObject
        {
            ["version"] = version,
            ["participants"] = new JsonArray(participants.Cast<JsonNode>().ToArray()),
            ["conversations"] = new JsonArray(conversations.Cast<JsonNode>().ToArray()),
            ["events"] = new JsonArray(events.Cast<JsonNode>().ToArray()),
        };
    }

    /// <summary>Builds a well-formed file around a manifest. Optional roll-up headers are derived from <paramref name="headerDates"/>.</summary>
    public static RsmfBuilder Wrap(JsonObject manifest, (string Begin, string End)? headerDates = null, params (string Name, byte[] Data)[] files)
    {
        var version = manifest["version"]?.GetValue<string>() ?? "2.0.0";
        var b = new RsmfBuilder()
            .Header("X-RSMF-Version", version)
            .Header("X-RSMF-Generator", "RsmfInspector fixture generator");
        if (headerDates is not null)
        {
            b.Header("X-RSMF-BeginDate", headerDates.Value.Begin);
            b.Header("X-RSMF-EndDate", headerDates.Value.End);
        }
        b.Header("X-RSMF-EventCount", (manifest["events"] as JsonArray)?.Count.ToString() ?? "0");
        b.Header("From", "Avery Example <avery.example@test.invalid>");
        b.Header("To", "Blake Sample <blake.sample@test.invalid>");
        b.Header("Subject", "Synthetic chat");
        b.Zip = RsmfBuilder.MakeZip(manifest.ToJsonString(), files);
        return b;
    }

    public static IEnumerable<JsonObject> Messages(int count, DateTimeOffset startUtc, TimeSpan step)
    {
        for (var i = 0; i < count; i++)
            yield return Message("C1", i % 2 == 0 ? "P1" : "P2", (startUtc + i * step).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), "msg " + i);
    }

    /// <summary>The manifest of the valid reference file (3 messages, an avatar and one image attachment).</summary>
    public static JsonObject ValidManifest(string version)
    {
        var events = new List<JsonObject>
        {
            Message("C1", "P1", "2026-03-03T14:00:00Z", BodyMarker + " hello", "m1"),
            Message("C1", "P2", "2026-03-03T14:05:00Z", "reply", "m2"),
            Message("C1", "P1", "2026-03-03T15:30:00Z", "see attached", "m3"),
        };
        events[1]["parent"] = "m1";
        events[1]["reactions"] = new JsonArray(new JsonObject { ["value"] = "thumbsup", ["count"] = 1, ["participants"] = new JsonArray("P1") });
        events[2]["attachments"] = new JsonArray(new JsonObject { ["id"] = "photo.png", ["display"] = "photo.png", ["size"] = Png.Length });
        events[2]["edits"] = new JsonArray(new JsonObject { ["participant"] = "P1", ["timestamp"] = "2026-03-03T15:31:00Z" });
        var m = Manifest(version, events);
        ((JsonObject)m["participants"]![0]!)["avatar"] = "avatar.png";
        ((JsonObject)m["participants"]![0]!)["custom"] = new JsonArray(new JsonObject { ["name"] = "Department", ["value"] = "Engineering" });
        if (version == "2.0.0")
        {
            events[0]["direction"] = "outgoing";
            events[0]["read_receipts"] = new JsonArray(new JsonObject { ["participant"] = "P2", ["timestamp"] = "2026-03-03T14:01:00Z", ["action"] = "read" });
        }
        return m;
    }

    public static readonly (string Begin, string End) ValidDates = ("2026-03-03T14:00:00Z", "2026-03-03T15:30:00Z");

    public static RsmfBuilder Valid(string version) =>
        Wrap(ValidManifest(version), ValidDates, ("photo.png", Png), ("avatar.png", Png));

    private static RsmfBuilder Timed(params string?[] timestamps) =>
        Wrap(Manifest("2.0.0", timestamps.Select((t, i) => Message("C1", i % 2 == 0 ? "P1" : "P2", t, "msg " + i))));

    /// <summary>All named fixtures: file name (relative, forward slashes) to content.</summary>
    public static Dictionary<string, byte[]> BuildAll()
    {
        var f = new Dictionary<string, byte[]>
        {
            ["valid-1.0.rsmf"] = Valid("1.0.0").Build(),
            ["valid-2.0.rsmf"] = Valid("2.0.0").Build(),
            ["sub/nested-valid.rsmf"] = Valid("2.0.0").Build(),
            ["UPPERCASE-EXT.RSMF"] = Valid("2.0.0").Build(),
            ["notes.txt"] = Encoding.UTF8.GetBytes("not an rsmf file"),
            ["sub/image.png"] = Png,
        };

        // damaged outer layers
        var rnd = new Random(20261118);
        var garbage = new byte[2048];
        rnd.NextBytes(garbage);
        garbage[0] = 0; // never looks like a header line
        f["damaged-mime.rsmf"] = garbage;

        var goodZip = Valid("2.0.0").Zip!;
        var truncated = Valid("2.0.0");
        truncated.Zip = goodZip.Take(goodZip.Length / 2).ToArray();
        f["damaged-zip.rsmf"] = truncated.Build();

        var badBase64 = Valid("2.0.0");
        badBase64.RawZipPartText = RsmfBuilder.Wrap(Convert.ToBase64String(goodZip)).Replace("A", "*").Replace("B", "#");
        f["damaged-base64.rsmf"] = badBase64.Build();

        var noManifest = Valid("2.0.0");
        noManifest.Zip = RsmfBuilder.MakeZip(null, ("photo.png", Png));
        f["missing-manifest.rsmf"] = noManifest.Build();

        var noZipPart = Valid("2.0.0");
        noZipPart.IncludeZipPart = false;
        f["missing-zip-attachment.rsmf"] = noZipPart.Build();

        var extra = Valid("2.0.0");
        extra.ExtraAttachments.Add(("other.zip", RsmfBuilder.MakeZip(null, ("x.txt", new byte[] { 1 }))));
        f["extra-attachment.rsmf"] = extra.Build();

        // references
        var missingAttachment = Valid("2.0.0");
        missingAttachment.Zip = RsmfBuilder.MakeZip(RebuildManifestText(missingAttachment), ("avatar.png", Png));
        f["missing-attachment.rsmf"] = missingAttachment.Build();

        f["broken-references.rsmf"] = BrokenReferences().Build();
        f["blank-vs-missing.rsmf"] = BlankVersusMissing().Build();

        // event-count boundaries
        var start = new DateTimeOffset(2026, 3, 3, 14, 0, 0, TimeSpan.Zero);
        foreach (var n in new[] { 9_999, 10_000, 10_001 })
            f[$"events-{n}.rsmf"] = Wrap(Manifest("2.0.0", Messages(n, start, TimeSpan.FromSeconds(1)))).Build();

        // time-window cases (zone under test: America/New_York)
        f["single-message.rsmf"] = Timed("2026-03-03T14:00:00Z").Build();
        f["midnight-crossing.rsmf"] = Timed("2026-03-03T23:50:00-05:00", "2026-03-04T00:10:00-05:00").Build();
        f["mixed-offsets.rsmf"] = Timed("2026-03-03T09:00:00-05:00", "2026-03-04T02:00:00+09:00", "2026-03-03T20:30:00+00:00", "2026-03-03T23:00:00-05:00").Build();
        f["dst-spring-forward.rsmf"] = Timed("2026-03-08T00:10:00-05:00", "2026-03-08T03:30:00-04:00", "2026-03-08T23:50:00-04:00").Build();
        f["dst-fall-back.rsmf"] = Timed("2026-11-01T00:10:00-04:00", "2026-11-01T01:30:00-05:00", "2026-11-01T23:50:00-05:00").Build();
        f["span-exactly-24h.rsmf"] = Timed("2026-03-03T12:00:00Z", "2026-03-04T12:00:00Z").Build();
        f["span-just-over-24h.rsmf"] = Timed("2026-03-03T12:00:00Z", "2026-03-04T12:00:01Z").Build();
        f["offsetless-timestamps.rsmf"] = Timed("2026-03-03T14:00:00", "2026-03-03T15:00:00").Build();
        f["invalid-timestamp.rsmf"] = Timed("2026-03-03T14:00:00Z", "03/03/2026 3pm", "2026-03-03T16:00:00Z").Build();
        f["missing-timestamp.rsmf"] = Timed("2026-03-03T14:00:00Z", null).Build();
        return f;
    }

    private static string RebuildManifestText(RsmfBuilder b)
    {
        using var zip = new ZipArchive(new MemoryStream(b.Zip!), ZipArchiveMode.Read);
        using var r = new StreamReader(zip.GetEntry("rsmf_manifest.json")!.Open());
        return r.ReadToEnd();
    }

    public static RsmfBuilder BrokenReferences()
    {
        var events = new List<JsonObject>
        {
            Message("C1", "P9", "2026-03-03T14:00:00Z", "unknown participant", "m1"),
            Message("C9", "P1", "2026-03-03T14:01:00Z", "unknown conversation", "m2"),
            Message("C1", "P1", "2026-03-03T14:02:00Z", "orphan reply", "m3"),
            Message("C1", "P2", "2026-03-03T14:03:00Z", "sub-record references", "m4"),
        };
        events[2]["parent"] = "does-not-exist";
        events[3]["reactions"] = new JsonArray(new JsonObject { ["value"] = "ok", ["count"] = 1, ["participants"] = new JsonArray("P8") });
        events[3]["edits"] = new JsonArray(new JsonObject { ["participant"] = "P7", ["timestamp"] = "2026-03-03T14:04:00Z" });
        events[3]["read_receipts"] = new JsonArray(new JsonObject { ["participant"] = "P6", ["action"] = "read" });
        var conv = Conversation("C1", "2.0.0", "P1", "P2", "P5");
        conv["custodian"] = "P4";
        conv["icon"] = "missing-icon.png";
        var m = Manifest("2.0.0", events, conversations: new[] { conv });
        ((JsonObject)m["participants"]![1]!)["avatar"] = "missing-avatar.png";
        return Wrap(m);
    }

    public static RsmfBuilder BlankVersusMissing()
    {
        var participants = new[]
        {
            Participant("P1", DisplayMarker, EmailMarker),
            Participant("P2", null),          // display missing
            Participant("P3", ""),            // display empty
            Participant("P4", "   "),         // display whitespace only
            Participant("P5", "Casey Demo", "not-an-email"),
        };
        participants[4]["display"] = "Casey Demo";
        var p6 = new JsonObject { ["id"] = "P6", ["display"] = null, ["custom"] = new JsonArray() }; // null display, empty collection
        var events = new List<JsonObject>
        {
            Message("C1", "P1", "2026-03-03T14:00:00Z", ""),
            Message("C1", "P2", "2026-03-03T14:01:00Z", "has body"),
        };
        events[0]["deleted"] = false;                                     // false is populated
        events[1]["reactions"] = new JsonArray(new JsonObject { ["value"] = "x", ["count"] = 0 }); // zero is populated
        var conv = Conversation("C1", "2.0.0", "P1", "P2", "P3", "P4", "P5", "P6");
        return Wrap(Manifest("2.0.0", events, participants.Append(p6).ToArray(), new[] { conv }));
    }

    public static void WriteAll(string folder)
    {
        foreach (var (name, bytes) in BuildAll())
        {
            var path = Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
    }
}
