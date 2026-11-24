using System.Globalization;
using System.Text;
using System.Text.Json;
using NodaTime;

namespace Rsmf.Core;

/// <summary>Writes the audit files for a run. Paths are relative to the input root.</summary>
public static class ReportWriter
{
    public static readonly string[] ReportFiles =
    {
        "summary.txt", "inventory.csv", "findings.jsonl", "metadata_coverage.csv",
        "time_window_exceptions.csv", "run.json", "conversation_metrics.csv", "findings.csv", "rule_guide.csv",
    };

    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAll(RunResult run)
    {
        WriteInventory(run);
        WriteFindings(run);
        WriteFindingsCsv(run);
        WriteRuleGuide(run);
        WriteCoverage(run);
        WriteTimeWindow(run);
        WriteConversations(run);
        WriteRunJson(run);
        WriteSummary(run);
    }

    // ---------------------------------------------------------------- helpers

    internal static string Csv(params object?[] cells)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0) sb.Append(',');
            var s = Convert.ToString(cells[i], CultureInfo.InvariantCulture) ?? "";
            // Neutralise spreadsheet formula injection from file-derived text.
            if (s.Length > 0 && cells[i] is string && "=+-@\t\r".Contains(s[0])) s = "'" + s;
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) s = "\"" + s.Replace("\"", "\"\"") + "\"";
            sb.Append(s);
        }
        return sb.ToString();
    }

    private static string Ts(Instant? t) => t.HasValue ? TimestampParser.Format(t.Value) : "";
    private static string Span(Duration? d) => d.HasValue ? TimestampParser.FormatDuration(d.Value) : "";
    private static string Iso(DateTimeOffset t) => t.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static string KindText(FileKind k) => k switch
    {
        FileKind.RsmfCandidate => "RSMF candidate",
        FileKind.NotCandidate => "not an RSMF candidate",
        _ => "link or junction (not followed)",
    };

    private static string TimeWindowText(TimeWindowStatus s) => s switch
    {
        TimeWindowStatus.Pass => "PASS",
        TimeWindowStatus.Fail => "FAIL",
        TimeWindowStatus.Indeterminate => "INDETERMINATE",
        TimeWindowStatus.NoMessages => "NO MESSAGES",
        _ => "NOT EVALUATED",
    };

    private static StreamWriter Open(RunResult run, string name, bool bom) =>
        new(Path.Combine(run.RunFolder, name), false, bom ? Utf8Bom : Utf8) { NewLine = "\r\n" };

    // ---------------------------------------------------------------- inventory.csv

    private static void WriteInventory(RunResult run)
    {
        using var w = Open(run, "inventory.csv", bom: true);
        w.WriteLine(Csv("path", "kind", "status", "size_bytes", "rsmf_version", "total_events", "message_events",
            "conversations", "unique_participants", "errors", "warnings", "info", "format_findings", "limit_findings",
            "local_findings", "incomplete_findings", "time_window_status", "earliest_message", "latest_message",
            "elapsed_span", "skipped_checks"));
        foreach (var f in run.Files)
        {
            var c = f.Kind == FileKind.RsmfCandidate;
            w.WriteLine(Csv(f.RelativePath, KindText(f.Kind), f.Status.ToString(), f.SizeBytes, f.RsmfVersion,
                f.Metrics.TotalEvents, f.Metrics.MessageEvents, f.Metrics.ConversationCount, f.Metrics.UniqueParticipantCount,
                c ? f.Count(Severity.Error) : null, c ? f.Count(Severity.Warning) : null, c ? f.Count(Severity.Info) : null,
                c ? f.Findings.Count(x => x.Category == FindingCategory.FORMAT) : null,
                c ? f.Findings.Count(x => x.Category == FindingCategory.LIMIT) : null,
                c ? f.Findings.Count(x => x.Category == FindingCategory.LOCAL) : null,
                c ? f.Findings.Count(x => x.Category == FindingCategory.INCOMPLETE) : null,
                c && f.Checked ? TimeWindowText(f.TimeWindow.Status) : "",
                Ts(f.TimeWindow.Earliest), Ts(f.TimeWindow.Latest), Span(f.TimeWindow.Span),
                string.Join(" | ", f.SkippedChecks)));
        }
        foreach (var e in run.Exceptions.Where(e => e.Kind != "file"))
            w.WriteLine(Csv(e.RelativePath, "inaccessible " + e.Kind, "Exception", null, "", null, null, null, null,
                null, null, null, null, null, null, null, "", "", "", "", e.Reason));
    }

    // ---------------------------------------------------------------- findings.jsonl

    private static void WriteFindings(RunResult run)
    {
        using var w = Open(run, "findings.jsonl", bom: false);
        w.NewLine = "\n";
        foreach (var (rel, finding) in run.ScanFindings) w.WriteLine(FindingJson(rel, finding, run.Options.IncludeSensitive));
        foreach (var f in run.Files)
            foreach (var finding in f.Findings)
                w.WriteLine(FindingJson(f.RelativePath, finding, run.Options.IncludeSensitive));
    }

    // ---------------------------------------------------------------- findings.csv

    /// <summary>
    /// One row per finding, led by the file name, for opening in a spreadsheet or sending to whoever
    /// produced the files. Same findings as findings.jsonl; the plain "problem" text comes from RuleHelp.
    /// </summary>
    private static void WriteFindingsCsv(RunResult run)
    {
        using var w = Open(run, "findings.csv", bom: true);
        var sensitive = run.Options.IncludeSensitive;
        var head = new List<object?> { "file_name", "file_path", "severity", "rule", "problem", "observed", "expected", "location", "record_id", "rule_code" };
        if (sensitive) head.Add("sensitive_detail");
        w.WriteLine(Csv(head.ToArray()));

        void Row(string relativePath, bool isFile, Finding f)
        {
            var cells = new List<object?>
            {
                isFile ? Path.GetFileName(relativePath) : "",
                relativePath,
                f.Severity.ToString().ToLowerInvariant(),
                RuleCatalog.Label(f.RuleId),
                RuleHelp.Plain(f.RuleId).Problem,
                f.Observed, f.Expected, f.Location, f.RecordId, f.RuleId,
            };
            if (sensitive) cells.Add(f.SensitiveDetail ?? "");
            w.WriteLine(Csv(cells.ToArray()));
        }

        foreach (var (rel, finding) in run.ScanFindings) Row(rel, false, finding);
        foreach (var f in run.Files)
            foreach (var finding in f.Findings)
                Row(f.RelativePath, true, finding);
    }

    // ---------------------------------------------------------------- rule_guide.csv

    private static readonly object?[] RuleGuideHead =
        { "rule", "severity", "category", "problem", "what_it_means", "how_it_is_checked", "why_it_matters", "what_to_do", "technical_check", "technical_detail", "source", "documentation", "rule_code" };

    private static object?[] RuleGuideRow(RuleDefinition rule)
    {
        var plain = RuleHelp.Plain(rule.Id);
        var help = RuleHelp.Get(rule.Id);
        var guide = RuleGuide.Get(rule.Id);
        return new object?[]
        {
            RuleCatalog.Label(rule.Id), rule.Severity.ToString().ToLowerInvariant(), rule.Category.ToString(), plain.Problem, plain.Meaning,
            guide.How, guide.Why, plain.Action,
            rule.Title, help.Meaning + " " + help.HowToFix, rule.Source, RuleGuide.LinksText(rule.Id), rule.Id,
        };
    }

    /// <summary>The explanation of every rule that fired in this run, most serious and most widespread first, with counts.</summary>
    private static void WriteRuleGuide(RunResult run)
    {
        using var w = Open(run, "rule_guide.csv", bom: true);
        w.WriteLine(Csv(RuleGuideHead.Concat(new object?[] { "files_affected", "findings" }).ToArray()));
        var all = run.ScanFindings.Select(s => (Path: s.RelativePath, s.Finding))
            .Concat(run.Files.SelectMany(f => f.Findings.Select(x => (Path: f.RelativePath, Finding: x))));
        var groups = all.GroupBy(t => t.Finding.RuleId)
            .Select(g => (Rule: RuleCatalog.Get(g.Key), Files: g.Select(t => t.Path).Distinct(StringComparer.Ordinal).Count(), Findings: g.Count()))
            .OrderByDescending(g => (int)g.Rule.Severity).ThenByDescending(g => g.Files).ThenBy(g => RuleCatalog.Number(g.Rule.Id));
        foreach (var g in groups)
            w.WriteLine(Csv(RuleGuideRow(g.Rule).Concat(new object?[] { g.Files, g.Findings }).ToArray()));
    }

    /// <summary>The explanation of every rule the program has, as CSV text (for the package's RULE_GUIDE.csv and "explain --csv").</summary>
    public static string RuleGuideCsv()
    {
        var sb = new StringBuilder();
        sb.Append(Csv(RuleGuideHead)).Append("\r\n");
        foreach (var rule in RuleCatalog.All) sb.Append(Csv(RuleGuideRow(rule))).Append("\r\n");
        return sb.ToString();
    }

    internal static string FindingJson(string path, Finding f, bool includeSensitive)
    {
        using var ms = new MemoryStream();
        using (var j = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            j.WriteStartObject();
            j.WriteString("path", path);
            j.WriteString("ruleId", f.RuleId);
            j.WriteNumber("ruleNumber", RuleCatalog.Number(f.RuleId));
            j.WriteString("category", f.Category.ToString());
            j.WriteString("severity", f.Severity.ToString().ToLowerInvariant());
            j.WriteString("message", f.Message);
            j.WriteString("observed", f.Observed);
            j.WriteString("expected", f.Expected);
            j.WriteString("source", f.Source);
            j.WriteString("location", f.Location);
            j.WriteString("recordId", f.RecordId);
            if (includeSensitive && f.SensitiveDetail is not null) j.WriteString("sensitiveDetail", f.SensitiveDetail);
            j.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // ---------------------------------------------------------------- metadata_coverage.csv

    private static void WriteCoverage(RunResult run)
    {
        using var w = Open(run, "metadata_coverage.csv", bom: true);
        var header = new List<object?> { "path", "level", "field", "requirement", "records", "missing", "blank", "populated",
            "invalid", "missing_ids", "blank_ids", "invalid_ids" };
        if (run.Options.IncludeSensitive) header.Add("sample_values");
        w.WriteLine(Csv(header.ToArray()));
        foreach (var f in run.Files)
        {
            foreach (var r in f.Coverage)
            {
                var cells = new List<object?> { f.RelativePath, r.Level, r.Field, r.Requirement, r.Records, r.Missing, r.Blank,
                    r.Populated, r.Invalid, Ids(r.MissingIds, r.Missing), Ids(r.BlankIds, r.Blank), Ids(r.InvalidIds, r.Invalid) };
                if (run.Options.IncludeSensitive) cells.Add(string.Join(" | ", r.SampleValues));
                w.WriteLine(Csv(cells.ToArray()));
            }
        }
    }

    private static string Ids(List<string> ids, int total)
    {
        var s = string.Join("; ", ids);
        return total > ids.Count ? $"{s}; (+{total - ids.Count} more)" : s;
    }

    // ---------------------------------------------------------------- time_window_exceptions.csv

    private static void WriteTimeWindow(RunResult run)
    {
        using var w = Open(run, "time_window_exceptions.csv", bom: true);
        w.WriteLine(Csv("path", "conversation_ids", "earliest_message", "latest_message", "elapsed_span", "time_zone", "status", "reason"));
        foreach (var f in run.Files.Where(f => f.TimeWindow.Status is TimeWindowStatus.Fail or TimeWindowStatus.Indeterminate))
        {
            var tw = f.TimeWindow;
            var convs = f.Metrics.Conversations.Where(c => c.MessageCount > 0).Select(c => c.RecordId).ToList();
            var convText = string.Join("; ", convs.Take(50)) + (convs.Count > 50 ? $"; (+{convs.Count - 50} more)" : "");
            var reason = tw.Reason;
            if (tw.Status == TimeWindowStatus.Indeterminate)
                reason += ": " + string.Join("; ", tw.IndeterminateLocations.Take(50))
                          + (tw.IndeterminateLocations.Count > 50 ? $"; (+{tw.IndeterminateLocations.Count - 50} more)" : "");
            w.WriteLine(Csv(f.RelativePath, convText, Ts(tw.Earliest), Ts(tw.Latest), Span(tw.Span), tw.ZoneId, TimeWindowText(tw.Status), reason));
        }
    }

    // ---------------------------------------------------------------- conversation_metrics.csv

    private static void WriteConversations(RunResult run)
    {
        using var w = Open(run, "conversation_metrics.csv", bom: true);
        w.WriteLine(Csv("path", "conversation", "events", "message_events", "participants", "earliest_message", "latest_message", "elapsed_span"));
        foreach (var f in run.Files)
            foreach (var c in f.Metrics.Conversations)
                w.WriteLine(Csv(f.RelativePath, c.RecordId, c.EventCount, c.MessageCount, c.ParticipantCount, Ts(c.Earliest), Ts(c.Latest), Span(c.Span)));
    }

    // ---------------------------------------------------------------- run.json

    private static string FinalStatus(RunResult run) => run.Status switch
    {
        RunStatus.Completed => "completed",
        RunStatus.CompletedWithExceptions => "completed with exceptions (scan coverage incomplete)",
        RunStatus.Cancelled => "cancelled (scan coverage incomplete)",
        _ => "failed (scan coverage incomplete)",
    };

    private static IEnumerable<Finding> AllFindings(RunResult run) =>
        run.ScanFindings.Select(s => s.Finding).Concat(run.Files.SelectMany(f => f.Findings));

    private static void WriteRunJson(RunResult run)
    {
        var o = run.Options;
        var all = AllFindings(run).ToList();
        using var fs = new FileStream(Path.Combine(run.RunFolder, "run.json"), FileMode.Create, FileAccess.Write);
        using var j = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        j.WriteStartObject();
        j.WriteString("runId", run.RunId);
        j.WriteString("startTime", Iso(run.Started));
        j.WriteString("endTime", Iso(run.Ended));
        j.WriteString("inputPath", run.InputPath);
        j.WriteString("inputRoot", run.InputRoot);
        j.WriteString("appName", AppInfo.Name);
        j.WriteString("appVersion", AppInfo.Version);
        j.WriteString("rulesetVersion", RuleCatalog.RulesetVersion);

        j.WriteStartObject("settings");
        j.WriteNumber("sizeThresholdBytes", o.SizeThresholdBytes);
        j.WriteNumber("sizeThresholdGb", (double)o.SizeThresholdBytes / SizeUnits.BytesPerGb);
        j.WriteNumber("eventLimit", RuleCatalog.EventLimit);
        j.WriteNumber("filesInspectedAtOnce", ScanRunner.Workers(o));
        if (o.ParticipantWarningThreshold.HasValue) j.WriteNumber("participantWarningThreshold", o.ParticipantWarningThreshold.Value);
        else j.WriteNull("participantWarningThreshold");
        j.WriteString("timeZone", run.ZoneId);
        j.WriteString("timeZoneSource", string.IsNullOrWhiteSpace(o.TimeZoneId) ? "default (UTC)" : "user-selected");
        j.WriteString("tzdbVersion", DateTimeZoneProviders.Tzdb.VersionId);
        j.WriteString("timeWindowMode", o.TimeWindowMode == TimeWindowMode.Both ? "calendar-day and elapsed (A+B)" : "elapsed-only (B)");
        j.WriteNumber("elapsedLimitHours", 24);
        j.WriteBoolean("includeSensitive", o.IncludeSensitive);
        j.WriteNumber("maxManifestBytes", o.MaxManifestBytes);
        j.WriteNumber("spoolMemoryLimitBytes", o.SpoolMemoryLimitBytes);
        j.WriteEndObject();

        j.WriteStartObject("files");
        j.WriteNumber("discovered", run.Files.Count);
        j.WriteNumber("rsmfCandidates", run.Candidates.Count());
        j.WriteNumber("checked", run.Candidates.Count(f => f.Checked));
        j.WriteNumber("unreadable", run.Candidates.Count(f => f.Status == FileStatus.Unreadable));
        j.WriteNumber("notChecked", run.Candidates.Count(f => f.Status == FileStatus.NotChecked));
        j.WriteNumber("notRsmfCandidates", run.Files.Count(f => f.Kind == FileKind.NotCandidate));
        j.WriteNumber("linksNotFollowed", run.Files.Count(f => f.Kind == FileKind.SkippedLink));
        j.WriteEndObject();

        j.WriteStartObject("findingsBySeverity");
        foreach (var s in new[] { Severity.Error, Severity.Warning, Severity.Info })
            j.WriteNumber(s.ToString().ToLowerInvariant(), all.Count(f => f.Severity == s));
        j.WriteEndObject();
        j.WriteStartObject("findingsByCategory");
        foreach (var c in Enum.GetValues<FindingCategory>()) j.WriteNumber(c.ToString(), all.Count(f => f.Category == c));
        j.WriteEndObject();

        j.WriteStartArray("skippedOrFailedChecks");
        foreach (var e in run.Exceptions)
        {
            j.WriteStartObject();
            j.WriteString("path", e.RelativePath);
            j.WriteString("kind", e.Kind);
            j.WriteString("reason", e.Reason);
            j.WriteEndObject();
        }
        foreach (var f in run.Files)
            foreach (var s in f.SkippedChecks)
            {
                j.WriteStartObject();
                j.WriteString("path", f.RelativePath);
                j.WriteString("kind", "check");
                j.WriteString("reason", s);
                j.WriteEndObject();
            }
        j.WriteEndArray();

        j.WriteBoolean("scanCoverageComplete", run.CoverageComplete);
        j.WriteString("finalStatus", FinalStatus(run));
        if (run.FailureMessage.Length > 0) j.WriteString("failure", run.FailureMessage);
        j.WriteEndObject();
    }

    // ---------------------------------------------------------------- summary.txt

    private static void WriteSummary(RunResult run)
    {
        var o = run.Options;
        var all = AllFindings(run).ToList();
        var candidates = run.Candidates.ToList();
        using var w = Open(run, "summary.txt", bom: false);
        w.WriteLine($"{AppInfo.Name} {AppInfo.Version} - run summary");
        w.WriteLine(new string('=', 60));
        w.WriteLine($"Run ID:            {run.RunId}");
        w.WriteLine($"Started:           {Iso(run.Started)}");
        w.WriteLine($"Ended:             {Iso(run.Ended)}");
        w.WriteLine($"Input:             {run.InputPath}");
        w.WriteLine($"Ruleset version:   {RuleCatalog.RulesetVersion}");
        w.WriteLine($"Final status:      {FinalStatus(run).ToUpperInvariant()}");
        if (run.FailureMessage.Length > 0) w.WriteLine($"Failure:           {run.FailureMessage}");
        w.WriteLine();
        w.WriteLine("Settings and thresholds");
        w.WriteLine("-----------------------");
        w.WriteLine($"Size threshold (L-SIZE-001):   {SizeUnits.FormatGb(o.SizeThresholdBytes)} GB = {o.SizeThresholdBytes:N0} bytes (finding when a file is larger)");
        w.WriteLine($"Event limit (L-EVT-001):       {RuleCatalog.EventLimit:N0} events (finding when a file has more)");
        w.WriteLine($"Participant warning threshold: {(o.ParticipantWarningThreshold.HasValue ? o.ParticipantWarningThreshold.Value.ToString("N0", CultureInfo.InvariantCulture) : "not set (count reported only)")}");
        w.WriteLine($"Time zone (Rule A):            {run.ZoneId} ({(string.IsNullOrWhiteSpace(o.TimeZoneId) ? "default" : "user-selected")}; tz database {DateTimeZoneProviders.Tzdb.VersionId})");
        w.WriteLine($"Time-window mode:              {(o.TimeWindowMode == TimeWindowMode.Both ? "Rule A (calendar day) and Rule B (elapsed <= 24 h)" : "elapsed-only: Rule B (elapsed <= 24 h)")}");
        w.WriteLine($"Sensitive values in reports:   {(o.IncludeSensitive ? "INCLUDED (explicitly requested)" : "excluded")}");
        w.WriteLine();
        w.WriteLine("Files");
        w.WriteLine("-----");
        w.WriteLine($"Discovered:                 {run.Files.Count}");
        w.WriteLine($"RSMF candidates:            {candidates.Count}");
        w.WriteLine($"  checked:                  {candidates.Count(f => f.Checked)}");
        w.WriteLine($"  no findings:              {candidates.Count(f => f.Status == FileStatus.Ok && f.Findings.Count == 0)}");
        w.WriteLine($"  with errors:              {candidates.Count(f => f.Status == FileStatus.Errors)}");
        w.WriteLine($"  with warnings only:       {candidates.Count(f => f.Status == FileStatus.Warnings)}");
        w.WriteLine($"  unreadable:               {candidates.Count(f => f.Status == FileStatus.Unreadable)}");
        w.WriteLine($"  not checked:              {candidates.Count(f => f.Status == FileStatus.NotChecked)}");
        w.WriteLine($"Not an RSMF candidate:      {run.Files.Count(f => f.Kind == FileKind.NotCandidate)}");
        w.WriteLine($"Links/junctions not followed: {run.Files.Count(f => f.Kind == FileKind.SkippedLink)}");
        w.WriteLine($"Scan coverage:              {(run.CoverageComplete ? "complete" : "INCOMPLETE")}");
        w.WriteLine();
        w.WriteLine("Findings");
        w.WriteLine("--------");
        w.WriteLine($"By severity:  error {all.Count(f => f.Severity == Severity.Error)}, warning {all.Count(f => f.Severity == Severity.Warning)}, info {all.Count(f => f.Severity == Severity.Info)}");
        w.WriteLine("By category:  " + string.Join(", ", Enum.GetValues<FindingCategory>().Select(c => $"{c} {all.Count(f => f.Category == c)}")));
        w.WriteLine();
        w.WriteLine("By rule (files affected / findings):");
        var byRule = run.Files.SelectMany(f => f.Findings.Select(x => (f.RelativePath, x)))
            .Concat(run.ScanFindings.Select(s => (s.RelativePath, x: s.Finding)))
            .GroupBy(t => t.x.RuleId).OrderBy(g => RuleCatalog.Number(g.Key));
        var any = false;
        foreach (var g in byRule)
        {
            any = true;
            var def = RuleCatalog.Get(g.Key);
            w.WriteLine($"  {RuleCatalog.Label(g.Key)}  {g.Key,-11} {def.Category,-10} {def.Severity.ToString().ToLowerInvariant(),-7} {g.Select(t => t.RelativePath).Distinct().Count(),5} / {g.Count(),-6} {def.Title}");
        }
        if (!any) w.WriteLine("  (none)");
        w.WriteLine();
        w.WriteLine("Time-window QC (message events only)");
        // (the plain-language explanations follow the exceptions section below)
        w.WriteLine("------------------------------------");
        foreach (var s in new[] { TimeWindowStatus.Pass, TimeWindowStatus.Fail, TimeWindowStatus.Indeterminate, TimeWindowStatus.NoMessages, TimeWindowStatus.NotEvaluated })
            w.WriteLine($"  {TimeWindowText(s),-14} {candidates.Count(f => f.TimeWindow.Status == s)}");
        w.WriteLine($"  Rule A failures: {candidates.Count(f => f.TimeWindow.RuleAFailed)}{(o.TimeWindowMode == TimeWindowMode.ElapsedOnly ? " (Rule A not checked in elapsed-only mode)" : "")}");
        w.WriteLine($"  Rule B failures: {candidates.Count(f => f.TimeWindow.RuleBFailed)}");
        w.WriteLine();
        w.WriteLine("Exceptions and skipped checks");
        w.WriteLine("-----------------------------");
        if (run.Exceptions.Count == 0 && run.Files.All(f => f.SkippedChecks.Count == 0)) w.WriteLine("  (none)");
        foreach (var e in run.Exceptions) w.WriteLine($"  [{e.Kind}] {e.RelativePath}: {e.Reason}");
        foreach (var f in run.Files)
            foreach (var s in f.SkippedChecks)
                w.WriteLine($"  [check] {f.RelativePath}: {s}");
        w.WriteLine();
        if (any)
        {
            w.WriteLine("What the findings mean");
            w.WriteLine("----------------------");
            foreach (var id in all.Select(f => f.RuleId).Distinct().OrderBy(RuleCatalog.Number))
            {
                w.Write(RuleHelp.Format(id).Replace("\r\n", "\n").Replace("\n", "\r\n"));
                w.WriteLine();
            }
        }
        w.WriteLine("Files in this run folder");
        w.WriteLine("------------------------");
        w.WriteLine("  summary.txt                 this file");
        w.WriteLine("  inventory.csv               one row per file discovered");
        w.WriteLine("  findings.csv                one finding per row, led by the file name (opens in Excel)");
        w.WriteLine("  rule_guide.csv              what each rule that fired means and what to do about it");
        w.WriteLine("  findings.jsonl              the same findings, one per line, for other programs");
        w.WriteLine("  metadata_coverage.csv       missing / blank / populated / invalid per field");
        w.WriteLine("  time_window_exceptions.csv  files that fail or are indeterminate");
        w.WriteLine("  conversation_metrics.csv    per-conversation counts, earliest, latest and span");
        w.WriteLine("  run.json                    settings, counts and final status");
        w.WriteLine();
        w.WriteLine("Record identifiers (participant, conversation and event ids, attachment file names) appear in");
        w.WriteLine("these reports. Message bodies, display names, email addresses, account ids and custom values");
        w.WriteLine(o.IncludeSensitive ? "ARE included in this run because the include option was set." : "are not included.");
        w.WriteLine();
        w.WriteLine("This tool performs local QC only. It does not certify files or guarantee Relativity ingestion.");
    }
}
