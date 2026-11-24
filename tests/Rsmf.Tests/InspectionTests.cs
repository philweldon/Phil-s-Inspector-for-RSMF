using System.Text.Json.Nodes;
using Rsmf.Core;
using Xunit;
using static Rsmf.Tests.FixtureGenerator;

namespace Rsmf.Tests;

/// <summary>Writes the full fixture folder once per test run.</summary>
public sealed class FixtureFolder : IDisposable
{
    public string Root { get; }
    public string Files { get; }

    public FixtureFolder()
    {
        // RSMF_FIXTURES_OUT keeps the generated folder (used for the CLI gate scan).
        var keep = Environment.GetEnvironmentVariable("RSMF_FIXTURES_OUT");
        Root = Path.Combine(Path.GetTempPath(), "rsmf-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Files = string.IsNullOrWhiteSpace(keep) ? Path.Combine(Root, "fixtures") : keep;
        Directory.CreateDirectory(Files);
        WriteAll(Files);
    }

    public string PathOf(string name) => Path.Combine(Files, name.Replace('/', Path.DirectorySeparatorChar));

    public string NewDir(string name)
    {
        var d = Path.Combine(Root, name + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(d);
        return d;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

[CollectionDefinition("fixtures")]
public sealed class FixtureCollection : ICollectionFixture<FixtureFolder> { }

[Collection("fixtures")]
public class InspectionTests
{
    private readonly FixtureFolder _fx;
    private static readonly InspectionOptions NewYork = new() { TimeZoneId = "America/New_York" };

    public InspectionTests(FixtureFolder fx) => _fx = fx;

    private FileResult Fixture(string name, InspectionOptions? options = null) =>
        FileInspector.Inspect(_fx.PathOf(name), name, options ?? NewYork, CancellationToken.None);

    private FileResult Bytes(byte[] content, InspectionOptions? options = null, string fileName = "test.rsmf")
    {
        var path = Path.Combine(_fx.NewDir("adhoc"), fileName);
        File.WriteAllBytes(path, content);
        return FileInspector.Inspect(path, fileName, options ?? NewYork, CancellationToken.None);
    }

    private static string[] Rules(FileResult r) => r.Findings.Select(f => f.RuleId).Distinct().OrderBy(x => x).ToArray();

    private static void Has(FileResult r, string rule) =>
        Assert.True(r.Findings.Any(f => f.RuleId == rule), $"expected {rule}; got: {string.Join(", ", Rules(r))}");

    private static void Lacks(FileResult r, string rule) =>
        Assert.False(r.Findings.Any(f => f.RuleId == rule), $"did not expect {rule}: {r.Findings.FirstOrDefault(f => f.RuleId == rule)?.Observed}");

    // ------------------------------------------------------------ valid files

    [Theory]
    [InlineData("valid-1.0.rsmf", "1.0.0")]
    [InlineData("valid-2.0.rsmf", "2.0.0")]
    public void Valid_files_have_no_findings(string name, string version)
    {
        var r = Fixture(name);
        Assert.True(r.Findings.Count == 0, "unexpected: " + string.Join(" | ", r.Findings.Select(f => $"{f.RuleId} {f.Location} {f.Observed} {f.Message}")));
        Assert.Equal(FileStatus.Ok, r.Status);
        Assert.Equal(version, r.RsmfVersion);
        Assert.Equal(3, r.Metrics.TotalEvents);
        Assert.Equal(3, r.Metrics.MessageEvents);
        Assert.Equal(1, r.Metrics.ConversationCount);
        Assert.Equal(2, r.Metrics.UniqueParticipantCount);
        Assert.Equal(TimeWindowStatus.Pass, r.TimeWindow.Status);
        var c = Assert.Single(r.Metrics.Conversations);
        Assert.Equal(3, c.MessageCount);
        Assert.Equal(2, c.ParticipantCount);
        Assert.Equal("1:30:00", TimestampParser.FormatDuration(c.Span!.Value));
    }

    [Fact]
    public void Every_finding_carries_required_fields()
    {
        var r = Fixture("broken-references.rsmf");
        Assert.NotEmpty(r.Findings);
        foreach (var f in r.Findings)
        {
            Assert.False(string.IsNullOrWhiteSpace(f.RuleId));
            Assert.False(string.IsNullOrWhiteSpace(f.Observed));
            Assert.False(string.IsNullOrWhiteSpace(f.Expected));
            Assert.False(string.IsNullOrWhiteSpace(f.Source));
            Assert.False(string.IsNullOrWhiteSpace(f.Location));
            Assert.Equal(RuleCatalog.Get(f.RuleId).Category, f.Category);
        }
        Assert.Contains(r.Findings, f => f.RuleId == "F-REF-002" && f.Location == "$.events[0].participant" && f.RecordId.StartsWith("event:m1"));
    }

    // ------------------------------------------------------------ damaged layers

    [Fact]
    public void Damaged_mime_is_reported_and_later_checks_are_skipped()
    {
        var r = Fixture("damaged-mime.rsmf");
        Has(r, "F-MIME-001");
        Has(r, "I-PARSE-001");
        Assert.Equal(FileStatus.Errors, r.Status);
        Assert.Null(r.Metrics.TotalEvents);
        Assert.NotEmpty(r.SkippedChecks);
    }

    [Fact]
    public void Damaged_zip_is_reported()
    {
        var r = Fixture("damaged-zip.rsmf");
        Has(r, "F-MIME-006");
        Has(r, "I-PARSE-001");
        Lacks(r, "F-MIME-001");
        Assert.Equal("2.0.0", r.RsmfVersion);
    }

    [Fact]
    public void Damaged_base64_is_reported()
    {
        var r = Fixture("damaged-base64.rsmf");
        Has(r, "F-MIME-006");
        Assert.Contains(r.Findings, f => f.RuleId == "F-MIME-006" && f.Observed.Contains("base64 alphabet"));
    }

    [Fact]
    public void Missing_manifest_is_reported()
    {
        var r = Fixture("missing-manifest.rsmf");
        Has(r, "F-ZIP-001");
        Lacks(r, "F-MIME-006");
        Assert.Null(r.Metrics.TotalEvents);
    }

    [Fact]
    public void Manifest_in_subfolder_is_not_at_root()
    {
        var b = Valid("2.0.0");
        b.Zip = RsmfBuilder.MakeZip(null, ("data/rsmf_manifest.json", new byte[] { (byte)'{', (byte)'}' }));
        var r = Bytes(b.Build());
        Assert.Contains(r.Findings, f => f.RuleId == "F-ZIP-001" && f.Observed.Contains("subfolder"));
    }

    [Fact]
    public void Missing_or_extra_zip_attachment_is_reported()
    {
        Has(Fixture("missing-zip-attachment.rsmf"), "F-MIME-005");
        var extra = Fixture("extra-attachment.rsmf");
        Assert.Contains(extra.Findings, f => f.RuleId == "F-MIME-005" && f.Observed == "2 attachment parts");
        Assert.Equal(3, extra.Metrics.TotalEvents); // the real rsmf.zip is still inspected
    }

    [Fact]
    public void Zip_attachment_must_be_base64_and_named_exactly()
    {
        var b = Valid("2.0.0");
        b.ZipFileName = "RSMF.ZIP";
        Assert.Contains(Bytes(b.Build()).Findings, f => f.RuleId == "F-MIME-005" && f.Observed.Contains("letter case"));
    }

    [Fact]
    public void Zip_attachment_that_is_not_base64_is_reported_and_still_inspected()
    {
        // A correctly quoted-printable-encoded rsmf.zip: the encoding breaks the rule, but the content is readable.
        var qp = Valid("2.0.0");
        qp.ZipEncoding = "quoted-printable";
        qp.RawZipPartText = RsmfBuilder.QuotedPrintable(qp.Zip!);
        var r = Bytes(qp.Build());
        var f = Assert.Single(r.Findings);
        Assert.Equal("F-MIME-005", f.RuleId);
        Assert.Equal("Content-Transfer-Encoding: quoted-printable", f.Observed);
        Assert.Equal("Content-Transfer-Encoding", f.Location);
        Assert.Equal(Severity.Error, f.Severity);
        Assert.Equal(3, r.Metrics.TotalEvents);
        Assert.Equal(TimeWindowStatus.Pass, r.TimeWindow.Status);

        // Declared 7bit while the body is base64 text: wrong encoding and, decoded as declared, not a ZIP.
        var mislabelled = Valid("2.0.0");
        mislabelled.ZipEncoding = "7bit";
        var m = Bytes(mislabelled.Build());
        Assert.Contains(m.Findings, x => x.RuleId == "F-MIME-005" && x.Observed == "Content-Transfer-Encoding: 7bit");
        Has(m, "F-MIME-006");
        Has(m, "I-PARSE-001");
        Assert.Null(m.Metrics.TotalEvents);

        // No Content-Transfer-Encoding header at all.
        var raw = System.Text.Encoding.UTF8.GetString(Valid("2.0.0").Build()).Replace("Content-Transfer-Encoding: base64\r\n", "");
        var none = Bytes(System.Text.Encoding.UTF8.GetBytes(raw));
        Assert.Contains(none.Findings, x => x.RuleId == "F-MIME-005" && x.Observed == "Content-Transfer-Encoding: (none declared)");
    }

    [Fact]
    public void Malformed_json_manifest_is_reported()
    {
        var b = Valid("2.0.0");
        b.Zip = RsmfBuilder.MakeZip("{ \"version\": \"2.0.0\", ");
        Has(Bytes(b.Build()), "F-MAN-001");
        b.Zip = RsmfBuilder.MakeZip("[1,2,3]");
        Assert.Contains(Bytes(b.Build()).Findings, f => f.RuleId == "F-MAN-001" && f.Observed.Contains("Array"));
    }

    // ------------------------------------------------------------ headers

    [Fact]
    public void Version_header_rules()
    {
        Has(Bytes(Valid("2.0.0").RemoveHeader("X-RSMF-Version").Build()), "F-MIME-002");
        Has(Bytes(Valid("2.0.0").SetHeader("X-RSMF-Version", "3.1.0").Build()), "F-MIME-003");
        Has(Bytes(Valid("2.0.0").SetHeader("X-RSMF-Version", "2.0").Build()), "F-MIME-003");

        var notFirst = Valid("2.0.0").RemoveHeader("X-RSMF-Version").Header("X-RSMF-Version", "2.0.0");
        var r = Bytes(notFirst.Build());
        Has(r, "F-MIME-004");
        Lacks(r, "F-MIME-002");

        var mismatch = Bytes(Valid("2.0.0").SetHeader("X-RSMF-Version", "1.0.0").Build());
        Has(mismatch, "Q-VER-001");
    }

    [Fact]
    public void Optional_header_values_are_compared_with_the_manifest()
    {
        var r = Bytes(Valid("2.0.0")
            .SetHeader("X-RSMF-EventCount", "4662")
            .SetHeader("X-RSMF-BeginDate", "2026-03-03T13:00:00Z")
            .SetHeader("X-RSMF-EndDate", "2026-03-03T10:30:00-05:00") // same instant as 15:30Z: must pass
            .Build());
        Has(r, "F-HDR-002");
        Has(r, "F-HDR-003");
        Lacks(r, "F-HDR-004");

        var bad = Bytes(Valid("2.0.0").SetHeader("X-RSMF-EventCount", "three").SetHeader("X-RSMF-EndDate", "yesterday").Build());
        Assert.Equal(2, bad.Findings.Count(f => f.RuleId == "F-HDR-001"));
        Lacks(bad, "F-HDR-002");
    }

    [Fact]
    public void Two_point_zero_headers_in_a_one_point_zero_file_and_long_custom_headers()
    {
        var r = Bytes(Valid("1.0.0").Header("X-RSMF-Custodian", "someone").Header("X-RSMF-" + new string('a', 255), "x")
            .Header("X-RSMF-ReviewBatch", "B7").Build());
        Has(r, "F-HDR-005");
        Has(r, "F-MIME-008");
        Assert.Contains(r.Coverage, c => c.Level == "header" && c.Field == "X-RSMF-ReviewBatch" && c.IsCustom && c.Populated == 1);
    }

    [Fact]
    public void Wrong_extension_and_size_threshold()
    {
        var r = Bytes(Valid("2.0.0").Build(), fileName: "chat.eml");
        Has(r, "F-MIME-007");

        var small = Fixture("valid-2.0.rsmf", new InspectionOptions { TimeZoneId = "America/New_York", SizeThresholdBytes = 100 });
        var f = Assert.Single(small.Findings, x => x.RuleId == "L-SIZE-001");
        Assert.Equal(FindingCategory.LIMIT, f.Category);
        Assert.Equal("<= 100 bytes", f.Expected);

        var exact = Fixture("valid-2.0.rsmf", new InspectionOptions { TimeZoneId = "America/New_York", SizeThresholdBytes = new FileInfo(_fx.PathOf("valid-2.0.rsmf")).Length });
        Lacks(exact, "L-SIZE-001");
        Assert.Equal(2_147_483_648L, new InspectionOptions().SizeThresholdBytes);
    }

    // ------------------------------------------------------------ manifest rules

    [Fact]
    public void Missing_attachment_and_unreferenced_member()
    {
        var r = Fixture("missing-attachment.rsmf");
        var f = Assert.Single(r.Findings);
        Assert.Equal("F-REF-009", f.RuleId);
        Assert.Equal("$.events[2].attachments[0].id", f.Location);

        var b = Valid("2.0.0");
        b.Zip = RsmfBuilder.MakeZip(Manifest("2.0.0", new[] { Message("C1", "P1", "2026-03-03T14:00:00Z") }).ToJsonString(), ("stray.bin", new byte[] { 1, 2 }));
        b.SetHeader("X-RSMF-EventCount", "1").RemoveHeader("X-RSMF-BeginDate").RemoveHeader("X-RSMF-EndDate");
        Assert.Equal(new[] { "F-ZIP-002" }, Rules(Bytes(b.Build())));
    }

    [Fact]
    public void Broken_participant_and_conversation_references()
    {
        var r = Fixture("broken-references.rsmf");
        foreach (var rule in new[] { "F-REF-001", "F-REF-002", "F-REF-003", "F-REF-004", "F-REF-005", "F-REF-006", "F-REF-007", "F-REF-008", "F-REF-010", "F-REF-011" })
            Has(r, rule);
        Assert.Equal(Severity.Warning, r.Findings.First(f => f.RuleId == "F-REF-002").Severity);
        Assert.Equal(Severity.Error, r.Findings.First(f => f.RuleId == "F-REF-003").Severity);
    }

    [Fact]
    public void Schema_violations_and_undefined_properties()
    {
        var m = Manifest("1.0.0", new[] { Message("C1", "P1", "2026-03-03T14:00:00Z") },
            participants: new[] { Participant("P1", null), Participant("P2", "B") });
        ((JsonObject)m["events"]![0]!)["attachment"] = "x.png"; // misspelt: should be "attachments"
        ((JsonObject)m["events"]![0]!)["direction"] = "incoming"; // 2.0-only field in a 1.0 manifest
        var r = Bytes(Wrap(m).Build());
        Assert.Contains(r.Findings, f => f.RuleId == "F-MAN-003" && f.Location == "$.participants[0]" && f.RecordId == "participant:P1");
        Assert.Contains(r.Findings, f => f.RuleId == "F-MAN-004" && f.Location == "$.events[0].attachment");
        Assert.Contains(r.Findings, f => f.RuleId == "F-MAN-004" && f.Location == "$.events[0].direction");
        Assert.Equal(Severity.Warning, r.Findings.First(f => f.RuleId == "F-MAN-004").Severity);

        var typeError = Manifest("2.0.0", new[] { Message("C1", "P1", "2026-03-03T14:00:00Z") });
        ((JsonObject)typeError["participants"]![0]!)["id"] = 1;
        Assert.Contains(Bytes(Wrap(typeError).Build()).Findings, f => f.RuleId == "F-MAN-003" && f.Location == "$.participants[0].id");
    }

    [Fact]
    public void Two_point_zero_schema_allows_history_events_and_requires_conversation_participants()
    {
        var history = new JsonObject { ["type"] = "history", ["conversation"] = "C1", ["participant"] = "P1", ["timestamp"] = "2026-03-03T14:00:00Z" };
        Lacks(Bytes(Wrap(Manifest("2.0.0", new[] { (JsonObject)history.DeepClone() })).Build()), "F-MAN-003");
        Has(Bytes(Wrap(Manifest("1.0.0", new[] { (JsonObject)history.DeepClone() })).Build()), "F-MAN-003");

        var conv = Conversation("C1", "2.0.0", "P1", "P2");
        conv.Remove("participants");
        conv.Remove("custodian");
        conv.Remove("type");
        var r = Bytes(Wrap(Manifest("2.0.0", new[] { Message("C1", "P1", "2026-03-03T14:00:00Z") }, conversations: new[] { conv })).Build());
        Assert.Contains(r.Findings, f => f.RuleId == "F-MAN-003" && f.Location == "$.conversations[0]"); // conflict C1
        Has(r, "F-MAN-011");                                                                           // conflict C2
    }

    [Fact]
    public void Empty_collections_duplicates_and_unsupported_manifest_version()
    {
        var empty = new JsonObject { ["version"] = "2.0.0", ["participants"] = new JsonArray(), ["conversations"] = new JsonArray(), ["events"] = new JsonArray() };
        var r = Bytes(Wrap(empty).Build());
        Has(r, "F-MAN-005");
        Has(r, "F-MAN-006");
        Has(r, "F-MAN-007");
        Assert.Equal(TimeWindowStatus.NoMessages, r.TimeWindow.Status);

        var dup = Manifest("2.0.0",
            new[] { Message("C1", "P1", "2026-03-03T14:00:00Z", id: "m1"), Message("C1", "P1", "2026-03-03T14:01:00Z", id: "m1") },
            participants: new[] { Participant("P1", "A"), Participant("P1", "B") },
            conversations: new[] { Conversation("C1", "2.0.0", "P1"), Conversation("C1", "2.0.0", "P1") });
        var d = Bytes(Wrap(dup).Build());
        Has(d, "F-MAN-008");
        Has(d, "F-MAN-009");
        Has(d, "F-MAN-010");

        var odd = Manifest("2.0.0", new[] { Message("C1", "P1", "2026-03-03T14:00:00Z") });
        odd["version"] = "9.9.9";
        var o = Bytes(Wrap(odd).SetHeader("X-RSMF-Version", "2.0.0").Build());
        Has(o, "F-MAN-002");
        Has(o, "Q-VER-001");
        Lacks(o, "F-MAN-003");
        Assert.Contains(o.SkippedChecks, s => s.Contains("schema validation"));
    }

    [Fact]
    public void Reaction_value_longer_than_thirty_characters()
    {
        var e = Message("C1", "P1", "2026-03-03T14:00:00Z");
        e["reactions"] = new JsonArray(
            new JsonObject { ["value"] = new string('x', 30) },
            new JsonObject { ["value"] = new string('x', 31) });
        var r = Bytes(Wrap(Manifest("2.0.0", new[] { e })).Build());
        var f = Assert.Single(r.Findings, x => x.RuleId == "F-MAN-012");
        Assert.Equal("$.events[0].reactions[1].value", f.Location);
        Assert.Equal("31 characters", f.Observed);
    }

    [Theory]
    [InlineData("events-9999.rsmf", 9_999, false)]
    [InlineData("events-10000.rsmf", 10_000, false)]
    [InlineData("events-10001.rsmf", 10_001, true)]
    public void Event_limit_boundary(string name, int events, bool expectFinding)
    {
        var r = Fixture(name);
        Assert.Equal(events, r.Metrics.TotalEvents);
        Assert.Equal(events, r.Metrics.MessageEvents);
        Assert.Equal(expectFinding, r.Findings.Any(f => f.RuleId == "L-EVT-001"));
        if (expectFinding)
            Assert.Equal(FindingCategory.LIMIT, r.Findings.First(f => f.RuleId == "L-EVT-001").Category);
        Lacks(r, "F-HDR-002");
    }

    // ------------------------------------------------------------ participant names

    private FileResult WithParticipants(InspectionOptions? options, params JsonObject[] participants)
    {
        var ids = participants.Select(p => p["id"]!.GetValue<string>()).ToArray();
        var events = ids.Select((id, i) => FixtureGenerator.Message("C1", id, $"2026-03-03T14:{i:00}:00Z", "Message " + i)).ToArray();
        var manifest = FixtureGenerator.Manifest("2.0.0", events, participants, new[] { FixtureGenerator.Conversation("C1", "2.0.0", ids) });
        return Bytes(FixtureGenerator.Wrap(manifest).Build(), options);
    }

    private static JsonObject Person(string id, string? display = null, string? email = null, string? account = null)
    {
        var p = FixtureGenerator.Participant(id, display, email);
        if (account is not null) p["account_id"] = account;
        return p;
    }

    [Theory]
    [InlineData("+1 (555) 010-0199", true)]
    [InlineData("555-010-0199", true)]
    [InlineData("5550100199", true)]
    [InlineData("+44 20 7946 0958", true)]
    [InlineData("tel:+15550100199", true)]
    [InlineData("555.010.0199 x12", true)]
    [InlineData("(555) 010-0199 ext. 4", true)]
    [InlineData("  +15550100199  ", true)]
    [InlineData("Avery Example", false)]
    [InlineData("Avery 555-010-0199", false)]      // a name with a number is a name
    [InlineData("555-0199", true)]                  // seven digits
    [InlineData("12345", false)]                    // too short
    [InlineData("1234567890123456", false)]         // too long
    [InlineData("55+50100199", false)]              // a plus sign only belongs at the start
    [InlineData("avery@test.invalid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Phone_number_detection(string? text, bool expected) =>
        Assert.Equal(expected, FileInspector.LooksLikePhoneNumber(text));

    [Fact]
    public void Unspecified_sender_placeholder_is_reported_once_per_participant_in_any_letter_case()
    {
        var r = WithParticipants(null, Person("P1", "Avery Example"), Person("P2", "Unspecified Sender"), Person("P3", "UNSPECIFIED SENDER (2)"),
            Person("P4", "Blake Sample", account: "unspecified sender"));
        var found = r.Findings.Where(f => f.RuleId == "Q-PART-002").ToList();
        Assert.Equal(new[] { "$.participants[1]", "$.participants[2]", "$.participants[3]" }, found.Select(f => f.Location));
        Assert.All(found, f => { Assert.Equal(Severity.Warning, f.Severity); Assert.Equal(FindingCategory.LOCAL, f.Category); });
        Assert.Equal("\"Unspecified Sender\" in display", found[0].Observed);
        Assert.Equal("\"Unspecified Sender\" in account_id", found[2].Observed);
        Lacks(r, "Q-PART-003");
        Lacks(WithParticipants(null, Person("P1", "Avery Example"), Person("P2", "Sender Unspecified")), "Q-PART-002");
    }

    [Fact]
    public void Participant_with_only_a_phone_number_is_reported_but_one_with_any_name_or_email_is_not()
    {
        var r = WithParticipants(null,
            Person("P1", "+1 (555) 010-0101"),                                   // phone as display          -> finding
            Person("P2", account: "555-010-0102"),                               // phone as account, no name -> finding
            Person("+15550100103"),                                              // nothing but a phone id    -> finding
            Person("P4", "555-010-0104", account: "+15550100104"),               // phone in both             -> finding
            Person("P5", "Avery Example", account: "555-010-0105"),              // has a display name
            Person("P6", "555-010-0106", email: "blake.sample@test.invalid"),    // has an email address
            Person("P7", "555-010-0107", account: "blake.sample"),               // has an account name
            Person("P8"),                                                        // no name, but no phone number either
            Person("P9", "Casey Example"));
        var found = r.Findings.Where(f => f.RuleId == "Q-PART-003").ToList();
        Assert.Equal(new[] { "$.participants[0]", "$.participants[1]", "$.participants[2]", "$.participants[3]" }, found.Select(f => f.Location));
        Assert.Equal("phone number only (in display); no name or email", found[0].Observed);
        Assert.Equal("phone number only (in account_id); no name or email", found[1].Observed);
        Assert.Equal("phone number only (in id); no name or email", found[2].Observed);
        Assert.Equal("phone number only (in display, account_id); no name or email", found[3].Observed);
        Assert.All(found, f => Assert.Equal(Severity.Warning, f.Severity));
        Lacks(r, "Q-PART-002");
    }

    [Fact]
    public void Phone_numbers_are_not_written_to_findings_unless_sensitive_values_are_included()
    {
        var plain = WithParticipants(null, Person("P1", "+1 (555) 010-0101"));
        var finding = Assert.Single(plain.Findings, f => f.RuleId == "Q-PART-003");
        Assert.Null(finding.SensitiveDetail);
        Assert.DoesNotContain("555", finding.Observed + finding.Message + finding.Location + finding.RecordId);

        var included = WithParticipants(new InspectionOptions { TimeZoneId = "America/New_York", IncludeSensitive = true }, Person("P1", "+1 (555) 010-0101"));
        Assert.Contains("+1 (555) 010-0101", Assert.Single(included.Findings, f => f.RuleId == "Q-PART-003").SensitiveDetail);
    }

    [Fact]
    public void Clean_fixtures_raise_neither_participant_name_rule()
    {
        foreach (var name in new[] { "valid-1.0.rsmf", "valid-2.0.rsmf", "single-message.rsmf", "blank-vs-missing.rsmf" })
        {
            Lacks(Fixture(name), "Q-PART-002");
            Lacks(Fixture(name), "Q-PART-003");
        }
    }

    [Fact]
    public void Participant_count_is_reported_and_threshold_is_optional()
    {
        var plain = Fixture("blank-vs-missing.rsmf");
        Assert.Equal(6, plain.Metrics.UniqueParticipantCount);
        Lacks(plain, "Q-PART-001");
        var limited = Fixture("blank-vs-missing.rsmf", new InspectionOptions { TimeZoneId = "America/New_York", ParticipantWarningThreshold = 5 });
        Assert.Equal(FindingCategory.LOCAL, Assert.Single(limited.Findings, f => f.RuleId == "Q-PART-001").Category);
        Lacks(Fixture("blank-vs-missing.rsmf", new InspectionOptions { TimeZoneId = "America/New_York", ParticipantWarningThreshold = 6 }), "Q-PART-001");
    }

    [Fact]
    public void Message_events_are_counted_separately_from_other_events()
    {
        var join = new JsonObject { ["type"] = "join", ["conversation"] = "C1", ["participant"] = "P2", ["timestamp"] = "2026-03-01T00:00:00Z" };
        var r = Bytes(Wrap(Manifest("2.0.0", new[] { join, Message("C1", "P1", "2026-03-03T14:00:00Z") })).Build());
        Assert.Equal(2, r.Metrics.TotalEvents);
        Assert.Equal(1, r.Metrics.MessageEvents);
        // The join event two days earlier is ignored by the time-window rules.
        Assert.Equal(TimeWindowStatus.Pass, r.TimeWindow.Status);
    }

    // ------------------------------------------------------------ metadata coverage

    [Fact]
    public void Coverage_distinguishes_missing_blank_populated_and_invalid()
    {
        var r = Fixture("blank-vs-missing.rsmf");
        CoverageRow Row(string level, string field) => r.Coverage.Single(c => c.Level == level && c.Field == field);

        var display = Row("participant", "display");
        Assert.Equal(6, display.Records);
        Assert.Equal(1, display.Missing);
        Assert.Equal(3, display.Blank);     // empty, whitespace-only, null
        Assert.Equal(2, display.Populated);
        Assert.Equal(new[] { "participant:P2" }, display.MissingIds);
        Assert.Equal(new[] { "participant:P3", "participant:P4", "participant:P6" }, display.BlankIds);

        var email = Row("participant", "email");
        Assert.Equal(1, email.Invalid);
        Assert.Equal(new[] { "participant:P5" }, email.InvalidIds);

        Assert.Equal(1, Row("participant", "custom").Blank);  // empty collection
        Assert.Equal(1, Row("event", "body").Blank);
        Assert.Equal(1, Row("event", "body").Populated);
        Assert.Equal(1, Row("event", "deleted").Populated);   // false is populated
        Assert.Equal(1, Row("event", "deleted").Missing);
        Assert.Equal("required", Row("event", "type").Requirement);
        Assert.Equal("optional", Row("event", "timestamp").Requirement);
        Assert.Equal(1, Row("header", "X-RSMF-Version").Populated);
        Assert.Equal(1, Row("header", "X-RSMF-Custodian").Missing);
        Assert.Equal(1, Row("file", "eventcollectionid").Missing);
        Assert.Empty(display.SampleValues);
    }

    [Fact]
    public void Coverage_lists_custom_fields_and_uses_version_specific_requirements()
    {
        var r = Fixture("valid-1.0.rsmf");
        var custom = r.Coverage.Single(c => c.Field == "custom:Department");
        Assert.True(custom.IsCustom);
        Assert.Equal("participant", custom.Level);
        Assert.Equal(1, custom.Populated);
        Assert.Equal("required", r.Coverage.Single(c => c.Level == "event" && c.Field == "timestamp").Requirement);
        Assert.Equal("not in this version", r.Coverage.Single(c => c.Level == "event" && c.Field == "direction").Requirement);
    }

    // ------------------------------------------------------------ time window

    [Fact]
    public void Single_message_passes()
    {
        var r = Fixture("single-message.rsmf");
        Assert.Equal(TimeWindowStatus.Pass, r.TimeWindow.Status);
        Assert.Equal("0:00:00", TimestampParser.FormatDuration(r.TimeWindow.Span!.Value));
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void Midnight_crossing_fails_rule_A_only_and_elapsed_only_mode_skips_A()
    {
        var r = Fixture("midnight-crossing.rsmf");
        Has(r, "Q-TIME-A");
        Lacks(r, "Q-TIME-B");
        Assert.True(r.TimeWindow.RuleAFailed);
        Assert.False(r.TimeWindow.RuleBFailed);
        Assert.Equal(new[] { "2026-03-03", "2026-03-04" }, r.TimeWindow.LocalDates);
        Assert.Equal(TimeWindowStatus.Fail, r.TimeWindow.Status);

        var elapsed = Fixture("midnight-crossing.rsmf", new InspectionOptions { TimeZoneId = "America/New_York", TimeWindowMode = TimeWindowMode.ElapsedOnly });
        Lacks(elapsed, "Q-TIME-A");
        Assert.Equal(TimeWindowStatus.Pass, elapsed.TimeWindow.Status);
        Assert.False(elapsed.TimeWindow.RuleAChecked);
    }

    [Fact]
    public void Mixed_offsets_are_converted_from_their_own_offset_into_the_selected_zone()
    {
        var ny = Fixture("mixed-offsets.rsmf");
        Assert.Equal(TimeWindowStatus.Pass, ny.TimeWindow.Status);
        Assert.Equal(new[] { "2026-03-03" }, ny.TimeWindow.LocalDates);
        Assert.Equal("14:00:00", TimestampParser.FormatDuration(ny.TimeWindow.Span!.Value));

        // The same instants span two calendar days in Tokyo.
        var tokyo = Fixture("mixed-offsets.rsmf", new InspectionOptions { TimeZoneId = "Asia/Tokyo" });
        Has(tokyo, "Q-TIME-A");
        Lacks(tokyo, "Q-TIME-B");
    }

    [Fact]
    public void Daylight_saving_days_are_still_one_calendar_day()
    {
        var spring = Fixture("dst-spring-forward.rsmf"); // 23-hour day
        Assert.Equal(TimeWindowStatus.Pass, spring.TimeWindow.Status);
        Assert.Equal("22:40:00", TimestampParser.FormatDuration(spring.TimeWindow.Span!.Value));

        var autumn = Fixture("dst-fall-back.rsmf"); // 25-hour day: one calendar day, but 24 h 40 min elapsed
        Lacks(autumn, "Q-TIME-A");
        Has(autumn, "Q-TIME-B");
        Assert.Equal(new[] { "2026-11-01" }, autumn.TimeWindow.LocalDates);
        Assert.Equal("24:40:00", TimestampParser.FormatDuration(autumn.TimeWindow.Span!.Value));
    }

    [Fact]
    public void Span_of_exactly_24_hours_passes_rule_B_and_one_second_more_fails()
    {
        var elapsedOnly = new InspectionOptions { TimeZoneId = "America/New_York", TimeWindowMode = TimeWindowMode.ElapsedOnly };
        var exact = Fixture("span-exactly-24h.rsmf", elapsedOnly);
        Lacks(exact, "Q-TIME-B");
        Assert.Equal(TimeWindowStatus.Pass, exact.TimeWindow.Status);

        var over = Fixture("span-just-over-24h.rsmf", elapsedOnly);
        var f = Assert.Single(over.Findings, x => x.RuleId == "Q-TIME-B");
        Assert.Contains("24:00:01", f.Observed);

        // In default mode A and B are separate findings.
        var both = Fixture("span-just-over-24h.rsmf");
        Has(both, "Q-TIME-A");
        Has(both, "Q-TIME-B");
    }

    [Fact]
    public void Offsetless_timestamps_are_treated_as_utc_with_a_warning()
    {
        var r = Fixture("offsetless-timestamps.rsmf");
        var f = Assert.Single(r.Findings);
        Assert.Equal("Q-TIME-001", f.RuleId);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal(2, r.TimeWindow.OffsetlessCount);
        Assert.Equal("2026-03-03T14:00:00Z", TimestampParser.Format(r.TimeWindow.Earliest!.Value));
        Assert.Equal(TimeWindowStatus.Pass, r.TimeWindow.Status);
    }

    [Theory]
    [InlineData("invalid-timestamp.rsmf", "$.events[1].timestamp (unparseable)", true)]
    [InlineData("missing-timestamp.rsmf", "$.events[1].timestamp (missing)", false)]
    public void Missing_or_unparseable_timestamp_makes_the_check_indeterminate(string name, string location, bool formatFinding)
    {
        var r = Fixture(name);
        Assert.Equal(TimeWindowStatus.Indeterminate, r.TimeWindow.Status);
        Assert.Equal(new[] { location }, r.TimeWindow.IndeterminateLocations);
        var f = Assert.Single(r.Findings, x => x.RuleId == "I-TIME-001");
        Assert.Equal(FindingCategory.INCOMPLETE, f.Category);
        Assert.Contains("$.events[1].timestamp", f.Observed);
        Lacks(r, "Q-TIME-A");
        Lacks(r, "Q-TIME-B");
        Assert.Equal(formatFinding, r.Findings.Any(x => x.RuleId == "F-MAN-013"));
    }

    [Theory]
    [InlineData("2018-09-11T09:55:00Z", true, true, "2018-09-11T09:55:00Z")]
    [InlineData("2010-02-18T10:05:11.0000000Z", true, true, "2010-02-18T10:05:11Z")]
    [InlineData("2018-09-11T09:55:00+02:00", true, true, "2018-09-11T07:55:00Z")]
    [InlineData("2018-09-11T09:55:00.123-0500", true, true, "2018-09-11T14:55:00.123Z")]
    [InlineData("2018-09-11T09:45:00", true, false, "2018-09-11T09:45:00Z")]
    [InlineData("2018-09-11T09:45", true, false, "2018-09-11T09:45:00Z")]
    [InlineData("2018-09-11", false, false, "")]
    [InlineData("11/09/2018 09:45", false, false, "")]
    [InlineData("2018-13-41T09:45:00Z", false, false, "")]
    [InlineData("", false, false, "")]
    [InlineData(null, false, false, "")]
    public void Timestamp_parser(string? text, bool ok, bool hasOffset, string utc)
    {
        Assert.Equal(ok, TimestampParser.TryParse(text, out var ts));
        if (!ok) return;
        Assert.Equal(hasOffset, ts.HasOffset);
        Assert.Equal(utc, TimestampParser.Format(ts.Instant));
    }

    // ------------------------------------------------------------ size caps

    [Fact]
    public void Manifest_over_the_size_cap_is_an_incomplete_finding()
    {
        var r = Fixture("events-10001.rsmf", new InspectionOptions { TimeZoneId = "America/New_York", MaxManifestBytes = 1000 });
        var f = Assert.Single(r.Findings, x => x.RuleId == "I-ZIP-001");
        Assert.Equal(FindingCategory.INCOMPLETE, f.Category);
        Assert.Null(r.Metrics.TotalEvents);
        Assert.True(r.Incomplete);
    }

    [Fact]
    public void Large_zip_spools_to_the_temp_folder_or_is_incomplete_without_one()
    {
        var temp = _fx.NewDir("spool");
        var spooled = Fixture("events-10001.rsmf", new InspectionOptions { TimeZoneId = "America/New_York", SpoolMemoryLimitBytes = 1024, TempDirectory = temp });
        Assert.Equal(10_001, spooled.Metrics.TotalEvents);
        Assert.Empty(Directory.GetFiles(temp)); // spool file is removed on close

        var none = Fixture("events-10001.rsmf", new InspectionOptions { TimeZoneId = "America/New_York", SpoolMemoryLimitBytes = 1024 });
        Has(none, "I-ZIP-001");
    }

    [Fact]
    public void Unknown_time_zone_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new InspectionOptions { TimeZoneId = "Mars/Olympus" }.ResolveZone());
        Assert.Equal("UTC", new InspectionOptions().ResolveZone().Id); // no zone chosen: UTC, not the PC's zone
    }
}
