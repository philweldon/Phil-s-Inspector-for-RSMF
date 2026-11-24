using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Rsmf.App;

public partial class App : Application
{
    /// <summary>Set when the app was started with <c>--selftest</c>; null in normal use.</summary>
    public static SelfTest? SelfTest { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Numbers and dates the app formats itself are culture-independent, matching the CLI and the reports.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        if (CommandLine.IsCommand(e.Args))
        {
            Environment.Exit(CommandLine.Run(e.Args));   // command-line use: no window is ever created
            return;
        }

        if (e.Args.Length >= 2 && e.Args[0] == "--selftest")
            SelfTest = new SelfTest(e.Args[1], e.Args.Length > 2 ? e.Args[2] : null);
        base.OnStartup(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (SelfTest is not null)
        {
            SelfTest.Fail("dispatcher-exception " + e.Exception.GetType().Name + ": " + e.Exception.Message);
            return;
        }
        // Keep the window alive and say what happened; never show file content here.
        MessageBox.Show("An unexpected error occurred: " + e.Exception.GetType().Name + "\n\n" + e.Exception.Message,
            Rsmf.Core.AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

/// <summary>
/// Start-up self-check used by the build's smoke step:
/// <c>RsmfInspector.exe --selftest &lt;log file&gt; [&lt;.rsmf file&gt;]</c>.
/// Confirms the main window renders and, when a file is given, that the viewer loads it; writes
/// each step to the log, shows no dialogs, and exits with 0 (pass), 1 (failure) or 2 (timed out).
/// </summary>
public sealed class SelfTest
{
    private readonly string _logPath;
    private readonly string? _file;
    private readonly DispatcherTimer _watchdog;
    private bool _finished;

    public SelfTest(string logPath, string? file)
    {
        _logPath = Path.GetFullPath(logPath);
        _file = file is null ? null : Path.GetFullPath(file);
        File.WriteAllText(_logPath, "selftest-started version=" + Rsmf.Core.AppInfo.Version + Environment.NewLine);
        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(300) };
        _watchdog.Tick += (_, _) => Finish(2, "selftest-timeout");
        _watchdog.Start();
    }

    private void Log(string line) => File.AppendAllText(_logPath, line + Environment.NewLine);

    public void Fail(string reason) => Finish(1, reason);

    private void Finish(int exitCode, string line)
    {
        if (_finished) return;
        _finished = true;
        _watchdog.Stop();
        Log(line);
        Log("selftest-finished exit=" + exitCode.ToString(CultureInfo.InvariantCulture));
        Application.Current.Shutdown(exitCode);
    }

    /// <summary>Saves a picture of a window's content beside the log, so the layout can be reviewed.</summary>
    private void SavePicture(Window window, string fileName)
    {
        try
        {
            if (window.Content is not FrameworkElement root || root.ActualWidth < 1 || root.ActualHeight < 1) return;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(window.Background ?? Brushes.White, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            }
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(Path.GetDirectoryName(_logPath)!, fileName));
            encoder.Save(stream);
            Log("picture " + fileName);
        }
        catch (Exception ex)
        {
            Log("picture-failed " + fileName + " " + ex.GetType().Name);
        }
    }

    /// <summary>Starts this same exe with command-line arguments and reports its exit code and the start and end of its output.</summary>
    private static async Task<string> RunSelfAsync(params string[] args)
    {
        var info = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var process = System.Diagnostics.Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill();
            return "timed-out";
        }
        string Line(string text, bool last)
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return lines.Length == 0 ? "" : last ? lines[^1] : lines[0];
        }
        var o = await stdout;
        var e = await stderr;
        return $"exit={process.ExitCode} out-first=[{Line(o, false)}] out-last=[{Line(o, true)}] err-first=[{Line(e, false)}]";
    }

    /// <summary>Called once the main window has rendered.</summary>
    public async Task RunAsync(MainWindow main)
    {
        try
        {
            Log("main-window-rendered title=" + main.Title);
            Log("icon " + main.SelfTestIcon());
            Log("click-sound " + MainWindow.SelfTestClickSound());
            Log("emoji " + EmojiPainter.SelfTest());
            if (_file is not null)
            {
                var temp = Path.Combine(Path.GetDirectoryName(_logPath)!, "selftest-tmp");
                var viewer = main.OpenViewerWith(_file, new Rsmf.Core.InspectionOptions { TimeZoneId = "America/New_York", TempDirectory = temp });
                var outcome = await viewer.LoadCompleted;
                Log("viewer " + outcome);
                await Task.Delay(1000); // let the message list and tabs render
                Log("search " + viewer.SelfTestSearch("ample"));
                await Task.Delay(800);
                SavePicture(viewer, "selftest-viewer.png");
                viewer.Close();
                if (!outcome.StartsWith("loaded", StringComparison.Ordinal))
                {
                    Finish(1, "viewer-load-failed");
                    return;
                }

                // A file with findings, when the folder has one: show the findings tab and its rule help.
                var withFindings = Path.Combine(Path.GetDirectoryName(_file)!, "broken-references.rsmf");
                if (File.Exists(withFindings))
                {
                    var second = main.OpenViewerWith(withFindings, new Rsmf.Core.InspectionOptions { TimeZoneId = "America/New_York", TempDirectory = temp });
                    var secondOutcome = await second.LoadCompleted;
                    await Task.Delay(600);
                    Log("findings " + (secondOutcome.StartsWith("loaded", StringComparison.Ordinal) ? second.SelfTestShowFirstFinding() : secondOutcome));
                    await Task.Delay(800);
                    SavePicture(second, "selftest-findings.png");
                    second.Close();
                }

                // Scan the folder the file is in through the normal path, so the grid and summary cards are exercised.
                var output = Path.Combine(Path.GetDirectoryName(_logPath)!, "selftest-out");
                Log("scan " + await main.SelfTestScanAsync(Path.GetDirectoryName(_file)!, output));
                await Task.Delay(1000); // let the rows render
                SavePicture(main, "selftest-main.png");
                Log("explorer " + main.SelfTestExplorer());
                await Task.Delay(800);
                SavePicture(main, "selftest-explorer.png");

                // Follow an explorer occurrence into the viewer, as a double-click would.
                var opened = main.SelfTestOpenEventOccurrence();
                if (opened is null) Log("click-through no event occurrence");
                else
                {
                    var openedOutcome = await opened.LoadCompleted;
                    await Task.Delay(600);
                    Log("click-through " + (openedOutcome.StartsWith("loaded", StringComparison.Ordinal) ? opened.SelfTestState() : openedOutcome));
                    opened.Close();
                }

                // A folder dropped on the window, and the opt-in settings file (written to the log folder, not beside the program).
                Log("drop " + await main.SelfTestDropAsync(Path.GetDirectoryName(_file)!, output));
                Log("settings " + main.SelfTestSettings(Path.Combine(Path.GetDirectoryName(_logPath)!, "selftest-settings.json"), output));

                // The same exe as a command-line tool, with its output captured.
                Log("cli-version " + await RunSelfAsync("--version"));
                Log("cli-scan " + await RunSelfAsync("scan", Path.GetDirectoryName(_file)!, "--out", output, "--tz", "America/New_York", "--quiet"));
                Log("cli-bad " + await RunSelfAsync("scan"));

                // No report folder chosen: the run folder must appear beside the scanned folder, not inside it.
                Log("beside " + await main.SelfTestBesideAsync(Path.GetDirectoryName(_file)!));
                await Task.Delay(500);
                SavePicture(main, "selftest-footer.png");

                // Convert the same folder to PDF and keep one PDF beside the log for inspection.
                Log("pdf " + await main.SelfTestPdfAsync(Path.GetDirectoryName(_file)!, output, Path.Combine(Path.GetDirectoryName(_logPath)!, "selftest-valid.pdf")));

                // An optional folder of layout stress files beside the log.
                var stress = Path.Combine(Path.GetDirectoryName(_logPath)!, "pdfstress");
                var stressFile = Path.Combine(stress, "pdf-stress.rsmf");
                if (File.Exists(stressFile))
                {
                    // The viewer on a file with emoji, several scripts and pictures.
                    var emojiViewer = main.OpenViewerWith(stressFile, new Rsmf.Core.InspectionOptions { TimeZoneId = "America/New_York", TempDirectory = Path.Combine(Path.GetDirectoryName(_logPath)!, "selftest-tmp") });
                    var emojiOutcome = await emojiViewer.LoadCompleted;
                    Log("emoji-viewer " + emojiOutcome + " " + emojiViewer.SelfTestShowText("Scripts:"));
                    await Task.Delay(1200);
                    SavePicture(emojiViewer, "selftest-emoji.png");
                    emojiViewer.Close();
                }
                if (Directory.Exists(stress))
                    Log("pdf-stress " + await main.SelfTestPdfAsync(stress, output, Path.Combine(Path.GetDirectoryName(_logPath)!, "selftest-stress.pdf")));

                // The shipped sample set, when it is beside the build folder.
                var samples = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_logPath)!, "..", "samples"));
                if (Directory.Exists(samples))
                {
                    Log("samples " + await main.SelfTestSamplesAsync(samples, output));
                    await Task.Delay(800);
                    SavePicture(main, "selftest-samples.png");
                }

                // The rule guide: every rule, with how and why it is checked and its source links.
                Log("guide " + main.SelfTestGuide());
                await Task.Delay(800);
                SavePicture(main, "selftest-guide.png");
            }
            await Task.Delay(500);
            Finish(0, "selftest-passed");
        }
        catch (Exception ex)
        {
            Finish(1, "selftest-exception " + ex.GetType().Name + ": " + ex.Message);
        }
    }
}
