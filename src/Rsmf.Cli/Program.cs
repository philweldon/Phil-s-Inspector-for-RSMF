using System.IO;
using System.Globalization;
using Rsmf.Core;

namespace Rsmf.Cli;

public static class Program
{
    /// <summary>The program name shown in help text. The app sets this when it runs commands itself.</summary>
    public static string CommandName { get; set; } = "rsmf-inspect";

    private const string Usage = """
        Phil's Inspector for RSMF files - local inspection and QC of .rsmf files

        Usage:
          rsmf-inspect scan <folder-or-file> --out <output-folder> [options]
          rsmf-inspect explain --csv
          rsmf-inspect explain [<rule number>]
          rsmf-inspect --version
          rsmf-inspect --help

        Options:
          --out <folder>               Folder in which a new run-<timestamp>-<id> folder is created (required)
          --tz <IANA zone>             Time zone for the calendar-day rule (default: UTC)
          --mode both|elapsed-only     Time-window mode (default: both)
          --size-threshold <GB>        Size limit in GB, decimals allowed (default: 2; 1 GB = 1,073,741,824 bytes)
          --participant-threshold <n>  Warn when a file has more unique participants than n (default: off)
          --parallel <n>               Files inspected at the same time, 1 to 16 (default: automatic, up to 4)
          --include-sensitive          Include message bodies and personal metadata values in reports
          --quiet                      Do not print progress

        Exit codes: 0 completed, no error findings; 1 completed with error findings or exceptions;
                    2 cancelled or failed; 64 bad arguments.
        """;

    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken external = default)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            stdout.WriteLine(Usage.Replace("rsmf-inspect", CommandName));
            return args.Length == 0 ? 64 : 0;
        }
        if (args[0] is "--version" or "-v" or "version")
        {
            stdout.WriteLine($"{AppInfo.Name} {AppInfo.Version} (ruleset {RuleCatalog.RulesetVersion})");
            return 0;
        }
        if (args[0] == "explain")
        {
            if (args.Length == 1)
            {
                foreach (var rule in RuleCatalog.All) stdout.WriteLine(RuleHelp.Format(rule.Id));
                return 0;
            }
            if (args[1] == "--csv")
            {
                stdout.Write(ReportWriter.RuleGuideCsv());
                return 0;
            }
            if (!RuleCatalog.TryResolve(args[1], out var chosen))
            {
                stderr.WriteLine($"Unknown rule: {args[1]}. Give a rule number such as 4, or run '{CommandName} explain' to list every rule.");
                return 64;
            }
            stdout.WriteLine(RuleHelp.Format(chosen.Id));
            return 0;
        }
        if (args[0] != "scan")
        {
            stderr.WriteLine($"Unknown command: {args[0]}");
            stderr.WriteLine(Usage.Replace("rsmf-inspect", CommandName));
            return 64;
        }

        string? input = null, output = null, tz = null;
        var mode = TimeWindowMode.Both;
        var size = RuleCatalog.DefaultSizeThresholdBytes;
        int? participants = null;
        var parallel = 0;
        bool sensitive = false, quiet = false;

        for (var i = 1; i < args.Length; i++)
        {
            string Next(string name)
            {
                if (i + 1 >= args.Length) throw new ArgumentException($"{name} needs a value");
                return args[++i];
            }
            try
            {
                switch (args[i])
                {
                    case "--out": output = Next("--out"); break;
                    case "--tz": tz = Next("--tz"); break;
                    case "--mode":
                        mode = Next("--mode") switch
                        {
                            "both" => TimeWindowMode.Both,
                            "elapsed-only" => TimeWindowMode.ElapsedOnly,
                            var m => throw new ArgumentException($"--mode must be both or elapsed-only, not {m}"),
                        };
                        break;
                    case "--size-threshold":
                        var sizeText = Next("--size-threshold");
                        if (SizeUnits.LooksLikeBytes(sizeText))
                            throw new ArgumentException("--size-threshold is now given in GB (for example 2 or 1.5), not bytes. " + sizeText + " GB is not a usable limit.");
                        if (!SizeUnits.TryParseGb(sizeText, out size))
                            throw new ArgumentException("--size-threshold must be a size in GB greater than zero, for example 2 or 1.5");
                        break;
                    case "--participant-threshold":
                        if (!int.TryParse(Next("--participant-threshold"), NumberStyles.None, CultureInfo.InvariantCulture, out var p))
                            throw new ArgumentException("--participant-threshold must be a whole number");
                        participants = p;
                        break;
                    case "--parallel":
                        if (!int.TryParse(Next("--parallel"), NumberStyles.None, CultureInfo.InvariantCulture, out parallel) || parallel < 1 || parallel > 16)
                            throw new ArgumentException("--parallel must be a whole number from 1 to 16");
                        break;
                    case "--include-sensitive": sensitive = true; break;
                    case "--quiet": quiet = true; break;
                    default:
                        if (args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unknown option: {args[i]}");
                        if (input is not null) throw new ArgumentException("Only one input path may be given");
                        input = args[i];
                        break;
                }
            }
            catch (ArgumentException ex)
            {
                stderr.WriteLine(ex.Message);
                return 64;
            }
        }

        if (input is null || output is null)
        {
            stderr.WriteLine("An input path and --out <output-folder> are required.");
            return 64;
        }
        if (!File.Exists(input) && !Directory.Exists(input))
        {
            stderr.WriteLine($"Input path not found: {input}");
            return 64;
        }

        var options = new InspectionOptions
        {
            TimeZoneId = tz, TimeWindowMode = mode, SizeThresholdBytes = size,
            ParticipantWarningThreshold = participants, IncludeSensitive = sensitive,
            MaxParallelFiles = parallel,
        };
        try { options.ResolveZone(); }
        catch (ArgumentException ex)
        {
            stderr.WriteLine(ex.Message);
            return 64;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(external);
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += onCancel;
        try
        {
            var last = -1;
            var progress = quiet ? null : new SyncProgress(p =>
            {
                if (p.Phase != "inspecting" || p.FilesDone == last) return;
                last = p.FilesDone;
                stdout.WriteLine($"[{p.FilesDone}/{p.FilesTotal}] {p.Current}");
            });
            var run = ScanRunner.Run(input, output, options, progress, cts.Token);

            stdout.WriteLine();
            stdout.WriteLine(File.ReadAllText(Path.Combine(run.RunFolder, "summary.txt")));
            stdout.WriteLine($"Reports written to: {run.RunFolder}");
            if (run.Status is RunStatus.Cancelled or RunStatus.Failed) return 2;
            var errors = run.Files.Any(f => f.Count(Severity.Error) > 0) || run.ScanFindings.Count > 0;
            return errors || run.Status == RunStatus.CompletedWithExceptions ? 1 : 0;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>Reports synchronously on the calling thread so console output stays ordered.</summary>
    private sealed class SyncProgress(Action<ScanProgress> action) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => action(value);
    }
}
