using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MimeKit;

namespace Rsmf.Core;

public sealed record HeaderEntry(string Name, string Value, int Index);

public sealed record ZipMember(string FullName, long Length);

/// <summary>
/// A parsed .rsmf file: outer headers, the rsmf.zip listing and the manifest. Holds the source file
/// open read-only until disposed. Whatever parsed safely is available even when later layers failed.
/// </summary>
public sealed class RsmfDocument : IDisposable
{
    public const string ManifestName = "rsmf_manifest.json";
    public const string ZipName = "rsmf.zip";

    public required string Path { get; init; }
    public long FileSize { get; internal set; }
    public List<Finding> Findings { get; } = new();
    public List<HeaderEntry> Headers { get; } = new();
    public List<ZipMember> ZipMembers { get; } = new();
    /// <summary>Checks that were skipped, with the reason.</summary>
    public List<string> SkippedChecks { get; } = new();
    public string? BodyText { get; internal set; }
    public string? ManifestText { get; internal set; }
    public JsonDocument? Manifest { get; internal set; }
    public bool Readable { get; internal set; }
    public bool MimeParsed { get; internal set; }
    public bool ZipOpened { get; internal set; }
    public bool ManifestParsed => Manifest is not null;

    private Stream? _source;
    private Stream? _spool;
    private ZipArchive? _archive;
    private MimeMessage? _message;

    public string? GetHeader(string name) =>
        Headers.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    /// <summary>Reads a ZIP member into memory, or returns null when absent, unreadable or larger than the cap.</summary>
    public byte[]? ReadMember(string fullName, long maxBytes)
    {
        var entry = _archive?.GetEntry(fullName);
        if (entry is null || entry.Length > maxBytes) return null;
        try
        {
            using var s = entry.Open();
            return ReadCapped(s, maxBytes, CancellationToken.None);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return null;
        }
    }

    internal static byte[]? ReadCapped(Stream s, long maxBytes, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (ms.Length + n > maxBytes) return null;
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }

    private void Skip(string what, string because)
    {
        SkippedChecks.Add($"{what}: {because}");
        Findings.Add(RuleCatalog.Make("I-PARSE-001", because, "(file)", detail: $"skipped {what}"));
    }

    /// <summary>Opens and parses a file. Never throws for bad content; cancellation propagates.</summary>
    public static RsmfDocument Open(string path, InspectionOptions options, bool loadText, CancellationToken ct)
    {
        var doc = new RsmfDocument { Path = path };
        try
        {
            doc.Load(options, loadText, ct);
        }
        catch (OperationCanceledException)
        {
            doc.Dispose();
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            doc.Findings.Add(RuleCatalog.Make("I-FILE-001", ex.GetType().Name, "(file)", detail: "read failed part-way through the file"));
            doc.SkippedChecks.Add("remaining checks: " + ex.GetType().Name);
        }
        return doc;
    }

    private void Load(InspectionOptions options, bool loadText, CancellationToken ct)
    {
        const string allLayers = "MIME, ZIP and manifest checks";
        try
        {
            _source = options.FileOpener(Path);
            FileSize = _source.Length;
            Readable = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            Findings.Add(RuleCatalog.Make("I-FILE-001", ex.GetType().Name, "(file)"));
            SkippedChecks.Add($"{allLayers}: file could not be opened ({ex.GetType().Name})");
            return;
        }

        // ---- MIME layer -------------------------------------------------------------------
        try
        {
            // persistent: part content stays as a window on the source stream instead of being copied to memory.
            var parser = new MimeParser(_source, MimeFormat.Entity, persistent: true);
            _message = parser.ParseMessage(ct);
        }
        catch (FormatException ex)
        {
            Findings.Add(RuleCatalog.Make("F-MIME-001", "MIME parse error: " + ex.GetType().Name, "(file)"));
            Skip(allLayers, "the file is not a parseable MIME message");
            return;
        }

        var index = 0;
        foreach (var h in _message.Headers)
            Headers.Add(new HeaderEntry(h.Field, (h.Value ?? "").Trim(), index++));

        if (Headers.Count == 0)
        {
            Findings.Add(RuleCatalog.Make("F-MIME-001", "no message headers found", "(file)"));
            Skip(allLayers, "the file is not a parseable MIME message");
            return;
        }
        MimeParsed = true;

        var parts = _message.BodyParts.OfType<MimePart>().ToList();
        var attachments = parts.Where(p => p.IsAttachment).ToList();
        var zipPart = attachments.FirstOrDefault(p => IsZipName(p.ContentDisposition?.FileName))
                      ?? parts.FirstOrDefault(p => IsZipName(p.ContentDisposition?.FileName) || IsZipName(p.ContentType?.Name));

        if (attachments.Count != 1)
            Findings.Add(RuleCatalog.Make("F-MIME-005", $"{attachments.Count} attachment parts", "MIME parts"));
        if (zipPart is null)
        {
            Findings.Add(RuleCatalog.Make("F-MIME-005", "no part with filename rsmf.zip", "MIME parts"));
            Skip("ZIP and manifest checks", "there is no rsmf.zip attachment");
        }
        else
        {
            if (!zipPart.IsAttachment)
                Findings.Add(RuleCatalog.Make("F-MIME-005", "rsmf.zip part is not Content-Disposition: attachment", "Content-Disposition"));
            else if (!string.Equals(zipPart.ContentDisposition?.FileName, ZipName, StringComparison.Ordinal))
                Findings.Add(RuleCatalog.Make("F-MIME-005", "filename differs from \"rsmf.zip\" in letter case", "Content-Disposition"));
            if (zipPart.ContentTransferEncoding != ContentEncoding.Base64)
                Findings.Add(RuleCatalog.Make("F-MIME-005", "Content-Transfer-Encoding: " + EncodingToken(zipPart.ContentTransferEncoding), "Content-Transfer-Encoding"));
        }

        if (loadText)
        {
            try { BodyText = _message.TextBody; }
            catch (Exception ex) when (ex is FormatException or IOException or ArgumentException) { BodyText = null; }
        }

        if (zipPart?.Content is null) return;

        // ---- ZIP layer --------------------------------------------------------------------
        var raw = zipPart.Content.Stream!;
        var encodedLength = raw.CanSeek ? raw.Length : FileSize;
        if (encodedLength <= options.SpoolMemoryLimitBytes)
        {
            _spool = new MemoryStream();
        }
        else if (options.TempDirectory is not null)
        {
            Directory.CreateDirectory(options.TempDirectory);
            var tmp = System.IO.Path.Combine(options.TempDirectory, "spool-" + Guid.NewGuid().ToString("N") + ".tmp");
            _spool = new FileStream(tmp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
        }
        else
        {
            Findings.Add(RuleCatalog.Make("I-ZIP-001", $"{encodedLength} encoded bytes", "rsmf.zip",
                detail: "rsmf.zip is larger than the in-memory limit and no temporary folder is available",
                expected: $"<= {options.SpoolMemoryLimitBytes} bytes"));
            SkippedChecks.Add("ZIP and manifest checks: rsmf.zip exceeds the in-memory limit");
            return;
        }

        long invalidBase64 = 0;
        if (zipPart.ContentTransferEncoding == ContentEncoding.Base64)
        {
            if (raw.CanSeek) raw.Position = 0;
            var scan = new Base64ScanStream(raw, ct);
            using (var decoded = new MimeKit.IO.FilteredStream(scan))
            {
                decoded.Add(MimeKit.IO.Filters.DecoderFilter.Create(ContentEncoding.Base64));
                decoded.CopyTo(_spool, 81920);
            }
            invalidBase64 = scan.InvalidCount;
        }
        else
        {
            zipPart.Content.DecodeTo(_spool, ct);
        }
        if (invalidBase64 > 0)
            Findings.Add(RuleCatalog.Make("F-MIME-006", $"{invalidBase64} characters outside the base64 alphabet", "rsmf.zip"));

        _spool.Position = 0;
        try
        {
            _archive = new ZipArchive(_spool, ZipArchiveMode.Read, leaveOpen: true);
            foreach (var e in _archive.Entries)
            {
                // Directory entries end with a slash and have no content.
                if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) continue;
                ZipMembers.Add(new ZipMember(e.FullName, e.Length));
            }
            ZipOpened = true;
        }
        catch (InvalidDataException)
        {
            _archive = null;
            Findings.Add(RuleCatalog.Make("F-MIME-006", "attachment is not a readable ZIP archive", "rsmf.zip"));
            Skip("manifest checks", "rsmf.zip could not be opened");
            return;
        }

        var entry = _archive.GetEntry(ManifestName);
        if (entry is null)
        {
            var nested = ZipMembers.Count(m => m.FullName.EndsWith("/" + ManifestName, StringComparison.OrdinalIgnoreCase)
                                               || string.Equals(m.FullName, ManifestName, StringComparison.OrdinalIgnoreCase));
            Findings.Add(RuleCatalog.Make("F-ZIP-001",
                nested > 0 ? "manifest found only in a subfolder or with different letter case" : "no rsmf_manifest.json in the archive",
                "rsmf.zip"));
            Skip("manifest checks", "rsmf_manifest.json is not at the ZIP root");
            return;
        }

        // ---- manifest ---------------------------------------------------------------------
        if (entry.Length > options.MaxManifestBytes)
        {
            ManifestTooLarge(entry.Length, options);
            return;
        }
        byte[]? bytes;
        try
        {
            using var es = entry.Open();
            bytes = ReadCapped(es, options.MaxManifestBytes, ct);
        }
        catch (InvalidDataException)
        {
            Findings.Add(RuleCatalog.Make("F-MIME-006", "ZIP member data is corrupt", "rsmf.zip/" + ManifestName));
            Skip("manifest checks", "rsmf_manifest.json could not be decompressed");
            return;
        }
        if (bytes is null)
        {
            ManifestTooLarge(entry.Length, options);
            return;
        }

        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            var parsed = JsonDocument.Parse(ms, new JsonDocumentOptions { MaxDepth = 64 });
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                var kind = parsed.RootElement.ValueKind;
                parsed.Dispose();
                Findings.Add(RuleCatalog.Make("F-MAN-001", "root is " + kind, "$"));
                Skip("manifest checks", "the manifest root is not a JSON object");
                return;
            }
            Manifest = parsed;
        }
        catch (JsonException ex)
        {
            Findings.Add(RuleCatalog.Make("F-MAN-001",
                $"JSON error at line {ex.LineNumber + 1}, position {ex.BytePositionInLine + 1}", "$"));
            Skip("manifest checks", "the manifest is not well-formed JSON");
            if (loadText) ManifestText = DecodeText(bytes);
            return;
        }
        if (loadText) ManifestText = DecodeText(bytes);
    }

    private void ManifestTooLarge(long length, InspectionOptions options)
    {
        Findings.Add(RuleCatalog.Make("I-ZIP-001", $"{length} bytes", "rsmf.zip/" + ManifestName,
            detail: "rsmf_manifest.json exceeds the in-memory size cap", expected: $"<= {options.MaxManifestBytes} bytes"));
        SkippedChecks.Add("manifest checks: rsmf_manifest.json exceeds the size cap");
    }

    private static string DecodeText(byte[] bytes)
    {
        var text = new UTF8Encoding(false, false).GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }

    /// <summary>The header token for a transfer encoding, as it appears in a MIME file.</summary>
    internal static string EncodingToken(ContentEncoding encoding) => encoding switch
    {
        ContentEncoding.SevenBit => "7bit",
        ContentEncoding.EightBit => "8bit",
        ContentEncoding.Binary => "binary",
        ContentEncoding.Base64 => "base64",
        ContentEncoding.QuotedPrintable => "quoted-printable",
        ContentEncoding.UUEncode => "x-uuencode",
        _ => "(none declared)",
    };

    private static bool IsZipName(string? name) => string.Equals(name, ZipName, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        Manifest?.Dispose();
        Manifest = null;
        _archive?.Dispose();
        _archive = null;
        _spool?.Dispose();
        _spool = null;
        _message?.Dispose();
        _message = null;
        _source?.Dispose();
        _source = null;
    }

    /// <summary>Read-only pass-through that counts bytes outside the base64 alphabet.</summary>
    private sealed class Base64ScanStream : Stream
    {
        private static readonly bool[] Valid = BuildTable();
        private readonly Stream _inner;
        private readonly CancellationToken _ct;
        public long InvalidCount { get; private set; }

        public Base64ScanStream(Stream inner, CancellationToken ct) { _inner = inner; _ct = ct; }

        private static bool[] BuildTable()
        {
            var t = new bool[256];
            foreach (var c in "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=\r\n\t ") t[c] = true;
            return t;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _ct.ThrowIfCancellationRequested();
            var n = _inner.Read(buffer, offset, count);
            for (var i = offset; i < offset + n; i++)
                if (!Valid[buffer[i]]) InvalidCount++;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
