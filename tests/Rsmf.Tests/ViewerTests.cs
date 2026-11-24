using System.Text.Json.Nodes;
using Rsmf.Core;
using Xunit;
using static Rsmf.Tests.FixtureGenerator;

namespace Rsmf.Tests;

[Collection("fixtures")]
public class ViewerTests
{
    private readonly FixtureFolder _fx;
    private static readonly InspectionOptions NewYork = new() { TimeZoneId = "America/New_York" };

    public ViewerTests(FixtureFolder fx) => _fx = fx;

    private ViewerDocument Open(string name) => ViewerDocument.Load(_fx.PathOf(name), NewYork, CancellationToken.None);

    [Fact]
    public void Valid_file_exposes_headers_text_manifest_conversations_and_messages()
    {
        using var v = Open("valid-2.0.rsmf");
        Assert.Empty(v.Unavailable);
        Assert.Equal("X-RSMF-Version", v.Headers[0].Name);
        Assert.Equal("Synthetic test conversation.", v.ExtractedText.Trim());
        Assert.StartsWith("{", v.ManifestText);
        Assert.Empty(v.Result.Findings);

        var c = Assert.Single(v.Conversations);
        Assert.Equal("Chat C1", c.Title);
        Assert.Equal(3, c.Messages.Count);
        var first = c.Messages[0];
        Assert.Equal(DisplayMarker, first.SenderName);
        Assert.Equal("2026-03-03 09:00:00 -05:00", first.TimestampText); // shown in the selected zone
        Assert.Contains(BodyMarker, first.Body);

        var withAttachment = c.Messages[2];
        var a = Assert.Single(withAttachment.Attachments);
        Assert.Equal("photo.png", a.Display);
        Assert.True(a.InArchive);
        Assert.True(a.IsInlineImage);
        Assert.Equal(Png, v.ReadInlineImage(a));
    }

    [Fact]
    public void Messages_are_chronological_and_untimed_events_come_last()
    {
        var m = Manifest("2.0.0", new[]
        {
            Message("C1", "P1", "2026-03-03T16:00:00Z", "third"),
            Message("C1", "P2", null, "no time"),
            Message("C1", "P1", "2026-03-03T09:00:00-05:00", "first"),
            Message("C1", "P9", "2026-03-03T15:00:00Z", "second"),
            Message("C7", "P1", "2026-03-03T15:00:00Z", "orphan"),
        });
        var path = Path.Combine(_fx.NewDir("viewer"), "order.rsmf");
        File.WriteAllBytes(path, Wrap(m).Build());
        using var v = ViewerDocument.Load(path, NewYork, CancellationToken.None);

        Assert.Equal(new[] { "first", "second", "third", "no time" }, v.Conversations[0].Messages.Select(x => x.Body));
        Assert.Equal("(no timestamp)", v.Conversations[0].Messages[3].TimestampText);
        Assert.Equal("P9 (unknown participant)", v.Conversations[0].Messages[1].SenderName);
        Assert.Equal(ViewerDocument.OrphanConversationId, v.Conversations[1].Id);
        Assert.Equal("orphan", Assert.Single(v.Conversations[1].Messages).Body);
    }

    [Fact]
    public void Chat_details_are_prepared_for_display()
    {
        using var v = Open("valid-2.0.rsmf");
        var m = v.Conversations[0].Messages;

        // P1 is the conversation's custodian and its first message is marked outgoing: shown on the right.
        Assert.True(m[0].AlignRight);
        Assert.False(m[1].AlignRight);
        Assert.Equal("outgoing", m[0].Direction);
        Assert.Equal(0, m[0].SenderIndex);
        Assert.Equal(1, m[1].SenderIndex);
        Assert.Equal("AE", m[0].SenderInitials);
        Assert.Equal("BS", m[1].SenderInitials);
        Assert.Equal("avatar.png", m[0].SenderAvatar);
        Assert.Equal("", m[1].SenderAvatar);
        Assert.Equal(Png, v.ReadAvatar(m[0].SenderAvatar));
        Assert.Null(v.ReadAvatar(""));

        Assert.All(m, x => Assert.True(x.IsMessage));
        Assert.All(m, x => Assert.Equal("Tuesday, 3 March 2026", x.DayText));
        Assert.Equal("09:00:00", m[0].TimeText);
        Assert.Equal("10:30:00", m[2].TimeText);

        var reaction = Assert.Single(m[1].Reactions);
        Assert.Equal("thumbsup", reaction.Value);                   // the value in the file
        Assert.Equal("\uD83D\uDC4D", reaction.Display);             // shown as the emoji it names (since 1.19.0)
        Assert.Equal("seen-it \u00D73", new ReactionView("seen-it", 3).Display);   // not an emoji name: shown as written
        Assert.Equal(1, m[2].EditCount);
        Assert.Equal(0, m[0].EditCount);
        Assert.Equal("m1", m[1].ParentId);
    }

    [Fact]
    public void Notices_untimed_events_and_unknown_senders_are_handled()
    {
        var join = new JsonObject { ["type"] = "join", ["conversation"] = "C1", ["participant"] = "P2", ["timestamp"] = "2026-03-04T03:30:00Z" };
        var m = Manifest("2.0.0", new[]
        {
            join,
            Message("C1", "P9", "2026-03-04T12:00:00Z", "from an unknown sender"),
            Message("C1", "P2", null, "no time"),
        });
        var path = Path.Combine(_fx.NewDir("viewer"), "chat.rsmf");
        File.WriteAllBytes(path, Wrap(m).Build());
        using var v = ViewerDocument.Load(path, NewYork, CancellationToken.None);
        var list = v.Conversations[0].Messages;

        Assert.False(list[0].IsMessage);
        Assert.Equal("Tuesday, 3 March 2026", list[0].DayText);   // 03:30Z is still the 3rd in New York
        Assert.Equal("22:30:00", list[0].TimeText);
        Assert.Equal("Wednesday, 4 March 2026", list[1].DayText);
        Assert.Equal(-1, list[1].SenderIndex);
        Assert.Equal("P", list[1].SenderInitials);                  // from the id, since the sender is unknown
        Assert.Equal("", list[2].DayText);
        Assert.Equal("", list[2].TimeText);
        Assert.False(list[2].AlignRight);                           // P2 is not the custodian
        Assert.True(list[1].SenderIndex < 0 && !list[1].AlignRight);
    }

    [Theory]
    [InlineData("Avery Example", "AE")]
    [InlineData("avery.example@test.invalid", "AE")]
    [InlineData("  single ", "S")]
    [InlineData("+1 555 0100", "#")]
    [InlineData("", "?")]
    [InlineData("---", "?")]
    public void Sender_initials(string name, string expected) => Assert.Equal(expected, ViewerDocument.Initials(name));

    [Theory]
    [InlineData("Hello hello HELLO", "hello", new[] { 0, 6, 12 })]
    [InlineData("aaaa", "aa", new[] { 0, 2 })]                 // non-overlapping
    [InlineData("price is $5.00 (net)", "$5.00 (", new[] { 9 })] // plain text, not a pattern
    [InlineData("nothing here", "xyz", new int[0])]
    [InlineData("", "a", new int[0])]
    [InlineData("abc", "", new int[0])]
    public void Text_search_finds_plain_case_insensitive_matches(string text, string term, int[] starts)
    {
        var hits = TextSearch.FindMatches(text, term);
        Assert.Equal(starts, hits.Select(h => h.Start));
        Assert.All(hits, h => Assert.Equal(term.Length, h.Length));
        Assert.Equal(starts.Length, TextSearch.Count(text, term));
        Assert.Equal(Math.Min(1, starts.Length), TextSearch.FindMatches(text, term, max: 1).Count);
        Assert.Empty(TextSearch.FindMatches(null, term));
    }

    [Fact]
    public void Message_hit_count_covers_body_sender_and_attachment_names()
    {
        using var v = Open("valid-2.0.rsmf");
        var m = v.Conversations[0].Messages;
        Assert.Equal(1, m[1].HitCount("reply"));
        Assert.Equal(0, m[0].HitCount("reply"));
        Assert.Equal(1, m[0].HitCount("avery"));            // sender name
        Assert.Equal(1, m[2].HitCount("PHOTO.png"));        // attachment name, counted once although id and display both match
        Assert.Equal(1, m[2].HitCount("attached"));         // body
        Assert.Equal(2, m[2].HitCount("e a") + m[2].HitCount("see")); // "see attached": one each
        Assert.All(m, x => Assert.Equal(x.HitCount("e") > 0, x.Matches("e")));
    }

    [Fact]
    public void Search_and_drill_down()
    {
        using var v = Open("broken-references.rsmf");
        var c = v.Conversations[0];
        Assert.Equal("orphan reply", Assert.Single(ViewerDocument.Search(c, "ORPHAN")).Body);
        Assert.Equal(c.Messages.Count, ViewerDocument.Search(c, "  ").Count);
        Assert.Empty(ViewerDocument.Search(c, "no such text"));

        var finding = v.Result.Findings.First(f => f.RuleId == "F-REF-004");
        var hit = v.FindEvent(finding.Location);
        Assert.NotNull(hit);
        Assert.Equal("orphan reply", hit!.Value.Message.Body);
        Assert.Equal(hit.Value.Message, v.FindRecord(finding.RecordId)!.Value.Message);
        Assert.Null(v.FindEvent("$.participants[0].avatar"));
    }

    [Fact]
    public void Non_image_attachments_are_listed_by_name_only_and_disguised_files_are_not_decoded()
    {
        var e = Message("C1", "P1", "2026-03-03T14:00:00Z");
        e["attachments"] = new JsonArray(
            new JsonObject { ["id"] = "report.docx", ["display"] = "report.docx" },
            new JsonObject { ["id"] = "fake.png", ["display"] = "fake.png" },
            new JsonObject { ["id"] = "gone.gif", ["display"] = "gone.gif" });
        var path = Path.Combine(_fx.NewDir("viewer"), "att.rsmf");
        File.WriteAllBytes(path, Wrap(Manifest("2.0.0", new[] { e }), null,
            ("report.docx", new byte[] { 1, 2, 3 }), ("fake.png", "MZ this is not an image"u8.ToArray())).Build());
        using var v = ViewerDocument.Load(path, NewYork, CancellationToken.None);

        var a = v.Conversations[0].Messages[0].Attachments;
        Assert.False(a[0].IsInlineImage);
        Assert.Null(v.ReadInlineImage(a[0]));
        Assert.True(a[1].IsInlineImage);
        Assert.Null(v.ReadInlineImage(a[1])); // content is not image data
        Assert.False(a[2].InArchive);
        Assert.False(a[2].IsInlineImage);
    }

    [Theory]
    [InlineData("damaged-mime.rsmf", "not a parseable MIME message", 0)]
    [InlineData("damaged-zip.rsmf", "rsmf.zip could not be opened", 0)]
    [InlineData("missing-manifest.rsmf", "rsmf_manifest.json could not be read", 0)]
    public void Malformed_files_show_what_parsed_and_state_what_is_unavailable(string name, string statement, int conversations)
    {
        using var v = Open(name);
        Assert.Contains(v.Unavailable, u => u.Contains(statement));
        Assert.Equal(conversations, v.Conversations.Count);
        Assert.NotEmpty(v.Result.Findings);
        if (name != "damaged-mime.rsmf")
        {
            Assert.NotEmpty(v.Headers);                 // headers parsed safely and are still shown
            Assert.Contains("Synthetic", v.ExtractedText);
        }
    }

    [Fact]
    public void Invalid_json_manifest_still_shows_raw_text()
    {
        var b = Valid("2.0.0");
        b.Zip = RsmfBuilder.MakeZip("{ \"version\": \"2.0.0\", oops");
        var path = Path.Combine(_fx.NewDir("viewer"), "badjson.rsmf");
        File.WriteAllBytes(path, b.Build());
        using var v = ViewerDocument.Load(path, NewYork, CancellationToken.None);
        Assert.Contains("oops", v.ManifestText);
        Assert.Contains(v.Unavailable, u => u.Contains("not valid JSON"));
    }

    [Fact]
    public void Image_signature_check()
    {
        Assert.True(ViewerDocument.LooksLikeImage(Png));
        Assert.True(ViewerDocument.LooksLikeImage(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
        Assert.True(ViewerDocument.LooksLikeImage("GIF89a...."u8.ToArray()));
        Assert.False(ViewerDocument.LooksLikeImage("<svg onload=x>"u8.ToArray()));
        Assert.False(ViewerDocument.LooksLikeImage(Array.Empty<byte>()));
    }
}
