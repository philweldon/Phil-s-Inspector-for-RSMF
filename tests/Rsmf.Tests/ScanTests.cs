using System.Text.Json;
using Rsmf.Core;
using Xunit;
using static Rsmf.Tests.FixtureGenerator;

namespace Rsmf.Tests;

[Collection("fixtures")]
public class ScanTests
{
    private readonly FixtureFolder _fx;
    private static readonly InspectionOptions NewYork = new() { TimeZoneId = "America/New_York" };
    private static readonly string[] SixReports =
        { "summary.txt", "inventory.csv", "findings.jsonl", "metadata_coverage.csv", "time_window_exceptions.csv", "run.json" };

    public ScanTests(FixtureFolder fx) => _fx = fx;

    private sealed class Progress(Action<ScanProgress> a) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => a(value);
    }

    private string SmallInput(params string[] names)
    {
        var dir = _fx.NewDir("input");
        foreach (var n in names)
        {
            var target = Path.Combine(dir, n.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(_fx.PathOf(n), target);
        }
        return dir;
    }

    private static void AssertSixReports(RunResult run)
    {
        foreach (var name in SixReports)
            Assert.True(File.Exists(Path.Combine(run.RunFolder, name)), "missing report: " + name);
        Assert.Matches(@"^run-\d{8}-\d{6}-[0-9a-f]{6}$", Path.GetFileName(run.RunFolder));
    }

    private static JsonElement RunJson(RunResult run) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(run.RunFolder, "run.json"))).RootElement;

    [Fact]
    public void Full_fixture_scan_writes_all_reports_with_relative_paths()
    {
        var run = ScanRunner.Run(_fx.Files, _fx.NewDir("out"), NewYork);
        AssertSixReports(run);

        var expected = BuildAll().Keys.Count;
        Assert.Equal(expected, run.Files.Count);
        Assert.Equal(expected - 2, run.Candidates.Count());
        Assert.Contains(run.Files, f => f.RelativePath == "notes.txt" && f.Kind == FileKind.NotCandidate);
        Assert.Contains(run.Candidates, f => f.RelativePath == "UPPERCASE-EXT.RSMF" && f.Status == FileStatus.Ok); // case-insensitive match
        Assert.Contains(run.Candidates, f => f.RelativePath == Path.Combine("sub", "nested-valid.rsmf") && f.Status == FileStatus.Ok); // recursive
        Assert.All(run.Files, f => Assert.False(Path.IsPathRooted(f.RelativePath)));

        // malformed files are recorded as exceptions and coverage is marked incomplete, but the scan continued
        Assert.Equal(RunStatus.CompletedWithExceptions, run.Status);
        Assert.Contains(run.Exceptions, e => e.RelativePath == "damaged-mime.rsmf");
        Assert.All(run.Candidates, f => Assert.True(f.Checked));

        var inventory = File.ReadAllLines(Path.Combine(run.RunFolder, "inventory.csv"));
        Assert.Equal(expected + 1, inventory.Length);
        Assert.Contains(inventory, l => l.StartsWith("notes.txt,not an RSMF candidate,"));
        Assert.DoesNotContain(inventory, l => l.Contains(_fx.Files));

        var findings = File.ReadAllLines(Path.Combine(run.RunFolder, "findings.jsonl"));
        Assert.Equal(run.Files.Sum(f => f.Findings.Count), findings.Length);
        foreach (var line in findings.Take(200))
        {
            var o = JsonDocument.Parse(line).RootElement;
            foreach (var p in new[] { "path", "ruleId", "category", "severity", "observed", "expected", "source", "location", "recordId" })
                Assert.True(o.TryGetProperty(p, out _), "finding lacks " + p);
        }

        var tw = File.ReadAllLines(Path.Combine(run.RunFolder, "time_window_exceptions.csv"));
        Assert.Equal("path,conversation_ids,earliest_message,latest_message,elapsed_span,time_zone,status,reason", tw[0].TrimStart('﻿'));
        Assert.Contains(tw, l => l.StartsWith("midnight-crossing.rsmf,conversation:C1,2026-03-04T04:50:00Z,2026-03-04T05:10:00Z,0:20:00,America/New_York,FAIL,"));
        Assert.Contains(tw, l => l.StartsWith("invalid-timestamp.rsmf,") && l.Contains("INDETERMINATE") && l.Contains("$.events[1].timestamp"));
        Assert.DoesNotContain(tw, l => l.StartsWith("valid-2.0.rsmf,"));

        var json = RunJson(run);
        Assert.Equal(run.RunId, json.GetProperty("runId").GetString());
        Assert.Equal(AppInfo.Version, json.GetProperty("appVersion").GetString());
        Assert.Equal(RuleCatalog.RulesetVersion, json.GetProperty("rulesetVersion").GetString());
        Assert.Equal(2147483648L, json.GetProperty("settings").GetProperty("sizeThresholdBytes").GetInt64());
        Assert.Equal("America/New_York", json.GetProperty("settings").GetProperty("timeZone").GetString());
        Assert.Equal(expected, json.GetProperty("files").GetProperty("discovered").GetInt32());
        Assert.Equal(expected - 2, json.GetProperty("files").GetProperty("checked").GetInt32());
        Assert.False(json.GetProperty("scanCoverageComplete").GetBoolean());
        Assert.True(json.GetProperty("findingsBySeverity").GetProperty("error").GetInt32() > 0);
        Assert.True(json.GetProperty("skippedOrFailedChecks").GetArrayLength() > 0);
        foreach (var p in new[] { "startTime", "endTime", "inputPath", "finalStatus" }) Assert.True(json.TryGetProperty(p, out _));

        var summary = File.ReadAllText(Path.Combine(run.RunFolder, "summary.txt"));
        Assert.Contains("2,147,483,648 bytes", summary);
        Assert.Contains("America/New_York", summary);
    }

    [Fact]
    public void Run_statistics_match_the_reports()
    {
        var run = ScanRunner.Run(_fx.Files, _fx.NewDir("out"), NewYork);
        var st = RunStatistics.From(run);
        Assert.Equal(28, st.Discovered);
        Assert.Equal(26, st.Candidates);
        Assert.Equal(2, st.OtherFiles);
        Assert.Equal(26, st.Checked);
        Assert.Equal(9, st.FilesClean);
        Assert.Equal(15, st.FilesWithErrors);
        Assert.Equal(2, st.FilesWithWarningsOnly);
        Assert.Equal(0, st.Unreadable);
        Assert.Equal(0, st.NotChecked);
        Assert.Equal(18, st.ErrorFindings);
        Assert.Equal(12, st.WarningFindings);
        Assert.Equal(5, st.InfoFindings);
        Assert.Equal(15, st.TimeWindowPass);
        Assert.Equal(4, st.TimeWindowFail);
        Assert.Equal(2, st.TimeWindowIndeterminate);
        Assert.False(st.CoverageComplete);
        Assert.Equal(st.FilesClean + st.FilesWithErrors + st.FilesWithWarningsOnly + st.Unreadable + st.NotChecked, st.Candidates);

        var json = RunJson(run);
        Assert.Equal(st.ErrorFindings, json.GetProperty("findingsBySeverity").GetProperty("error").GetInt32());
        Assert.Equal(st.WarningFindings, json.GetProperty("findingsBySeverity").GetProperty("warning").GetInt32());
        Assert.Equal(st.Checked, json.GetProperty("files").GetProperty("checked").GetInt32());

        var clean = RunStatistics.From(ScanRunner.Run(SmallInput("valid-2.0.rsmf"), _fx.NewDir("out"), NewYork));
        Assert.True(clean.CoverageComplete);
        Assert.Equal(1, clean.FilesClean);
        Assert.Equal(0, clean.ErrorFindings);
    }

    [Fact]
    public void Reports_exclude_bodies_and_personal_values_by_default_and_include_them_on_request()
    {
        var input = SmallInput("valid-2.0.rsmf", "broken-references.rsmf", "blank-vs-missing.rsmf");
        var plain = ScanRunner.Run(input, _fx.NewDir("out"), NewYork);
        foreach (var file in Directory.GetFiles(plain.RunFolder))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain(BodyMarker, text);
            Assert.DoesNotContain(EmailMarker, text);
            Assert.DoesNotContain(DisplayMarker, text);
            Assert.DoesNotContain("unknown participant", text); // a message body
        }

        var open = ScanRunner.Run(input, _fx.NewDir("out"), new InspectionOptions { TimeZoneId = "America/New_York", IncludeSensitive = true });
        var findings = File.ReadAllText(Path.Combine(open.RunFolder, "findings.jsonl"));
        Assert.Contains("sensitiveDetail", findings);
        Assert.Contains("unknown participant", findings);
        var coverage = File.ReadAllText(Path.Combine(open.RunFolder, "metadata_coverage.csv"));
        Assert.Contains("sample_values", coverage);
        Assert.Contains(EmailMarker, coverage);
        Assert.True(RunJson(open).GetProperty("settings").GetProperty("includeSensitive").GetBoolean());
    }

    [Fact]
    public void Clean_scan_completes_with_full_coverage()
    {
        var run = ScanRunner.Run(SmallInput("valid-1.0.rsmf", "valid-2.0.rsmf"), _fx.NewDir("out"), NewYork);
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.True(RunJson(run).GetProperty("scanCoverageComplete").GetBoolean());
        Assert.Equal("completed", RunJson(run).GetProperty("finalStatus").GetString());
        Assert.False(Directory.Exists(Path.Combine(run.RunFolder, ".tmp")));
        Assert.Single(File.ReadAllLines(Path.Combine(run.RunFolder, "time_window_exceptions.csv")));
    }

    [Fact]
    public void Single_file_input_is_supported()
    {
        var run = ScanRunner.Run(_fx.PathOf("midnight-crossing.rsmf"), _fx.NewDir("out"), NewYork);
        AssertSixReports(run);
        var f = Assert.Single(run.Files);
        Assert.Equal("midnight-crossing.rsmf", f.RelativePath);
        Assert.Contains(f.Findings, x => x.RuleId == "Q-TIME-A");
    }

    [Fact]
    public void Cancellation_mid_scan_still_writes_reports_and_marks_remaining_files()
    {
        var input = SmallInput("valid-1.0.rsmf", "valid-2.0.rsmf", "single-message.rsmf", "midnight-crossing.rsmf", "events-9999.rsmf");
        using var cts = new CancellationTokenSource();
        var progress = new Progress(p => { if (p.Phase == "inspecting" && p.FilesDone == 2) cts.Cancel(); });

        // One file at a time, so exactly two are finished when the scan is cancelled.
        var oneAtATime = new InspectionOptions { TimeZoneId = "America/New_York", MaxParallelFiles = 1 };
        var run = ScanRunner.Run(input, _fx.NewDir("out"), oneAtATime, progress, cts.Token);

        Assert.Equal(RunStatus.Cancelled, run.Status);
        AssertSixReports(run);
        Assert.Equal(2, run.Candidates.Count(f => f.Checked));
        var pending = run.Candidates.Where(f => !f.Checked).ToList();
        Assert.Equal(3, pending.Count);
        Assert.All(pending, f =>
        {
            Assert.Equal(FileStatus.NotChecked, f.Status);
            Assert.Equal("I-RUN-001", Assert.Single(f.Findings).RuleId);
        });
        var json = RunJson(run);
        Assert.StartsWith("cancelled", json.GetProperty("finalStatus").GetString());
        Assert.False(json.GetProperty("scanCoverageComplete").GetBoolean());
        Assert.Equal(5, json.GetProperty("files").GetProperty("discovered").GetInt32());
        Assert.Equal(2, json.GetProperty("files").GetProperty("checked").GetInt32());
        Assert.Equal(5, File.ReadAllLines(Path.Combine(run.RunFolder, "inventory.csv")).Length - 1);
    }

    [Fact]
    public void Cancellation_before_start_is_a_cancelled_run_with_reports()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var run = ScanRunner.Run(SmallInput("valid-2.0.rsmf"), _fx.NewDir("out"), NewYork, null, cts.Token);
        Assert.Equal(RunStatus.Cancelled, run.Status);
        AssertSixReports(run);
    }

    [Fact]
    public void Unreadable_file_is_recorded_and_the_scan_continues()
    {
        var input = SmallInput("valid-1.0.rsmf", "valid-2.0.rsmf", "single-message.rsmf");
        var locked = Path.Combine(input, "valid-2.0.rsmf");
        RunResult run;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            run = ScanRunner.Run(input, _fx.NewDir("out"), NewYork);
        }

        var bad = run.Files.Single(f => f.RelativePath == "valid-2.0.rsmf");
        Assert.Equal(FileStatus.Unreadable, bad.Status);
        var finding = Assert.Single(bad.Findings);
        Assert.Equal("I-FILE-001", finding.RuleId);
        Assert.Equal(FindingCategory.INCOMPLETE, finding.Category);
        Assert.True(bad.SizeBytes > 0);
        Assert.Equal(2, run.Candidates.Count(f => f.Status == FileStatus.Ok));
        Assert.Equal(RunStatus.CompletedWithExceptions, run.Status);
        Assert.Contains(run.Exceptions, e => e.RelativePath == "valid-2.0.rsmf" && e.Reason.StartsWith("unreadable"));
        Assert.False(RunJson(run).GetProperty("scanCoverageComplete").GetBoolean());
        Assert.Equal(1, RunJson(run).GetProperty("files").GetProperty("unreadable").GetInt32());
    }

    [Fact]
    public void Injected_open_failure_is_reported_as_unreadable()
    {
        var options = new InspectionOptions
        {
            TimeZoneId = "America/New_York",
            FileOpener = p => p.EndsWith("valid-1.0.rsmf") ? throw new UnauthorizedAccessException("denied") : InspectionOptions.DefaultOpen(p),
        };
        var run = ScanRunner.Run(SmallInput("valid-1.0.rsmf", "valid-2.0.rsmf"), _fx.NewDir("out"), options);
        Assert.Equal(FileStatus.Unreadable, run.Files.Single(f => f.RelativePath == "valid-1.0.rsmf").Status);
        Assert.Equal(FileStatus.Ok, run.Files.Single(f => f.RelativePath == "valid-2.0.rsmf").Status);
    }

    [Fact]
    public void Symbolic_links_are_inventoried_but_not_followed()
    {
        var outside = _fx.NewDir("outside");
        File.Copy(_fx.PathOf("midnight-crossing.rsmf"), Path.Combine(outside, "linked-target.rsmf"));
        var input = SmallInput("valid-2.0.rsmf");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(input, "linkdir"), outside);
            File.CreateSymbolicLink(Path.Combine(input, "linkfile.rsmf"), Path.Combine(outside, "linked-target.rsmf"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // creating links needs a privilege on Windows; nothing to test without it
        }

        var run = ScanRunner.Run(input, _fx.NewDir("out"), NewYork);
        Assert.Equal(2, run.Files.Count(f => f.Kind == FileKind.SkippedLink));
        Assert.Single(run.Candidates);
        Assert.DoesNotContain(run.Files, f => f.RelativePath.Contains("linked-target"));
        Assert.Contains(File.ReadAllLines(Path.Combine(run.RunFolder, "inventory.csv")), l => l.Contains("link or junction (not followed)"));
    }

    [Fact]
    public void Inaccessible_directory_is_recorded_and_the_scan_continues()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return; // needs POSIX permissions enforced for this user
        var input = SmallInput("valid-2.0.rsmf", "sub/nested-valid.rsmf");
        var sub = Path.Combine(input, "sub");
        File.SetUnixFileMode(sub, UnixFileMode.None);
        try
        {
            var run = ScanRunner.Run(input, _fx.NewDir("out"), NewYork);
            Assert.Contains(run.ScanFindings, s => s.Finding.RuleId == "I-SCAN-001" && s.RelativePath == "sub");
            Assert.Equal(RunStatus.CompletedWithExceptions, run.Status);
            Assert.Single(run.Candidates);
        }
        finally
        {
            File.SetUnixFileMode(sub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void Directory_that_cannot_be_listed_is_recorded_and_the_scan_continues()
    {
        var input = SmallInput("valid-2.0.rsmf", "sub/nested-valid.rsmf");
        var options = new InspectionOptions
        {
            TimeZoneId = "America/New_York",
            DirectoryLister = d => Path.GetFileName(d) == "sub" ? throw new UnauthorizedAccessException("denied") : InspectionOptions.DefaultList(d),
        };
        var run = ScanRunner.Run(input, _fx.NewDir("out"), options);
        var (rel, finding) = Assert.Single(run.ScanFindings);
        Assert.Equal("sub", rel);
        Assert.Equal("I-SCAN-001", finding.RuleId);
        Assert.Equal(RunStatus.CompletedWithExceptions, run.Status);
        Assert.Equal(FileStatus.Ok, Assert.Single(run.Candidates).Status);
        Assert.False(RunJson(run).GetProperty("scanCoverageComplete").GetBoolean());
        Assert.Contains(File.ReadAllLines(Path.Combine(run.RunFolder, "inventory.csv")), l => l.StartsWith("sub,inaccessible directory,"));
        Assert.Contains("I-SCAN-001", File.ReadAllText(Path.Combine(run.RunFolder, "findings.jsonl")));
    }

    [Fact]
    public void Missing_input_path_is_a_failed_run_with_reports()
    {
        var run = ScanRunner.Run(Path.Combine(_fx.Root, "does-not-exist"), _fx.NewDir("out"), NewYork);
        Assert.Equal(RunStatus.Failed, run.Status);
        AssertSixReports(run);
        Assert.StartsWith("failed", RunJson(run).GetProperty("finalStatus").GetString());
        Assert.Contains("DirectoryNotFoundException", RunJson(run).GetProperty("failure").GetString());
    }

    [Fact]
    public void Each_run_gets_a_new_folder_and_source_files_are_not_modified()
    {
        var input = SmallInput("valid-2.0.rsmf");
        var source = Path.Combine(input, "valid-2.0.rsmf");
        var before = (File.ReadAllBytes(source), File.GetLastWriteTimeUtc(source));
        var outDir = _fx.NewDir("out");
        var a = ScanRunner.Run(input, outDir, NewYork);
        var b = ScanRunner.Run(input, outDir, NewYork);
        Assert.NotEqual(a.RunFolder, b.RunFolder);
        Assert.Equal(before.Item1, File.ReadAllBytes(source));
        Assert.Equal(before.Item2, File.GetLastWriteTimeUtc(source));
        Assert.Equal(new[] { "valid-2.0.rsmf" }, Directory.GetFileSystemEntries(input).Select(Path.GetFileName));
    }

    [Fact]
    public void Output_folder_inside_the_input_folder_is_not_scanned_into_itself()
    {
        var input = SmallInput("valid-2.0.rsmf");
        var run = ScanRunner.Run(input, Path.Combine(input, "reports"), NewYork);
        Assert.DoesNotContain(run.Files, f => f.RelativePath.Contains(Path.GetFileName(run.RunFolder)));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    [InlineData("tr-TR")]
    public void Output_is_identical_under_other_regional_settings(string cultureName)
    {
        var previous = (System.Globalization.CultureInfo.CurrentCulture, System.Globalization.CultureInfo.CurrentUICulture);
        var culture = System.Globalization.CultureInfo.GetCultureInfo(cultureName);
        Assert.NotEqual("", culture.Name); // real culture data is available to the test host
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
            System.Globalization.CultureInfo.CurrentUICulture = culture;
            var run = ScanRunner.Run(SmallInput("valid-2.0.rsmf", "midnight-crossing.rsmf", "broken-references.rsmf", "blank-vs-missing.rsmf"), _fx.NewDir("out"), NewYork);

            Assert.Equal(culture, System.Globalization.CultureInfo.CurrentCulture); // restored afterwards
            AssertSixReports(run); // folder name uses Gregorian ASCII digits
            var summary = File.ReadAllText(Path.Combine(run.RunFolder, "summary.txt"));
            Assert.Contains("2,147,483,648 bytes", summary);
            Assert.Contains("10,000 events", summary);
            Assert.Matches(@"Started:\s+20\d\d-\d\d-\d\dT\d\d:\d\d:\d\d[+-]\d\d:\d\d", summary);
            var findings = File.ReadAllText(Path.Combine(run.RunFolder, "findings.jsonl"));
            Assert.Contains("\"ruleId\":\"Q-TIME-A\"", findings);
            Assert.Contains("but should be", findings); // schema messages stay in English
            Assert.Contains("0:20:00", File.ReadAllText(Path.Combine(run.RunFolder, "time_window_exceptions.csv")));
            Assert.Equal(FileStatus.Ok, run.Files.Single(f => f.RelativePath == "valid-2.0.rsmf").Status);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous.Item1;
            System.Globalization.CultureInfo.CurrentUICulture = previous.Item2;
        }
    }

    [Fact]
    public void Csv_cells_are_quoted_and_formula_prefixes_neutralised()
    {
        Assert.Equal("a,\"b,c\",\"d\"\"e\",5,", ReportWriter.Csv("a", "b,c", "d\"e", 5, null));
        Assert.Equal("'=cmd()", ReportWriter.Csv("=cmd()"));
        Assert.Equal("-5", ReportWriter.Csv(-5));
    }

    // ------------------------------------------------------------ CLI

    [Fact]
    public void Cli_scan_produces_the_six_reports_and_version_is_printed()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var outDir = _fx.NewDir("cli-out");
        var code = Rsmf.Cli.Program.Run(new[] { "scan", _fx.Files, "--out", outDir, "--tz", "America/New_York", "--quiet" }, stdout, stderr);
        Assert.Equal(1, code); // fixtures contain error findings
        var runFolder = Assert.Single(Directory.GetDirectories(outDir));
        foreach (var name in SixReports) Assert.True(File.Exists(Path.Combine(runFolder, name)));
        Assert.Contains("Reports written to:", stdout.ToString());

        var v = new StringWriter();
        Assert.Equal(0, Rsmf.Cli.Program.Run(new[] { "--version" }, v, stderr));
        Assert.Equal($"Phil's Inspector for RSMF files {AppInfo.Version} (ruleset {RuleCatalog.RulesetVersion})", v.ToString().Trim());
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppInfo.Version);
    }

    [Fact]
    public void Cli_options_and_argument_errors()
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var input = SmallInput("valid-2.0.rsmf", "midnight-crossing.rsmf");
        Assert.Equal(0, Rsmf.Cli.Program.Run(new[] { "scan", input, "--out", _fx.NewDir("o"), "--tz", "America/New_York", "--mode", "elapsed-only", "--quiet" }, o, e));
        Assert.Equal(1, Rsmf.Cli.Program.Run(new[] { "scan", input, "--out", _fx.NewDir("o"), "--tz", "America/New_York", "--quiet" }, o, e));
        Assert.Equal(1, Rsmf.Cli.Program.Run(new[] { "scan", input, "--out", _fx.NewDir("o"), "--tz", "America/New_York", "--mode", "elapsed-only", "--size-threshold", "0.000001", "--quiet" }, o, e));
        Assert.Equal(0, Rsmf.Cli.Program.Run(new[] { "scan", input, "--out", _fx.NewDir("o"), "--tz", "America/New_York", "--mode", "elapsed-only", "--size-threshold", "1.5", "--quiet" }, o, e));
        var bytesError = new StringWriter();
        Assert.Equal(64, Rsmf.Cli.Program.Run(new[] { "scan", input, "--out", _fx.NewDir("o"), "--size-threshold", "2147483648" }, o, bytesError));
        Assert.Contains("now given in GB", bytesError.ToString());
        Assert.Equal(64, Rsmf.Cli.Program.Run(new[] { "scan", input, "--out", _fx.NewDir("o"), "--size-threshold", "big" }, o, e));
        Assert.Equal(64, Rsmf.Cli.Program.Run(new[] { "scan", input, "--out", _fx.NewDir("o"), "--size-threshold", "0" }, o, e));
        Assert.Equal(64, Rsmf.Cli.Program.Run(new[] { "scan", input }, o, e));
        Assert.Equal(64, Rsmf.Cli.Program.Run(new[] { "scan", input, "--out", _fx.NewDir("o"), "--tz", "Nowhere/Land" }, o, e));
        Assert.Equal(64, Rsmf.Cli.Program.Run(new[] { "scan", input, "--out", _fx.NewDir("o"), "--mode", "sideways" }, o, e));
        Assert.Equal(64, Rsmf.Cli.Program.Run(new[] { "scan", Path.Combine(input, "nope"), "--out", _fx.NewDir("o") }, o, e));
        Assert.Equal(64, Rsmf.Cli.Program.Run(new[] { "frobnicate" }, o, e));
    }

    // ------------------------------------------------------------ size threshold in GB

    [Theory]
    [InlineData("2", 2_147_483_648L)]
    [InlineData("2.0", 2_147_483_648L)]
    [InlineData(" 2 GB ", 2_147_483_648L)]
    [InlineData("2gb", 2_147_483_648L)]
    [InlineData("1.5", 1_610_612_736L)]
    [InlineData("0.5", 536_870_912L)]
    [InlineData("1", 1_073_741_824L)]
    [InlineData("100000", 107_374_182_400_000L)]
    public void Size_in_gb_parses_to_bytes(string text, long bytes)
    {
        Assert.True(SizeUnits.TryParseGb(text, out var parsed));
        Assert.Equal(bytes, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("two")]
    [InlineData("1,5")]            // a comma is not a decimal point
    [InlineData("2147483648")]     // a byte count from an older version
    [InlineData("100000.1")]
    [InlineData("1e3")]
    [InlineData("NaN")]
    [InlineData(null)]
    public void Size_in_gb_rejects_bad_values(string? text)
    {
        Assert.False(SizeUnits.TryParseGb(text, out _));
    }

    [Fact]
    public void Size_formatting_and_the_unchanged_default()
    {
        Assert.Equal(2_147_483_648L, RuleCatalog.DefaultSizeThresholdBytes);
        Assert.Equal("2", SizeUnits.FormatGb(RuleCatalog.DefaultSizeThresholdBytes));
        Assert.Equal("1.5", SizeUnits.FormatGb(1_610_612_736L));
        Assert.Equal("0.000000931", SizeUnits.FormatGb(1000));
        Assert.True(SizeUnits.TryParseGb(SizeUnits.FormatGb(RuleCatalog.DefaultSizeThresholdBytes), out var roundTrip));
        Assert.Equal(RuleCatalog.DefaultSizeThresholdBytes, roundTrip);
        Assert.True(SizeUnits.LooksLikeBytes("2147483648"));
        Assert.False(SizeUnits.LooksLikeBytes("2"));
        Assert.False(SizeUnits.LooksLikeBytes("abc"));

        // reports state both figures; run.json keeps the exact byte count
        var run = ScanRunner.Run(SmallInput("valid-2.0.rsmf"), _fx.NewDir("out"), new InspectionOptions { TimeZoneId = "America/New_York", SizeThresholdBytes = 1_610_612_736L });
        Assert.Contains("1.5 GB = 1,610,612,736 bytes", File.ReadAllText(Path.Combine(run.RunFolder, "summary.txt")));
        var settings = RunJson(run).GetProperty("settings");
        Assert.Equal(1_610_612_736L, settings.GetProperty("sizeThresholdBytes").GetInt64());
        Assert.Equal(1.5, settings.GetProperty("sizeThresholdGb").GetDouble());
    }

    // ------------------------------------------------------------ remembered settings and dropped items

    [Fact]
    public void User_settings_round_trip_and_never_hold_the_sensitive_option()
    {
        var path = Path.Combine(_fx.NewDir("settings"), UserSettings.FileName);
        Assert.Null(UserSettings.Load(path));

        var saved = new UserSettings { TimeZoneId = "Europe/London", TimeWindowMode = "elapsed-only", SizeThresholdBytes = 1000, ParticipantWarningThreshold = 40, OutputFolder = @"C:\out" };
        Assert.True(saved.Save(path));
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("sensitive", text, StringComparison.OrdinalIgnoreCase);

        var loaded = UserSettings.Load(path)!;
        Assert.Equal("Europe/London", loaded.TimeZoneId);
        Assert.Equal("elapsed-only", loaded.TimeWindowMode);
        Assert.Equal(1000, loaded.SizeThresholdBytes);
        Assert.Equal(40, loaded.ParticipantWarningThreshold);
        Assert.Equal(@"C:\out", loaded.OutputFolder);

        Assert.True(UserSettings.Forget(path));
        Assert.False(File.Exists(path));
        Assert.True(UserSettings.Forget(path)); // already gone is fine
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("")]
    public void Corrupt_settings_files_are_ignored(string content)
    {
        var path = Path.Combine(_fx.NewDir("settings"), UserSettings.FileName);
        File.WriteAllText(path, content);
        Assert.Null(UserSettings.Load(path));
    }

    [Fact]
    public void Invalid_saved_values_fall_back_to_defaults()
    {
        var path = Path.Combine(_fx.NewDir("settings"), UserSettings.FileName);
        File.WriteAllText(path, """{ "timeZoneId": "Mars/Olympus", "timeWindowMode": "sideways", "sizeThresholdBytes": -5, "participantWarningThreshold": -1, "outputFolder": "  " }""");
        var loaded = UserSettings.Load(path)!;
        Assert.Null(loaded.TimeZoneId);
        Assert.Equal("both", loaded.TimeWindowMode);
        Assert.Equal(RuleCatalog.DefaultSizeThresholdBytes, loaded.SizeThresholdBytes);
        Assert.Null(loaded.ParticipantWarningThreshold);
        Assert.Null(loaded.OutputFolder);

        Assert.False(new UserSettings().Save(Path.Combine(_fx.Root, "no-such-folder", "x", UserSettings.FileName)));
    }

    [Fact]
    public void Dropped_items_resolve_to_one_folder_or_file()
    {
        var folder = SmallInput("valid-2.0.rsmf", "single-message.rsmf");
        var file = Path.Combine(folder, "valid-2.0.rsmf");
        var other = Path.Combine(folder, "single-message.rsmf");

        var one = DroppedInput.From(new[] { file });
        Assert.Equal((DroppedKind.File, file, ""), (one.Kind, one.Path, one.Note));

        var dir = DroppedInput.From(new[] { folder });
        Assert.Equal((DroppedKind.Folder, folder, ""), (dir.Kind, dir.Path, dir.Note));

        var several = DroppedInput.From(new[] { file, other });
        Assert.Equal(DroppedKind.File, several.Kind);
        Assert.Equal(file, several.Path);
        Assert.Contains("2 items were dropped", several.Note);

        var mixed = DroppedInput.From(new[] { file, folder });
        Assert.Equal(DroppedKind.Folder, mixed.Kind);       // a folder wins over files
        Assert.Contains("first folder", mixed.Note);

        Assert.Equal(DroppedKind.Nothing, DroppedInput.From(new[] { Path.Combine(folder, "missing.rsmf") }).Kind);
        Assert.Equal(DroppedKind.Nothing, DroppedInput.From(null).Kind);
        Assert.Equal(DroppedKind.Nothing, DroppedInput.From(new[] { "", "  " }).Kind);
    }

    // ------------------------------------------------------------ findings explorer

    [Fact]
    public void Findings_index_groups_a_run_by_rule()
    {
        var run = ScanRunner.Run(_fx.Files, _fx.NewDir("out"), NewYork);
        var groups = FindingsIndex.Build(run);

        Assert.Equal(run.Files.Sum(f => f.Findings.Count), groups.Sum(g => g.FindingCount));
        Assert.Equal(groups.Count, groups.Select(g => g.Rule.Id).Distinct().Count());
        Assert.All(groups, g => Assert.All(g.Occurrences, o => Assert.Equal(g.Rule.Id, o.Finding.RuleId)));
        Assert.All(groups, g => Assert.Equal(g.Occurrences.Select(o => o.RelativePath).Distinct().Count(), g.FileCount));

        // errors first, then warnings, then info; most widespread first within a severity
        var severities = groups.Select(g => g.Rule.Severity).ToList();
        Assert.Equal(severities.OrderByDescending(s => s), severities);
        Assert.Equal("Q-TIME-A", groups[0].Rule.Id);                 // 3 files, the most widespread error
        Assert.Equal(3, groups[0].FileCount);
        Assert.Equal(Severity.Info, groups[^1].Rule.Severity);

        var mime5 = groups.Single(g => g.Rule.Id == "F-MIME-005");
        Assert.Equal(3, mime5.FindingCount);                          // two files, one of them with two findings
        Assert.Equal(2, mime5.FileCount);
        var occurrence = groups.Single(g => g.Rule.Id == "F-REF-004").Occurrences.Single();
        Assert.Equal("broken-references.rsmf", occurrence.RelativePath);
        Assert.True(occurrence.IsRsmfFile);
        Assert.True(File.Exists(occurrence.FullPath));
        Assert.Equal("$.events[2].parent", occurrence.Finding.Location);

        Assert.Empty(FindingsIndex.Build(ScanRunner.Run(SmallInput("valid-2.0.rsmf"), _fx.NewDir("out"), NewYork)));
    }

    [Fact]
    public void Findings_index_includes_folder_level_findings()
    {
        var options = new InspectionOptions
        {
            TimeZoneId = "America/New_York",
            DirectoryLister = d => Path.GetFileName(d) == "sub" ? throw new UnauthorizedAccessException("denied") : InspectionOptions.DefaultList(d),
        };
        var run = ScanRunner.Run(SmallInput("valid-2.0.rsmf", "sub/nested-valid.rsmf"), _fx.NewDir("out"), options);
        var group = Assert.Single(FindingsIndex.Build(run));
        Assert.Equal("I-SCAN-001", group.Rule.Id);
        var o = Assert.Single(group.Occurrences);
        Assert.Equal("sub", o.RelativePath);
        Assert.False(o.IsRsmfFile);
    }

    // ------------------------------------------------------------ rule help

    [Fact]
    public void Every_rule_has_a_plain_language_explanation()
    {
        Assert.Equal(RuleCatalog.All.Select(r => r.Id).OrderBy(x => x), RuleHelp.RuleIds.OrderBy(x => x));
        foreach (var rule in RuleCatalog.All)
        {
            var help = RuleHelp.Get(rule.Id);
            Assert.True(help.Meaning.Length >= 40, rule.Id + " meaning is too short");
            Assert.True(help.HowToFix.Length >= 20, rule.Id + " advice is too short");
            Assert.EndsWith(".", help.Meaning);
            Assert.EndsWith(".", help.HowToFix);
            Assert.DoesNotContain("TODO", help.Meaning + help.HowToFix);
            // The help never promises an outcome in Relativity.
            Assert.DoesNotContain("guarantee", (help.Meaning + help.HowToFix).ToLowerInvariant());
            Assert.DoesNotContain("certif", (help.Meaning + help.HowToFix).ToLowerInvariant());

            var block = RuleHelp.Format(rule.Id, 80);
            Assert.StartsWith("Rule " + RuleCatalog.Label(rule.Id) + "  [" + rule.Category, block);
            Assert.Contains("Code " + rule.Id + ".", block.Replace("\r", "").Replace("\n      ", " "));
            Assert.Contains("What it means:", block);
            Assert.Contains("What to do:", block);
            Assert.Contains("Source:", block);
            Assert.All(block.Split('\n').Skip(1), line => Assert.True(line.TrimEnd('\r').Length <= 80 || !line.Trim().Contains(' '), "line too long: " + line));
        }
        Assert.False(RuleHelp.Has("X-NOPE-001"));
        Assert.Contains("No explanation", RuleHelp.Get("X-NOPE-001").Meaning);
    }

    [Fact]
    public void Every_rule_is_also_explained_without_jargon()
    {
        Assert.Equal(RuleCatalog.All.Select(r => r.Id).OrderBy(x => x), RuleHelp.PlainRuleIds.OrderBy(x => x));
        var jargon = new[] { "MIME", "base64", "JSON", "schema", "ISO 8601", "header", "manifest", "X-RSMF", "UTF", "regenerate", "instant", "offset", "roll-up", "parse" };
        foreach (var rule in RuleCatalog.All)
        {
            var plain = RuleHelp.Plain(rule.Id);
            Assert.InRange(plain.Problem.Length, 15, 70);
            Assert.False(plain.Problem.EndsWith("."), rule.Id + " problem should be a short label");
            Assert.True(plain.Meaning.Length >= 40 && plain.Meaning.EndsWith("."), rule.Id + " meaning");
            Assert.True(plain.Action.Length >= 20 && plain.Action.EndsWith("."), rule.Id + " action");
            var all = plain.Problem + " " + plain.Meaning + " " + plain.Action;
            Assert.All(jargon, word => Assert.False(all.Contains(word, StringComparison.OrdinalIgnoreCase), $"{rule.Id} uses the technical word \"{word}\""));
            Assert.DoesNotContain("guarantee", all.ToLowerInvariant());
            Assert.DoesNotContain("certif", all.ToLowerInvariant());

            var block = RuleHelp.Format(rule.Id, 80);
            Assert.Contains(plain.Problem, block.Split('\n')[0]);
            Assert.Contains("Technical:", block);
        }
        Assert.Contains("No plain-language", RuleHelp.Plain("X-NOPE-001").Problem);
    }

    [Fact]
    public void Summary_explains_the_rules_that_fired_and_cli_explain_works()
    {
        var run = ScanRunner.Run(SmallInput("valid-2.0.rsmf", "midnight-crossing.rsmf", "missing-attachment.rsmf"), _fx.NewDir("out"), NewYork);
        var summary = File.ReadAllText(Path.Combine(run.RunFolder, "summary.txt"));
        Assert.Contains("What the findings mean", summary);
        Assert.Contains("Rule 042  [LOCAL, error]", summary);          // Q-TIME-A
        Assert.Contains("Rule 037  [FORMAT, error]", summary);         // F-REF-009
        Assert.Contains("Code Q-TIME-A.", summary);
        Assert.Contains("  042  Q-TIME-A", summary);                    // the by-rule table shows the number, then the code
        Assert.Contains("What to do:", summary);
        Assert.DoesNotContain("Rule 001  [", summary); // only rules that fired are explained
        Assert.DoesNotContain("\n\n\n\n", summary.Replace("\r", ""));

        var clean = ScanRunner.Run(SmallInput("valid-2.0.rsmf"), _fx.NewDir("out"), NewYork);
        Assert.DoesNotContain("What the findings mean", File.ReadAllText(Path.Combine(clean.RunFolder, "summary.txt")));

        var o = new StringWriter();
        var e = new StringWriter();
        Assert.Equal(0, Rsmf.Cli.Program.Run(new[] { "explain", "q-time-b" }, o, e));
        Assert.Contains("Rule 043  [LOCAL, error]", o.ToString());
        var byNumber = new StringWriter();
        Assert.Equal(0, Rsmf.Cli.Program.Run(new[] { "explain", "43" }, byNumber, e));
        Assert.Equal(o.ToString(), byNumber.ToString());
        Assert.Contains("25 hours", o.ToString());
        var all = new StringWriter();
        Assert.Equal(0, Rsmf.Cli.Program.Run(new[] { "explain" }, all, e));
        Assert.All(RuleCatalog.All, r => Assert.Contains("Rule " + RuleCatalog.Label(r.Id) + "  [", all.ToString()));
        Assert.Equal(64, Rsmf.Cli.Program.Run(new[] { "explain", "nope" }, o, e));
    }

    // ------------------------------------------------------------ a read that fails after the file opened

    /// <summary>A file stream that reads normally for a while and then fails, like a dropped network share or a bad disk sector.</summary>
    private sealed class FailingStream(Stream inner, long failAfter) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (inner.Position >= failAfter) throw new IOException("simulated read failure");
            return inner.Read(buffer, offset, (int)Math.Min(count, failAfter - inner.Position));
        }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    [Theory]
    [InlineData(1L)]          // fails almost at once
    [InlineData(300L)]        // fails inside the headers
    [InlineData(1200L)]       // fails inside the base64 attachment
    [InlineData(1900L)]       // fails just before the end
    public void A_read_that_fails_part_way_through_marks_that_file_unreadable_and_the_scan_carries_on(long failAfter)
    {
        var input = SmallInput("valid-1.0.rsmf", "valid-2.0.rsmf", "single-message.rsmf");
        var options = new InspectionOptions
        {
            TimeZoneId = "America/New_York",
            FileOpener = p => p.EndsWith("valid-2.0.rsmf", StringComparison.Ordinal)
                ? new FailingStream(InspectionOptions.DefaultOpen(p), failAfter)
                : InspectionOptions.DefaultOpen(p),
        };

        var run = ScanRunner.Run(input, _fx.NewDir("out"), options);

        Assert.Equal(RunStatus.CompletedWithExceptions, run.Status);
        Assert.False(run.CoverageComplete);
        AssertSixReports(run);
        var broken = run.Candidates.Single(f => f.RelativePath == "valid-2.0.rsmf");
        Assert.Equal(FileStatus.Unreadable, broken.Status);
        var finding = Assert.Single(broken.Findings, f => f.RuleId == "I-FILE-001");
        Assert.Equal("IOException", finding.Observed);
        Assert.DoesNotContain(broken.Findings, f => f.RuleId.StartsWith("F-", StringComparison.Ordinal));   // a half-read file is not judged on format
        Assert.All(run.Candidates.Where(f => f != broken), f => Assert.Equal(FileStatus.Ok, f.Status));
        Assert.Contains(run.Exceptions, x => x.ToString().Contains("valid-2.0.rsmf") && x.ToString().Contains("unreadable"));
    }

    [Fact]
    public void Viewer_says_what_is_unavailable_when_the_read_fails_part_way_through()
    {
        var options = new InspectionOptions
        {
            TimeZoneId = "America/New_York",
            FileOpener = p => new FailingStream(InspectionOptions.DefaultOpen(p), 1200),
        };
        using var doc = ViewerDocument.Load(_fx.PathOf("valid-2.0.rsmf"), options, CancellationToken.None);
        Assert.Empty(doc.Conversations);
        Assert.NotEmpty(doc.Unavailable);
        Assert.Contains(doc.Result.Findings, f => f.RuleId == "I-FILE-001");
    }

    // ------------------------------------------------------------ cancelling inside one large file

    /// <summary>Counts what is read from a file and cancels the scan once a given amount has been read.</summary>
    private sealed class CancellingStream(Stream inner, long cancelAfter, CancellationTokenSource cts) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            BytesRead += n;
            if (BytesRead >= cancelAfter) cts.Cancel();
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Cancelling_while_one_large_file_is_being_decoded_stops_promptly_and_still_writes_reports(int workers)
    {
        // One file of about 16 MB (a 12 MB attachment, base64-encoded) beside a small one.
        var input = _fx.NewDir("input");
        var payload = new byte[12 * 1024 * 1024];
        new Random(5).NextBytes(payload);
        var manifest = FixtureGenerator.ValidManifest("2.0.0");
        var big = Path.Combine(input, "big.rsmf");
        File.WriteAllBytes(big, FixtureGenerator.Wrap(manifest, FixtureGenerator.ValidDates,
            ("photo.png", FixtureGenerator.Png), ("avatar.png", FixtureGenerator.Png), ("extra.bin", payload)).Build());
        File.Copy(_fx.PathOf("valid-1.0.rsmf"), Path.Combine(input, "a-small.rsmf"));
        var size = new FileInfo(big).Length;
        Assert.True(size > 15_000_000);

        using var cts = new CancellationTokenSource();
        CancellingStream? watched = null;
        var options = new InspectionOptions
        {
            TimeZoneId = "America/New_York",
            MaxParallelFiles = workers,
            FileOpener = p => p.EndsWith("big.rsmf", StringComparison.Ordinal)
                ? watched = new CancellingStream(InspectionOptions.DefaultOpen(p), 1_000_000, cts)
                : InspectionOptions.DefaultOpen(p),
        };

        var run = ScanRunner.Run(input, _fx.NewDir("out"), options, null, cts.Token);

        Assert.Equal(RunStatus.Cancelled, run.Status);
        AssertSixReports(run);
        Assert.NotNull(watched);
        Assert.True(watched!.BytesRead < size / 2, $"read {watched.BytesRead:N0} of {size:N0} bytes after cancelling at 1,000,000");

        var bigResult = run.Candidates.Single(f => f.RelativePath == "big.rsmf");
        Assert.False(bigResult.Checked);
        Assert.Equal(FileStatus.NotChecked, bigResult.Status);
        Assert.Equal("I-RUN-001", Assert.Single(bigResult.Findings).RuleId);
        Assert.False(Directory.Exists(Path.Combine(run.RunFolder, ".tmp")));        // no working file left behind
        Assert.StartsWith("cancelled", RunJson(run).GetProperty("finalStatus").GetString());
    }

    // ------------------------------------------------------------ real Windows permissions

    /// <summary>
    /// Uses real NTFS "deny" entries rather than a simulated failure. Runs on Windows only; elsewhere it
    /// does nothing. The entries are added to copies inside the test's own temporary folder and removed again.
    /// </summary>
    [Fact]
    public void Real_access_denied_folder_and_file_are_reported_and_the_scan_carries_on()
    {
        if (!OperatingSystem.IsWindows()) return;

        var input = SmallInput("valid-1.0.rsmf", "valid-2.0.rsmf", "sub/nested-valid.rsmf");
        var folder = new DirectoryInfo(Path.Combine(input, "sub"));
        var file = new FileInfo(Path.Combine(input, "valid-2.0.rsmf"));
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var noListing = new System.Security.AccessControl.FileSystemAccessRule(me,
            System.Security.AccessControl.FileSystemRights.ListDirectory, System.Security.AccessControl.AccessControlType.Deny);
        var noReading = new System.Security.AccessControl.FileSystemAccessRule(me,
            System.Security.AccessControl.FileSystemRights.ReadData, System.Security.AccessControl.AccessControlType.Deny);

        var folderAcl = folder.GetAccessControl();
        folderAcl.AddAccessRule(noListing);
        folder.SetAccessControl(folderAcl);
        var fileAcl = file.GetAccessControl();
        fileAcl.AddAccessRule(noReading);
        file.SetAccessControl(fileAcl);
        try
        {
            var run = ScanRunner.Run(input, _fx.NewDir("out"), NewYork);

            Assert.Equal(RunStatus.CompletedWithExceptions, run.Status);
            Assert.False(run.CoverageComplete);
            AssertSixReports(run);

            var scanFinding = Assert.Single(run.ScanFindings);
            Assert.Equal("sub", scanFinding.RelativePath);
            Assert.Equal("I-SCAN-001", scanFinding.Finding.RuleId);

            var denied = run.Candidates.Single(f => f.RelativePath == "valid-2.0.rsmf");
            Assert.Equal(FileStatus.Unreadable, denied.Status);
            Assert.Equal("UnauthorizedAccessException", Assert.Single(denied.Findings, f => f.RuleId == "I-FILE-001").Observed);

            Assert.Equal(FileStatus.Ok, run.Candidates.Single(f => f.RelativePath == "valid-1.0.rsmf").Status);
            Assert.DoesNotContain(run.Files, f => f.RelativePath.Contains("nested-valid"));   // never seen: its folder could not be listed
            Assert.Equal(2, run.Exceptions.Count);
        }
        finally
        {
            folderAcl = folder.GetAccessControl();
            folderAcl.RemoveAccessRule(noListing);
            folder.SetAccessControl(folderAcl);
            fileAcl = file.GetAccessControl();
            fileAcl.RemoveAccessRule(noReading);
            file.SetAccessControl(fileAcl);
        }
        Assert.Single(Directory.GetFiles(folder.FullName));        // the entries are gone: the folder can be listed again
        using (var check = file.OpenRead()) Assert.True(check.ReadByte() >= 0);
    }

    // ------------------------------------------------------------ reports beside the scanned folder

    [Fact]
    public void Default_report_location_is_beside_the_scanned_folder_never_inside_it()
    {
        var parent = _fx.NewDir("case");
        var source = Path.Combine(parent, "Chats 2026");
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        var file = Path.Combine(source, "sub", "one.rsmf");
        File.Copy(_fx.PathOf("valid-2.0.rsmf"), file);

        Assert.Equal(parent, OutputLocation.Beside(source));
        Assert.Equal(parent, OutputLocation.Beside(source + Path.DirectorySeparatorChar));
        Assert.Equal(source, OutputLocation.Beside(file));                       // a single file: beside the folder that holds it
        Assert.Null(OutputLocation.Beside(Path.Combine(parent, "does-not-exist")));
        Assert.Null(OutputLocation.Beside(Path.GetPathRoot(parent)!));          // a drive root has nothing beside it

        Assert.Equal("Chats 2026-rsmf-report-", OutputLocation.Prefix(source, "rsmf-report"));
        Assert.Equal("one-pdf-", OutputLocation.Prefix(file, "pdf"));
        Assert.True(OutputLocation.Prefix(Path.Combine(parent, new string('x', 100)), "pdf").Length <= 65);

        var before = Directory.GetFileSystemEntries(source, "*", SearchOption.AllDirectories).OrderBy(x => x).ToArray();
        var run = ScanRunner.Run(source, OutputLocation.Beside(source)!, NewYork, folderPrefix: OutputLocation.Prefix(source, "rsmf-report"));
        Assert.Equal(parent, Path.GetDirectoryName(run.RunFolder));
        Assert.Matches(@"^Chats 2026-rsmf-report-\d{8}-\d{6}-[0-9a-f]{6}$", Path.GetFileName(run.RunFolder));
        Assert.All(SixReports, name => Assert.True(File.Exists(Path.Combine(run.RunFolder, name)), name));
        Assert.Equal(before, Directory.GetFileSystemEntries(source, "*", SearchOption.AllDirectories).OrderBy(x => x).ToArray());
        Assert.Equal("one.rsmf", Path.GetFileName(Assert.Single(run.Candidates).Path));
    }

    [Fact]
    public void CanWrite_probes_without_leaving_anything_behind()
    {
        var dir = _fx.NewDir("probe");
        Assert.True(OutputLocation.CanWrite(dir));
        Assert.Empty(Directory.GetFileSystemEntries(dir));
        Assert.False(OutputLocation.CanWrite(Path.Combine(dir, "missing")));
    }

    [Fact]
    public void Run_folder_keeps_its_usual_name_when_no_prefix_is_given()
    {
        var run = ScanRunner.Run(SmallInput("valid-2.0.rsmf"), _fx.NewDir("out"), NewYork);
        Assert.Matches(@"^run-\d{8}-\d{6}-[0-9a-f]{6}$", Path.GetFileName(run.RunFolder));
    }

    // ------------------------------------------------------------ rule numbers

    [Fact]
    public void Rule_numbers_are_permanent()
    {
        // Frozen on purpose. A rule keeps its number for ever; add new rules at the end, never renumber.
        var frozen = new[]
        {
            "F-MIME-001", "F-MIME-002", "F-MIME-003", "F-MIME-004", "F-MIME-005", "F-MIME-006", "F-MIME-007", "F-MIME-008",
            "F-HDR-001", "F-HDR-002", "F-HDR-003", "F-HDR-004", "F-HDR-005", "F-ZIP-001", "F-ZIP-002",
            "F-MAN-001", "F-MAN-002", "F-MAN-003", "F-MAN-004", "F-MAN-005", "F-MAN-006", "F-MAN-007", "F-MAN-008", "F-MAN-009",
            "F-MAN-010", "F-MAN-011", "F-MAN-012", "F-MAN-013",
            "F-REF-001", "F-REF-002", "F-REF-003", "F-REF-004", "F-REF-005", "F-REF-006", "F-REF-007", "F-REF-008", "F-REF-009",
            "F-REF-010", "F-REF-011", "L-SIZE-001", "L-EVT-001", "Q-TIME-A", "Q-TIME-B", "Q-TIME-001",
            "Q-PART-001", "Q-PART-002", "Q-PART-003", "Q-VER-001",
            "I-FILE-001", "I-SCAN-001", "I-ZIP-001", "I-PARSE-001", "I-TIME-001", "I-RUN-001",
        };
        for (var i = 0; i < frozen.Length; i++) Assert.Equal(i + 1, RuleCatalog.Number(frozen[i]));
        Assert.Equal(RuleCatalog.All.Count, RuleCatalog.All.Select(r => RuleCatalog.Number(r.Id)).Where(n => n > 0).Distinct().Count());   // every rule numbered, no repeats
        Assert.Equal("004", RuleCatalog.Label("F-MIME-004"));
        Assert.Equal(0, RuleCatalog.Number("X-NOPE-001"));
    }

    [Theory]
    [InlineData("4", "F-MIME-004")]
    [InlineData("004", "F-MIME-004")]
    [InlineData("rule 47", "Q-PART-003")]
    [InlineData("#42", "Q-TIME-A")]
    [InlineData("q-time-a", "Q-TIME-A")]
    [InlineData("F-MAN-003", "F-MAN-003")]
    public void A_rule_can_be_looked_up_by_number_or_by_its_old_code(string typed, string code)
    {
        Assert.True(RuleCatalog.TryResolve(typed, out var rule));
        Assert.Equal(code, rule.Id);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("55")]
    [InlineData("nope")]
    [InlineData("")]
    public void Unknown_rule_numbers_and_codes_are_refused(string typed) => Assert.False(RuleCatalog.TryResolve(typed, out _));

    [Fact]
    public void Findings_jsonl_carries_the_rule_number_beside_the_code()
    {
        var run = ScanRunner.Run(SmallInput("midnight-crossing.rsmf"), _fx.NewDir("out"), NewYork);
        var line = File.ReadAllLines(Path.Combine(run.RunFolder, "findings.jsonl")).First(l => l.Contains("\"Q-TIME-A\""));
        using var doc = System.Text.Json.JsonDocument.Parse(line);
        Assert.Equal("Q-TIME-A", doc.RootElement.GetProperty("ruleId").GetString());
        Assert.Equal(42, doc.RootElement.GetProperty("ruleNumber").GetInt32());
    }

    // ------------------------------------------------------------ findings.csv and rule_guide.csv

    private static List<string[]> ReadCsv(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new System.Text.StringBuilder();
        var quoted = false;
        text = text.TrimStart('\uFEFF');
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n') { row.Add(cell.ToString().TrimEnd('\r')); cell.Clear(); rows.Add(row.ToArray()); row.Clear(); }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }

    [Fact]
    public void Findings_csv_has_one_row_per_finding_led_by_the_file_name()
    {
        var run = ScanRunner.Run(_fx.Root, _fx.NewDir("out"), NewYork);
        var rows = ReadCsv(File.ReadAllText(Path.Combine(run.RunFolder, "findings.csv")));

        Assert.Equal(new[] { "file_name", "file_path", "severity", "rule", "problem", "observed", "expected", "location", "record_id", "rule_code" }, rows[0]);
        var expected = run.ScanFindings.Count + run.Files.Sum(f => f.Findings.Count);
        Assert.Equal(expected, rows.Count - 1);
        Assert.Equal(File.ReadAllLines(Path.Combine(run.RunFolder, "findings.jsonl")).Length, rows.Count - 1);
        Assert.All(rows.Skip(1), r => Assert.Equal(10, r.Length));

        var nested = rows.Skip(1).First(r => r[1].EndsWith("broken-references.rsmf", StringComparison.Ordinal));
        Assert.Equal("broken-references.rsmf", nested[0]);
        Assert.All(rows.Skip(1), r =>
        {
            Assert.Contains(r[2], new[] { "error", "warning", "info" });
            Assert.Matches(@"^\d{3}$", r[3]);
            Assert.Equal(RuleCatalog.Label(r[9]), r[3]);
            Assert.Equal(RuleHelp.Plain(r[9]).Problem, r[4]);
        });
        // Default mode: no message text or personal values.
        var text = File.ReadAllText(Path.Combine(run.RunFolder, "findings.csv"));
        Assert.DoesNotContain(FixtureGenerator.BodyMarker, text);
        Assert.DoesNotContain(FixtureGenerator.EmailMarker, text);
    }

    [Fact]
    public void Findings_csv_gains_a_sensitive_column_only_when_asked()
    {
        var run = ScanRunner.Run(SmallInput("broken-references.rsmf"), _fx.NewDir("out"), new InspectionOptions { TimeZoneId = "America/New_York", IncludeSensitive = true });
        var rows = ReadCsv(File.ReadAllText(Path.Combine(run.RunFolder, "findings.csv")));
        Assert.Equal("sensitive_detail", rows[0][^1]);
        Assert.Equal(11, rows[0].Length);
    }

    [Fact]
    public void Rule_guide_csv_explains_exactly_the_rules_that_fired_most_serious_first()
    {
        var run = ScanRunner.Run(_fx.Root, _fx.NewDir("out"), NewYork);
        var rows = ReadCsv(File.ReadAllText(Path.Combine(run.RunFolder, "rule_guide.csv")));
        Assert.Equal(new[] { "rule", "severity", "category", "problem", "what_it_means", "how_it_is_checked", "why_it_matters", "what_to_do", "technical_check", "technical_detail", "source", "documentation", "rule_code", "files_affected", "findings" }, rows[0]);

        var fired = run.ScanFindings.Select(s => s.Finding.RuleId).Concat(run.Files.SelectMany(f => f.Findings.Select(x => x.RuleId))).Distinct().OrderBy(x => x).ToArray();
        Assert.Equal(fired, rows.Skip(1).Select(r => r[12]).OrderBy(x => x).ToArray());
        Assert.Equal(run.ScanFindings.Count + run.Files.Sum(f => f.Findings.Count), rows.Skip(1).Sum(r => int.Parse(r[14])));

        var rank = rows.Skip(1).Select(r => r[1] switch { "error" => 2, "warning" => 1, _ => 0 }).ToList();
        Assert.Equal(rank.OrderByDescending(x => x), rank);
        Assert.All(rows.Skip(1), r =>
        {
            Assert.Equal(RuleCatalog.Label(r[12]), r[0]);
            Assert.Equal(RuleHelp.Plain(r[12]).Meaning, r[4]);
            Assert.Equal(RuleHelp.Plain(r[12]).Action, r[7]);
        });
    }

    [Fact]
    public void Full_rule_guide_lists_every_rule_and_matches_the_file_shipped_in_the_package()
    {
        var o = new StringWriter(); var e = new StringWriter();
        Assert.Equal(0, Rsmf.Cli.Program.Run(new[] { "explain", "--csv" }, o, e));
        var rows = ReadCsv(o.ToString());
        Assert.Equal(RuleCatalog.All.Select(r => r.Id), rows.Skip(1).Select(r => r[12]));
        Assert.Equal(Enumerable.Range(1, RuleCatalog.All.Count).Select(n => n.ToString("000")), rows.Skip(1).Select(r => r[0]));
        Assert.All(rows, r => Assert.Equal(13, r.Length));

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "RULES.md"))) dir = dir.Parent;
        Assert.NotNull(dir);
        Assert.Equal(o.ToString(), File.ReadAllText(Path.Combine(dir!.FullName, "RULE_GUIDE.csv")).TrimStart('\uFEFF'));
    }

    [Fact]
    public void Every_rule_has_a_guide_entry_saying_how_and_why_with_trusted_links()
    {
        Assert.Equal(RuleCatalog.All.Select(r => r.Id).OrderBy(x => x), RuleGuide.RuleIds.OrderBy(x => x));
        foreach (var rule in RuleCatalog.All)
        {
            var guide = RuleGuide.Get(rule.Id);
            Assert.True(guide.How.Length >= 40 && guide.How.EndsWith("."), rule.Id + " how");
            Assert.True(guide.Why.Length >= 40 && guide.Why.EndsWith("."), rule.Id + " why");
            var all = (guide.How + " " + guide.Why).ToLowerInvariant();
            Assert.DoesNotContain("guarantee", all);
            Assert.DoesNotContain("certif", all);
            Assert.False(string.IsNullOrWhiteSpace(RuleGuide.Origin(rule)));

            var links = RuleGuide.Links(rule.Id);
            Assert.All(links, l =>
            {
                Assert.True(RuleGuide.IsTrustedLink(l.Url), rule.Id + " link " + l.Url);
                Assert.False(string.IsNullOrWhiteSpace(l.Label));
                Assert.DoesNotContain("\u00A7", l.Section);
            });
            Assert.Equal(links.Count, links.Select(l => l.Url).Distinct().Count());
            // Every rule that speaks for Relativity's format or limits points at something Relativity published.
            if (rule.Category is FindingCategory.FORMAT or FindingCategory.LIMIT) Assert.NotEmpty(links);

            var block = RuleHelp.Format(rule.Id, 80);
            Assert.Contains("How checked:", block);
            Assert.Contains("Why:", block);
            Assert.Contains("Read more:", block);
        }
        Assert.Empty(RuleGuide.Links("Q-TIME-A"));
        Assert.Contains("not a Relativity requirement", RuleGuide.Origin(RuleCatalog.Get("Q-TIME-A")));
        Assert.Contains(RuleGuide.Links("F-MIME-002"), l => l.Url.StartsWith("https://help.relativity.com/", StringComparison.Ordinal) && l.Section.Contains("Header"));
        Assert.False(RuleGuide.IsTrustedLink("https://example.com/"));
        Assert.False(RuleGuide.IsTrustedLink("file:///C:/Windows/notepad.exe"));
        Assert.False(RuleGuide.IsTrustedLink(null));
    }

    // ------------------------------------------------------------ several files at once

    private static readonly string[] ContentReports =
        { "inventory.csv", "findings.jsonl", "metadata_coverage.csv", "time_window_exceptions.csv", "conversation_metrics.csv" };

    /// <summary>Clean, faulty, damaged and large fixtures in a folder of their own.</summary>
    private string MixedInput() => SmallInput(
        "valid-1.0.rsmf", "valid-2.0.rsmf", "sub/nested-valid.rsmf", "single-message.rsmf", "midnight-crossing.rsmf",
        "broken-references.rsmf", "blank-vs-missing.rsmf", "damaged-zip.rsmf", "damaged-mime.rsmf", "missing-manifest.rsmf",
        "invalid-timestamp.rsmf", "mixed-offsets.rsmf", "span-just-over-24h.rsmf", "events-9999.rsmf", "events-10001.rsmf", "notes.txt");

    [Theory]
    [InlineData(4, 64L * 1024 * 1024)]
    [InlineData(8, 64L * 1024 * 1024)]
    [InlineData(4, 1500L)]            // files of 1,500 bytes or more take the "large file" path and run alone
    [InlineData(3, 1L)]               // every file runs alone
    public void Reports_are_identical_whether_files_are_inspected_one_at_a_time_or_together(int workers, long largeFileBytes)
    {
        var input = MixedInput();
        var sequential = ScanRunner.Run(input, _fx.NewDir("out"), new InspectionOptions { TimeZoneId = "America/New_York", MaxParallelFiles = 1 });
        var parallel = ScanRunner.Run(input, _fx.NewDir("out"),
            new InspectionOptions { TimeZoneId = "America/New_York", MaxParallelFiles = workers, LargeFileBytes = largeFileBytes });

        Assert.Equal(sequential.Status, parallel.Status);
        Assert.Equal(sequential.Files.Select(f => f.RelativePath), parallel.Files.Select(f => f.RelativePath));
        Assert.Equal(sequential.Exceptions.Select(x => x.ToString()), parallel.Exceptions.Select(x => x.ToString()));
        foreach (var name in ContentReports)
            Assert.Equal(File.ReadAllText(Path.Combine(sequential.RunFolder, name)), File.ReadAllText(Path.Combine(parallel.RunFolder, name)));
        Assert.Equal(workers, RunJson(parallel).GetProperty("settings").GetProperty("filesInspectedAtOnce").GetInt32());
        Assert.False(Directory.Exists(Path.Combine(parallel.RunFolder, ".tmp")));
    }

    [Fact]
    public void Progress_counts_up_one_file_at_a_time_when_files_are_inspected_together()
    {
        var seen = new List<int>();
        var progress = new Progress(p => { if (p.Phase == "inspecting") lock (seen) seen.Add(p.FilesDone); });
        var run = ScanRunner.Run(MixedInput(), _fx.NewDir("out"), new InspectionOptions { TimeZoneId = "America/New_York", MaxParallelFiles = 4 }, progress);
        var total = run.Candidates.Count();
        Assert.Equal(Enumerable.Range(0, total + 1), seen);
    }

    [Fact]
    public void Cancellation_while_files_are_inspected_together_keeps_finished_results_and_marks_the_rest()
    {
        using var cts = new CancellationTokenSource();
        var progress = new Progress(p => { if (p.Phase == "inspecting" && p.FilesDone == 3) cts.Cancel(); });
        var run = ScanRunner.Run(MixedInput(), _fx.NewDir("out"), new InspectionOptions { TimeZoneId = "America/New_York", MaxParallelFiles = 4 }, progress, cts.Token);

        Assert.Equal(RunStatus.Cancelled, run.Status);
        AssertSixReports(run);
        var done = run.Candidates.Count(f => f.Checked);
        Assert.InRange(done, 3, run.Candidates.Count() - 1);
        Assert.All(run.Candidates.Where(f => !f.Checked), f =>
        {
            Assert.Equal(FileStatus.NotChecked, f.Status);
            Assert.Equal("I-RUN-001", Assert.Single(f.Findings).RuleId);
        });
        Assert.Equal(done, RunJson(run).GetProperty("files").GetProperty("checked").GetInt32());
    }

    [Fact]
    public void Cli_rejects_a_bad_parallel_value_and_accepts_a_good_one()
    {
        var o = new StringWriter(); var e = new StringWriter();
        var input = SmallInput("valid-2.0.rsmf", "broken-references.rsmf");
        Assert.Equal(64, Rsmf.Cli.Program.Run(new[] { "scan", input, "--out", _fx.NewDir("out"), "--parallel", "0" }, o, e));
        Assert.Contains("--parallel", e.ToString());
        Assert.Equal(64, Rsmf.Cli.Program.Run(new[] { "scan", input, "--out", _fx.NewDir("out"), "--parallel", "17" }, o, e));
        Assert.Equal(1, Rsmf.Cli.Program.Run(new[] { "scan", input, "--out", _fx.NewDir("out"), "--tz", "America/New_York", "--parallel", "2", "--quiet" }, o, e));
    }

    // ------------------------------------------------------------ application icon

    [Fact]
    public void App_icon_file_holds_the_standard_sizes()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "RULES.md"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var ico = File.ReadAllBytes(Path.Combine(dir!.FullName, "src", "Rsmf.App", "brick.ico"));

        Assert.Equal(0, BitConverter.ToUInt16(ico, 0));
        Assert.Equal(1, BitConverter.ToUInt16(ico, 2));          // 1 = icon
        int count = BitConverter.ToUInt16(ico, 4);
        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + 16 * i;
            sizes.Add(ico[entry] == 0 ? 256 : ico[entry]);
            Assert.Equal(32, BitConverter.ToUInt16(ico, entry + 6));   // 32-bit colour with transparency
            var length = BitConverter.ToInt32(ico, entry + 8);
            var offset = BitConverter.ToInt32(ico, entry + 12);
            Assert.True(length > 0 && offset + length <= ico.Length);
        }
        Assert.Equal(new[] { 16, 20, 24, 32, 40, 48, 64, 256 }, sizes);
    }

    // ------------------------------------------------------------ rule catalogue vs docs/RULES.md

    [Fact]
    public void Rule_catalogue_matches_docs_RULES_md()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "RULES.md"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var text = File.ReadAllLines(Path.Combine(dir!.FullName, "docs", "RULES.md"));

        var documented = new Dictionary<string, (string Severity, string Section)>();
        var section = "";
        foreach (var line in text)
        {
            if (line.StartsWith("## ")) section = line[3..].Split(' ')[0];
            var cells = line.Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length > 3 && System.Text.RegularExpressions.Regex.IsMatch(cells[1], @"^[FLQI]-[A-Z]+-[0-9A-Z]+$"))
                documented[cells[1]] = (cells[2], section);
        }

        Assert.Equal(documented.Keys.OrderBy(k => k), RuleCatalog.All.Select(r => r.Id).OrderBy(k => k));
        foreach (var rule in RuleCatalog.All)
        {
            Assert.Equal(documented[rule.Id].Severity, rule.Severity.ToString().ToLowerInvariant());
            Assert.Equal(documented[rule.Id].Section, rule.Category.ToString());
            Assert.False(string.IsNullOrWhiteSpace(rule.Source));
        }
        Assert.Contains($"Ruleset version: **{RuleCatalog.RulesetVersion}**", string.Join("\n", text));
    }
}
