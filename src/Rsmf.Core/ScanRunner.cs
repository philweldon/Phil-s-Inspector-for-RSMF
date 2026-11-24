using System.Reflection;

namespace Rsmf.Core;

public static class AppInfo
{
    public const string Name = "Phil's Inspector for RSMF files";

    /// <summary>Application version, from Directory.Build.props.</summary>
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}

public sealed record ScanProgress(string Phase, int FilesDone, int FilesTotal, string? Current);

public sealed record ScanException(string RelativePath, string Kind, string Reason);

public enum RunStatus { Completed, CompletedWithExceptions, Cancelled, Failed }

public sealed class RunResult
{
    public required string RunId { get; init; }
    public required string RunFolder { get; init; }
    public required string InputPath { get; init; }
    public string InputRoot { get; set; } = "";
    public required InspectionOptions Options { get; init; }
    public DateTimeOffset Started { get; init; }
    public DateTimeOffset Ended { get; set; }
    public string ZoneId { get; set; } = "";
    public List<FileResult> Files { get; } = new();
    /// <summary>Findings that belong to the scan rather than to one file (inaccessible directories).</summary>
    public List<(string RelativePath, Finding Finding)> ScanFindings { get; } = new();
    public List<ScanException> Exceptions { get; } = new();
    public RunStatus Status { get; set; } = RunStatus.Completed;
    public string FailureMessage { get; set; } = "";
    public bool CoverageComplete => Status == RunStatus.Completed;

    public IEnumerable<FileResult> Candidates => Files.Where(f => f.Kind == FileKind.RsmfCandidate);
}

/// <summary>Scans a folder (or one file), inspects every .rsmf candidate and always writes the audit reports.</summary>
public static class ScanRunner
{
    public static RunResult Run(string inputPath, string outputRoot, InspectionOptions options,
        IProgress<ScanProgress>? progress = null, CancellationToken ct = default, string folderPrefix = "run-")
    {
        using var culture = InvariantScope.Enter();
        var started = DateTimeOffset.Now;
        Directory.CreateDirectory(outputRoot);
        string runId, runFolder;
        do
        {
            runId = Guid.NewGuid().ToString("N")[..6];
            runFolder = Path.Combine(Path.GetFullPath(outputRoot), folderPrefix + started.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + runId);
        } while (Directory.Exists(runFolder));
        Directory.CreateDirectory(runFolder);

        var tempDir = Path.Combine(runFolder, ".tmp");
        var run = new RunResult
        {
            RunId = runId, RunFolder = runFolder, InputPath = Path.GetFullPath(inputPath),
            Options = options.With(tempDir), Started = started,
        };

        try
        {
            run.ZoneId = run.Options.ResolveZone().Id;
            Discover(run, ct);
            InspectAll(run, progress, ct);
            if (run.Status == RunStatus.Completed && (run.Exceptions.Count > 0 || run.Files.Any(f => f.Incomplete)))
                run.Status = RunStatus.CompletedWithExceptions;
        }
        catch (OperationCanceledException)
        {
            run.Status = RunStatus.Cancelled;
            MarkNotChecked(run);
        }
        catch (Exception ex)
        {
            run.Status = RunStatus.Failed;
            run.FailureMessage = $"{ex.GetType().Name}: {ex.Message}";
            MarkNotChecked(run);
        }
        finally
        {
            run.Ended = DateTimeOffset.Now;
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            ReportWriter.WriteAll(run);
            progress?.Report(new ScanProgress("done", run.Candidates.Count(f => f.Checked), run.Candidates.Count(), null));
        }
        return run;
    }

    private static void MarkNotChecked(RunResult run)
    {
        foreach (var f in run.Candidates.Where(f => !f.Checked && f.Findings.Count == 0))
        {
            f.Findings.Add(RuleCatalog.Make("I-RUN-001",
                run.Status == RunStatus.Cancelled ? "scan cancelled" : "scan failed", "(file)"));
            f.SkippedChecks.Add("all checks: " + (run.Status == RunStatus.Cancelled ? "scan cancelled" : "scan failed"));
        }
    }

    private static void Discover(RunResult run, CancellationToken ct)
    {
        var input = run.InputPath;
        if (File.Exists(input))
        {
            run.InputRoot = Path.GetDirectoryName(input) ?? input;
            long size = 0;
            try { size = new FileInfo(input).Length; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            run.Files.Add(new FileResult { Path = input, RelativePath = Path.GetFileName(input), SizeBytes = size });
            return;
        }
        if (!Directory.Exists(input)) throw new DirectoryNotFoundException("Input path not found: " + input);
        run.InputRoot = input;
        Walk(input, run, ct);
    }

    private static void Walk(string dir, RunResult run, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        List<FileSystemInfo> entries;
        try
        {
            entries = run.Options.DirectoryLister(dir).OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            var rel = Rel(run, dir);
            run.Exceptions.Add(new ScanException(rel, "directory", "could not be listed: " + ex.GetType().Name));
            run.ScanFindings.Add((rel, RuleCatalog.Make("I-SCAN-001", ex.GetType().Name, "(directory)")));
            return;
        }

        foreach (var e in entries)
        {
            var rel = Rel(run, e.FullName);
            bool isLink, isDir;
            try
            {
                // Symbolic links and junctions are never followed. (Cloud placeholder files are ordinary files here.)
                isLink = e.LinkTarget is not null;
                isDir = (e.Attributes & FileAttributes.Directory) != 0;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                run.Exceptions.Add(new ScanException(rel, "entry", "attributes could not be read: " + ex.GetType().Name));
                continue;
            }

            if (isLink)
            {
                run.Files.Add(new FileResult { Path = e.FullName, RelativePath = rel, Kind = FileKind.SkippedLink });
                continue;
            }
            if (isDir)
            {
                if (string.Equals(e.FullName, run.RunFolder, StringComparison.OrdinalIgnoreCase)) continue;
                Walk(e.FullName, run, ct);
                continue;
            }

            long size = 0;
            try { size = ((FileInfo)e).Length; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            var candidate = string.Equals(e.Extension, ".rsmf", StringComparison.OrdinalIgnoreCase);
            run.Files.Add(new FileResult
            {
                Path = e.FullName, RelativePath = rel, SizeBytes = size,
                Kind = candidate ? FileKind.RsmfCandidate : FileKind.NotCandidate,
            });
        }
    }

    private static string Rel(RunResult run, string path)
    {
        var rel = Path.GetRelativePath(run.InputRoot, path);
        return rel == "." ? "." : rel;
    }

    internal static int Workers(InspectionOptions options) =>
        options.MaxParallelFiles > 0 ? options.MaxParallelFiles : Math.Clamp(Environment.ProcessorCount, 1, 4);

    /// <summary>
    /// Inspects the candidates, several at a time. Each file is inspected on its own; results are put back
    /// in discovery order afterwards, so the reports do not depend on which file happened to finish first.
    /// </summary>
    private static void InspectAll(RunResult run, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var slots = new List<(int Index, FileResult Slot)>();
        for (var i = 0; i < run.Files.Count; i++)
            if (run.Files[i].Kind == FileKind.RsmfCandidate) slots.Add((i, run.Files[i]));
        var total = slots.Count;
        progress?.Report(new ScanProgress("inspecting", 0, total, null));

        var results = new FileResult?[total];
        var workers = Workers(run.Options);
        var done = 0;
        var progressGate = new object();
        var exclusiveGate = new object();
        using var permits = new SemaphoreSlim(workers, workers);

        void InspectOne(int n)
        {
            var slot = slots[n].Slot;
            var exclusive = workers > 1 && slot.SizeBytes >= run.Options.LargeFileBytes;
            var taken = 0;
            try
            {
                if (exclusive)
                {
                    lock (exclusiveGate)
                    {
                        for (; taken < workers; taken++) permits.Wait(ct);
                    }
                }
                else
                {
                    permits.Wait(ct);
                    taken = 1;
                }

                FileResult result;
                try
                {
                    result = FileInspector.Inspect(slot.Path, slot.RelativePath, run.Options, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // A defect in a check must not stop the scan: record it and carry on.
                    result = new FileResult { Path = slot.Path, RelativePath = slot.RelativePath, SizeBytes = slot.SizeBytes, Checked = true };
                    result.Findings.Add(RuleCatalog.Make("I-PARSE-001", "unexpected error: " + ex.GetType().Name, "(file)", detail: "inspection stopped early"));
                    result.SkippedChecks.Add("remaining checks: unexpected error " + ex.GetType().Name);
                }
                if (result.SizeBytes == 0) result.SizeBytes = slot.SizeBytes;
                results[n] = result;
                lock (progressGate)
                {
                    done++;
                    progress?.Report(new ScanProgress("inspecting", done, total, slot.RelativePath));
                }
            }
            finally
            {
                if (taken > 0) permits.Release(taken);
            }
        }

        var cancelled = false;
        try
        {
            if (workers == 1)
            {
                for (var n = 0; n < total; n++)
                {
                    ct.ThrowIfCancellationRequested();
                    InspectOne(n);
                }
            }
            else
            {
                Parallel.For(0, total, new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = ct }, n =>
                {
                    using var culture = InvariantScope.Enter();
                    InspectOne(n);
                });
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (AggregateException ex) when (ex.Flatten().InnerExceptions.All(e => e is OperationCanceledException))
        {
            cancelled = true;
        }
        finally
        {
            // Whatever happened, keep every finished result, in discovery order.
            for (var n = 0; n < total; n++)
            {
                if (results[n] is not { } result) continue;
                run.Files[slots[n].Index] = result;
                if (result.Status == FileStatus.Unreadable)
                    run.Exceptions.Add(new ScanException(result.RelativePath, "file", "unreadable: " + result.Findings.First(f => f.RuleId == "I-FILE-001").Observed));
                else if (result.Findings.Any(f => f.RuleId is "F-MIME-001" or "F-MIME-006" or "F-ZIP-001" or "F-MAN-001"))
                    run.Exceptions.Add(new ScanException(result.RelativePath, "file", "malformed: " + string.Join(", ",
                        result.Findings.Where(f => f.RuleId is "F-MIME-001" or "F-MIME-006" or "F-ZIP-001" or "F-MAN-001").Select(f => f.RuleId).Distinct())));
            }
        }
        if (cancelled) throw new OperationCanceledException(ct);
    }
}
