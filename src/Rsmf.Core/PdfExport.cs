using System.Globalization;
using System.Text;

namespace Rsmf.Core;

public enum PdfStatus { Converted, NotConverted, Cancelled, Failed }

/// <summary>One source file in a PDF conversion run.</summary>
public sealed class PdfEntry
{
    public required string Source { get; init; }
    /// <summary>Path relative to the chosen input folder (just the file name for a single file).</summary>
    public required string RelativePath { get; init; }
    public string PdfPath { get; set; } = "";
    public PdfStatus Status { get; set; } = PdfStatus.NotConverted;
    public int Pages { get; set; }
    public int Conversations { get; set; }
    public int Events { get; set; }
    public long PdfBytes { get; set; }
    public string Note { get; set; } = "";
}

public sealed record PdfProgress(int FilesDone, int FilesTotal, string CurrentFile, int PagesDone);

/// <summary>The result of a PDF conversion run: the folder written and one entry per source file.</summary>
public sealed class PdfRun
{
    public required string RunFolder { get; init; }
    public required string Input { get; init; }
    public required string ZoneId { get; init; }
    public required int Dpi { get; init; }
    public DateTime StartedUtc { get; init; }
    public List<PdfEntry> Entries { get; } = new();
    public List<string> Notes { get; } = new();
    public bool Cancelled { get; set; }

    public int Converted => Entries.Count(e => e.Status == PdfStatus.Converted);
    public int NotConverted => Entries.Count - Converted;
    public int Pages => Entries.Sum(e => e.Pages);
}

/// <summary>
/// File discovery, output naming and the conversion log for "Convert RSMF folder to PDF".
/// Rendering the pages is done by the app; nothing here reads message content.
/// </summary>
public static class PdfExport
{
    public const string LogFileName = "pdf_conversion_log.csv";
    public const string SummaryFileName = "pdf_summary.txt";

    /// <summary>Creates a new, uniquely named folder for one conversion run under the chosen output folder.</summary>
    public static string NewRunFolder(string outputRoot, DateTime startedLocal, string folderPrefix = "pdf-")
    {
        string folder;
        do
        {
            folder = Path.Combine(Path.GetFullPath(outputRoot),
                folderPrefix + startedLocal.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6]);
        } while (Directory.Exists(folder));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// Lists the .rsmf files under a folder (or the one file given), in a stable order. Folders that cannot
    /// be listed are skipped and named in <paramref name="notes"/>. Links to other folders are not followed.
    /// </summary>
    public static List<PdfEntry> Discover(string input, InspectionOptions options, List<string> notes, CancellationToken ct)
    {
        var result = new List<PdfEntry>();
        input = Path.GetFullPath(input);
        if (File.Exists(input))
        {
            result.Add(new PdfEntry { Source = input, RelativePath = Path.GetFileName(input) });
            return result;
        }
        if (!Directory.Exists(input))
        {
            notes.Add("The folder does not exist: " + input);
            return result;
        }

        var pending = new Stack<(string Dir, string Rel)>();
        pending.Push((input, ""));
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, rel) = pending.Pop();
            List<FileSystemInfo> entries;
            try
            {
                entries = options.DirectoryLister(dir).OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                notes.Add($"Folder not readable, skipped: {(rel.Length == 0 ? "." : rel)} ({ex.GetType().Name})");
                continue;
            }
            foreach (var e in Enumerable.Reverse(entries).Where(e => e is DirectoryInfo))
            {
                if ((e.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                pending.Push((e.FullName, rel.Length == 0 ? e.Name : Path.Combine(rel, e.Name)));
            }
            foreach (var e in entries.Where(e => e is FileInfo))
            {
                if (!string.Equals(e.Extension, ".rsmf", StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(new PdfEntry { Source = e.FullName, RelativePath = rel.Length == 0 ? e.Name : Path.Combine(rel, e.Name) });
            }
        }
        return result;
    }

    /// <summary>
    /// Gives every entry its PDF path inside the run folder: the same relative path with ".pdf" in place of
    /// ".rsmf". Two sources that would collide (names differing only by case) get "-2", "-3" and so on.
    /// </summary>
    public static void AssignTargets(string runFolder, IEnumerable<PdfEntry> entries)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            var dir = Path.GetDirectoryName(e.RelativePath) ?? "";
            var stem = Path.GetFileNameWithoutExtension(e.RelativePath);
            var candidate = Path.Combine(dir, stem + ".pdf");
            for (var n = 2; !used.Add(candidate); n++) candidate = Path.Combine(dir, stem + "-" + n.ToString(CultureInfo.InvariantCulture) + ".pdf");
            e.PdfPath = Path.Combine(runFolder, candidate);
        }
    }

    /// <summary>Writes the conversion log and a plain-language summary into the run folder.</summary>
    public static void WriteLog(PdfRun run)
    {
        using var culture = InvariantScope.Enter();
        var csv = new StringBuilder();
        csv.Append(ReportWriter.Csv("source_file", "status", "pdf_file", "pages", "conversations", "events", "pdf_bytes", "note")).Append("\r\n");
        foreach (var e in run.Entries)
        {
            csv.Append(ReportWriter.Csv(
                e.RelativePath,
                StatusText(e.Status),
                e.Status == PdfStatus.Converted ? Path.GetRelativePath(run.RunFolder, e.PdfPath) : "",
                e.Pages, e.Conversations, e.Events, e.PdfBytes, e.Note)).Append("\r\n");
        }
        File.WriteAllText(Path.Combine(run.RunFolder, LogFileName), csv.ToString(), new UTF8Encoding(true));

        var s = new StringBuilder();
        s.AppendLine($"{AppInfo.Name} {AppInfo.Version} - conversion of RSMF files to PDF");
        s.AppendLine();
        s.AppendLine($"Started (UTC):        {run.StartedUtc:yyyy-MM-dd HH:mm:ss}");
        s.AppendLine($"Input:                {run.Input}");
        s.AppendLine($"Page size:            US Letter, pictures at {run.Dpi} dots per inch");
        s.AppendLine($"RSMF files found:     {run.Entries.Count}");
        s.AppendLine($"Converted:            {run.Converted} file(s), {run.Pages} page(s)");
        s.AppendLine($"Not converted:        {run.NotConverted}{(run.NotConverted > 0 ? "  (see " + LogFileName + " for the reason per file)" : "")}");
        s.AppendLine($"Final status:         {(run.Cancelled ? "cancelled (not every file was converted)" : run.NotConverted > 0 ? "completed with exceptions" : "completed")}");
        foreach (var note in run.Notes) s.AppendLine("Note:                 " + note);
        s.AppendLine();
        s.AppendLine("What these PDFs are");
        s.AppendLine("- One PDF per RSMF file. Each conversation starts on a new page; events are in time order.");
        s.AppendLine("- Nothing that is not in the RSMF file is added to a page: no header, footer, page number, file");
        s.AppendLine("  name, tool name, date, note or count. The PDFs carry no title, author or other document");
        s.AppendLine("  information either. This file and " + LogFileName + " are the only record of the conversion.");
        s.AppendLine("- Shown from the file: the conversation's name, platform and type; each event's sender, timestamp");
        s.AppendLine("  exactly as written, text, inline pictures, attachment names, reactions, and the event type for");
        s.AppendLine("  events that are not messages. The words parent, deleted, reactions and edits are RSMF's own");
        s.AppendLine("  field names, used as labels; the number after \"edits\" is how many edits the file records.");
        s.AppendLine("- Bubbles are blue on the right for messages the file marks as outgoing or as sent by the");
        s.AppendLine("  conversation's custodian, and green on the left for everyone else. If the file marks neither,");
        s.AppendLine("  every bubble is green. Colour and placement are presentation only.");
        s.AppendLine("- THE PDFS CONTAIN MESSAGE TEXT, PARTICIPANT NAMES AND INLINE PICTURES. Handle them like the source files.");
        s.AppendLine("- Every page is a picture, so the text in the PDFs cannot be searched or copied.");
        s.AppendLine("- Attachments other than inline pictures are listed by name only; they are never opened.");
        s.AppendLine("- These are renderings for review made by this tool. They are not certified copies and say nothing");
        s.AppendLine("  about how Relativity or any other system would display or accept the files. (That statement is");
        s.AppendLine("  made here because it is not printed on the pages.)");
        File.WriteAllText(Path.Combine(run.RunFolder, SummaryFileName), s.ToString(), new UTF8Encoding(true));
    }

    public static string StatusText(PdfStatus status) => status switch
    {
        PdfStatus.Converted => "converted",
        PdfStatus.Cancelled => "not converted (cancelled)",
        PdfStatus.Failed => "not converted (error)",
        _ => "not converted",
    };

    /// <summary>
    /// Cuts long text into pieces that each fit comfortably on a page: at most <paramref name="maxChars"/>
    /// characters and <paramref name="maxLines"/> line breaks, preferring to cut at a line break, then at a space.
    /// Joined together the pieces give back the original text, except that the single line break or space at a cut is dropped.
    /// </summary>
    public static List<string> SplitForPages(string text, int maxChars = 800, int maxLines = 20)
    {
        var pieces = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var end = PieceLimit(text, start, maxChars, maxLines);
            if (end < text.Length && text[end] != '\n')
            {
                var floor = start + (end - start) / 2;
                var cut = text.LastIndexOf('\n', end - 1, end - floor);
                if (cut < 0) cut = text.LastIndexOf(' ', end - 1, end - floor);
                if (cut > start) end = cut;
                else if (char.IsLowSurrogate(text[end]) && end - 1 > start) end--;   // never split a surrogate pair
            }
            pieces.Add(text[start..end].TrimEnd('\r'));
            start = end;
            if (start < text.Length && (text[start] == '\n' || text[start] == ' ')) start++;
        }
        return pieces;
    }

    /// <summary>
    /// The furthest a piece starting at <paramref name="start"/> may reach: at most <paramref name="maxChars"/>
    /// characters, stopping at the line break that would be one too many. Returns the index just past the piece.
    /// </summary>
    public static int PieceLimit(string text, int start, int maxChars = 800, int maxLines = 20)
    {
        var limit = Math.Min(text.Length, start + maxChars);
        var lines = 0;
        for (var i = start; i < limit; i++)
        {
            if (text[i] != '\n') continue;
            if (++lines > maxLines) return i;
        }
        return limit;
    }
}
