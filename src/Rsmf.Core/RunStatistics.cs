namespace Rsmf.Core;

/// <summary>Headline figures for a run, used by the app's summary cards.</summary>
public sealed class RunStatistics
{
    public int Discovered { get; private init; }
    public int Candidates { get; private init; }
    public int OtherFiles { get; private init; }
    public int Checked { get; private init; }
    public int FilesClean { get; private init; }
    public int FilesWithErrors { get; private init; }
    public int FilesWithWarningsOnly { get; private init; }
    public int Unreadable { get; private init; }
    public int NotChecked { get; private init; }
    public int ErrorFindings { get; private init; }
    public int WarningFindings { get; private init; }
    public int InfoFindings { get; private init; }
    public int TimeWindowPass { get; private init; }
    public int TimeWindowFail { get; private init; }
    public int TimeWindowIndeterminate { get; private init; }
    public bool CoverageComplete { get; private init; }
    public RunStatus Status { get; private init; }

    public static RunStatistics From(RunResult run)
    {
        var candidates = run.Candidates.ToList();
        var findings = run.ScanFindings.Select(s => s.Finding).Concat(run.Files.SelectMany(f => f.Findings)).ToList();
        return new RunStatistics
        {
            Discovered = run.Files.Count,
            Candidates = candidates.Count,
            OtherFiles = run.Files.Count - candidates.Count,
            Checked = candidates.Count(f => f.Checked),
            FilesClean = candidates.Count(f => f.Status == FileStatus.Ok),
            FilesWithErrors = candidates.Count(f => f.Status == FileStatus.Errors),
            FilesWithWarningsOnly = candidates.Count(f => f.Status == FileStatus.Warnings),
            Unreadable = candidates.Count(f => f.Status == FileStatus.Unreadable),
            NotChecked = candidates.Count(f => f.Status == FileStatus.NotChecked),
            ErrorFindings = findings.Count(f => f.Severity == Severity.Error),
            WarningFindings = findings.Count(f => f.Severity == Severity.Warning),
            InfoFindings = findings.Count(f => f.Severity == Severity.Info),
            TimeWindowPass = candidates.Count(f => f.TimeWindow.Status == TimeWindowStatus.Pass),
            TimeWindowFail = candidates.Count(f => f.TimeWindow.Status == TimeWindowStatus.Fail),
            TimeWindowIndeterminate = candidates.Count(f => f.TimeWindow.Status == TimeWindowStatus.Indeterminate),
            CoverageComplete = run.CoverageComplete,
            Status = run.Status,
        };
    }
}
