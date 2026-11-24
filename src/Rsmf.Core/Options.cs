using NodaTime;

namespace Rsmf.Core;

public enum TimeWindowMode
{
    /// <summary>Check Rule A (calendar day) and Rule B (elapsed span).</summary>
    Both,
    /// <summary>Check Rule B only.</summary>
    ElapsedOnly,
}

/// <summary>All settings and thresholds for an inspection or scan. Every value is echoed in run.json.</summary>
public sealed class InspectionOptions
{
    /// <summary>L-SIZE-001 threshold in bytes. A file larger than this raises the finding.</summary>
    public long SizeThresholdBytes { get; init; } = RuleCatalog.DefaultSizeThresholdBytes;

    /// <summary>Optional Q-PART-001 threshold. Null disables the rule.</summary>
    public int? ParticipantWarningThreshold { get; init; }

    /// <summary>IANA zone id used for Rule A. Null means the PC's zone.</summary>
    public string? TimeZoneId { get; init; }

    public TimeWindowMode TimeWindowMode { get; init; } = TimeWindowMode.Both;

    /// <summary>Include message bodies and personal metadata values in report output.</summary>
    public bool IncludeSensitive { get; init; }

    /// <summary>Largest manifest (uncompressed) that will be loaded into memory.</summary>
    public long MaxManifestBytes { get; init; } = 512L * 1024 * 1024;

    /// <summary>Largest decoded rsmf.zip held in memory; larger archives spool to <see cref="TempDirectory"/>.</summary>
    public long SpoolMemoryLimitBytes { get; init; } = 128L * 1024 * 1024;

    /// <summary>How many files a scan inspects at the same time. 0 picks automatically (up to 4); 1 inspects one at a time.</summary>
    public int MaxParallelFiles { get; init; }

    /// <summary>Files at least this large are inspected with nothing else running, so memory use stays bounded.</summary>
    public long LargeFileBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>Folder for temporary spool files (deleted on close). Null disables disk spooling.</summary>
    public string? TempDirectory { get; init; }

    /// <summary>Opens a source file read-only. Replaceable for tests.</summary>
    public Func<string, Stream> FileOpener { get; init; } = DefaultOpen;

    /// <summary>Lists the entries of a directory. Replaceable for tests.</summary>
    public Func<string, IEnumerable<FileSystemInfo>> DirectoryLister { get; init; } = DefaultList;

    public static IEnumerable<FileSystemInfo> DefaultList(string directory) =>
        new DirectoryInfo(directory).EnumerateFileSystemInfos();

    public static Stream DefaultOpen(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);

    public DateTimeZone ResolveZone()
    {
        // No zone chosen: UTC, the same on every PC. (Up to 1.24.1 this was the PC's own zone.)
        if (string.IsNullOrWhiteSpace(TimeZoneId)) return DateTimeZone.Utc;
        return DateTimeZoneProviders.Tzdb.GetZoneOrNull(TimeZoneId!)
               ?? throw new ArgumentException($"Unknown IANA time zone: {TimeZoneId}");
    }

    public InspectionOptions With(string? tempDirectory) => new()
    {
        SizeThresholdBytes = SizeThresholdBytes,
        ParticipantWarningThreshold = ParticipantWarningThreshold,
        TimeZoneId = TimeZoneId,
        TimeWindowMode = TimeWindowMode,
        IncludeSensitive = IncludeSensitive,
        MaxManifestBytes = MaxManifestBytes,
        SpoolMemoryLimitBytes = SpoolMemoryLimitBytes,
        TempDirectory = tempDirectory,
        MaxParallelFiles = MaxParallelFiles,
        LargeFileBytes = LargeFileBytes,
        FileOpener = FileOpener,
        DirectoryLister = DirectoryLister,
    };
}
