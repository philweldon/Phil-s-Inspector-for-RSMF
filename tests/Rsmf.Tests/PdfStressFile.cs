using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace Rsmf.Tests;

/// <summary>
/// One synthetic RSMF file that exercises the PDF page layout: very long messages, many line breaks,
/// text without spaces, several scripts and emoji, real pictures, reactions, notices and two conversations.
/// It is not part of the fixture or sample sets.
/// </summary>
public static class PdfStressFile
{
    public const string FileName = "pdf-stress.rsmf";

    /// <summary>A real PNG picture (colour gradient with a grid), written without any imaging library.</summary>
    public static byte[] MakePng(int width, int height)
    {
        var raw = new byte[height * (1 + width * 3)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (1 + width * 3);
            for (var x = 0; x < width; x++)
            {
                var grid = x % 40 == 0 || y % 40 == 0;
                raw[row + 1 + x * 3] = grid ? (byte)255 : (byte)(x * 255 / width);
                raw[row + 2 + x * 3] = grid ? (byte)255 : (byte)(y * 255 / height);
                raw[row + 3 + x * 3] = grid ? (byte)255 : (byte)140;
            }
        }
        using var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw);

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8; header[9] = 2;                 // 8 bits per channel, RGB
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", packed.ToArray());
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void WriteBigEndian(byte[] target, int at, uint value)
    {
        target[at] = (byte)(value >> 24); target[at + 1] = (byte)(value >> 16); target[at + 2] = (byte)(value >> 8); target[at + 3] = (byte)value;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        s.Write(length);
        var body = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        s.Write(body);
        var crc = 0xFFFFFFFFu;
        foreach (var b in body)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        var sum = new byte[4];
        WriteBigEndian(sum, 0, ~crc);
        s.Write(sum);
    }

    public static byte[] Build()
    {
        var wide = MakePng(640, 360);
        var tall = MakePng(200, 900);
        string At(int minute) => new DateTimeOffset(2026, 3, 3, 14, 0, 0, TimeSpan.Zero).AddMinutes(minute).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        var paragraph = "This is a long paragraph of ordinary words that should wrap across several lines inside the bubble without being cut off at the right edge. ";

        var events = new List<JsonObject>
        {
            FixtureGenerator.Message("C1", "P1", At(0), "Short opening message.", "s1"),
            new() { ["type"] = "join", ["participant"] = "P3", ["conversation"] = "C1", ["timestamp"] = At(1), ["body"] = "Welcome \uD83D\uDC4B" },
            FixtureGenerator.Message("C1", "P2", At(2), string.Concat(Enumerable.Repeat(paragraph, 12)), "s2"),
            FixtureGenerator.Message("C1", "P3", At(3), string.Join("\n\n", Enumerable.Range(1, 90).Select(i => $"Paragraph {i}. " + paragraph + paragraph)), "s3"),
            FixtureGenerator.Message("C1", "P1", At(4), string.Join("\n", Enumerable.Range(1, 150).Select(i => "line " + i)), "s4"),
            FixtureGenerator.Message("C1", "P2", At(5), new string('W', 3000), "s5"),
            FixtureGenerator.Message("C1", "P3", At(6), "Scripts: café naïve Über  你好世界  こんにちは  안녕하세요  مرحبا بالعالم  שלום  สวัสดี  Привет  emoji 😀 👍 ❤️ 🎉", "s6"),
            FixtureGenerator.Message("C1", "P1", At(7), "A wide picture and a tall one:", "s7"),
            FixtureGenerator.Message("C1", "P2", At(8), "Tabs\tand\tcontrol \u0001\u0002 characters, and <b>markup</b> that must stay plain text: <script>alert(1)</script>", "s8"),
            new() { ["type"] = "disclaimer", ["participant"] = "P1", ["conversation"] = "C1", ["timestamp"] = At(9), ["body"] = "This conversation may be recorded for quality purposes." },
            new() { ["type"] = "leave", ["participant"] = "P3", ["conversation"] = "C1", ["timestamp"] = At(10) },
            FixtureGenerator.Message("C1", "P1", new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), "Next day.", "s9"),
            FixtureGenerator.Message("C1", "P2", null, "No timestamp on this one.", "s10"),
            FixtureGenerator.Message("C2", "P2", At(20), "Second conversation, first message.", "t1"),
            FixtureGenerator.Message("C2", "P4", At(21), "A sender with a very long display name.", "t2"),
        };
        events[7]["attachments"] = new JsonArray(
            new JsonObject { ["id"] = "wide.png", ["display"] = "wide.png", ["size"] = wide.Length },
            new JsonObject { ["id"] = "tall.png", ["display"] = "tall.png", ["size"] = tall.Length },
            new JsonObject { ["id"] = "report.docx", ["display"] = "Quarterly report \uD83D\uDCC4 (final).docx", ["size"] = 12 });
        events[0]["direction"] = "outgoing";
        events[2]["reactions"] = new JsonArray(
            new JsonObject { ["value"] = "👍", ["count"] = 12, ["participants"] = new JsonArray("P1") },
            new JsonObject { ["value"] = "❤️", ["count"] = 3, ["participants"] = new JsonArray("P3") },
            new JsonObject { ["value"] = "laugh", ["count"] = 1, ["participants"] = new JsonArray("P1") });
        events[2]["parent"] = "s1";
        events[5]["edits"] = new JsonArray(
            new JsonObject { ["participant"] = "P2", ["timestamp"] = At(6) },
            new JsonObject { ["participant"] = "P2", ["timestamp"] = At(7) });
        events[8]["deleted"] = true;

        var participants = new[]
        {
            FixtureGenerator.Participant("P1", "Avery Example"),
            FixtureGenerator.Participant("P2", "Blake Sample \uD83D\uDE0E"),
            FixtureGenerator.Participant("P3", "Casey 東京 Tanaka"),
            FixtureGenerator.Participant("P4", "Dr. Maximilian Alexander Featherstonehaugh-Cholmondeley of the Extremely Long Display Name Department, Third Floor"),
        };
        var conversations = new[]
        {
            FixtureGenerator.Conversation("C1", "2.0.0", "P1", "P2", "P3"),
            FixtureGenerator.Conversation("C2", "2.0.0", "P2", "P4"),
        };
        conversations[1]["display"] = "Weekend plans \uD83C\uDF89\uD83C\uDFD6\uFE0F";
        var manifest = FixtureGenerator.Manifest("2.0.0", events, participants, conversations);
        return FixtureGenerator.Wrap(manifest, null, ("wide.png", wide), ("tall.png", tall), ("report.docx", Encoding.ASCII.GetBytes("not opened   "))).Build();
    }
}
