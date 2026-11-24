namespace Rsmf.Core;

/// <summary>One finding together with the file (or folder) it was raised against.</summary>
public sealed record FindingOccurrence(string RelativePath, string FullPath, bool IsRsmfFile, Finding Finding);

/// <summary>All findings of one rule across a run.</summary>
public sealed class RuleGroup
{
    public required RuleDefinition Rule { get; init; }
    public required IReadOnlyList<FindingOccurrence> Occurrences { get; init; }
    public int FindingCount => Occurrences.Count;
    public int FileCount { get; init; }
}

/// <summary>Groups a run's findings by rule for the findings explorer.</summary>
public static class FindingsIndex
{
    /// <summary>
    /// Groups ordered by severity (errors first), then by number of files affected, then by rule id.
    /// Within a group, occurrences keep scan order (path, then order within the file).
    /// </summary>
    public static List<RuleGroup> Build(RunResult run)
    {
        var all = new List<FindingOccurrence>();
        foreach (var (relativePath, finding) in run.ScanFindings)
            all.Add(new FindingOccurrence(relativePath, Path.Combine(run.InputRoot, relativePath), false, finding));
        foreach (var file in run.Files)
            foreach (var finding in file.Findings)
                all.Add(new FindingOccurrence(file.RelativePath, file.Path, file.Kind == FileKind.RsmfCandidate, finding));

        return all.GroupBy(o => o.Finding.RuleId, StringComparer.Ordinal)
            .Select(g => new RuleGroup
            {
                Rule = RuleCatalog.Get(g.Key),
                Occurrences = g.ToList(),
                FileCount = g.Select(o => o.RelativePath).Distinct(StringComparer.Ordinal).Count(),
            })
            .OrderByDescending(g => g.Rule.Severity)
            .ThenByDescending(g => g.FileCount)
            .ThenBy(g => g.Rule.Id, StringComparer.Ordinal)
            .ToList();
    }
}
