using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using NodaTime;
using Rsmf.Core;

namespace Rsmf.App;

public partial class MainWindow : Window
{
    private const string AllCategories = "All categories";
    private const string AllSeverities = "All severities";
    private const string ModeBoth = "Calendar day and elapsed span (Rules A + B)";
    private const string ModeElapsed = "Elapsed span only (Rule B)";

    private readonly ObservableCollection<FileRow> _rows = new();
    private readonly ICollectionView _view;
    private CancellationTokenSource? _cts;
    private InspectionOptions? _lastOptions;
    private string? _lastOutputFolder;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"{AppInfo.Name} {AppInfo.Version}";
        VersionText.Text = $"Version {AppInfo.Version}  \u00B7  ruleset {RuleCatalog.RulesetVersion}";

        ZoneBox.ItemsSource = DateTimeZoneProviders.Tzdb.Ids.OrderBy(id => id, StringComparer.Ordinal).ToList();
        ZoneBox.Text = new InspectionOptions().ResolveZone().Id;
        ModeBox.ItemsSource = new[] { ModeBoth, ModeElapsed };
        ModeBox.SelectedIndex = 0;
        SizeBox.Text = SizeUnits.FormatGb(RuleCatalog.DefaultSizeThresholdBytes);

        CategoryFilter.ItemsSource = new[] { AllCategories }.Concat(Enum.GetNames<FindingCategory>()).ToList();
        CategoryFilter.SelectedIndex = 0;
        SeverityFilter.ItemsSource = new[] { AllSeverities, "error", "warning", "info" };
        SeverityFilter.SelectedIndex = 0;

        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterRow;
        ResultsGrid.ItemsSource = _view;
        UpdateRowCount();

        InitGuide();

        if (Directory.Exists(SamplesFolder)) SamplesButton.Visibility = Visibility.Visible;

        // Settings are only ever read back when the user chose to keep them (never during a self-check).
        if (App.SelfTest is null && (UserSettings.Load(SettingsPath) ?? UserSettings.Load(LegacySettingsPath)) is { } saved)
        {
            ApplySettings(saved);
            RememberBox.IsChecked = true;
        }
        _settingsReady = true;

        if (App.SelfTest is { } selfTest)
            ContentRendered += async (_, _) => await selfTest.RunAsync(this);
    }

    // ------------------------------------------------------------------ settings

    private bool _settingsReady;

    /// <summary>Where remembered settings are kept: the user's local application data folder, only when the user ticks the box.</summary>
    private static string SettingsPath => UserSettings.DefaultPath;

    /// <summary>Where versions up to 1.22.0 kept them: beside the program. Still read (and removed on untick), never written.</summary>
    private static string LegacySettingsPath => System.IO.Path.Combine(AppContext.BaseDirectory, UserSettings.FileName);

    /// <summary>Saves to <see cref="SettingsPath"/>, creating its folder first. False when that cannot be done.</summary>
    private static bool SaveSettings(UserSettings s)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
        return s.Save(SettingsPath);
    }

    private void ApplySettings(UserSettings s)
    {
        if (s.TimeZoneId is not null) ZoneBox.Text = s.TimeZoneId;
        ModeBox.SelectedIndex = s.TimeWindowMode == "elapsed-only" ? 1 : 0;
        SizeBox.Text = SizeUnits.FormatGb(s.SizeThresholdBytes);
        ParticipantBox.Text = s.ParticipantWarningThreshold?.ToString(CultureInfo.InvariantCulture) ?? "";
        OutputBox.Text = s.OutputFolder ?? "";
    }

    private static UserSettings ToSettings(InspectionOptions options, string outputFolder) => new()
    {
        TimeZoneId = options.TimeZoneId,
        TimeWindowMode = options.TimeWindowMode == TimeWindowMode.ElapsedOnly ? "elapsed-only" : "both",
        SizeThresholdBytes = options.SizeThresholdBytes,
        ParticipantWarningThreshold = options.ParticipantWarningThreshold,
        OutputFolder = outputFolder,
    };

    private void OnRememberChanged(object sender, RoutedEventArgs e)
    {
        if (!_settingsReady) return;
        if (RememberBox.IsChecked == true)
        {
            StatusText.Text = "Settings will be saved when the next scan starts.";
        }
        else
        {
            StatusText.Text = UserSettings.Forget(SettingsPath) & UserSettings.Forget(LegacySettingsPath)
                ? "Saved settings removed."
                : "The saved settings file could not be removed.";
        }
    }

    /// <summary>Round-trips the window's settings through a file (self-check only; writes to the given path).</summary>
    internal string SelfTestSettings(string path, string outputFolder)
    {
        var presentBefore = File.Exists(SettingsPath);
        ZoneBox.Text = "Europe/London";
        ModeBox.SelectedIndex = 1;
        ParticipantBox.Text = "12";
        OutputBox.Text = outputFolder;
        if (!TryBuildOptions(out var options, out var output)) return "settings-invalid";
        var saved = ToSettings(options, output).Save(path);
        ZoneBox.Text = "America/New_York";
        ModeBox.SelectedIndex = 0;
        ParticipantBox.Text = "";
        var loaded = UserSettings.Load(path);
        if (loaded is not null) ApplySettings(loaded);
        var result = $"saved={saved} size-box=[{SizeBox.Text}] size-bytes={options.SizeThresholdBytes} zone={ZoneBox.Text} mode={ModeBox.SelectedIndex} participants={ParticipantBox.Text} remember-box={RememberBox.IsChecked == true} " +
                     $"settings-file-changed={File.Exists(SettingsPath) != presentBefore}";
        ZoneBox.Text = "America/New_York";
        ModeBox.SelectedIndex = 0;
        ParticipantBox.Text = "";
        return result;
    }

    // ------------------------------------------------------------------ dropped files and folders

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return; // leave text drags to the text boxes
        e.Effects = _cts is null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true;
        if (_cts is not null) return; // a scan is running
        await HandleDroppedAsync(e.Data.GetData(DataFormats.FileDrop) as string[]);
    }

    private async Task HandleDroppedAsync(string[]? paths)
    {
        var input = DroppedInput.From(paths);
        if (input.Kind == DroppedKind.Nothing)
        {
            MessageBox.Show(this, input.Note, Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!TryBuildOptions(out var options, out var output)) return;
        await RunScanAsync(input.Path, output, options, openViewer: input.Kind == DroppedKind.File);
        if (input.Note.Length > 0) StatusText.Text += " " + input.Note;
    }

    /// <summary>Handles a dropped path through the normal path (self-check only).</summary>
    internal async Task<string> SelfTestDropAsync(string path, string outputFolder)
    {
        OutputBox.Text = outputFolder;
        await HandleDroppedAsync(new[] { path });
        return $"rows={_rows.Count} checked={CardFilesValue.Text} status=[{StatusText.Text}]";
    }

    private bool TryBuildOptions(out InspectionOptions options, out string outputFolder)
    {
        options = new InspectionOptions();
        outputFolder = OutputBox.Text.Trim();

        var zoneId = ZoneBox.Text.Trim();
        if (zoneId.Length == 0 || DateTimeZoneProviders.Tzdb.GetZoneOrNull(zoneId) is null)
            return Fail("The time zone is not a known IANA zone id (for example America/New_York).");

        if (SizeUnits.LooksLikeBytes(SizeBox.Text))
            return Fail("The size threshold is in GB (for example 2 or 1.5), not bytes.");
        if (!SizeUnits.TryParseGb(SizeBox.Text, out var size))
            return Fail("The size threshold must be a size in GB greater than zero, for example 2 or 1.5.");

        int? participants = null;
        var participantText = ParticipantBox.Text.Trim();
        if (participantText.Length > 0)
        {
            if (!int.TryParse(participantText, NumberStyles.None, CultureInfo.InvariantCulture, out var p))
                return Fail("The participant warning threshold must be a whole number, or blank to turn it off.");
            participants = p;
        }

        // A blank report folder means "beside whatever is scanned"; that is worked out when the scan starts.
        if (outputFolder.Length > 0)
        {
            try
            {
                outputFolder = System.IO.Path.GetFullPath(outputFolder);
                Directory.CreateDirectory(outputFolder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return Fail("The report folder cannot be used: " + ex.Message);
            }
        }

        options = new InspectionOptions
        {
            TimeZoneId = zoneId,
            TimeWindowMode = ModeBox.SelectedIndex == 1 ? TimeWindowMode.ElapsedOnly : TimeWindowMode.Both,
            SizeThresholdBytes = size,
            ParticipantWarningThreshold = participants,
            IncludeSensitive = SensitiveBox.IsChecked == true,
        };
        if (RememberBox.IsChecked == true && App.SelfTest is null && !SaveSettings(ToSettings(options, outputFolder)))
            StatusText.Text = "The settings could not be saved (the settings folder is not writable).";
        return true;

        bool Fail(string message)
        {
            MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void OnBrowseOutput(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the folder for reports" };
        if (dialog.ShowDialog(this) == true) OutputBox.Text = dialog.FolderName;
    }

    // ------------------------------------------------------------------ scanning

    // ------------------------------------------------------------------ click sound

    /// <summary>The soft click from Windows' own sound set. Nothing is bundled; a PC without the file stays silent.</summary>
    private static string ClickSoundPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Navigation Start.wav");

    private static readonly Lazy<System.Media.SoundPlayer?> ClickSound = new(() =>
    {
        try
        {
            if (!File.Exists(ClickSoundPath)) return null;
            var player = new System.Media.SoundPlayer(ClickSoundPath);
            player.Load();
            return player;
        }
        catch (Exception)
        {
            return null;   // unreadable or not a sound file: no click, nothing else changes
        }
    });

    private static void PlayClick()
    {
        try { ClickSound.Value?.Play(); }
        catch (Exception) { /* a sound must never get in the way of a scan */ }
    }

    /// <summary>Reports whether the click sound is available on this PC (self-check only; nothing is played).</summary>
    internal static string SelfTestClickSound() =>
        $"file-present={File.Exists(ClickSoundPath)} loaded={ClickSound.Value is not null}";

    private async void OnScanFolder(object sender, RoutedEventArgs e)
    {
        PlayClick();
        if (!TryBuildOptions(out var options, out var output)) return;
        var dialog = new OpenFolderDialog { Title = "Choose the folder to scan" };
        if (dialog.ShowDialog(this) != true) return;
        await RunScanAsync(dialog.FolderName, output, options, openViewer: false);
    }

    private async void OnOpenFile(object sender, RoutedEventArgs e)
    {
        if (!TryBuildOptions(out var options, out var output)) return;
        var dialog = new OpenFileDialog
        {
            Title = "Open RSMF file",
            Filter = "RSMF files (*.rsmf)|*.rsmf|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        await RunScanAsync(dialog.FileName, output, options, openViewer: true);
    }

    /// <summary>Runs a scan through the normal path and reports what the window then shows (self-check only).</summary>
    internal async Task<string> SelfTestScanAsync(string input, string output)
    {
        await RunScanAsync(input, output, new InspectionOptions { TimeZoneId = "America/New_York" }, openViewer: false);
        return $"rows={_rows.Count} checked={CardFilesValue.Text} errors={CardErrorsValue.Text} warnings={CardWarningsValue.Text} " +
               $"time-window=[{CardTimeValue.Text}] coverage={CardCoverageValue.Text} status=[{StatusText.Text}]";
    }

    private void UpdateCards(RunResult run)
    {
        var st = RunStatistics.From(run);
        string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

        CardFilesValue.Text = N(st.Checked);
        CardFilesNote.Text = $"of {N(st.Candidates)} RSMF file(s); {N(st.FilesClean)} clean" + (st.OtherFiles > 0 ? $"; {N(st.OtherFiles)} other" : "");

        CardErrorsValue.Text = N(st.ErrorFindings);
        CardErrorsValue.Foreground = st.ErrorFindings > 0 ? Palette.Error : Palette.Ok;
        CardErrorsNote.Text = st.ErrorFindings > 0 ? $"in {N(st.FilesWithErrors)} file(s)" : "No error findings";

        CardWarningsValue.Text = N(st.WarningFindings);
        CardWarningsValue.Foreground = st.WarningFindings > 0 ? Palette.Warning : Palette.Ok;
        CardWarningsNote.Text = st.WarningFindings > 0 ? $"{N(st.FilesWithWarningsOnly)} file(s) with warnings only" : "No warnings";

        CardTimeValue.Text = $"{N(st.TimeWindowPass)} pass \u00B7 {N(st.TimeWindowFail)} fail";
        CardTimeValue.Foreground = st.TimeWindowFail > 0 ? Palette.Error : st.TimeWindowIndeterminate > 0 ? Palette.Warning : Palette.Ok;
        CardTimeNote.Text = st.TimeWindowIndeterminate > 0 ? $"{N(st.TimeWindowIndeterminate)} indeterminate" : "None indeterminate";

        CardCoverageValue.Text = st.CoverageComplete ? "Complete" : "Incomplete";
        CardCoverageValue.Foreground = st.CoverageComplete ? Palette.Ok : Palette.Warning;
        CardCoverageNote.Text = st.Status switch
        {
            RunStatus.Completed => "Every file was checked",
            RunStatus.CompletedWithExceptions => "Some files were unreadable or malformed",
            RunStatus.Cancelled => $"Cancelled; {N(st.NotChecked)} file(s) not checked",
            _ => "The scan failed",
        };
    }

    /// <summary>The sample files shipped beside the program, when present.</summary>
    private static string SamplesFolder => System.IO.Path.Combine(AppContext.BaseDirectory, "samples");

    private async void OnTrySamples(object sender, RoutedEventArgs e)
    {
        if (!TryBuildOptions(out var options, out var output)) return;
        await RunSamplesAsync(SamplesFolder, output, options);
    }

    /// <summary>Scans the sample set with the two settings it is built for; other settings come from the window.</summary>
    private async Task RunSamplesAsync(string folder, string output, InspectionOptions options)
    {
        var sampleOptions = new InspectionOptions
        {
            TimeZoneId = "America/New_York",
            ParticipantWarningThreshold = 5,
            TimeWindowMode = TimeWindowMode.Both,
            SizeThresholdBytes = options.SizeThresholdBytes,
            IncludeSensitive = options.IncludeSensitive,
        };
        await RunScanAsync(folder, output, sampleOptions, openViewer: false);
        StatusText.Text += " Samples scanned with time zone America/New_York, rules A + B and participant threshold 5.";
    }

    /// <summary>Scans a sample folder through the same path as the button (self-check only).</summary>
    internal async Task<string> SelfTestSamplesAsync(string folder, string output)
    {
        await RunSamplesAsync(folder, output, new InspectionOptions());
        return $"rows={_rows.Count} checked={CardFilesValue.Text} note=[{CardFilesNote.Text}] errors={CardErrorsValue.Text} warnings={CardWarningsValue.Text} " +
               $"rules={RuleGroupGrid.Items.Count} button-visible={SamplesButton.Visibility == Visibility.Visible}";
    }

    /// <summary>Opens the first explorer occurrence that points at an event, as a double-click would (self-check only).</summary>
    internal ViewerWindow? SelfTestOpenEventOccurrence()
    {
        var occurrence = RuleGroupGrid.Items.OfType<RuleGroupRow>().SelectMany(g => g.Group.Occurrences)
            .FirstOrDefault(o => o.IsRsmfFile && o.Finding.Location.StartsWith("$.events[", StringComparison.Ordinal));
        return occurrence is null ? null : OpenViewer(occurrence.FullPath, occurrence.Finding);
    }

    /// <summary>True when the input is the sample folder shipped with the program.</summary>
    private static bool IsShippedSamples(string input)
    {
        try
        {
            return string.Equals(
                System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(input)),
                System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(SamplesFolder)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Documents\RsmfInspector, created if need be: where sample reports go when the place beside the shipped
    /// samples is read-only (a Microsoft Store install). Null when it cannot be used.
    /// </summary>
    private static string? SamplesReportFolder()
    {
        try
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(documents)) return null;
            var folder = System.IO.Path.Combine(documents, "RsmfInspector");
            Directory.CreateDirectory(folder);
            return OutputLocation.CanWrite(folder) ? folder : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Settles where this run's folder goes. With no folder chosen it goes beside the scanned folder, named
    /// after it; if that place cannot be written to, the shipped samples report under Documents and for
    /// anything else the user is asked to pick a folder.
    /// </summary>
    private bool TryResolveOutput(string input, string kind, ref string output, out string prefix)
    {
        prefix = kind == "pdf" ? "pdf-" : "run-";
        if (output.Length > 0) return true;

        var beside = OutputLocation.Beside(input);
        if (beside is not null && OutputLocation.CanWrite(beside))
        {
            output = beside;
            prefix = OutputLocation.Prefix(input, kind);
            return true;
        }

        if (IsShippedSamples(input) && SamplesReportFolder() is { } documents)
        {
            output = documents;
            prefix = OutputLocation.Prefix(input, kind);
            return true;
        }

        MessageBox.Show(this,
            (beside is null ? "There is no folder beside the one you chose" : "The folder beside the one you chose cannot be written to") +
            ", so the reports cannot go there. Choose a folder for them.",
            Title, MessageBoxButton.OK, MessageBoxImage.Information);
        var dialog = new OpenFolderDialog { Title = "Choose the folder for reports" };
        if (dialog.ShowDialog(this) != true) return false;
        output = dialog.FolderName;
        return true;
    }

    /// <summary>Shows a finished report in Notepad. Only the tool's own text report is ever opened this way.</summary>
    private void ShowReport(string path)
    {
        if (App.SelfTest is not null || !File.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path }, UseShellExecute = false });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            StatusText.Text += " The report could not be opened automatically; use Show in Explorer.";
        }
    }

    private async Task RunScanAsync(string input, string output, InspectionOptions options, bool openViewer)
    {
        if (!TryResolveOutput(input, "rsmf-report", ref output, out var folderPrefix)) return;
        SetBusy(true);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        ScanProgressBar.IsIndeterminate = true;
        StatusText.Text = "Discovering files...";
        var progress = new Progress<ScanProgress>(p =>
        {
            if (p.Phase != "inspecting") return;
            ScanProgressBar.IsIndeterminate = false;
            ScanProgressBar.Maximum = Math.Max(1, p.FilesTotal);
            ScanProgressBar.Value = p.FilesDone;
            StatusText.Text = $"Checking {p.FilesDone} of {p.FilesTotal}";
        });

        RunResult? run = null;
        try
        {
            run = await Task.Run(() => ScanRunner.Run(input, output, options, progress, token, folderPrefix));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "The scan could not be completed: " + ex.GetType().Name + "\n\n" + ex.Message, Title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            ScanProgressBar.IsIndeterminate = false;
            SetBusy(false);
        }
        if (run is null)
        {
            StatusText.Text = "Scan failed.";
            return;
        }

        _lastOptions = options;
        _lastOutputFolder = output;
        _rows.Clear();
        foreach (var f in run.Files) _rows.Add(new FileRow(f));
        UpdateRowCount();
        UpdateCards(run);
        UpdateExplorer(run);
        RunFolderBox.Text = run.RunFolder;
        ShowFolderButton.IsEnabled = true;
        ScanProgressBar.Maximum = 1;
        ScanProgressBar.Value = run.Status == RunStatus.Cancelled ? 0 : 1;

        var checkedCount = run.Candidates.Count(f => f.Checked);
        StatusText.Text = run.Status switch
        {
            RunStatus.Completed => $"Completed: {checkedCount} file(s) checked.",
            RunStatus.CompletedWithExceptions => $"Completed with exceptions: {checkedCount} checked; coverage incomplete.",
            RunStatus.Cancelled => $"Cancelled: {checkedCount} of {run.Candidates.Count()} checked; reports written.",
            _ => "Failed: " + run.FailureMessage,
        };

        if (openViewer && run.Status != RunStatus.Cancelled && run.Files.Count == 1) OpenViewer(run.Files[0].Path);
        else ShowReport(System.IO.Path.Combine(run.RunFolder, "summary.txt"));
    }

    // ------------------------------------------------------------------ PDF conversion

    private async void OnConvertPdf(object sender, RoutedEventArgs e)
    {
        if (!TryBuildOptions(out var options, out var output)) return;
        var dialog = new OpenFolderDialog { Title = "Choose the folder of RSMF files to convert to PDF" };
        if (dialog.ShowDialog(this) != true) return;
        var answer = MessageBox.Show(this,
            "One PDF will be made for each RSMF file in:\n" + dialog.FolderName + "\n\n" +
            "The PDFs go to a new folder beside that one (or in the report folder, if you chose one). Unlike the QC reports, they contain the message text, " +
            "participant names and inline pictures, so handle them like the source files.\n\nContinue?",
            Title, MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (answer != MessageBoxResult.OK) return;
        await RunPdfAsync(dialog.FolderName, output, options);
    }

    private async Task<PdfRun?> RunPdfAsync(string input, string output, InspectionOptions options)
    {
        if (!TryResolveOutput(input, "pdf", ref output, out var folderPrefix)) return null;
        SetBusy(true);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        ScanProgressBar.IsIndeterminate = true;
        StatusText.Text = "Finding RSMF files...";
        var progress = new Progress<PdfProgress>(p =>
        {
            ScanProgressBar.IsIndeterminate = false;
            ScanProgressBar.Maximum = Math.Max(1, p.FilesTotal);
            ScanProgressBar.Value = p.FilesDone;
            StatusText.Text = $"Converting file {Math.Min(p.FilesDone + 1, p.FilesTotal)} of {p.FilesTotal} to PDF  \u00B7  {p.PagesDone.ToString("N0", CultureInfo.InvariantCulture)} page(s) so far";
        });

        PdfRun? run = null;
        try
        {
            run = await PdfConverter.RunAsync(input, output, options, progress, token, folderPrefix);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "The conversion could not be completed: " + ex.GetType().Name + "\n\n" + ex.Message, Title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            ScanProgressBar.IsIndeterminate = false;
            SetBusy(false);
        }
        if (run is null)
        {
            StatusText.Text = "PDF conversion failed.";
            return null;
        }

        RunFolderBox.Text = run.RunFolder;
        ShowFolderButton.IsEnabled = true;
        ScanProgressBar.Maximum = 1;
        ScanProgressBar.Value = run.Cancelled ? 0 : 1;
        var pages = run.Pages.ToString("N0", CultureInfo.InvariantCulture);
        var missed = run.NotConverted > 0 ? $" {run.NotConverted} not converted; see {PdfExport.LogFileName}." : "";
        StatusText.Text = run.Entries.Count == 0
            ? "No .rsmf files were found in that folder."
            : run.Cancelled
                ? $"PDF conversion cancelled: {run.Converted} of {run.Entries.Count} file(s) converted, {pages} page(s)."
                : $"PDF: {run.Converted} of {run.Entries.Count} file(s) converted, {pages} page(s)." + missed;
        ShowReport(System.IO.Path.Combine(run.RunFolder, PdfExport.SummaryFileName));
        return run;
    }

    /// <summary>Scans with no report folder chosen and says where the run folder went (self-check only).</summary>
    internal async Task<string> SelfTestBesideAsync(string input)
    {
        OutputBox.Text = "";
        if (!TryBuildOptions(out var options, out var output)) return "options-invalid";
        await RunScanAsync(input, output, options, openViewer: false);
        var folder = RunFolderBox.Text;
        var expectedParent = OutputLocation.Beside(input) ?? "";
        return $"output-box-blank={output.Length == 0} folder=[{System.IO.Path.GetFileName(folder)}] beside-source={string.Equals(System.IO.Path.GetDirectoryName(folder), expectedParent, StringComparison.OrdinalIgnoreCase)} " +
               $"inside-source={folder.StartsWith(System.IO.Path.GetFullPath(input) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)} summary={File.Exists(System.IO.Path.Combine(folder, "summary.txt"))}";
    }

    /// <summary>Converts a folder through the normal path and keeps a copy of one PDF for inspection (self-check only).</summary>
    internal async Task<string> SelfTestPdfAsync(string input, string output, string keepCopy)
    {
        var watch = Stopwatch.StartNew();
        var run = await RunPdfAsync(input, output, new InspectionOptions { TimeZoneId = "America/New_York" });
        if (run is null) return "failed";
        var converted = run.Entries.Where(x => x.Status == PdfStatus.Converted).ToList();
        var keep = converted.FirstOrDefault(x => System.IO.Path.GetFileName(x.Source) == "valid-2.0.rsmf") ?? converted.FirstOrDefault();
        if (keep is not null) File.Copy(keep.PdfPath, keepCopy, overwrite: true);
        var largest = converted.OrderByDescending(x => x.Pages).FirstOrDefault();
        var failed = string.Join(";", run.Entries.Where(x => x.Status == PdfStatus.Failed).Select(x => x.RelativePath + " " + x.Note));
        return $"files={run.Entries.Count} converted={run.Converted} not-converted={run.NotConverted} pages={run.Pages} " +
               $"largest=[{largest?.RelativePath} pages={largest?.Pages} bytes={largest?.PdfBytes}] seconds={watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} " +
               $"log={File.Exists(System.IO.Path.Combine(run.RunFolder, PdfExport.LogFileName))} folder=[{run.RunFolder}] failed=[{failed}] status=[{StatusText.Text}]";
    }

    private void SetBusy(bool busy)
    {
        ScanButton.IsEnabled = !busy;
        RememberBox.IsEnabled = !busy;
        SamplesButton.IsEnabled = !busy;
        PdfButton.IsEnabled = !busy;
        OpenButton.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        ZoneBox.IsEnabled = !busy;
        ModeBox.IsEnabled = !busy;
        SizeBox.IsEnabled = !busy;
        ParticipantBox.IsEnabled = !busy;
        OutputBox.IsEnabled = !busy;
        SensitiveBox.IsEnabled = !busy;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        StatusText.Text = "Cancelling...";
        CancelButton.IsEnabled = false;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e) => _cts?.Cancel();

    private void OnShowFolder(object sender, RoutedEventArgs e)
    {
        var folder = RunFolderBox.Text;
        if (!Directory.Exists(folder)) return;
        try
        {
            // Opens the report folder in File Explorer. Source files and attachments are never opened or run.
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = false });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            MessageBox.Show(this, "File Explorer could not be started: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ------------------------------------------------------------------ results grid

    private FindingCategory? SelectedCategory =>
        CategoryFilter.SelectedItem is string s && Enum.TryParse<FindingCategory>(s, out var c) ? c : null;

    private Severity? SelectedSeverity => (SeverityFilter.SelectedItem as string) switch
    {
        "error" => Severity.Error,
        "warning" => Severity.Warning,
        "info" => Severity.Info,
        _ => null,
    };

    private bool FilterRow(object item) => item is FileRow row && row.Matches(SelectedCategory, SelectedSeverity);

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_view is null) return; // raised while the window is still being built
        _view.Refresh();
        UpdateRowCount();
    }

    private void UpdateRowCount()
    {
        var shown = _view.Cast<object>().Count();
        RowCountText.Text = _rows.Count == 0 ? "No scan yet." : $"{shown} of {_rows.Count} file(s) shown";
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not FileRow row) return;
        if (!row.IsCandidate)
        {
            MessageBox.Show(this, "This entry is not an RSMF candidate, so there is nothing to view.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenViewer(row.FullPath);
    }

    // ------------------------------------------------------------------ findings explorer

    private void UpdateExplorer(RunResult run)
    {
        var groups = FindingsIndex.Build(run).Select(g => new RuleGroupRow(g)).ToList();
        RuleGroupGrid.ItemsSource = groups;
        ExplorerTab.Header = groups.Count == 0 ? "Findings by rule" : $"Findings by rule ({groups.Count})";
        if (groups.Count > 0) RuleGroupGrid.SelectedIndex = 0;
        else ShowRuleGroup(null);
    }

    private void OnRuleGroupSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized) return;
        ShowRuleGroup(RuleGroupGrid.SelectedItem as RuleGroupRow);
    }

    private void ShowRuleGroup(RuleGroupRow? row)
    {
        if (row is null)
        {
            OccurrenceGrid.ItemsSource = null;
            ExplorerTitle.Text = _rows.Count == 0
                ? "Run a scan, then select a rule to see every file it affects."
                : "This scan raised no findings.";
            ExplorerHelp.Visibility = Visibility.Collapsed;
            return;
        }
        var help = RuleHelp.Get(row.RuleId);
        var plain = RuleHelp.Plain(row.RuleId);
        ExplorerTitle.Text = $"Rule {row.RuleNumber}  \u00B7  {plain.Problem}  \u00B7  {row.Findings.ToString("N0", CultureInfo.InvariantCulture)} finding(s) in {row.Files.ToString("N0", CultureInfo.InvariantCulture)} file(s)";
        ExplorerMeaning.Text = plain.Meaning;
        ExplorerFix.Text = plain.Action;
        ExplorerTechnical.Text = $"Technical detail (code {row.RuleId}, {row.Severity}): checks that \"{row.Title}\". {help.Meaning} {help.HowToFix}";

        // When nearly every file shows the same thing, it is how the files were made, not a few bad files.
        var checkedFiles = _rows.Count(r => r.Result.Kind == FileKind.RsmfCandidate && r.Result.Checked);
        var everywhere = checkedFiles >= 5 && row.Files * 10 >= checkedFiles * 9;
        ExplorerEverywhere.Text = everywhere
            ? (row.Files == checkedFiles ? "This appears in every file that was checked" : $"This appears in {row.Files.ToString("N0", CultureInfo.InvariantCulture)} of the {checkedFiles.ToString("N0", CultureInfo.InvariantCulture)} files that were checked") +
              ", so it comes from how the files were made, not from a few damaged files. One correction at the source, or one decision to accept it, covers all of them."
            : "";
        ExplorerEverywhere.Visibility = everywhere ? Visibility.Visible : Visibility.Collapsed;
        ExplorerHelp.Visibility = Visibility.Visible;
        OccurrenceGrid.ItemsSource = row.Group.Occurrences.Select(o => new OccurrenceRow(o)).ToList();
    }

    // ------------------------------------------------------------------ rule guide

    private readonly List<GuideRow> _guideRows = RuleCatalog.All.Select(r => new GuideRow(r)).ToList();
    private ICollectionView? _guideView;

    /// <summary>Fills the Rule guide tab. It lists every rule the app has and needs no scan.</summary>
    private void InitGuide()
    {
        GuideCategory.ItemsSource = new[] { AllCategories }.Concat(Enum.GetNames<FindingCategory>()).ToList();
        GuideCategory.SelectedIndex = 0;
        _guideView = CollectionViewSource.GetDefaultView(_guideRows);
        _guideView.Filter = FilterGuideRow;
        GuideGrid.ItemsSource = _guideView;
        GuideGrid.SelectedIndex = 0;
        UpdateGuideCount();
    }

    private bool FilterGuideRow(object item)
    {
        if (item is not GuideRow row) return false;
        if (GuideCategory.SelectedItem is string category && category != AllCategories && row.Category != category) return false;
        var words = GuideSearch.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.All(w => row.SearchText.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshGuide()
    {
        if (_guideView is null) return;
        var selected = GuideGrid.SelectedItem;
        _guideView.Refresh();
        if (selected is not null && GuideGrid.Items.Contains(selected)) GuideGrid.SelectedItem = selected;
        else if (GuideGrid.Items.Count > 0) GuideGrid.SelectedIndex = 0;
        UpdateGuideCount();
    }

    private void UpdateGuideCount()
    {
        GuideCount.Text = GuideGrid.Items.Count == _guideRows.Count
            ? $"{_guideRows.Count} rules. Select one to see what it checks, how, and why."
            : $"{GuideGrid.Items.Count} of {_guideRows.Count} rules shown";
    }

    private void OnGuideFilterChanged(object sender, SelectionChangedEventArgs e) => RefreshGuide();

    private void OnGuideSearchChanged(object sender, TextChangedEventArgs e) => RefreshGuide();

    private void OnGuideRuleSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_guideView is null) return;
        ShowGuideRule(GuideGrid.SelectedItem as GuideRow);
    }

    private void ShowGuideRule(GuideRow? row)
    {
        if (row is null)
        {
            GuideTitle.Text = "No rule matches the search.";
            GuideBody.Visibility = Visibility.Collapsed;
            return;
        }
        var plain = RuleHelp.Plain(row.RuleId);
        var help = RuleHelp.Get(row.RuleId);
        var guide = RuleGuide.Get(row.RuleId);
        var links = RuleGuide.Links(row.RuleId).Select(l => new GuideLinkRow(l)).ToList();
        GuideTitle.Text = $"Rule {row.RuleNumber}  \u00B7  {plain.Problem}";
        GuideOrigin.Text = RuleGuide.Origin(row.Rule);
        GuideWhat.Text = plain.Meaning;
        GuideHow.Text = guide.How;
        GuideWhy.Text = guide.Why;
        GuideDo.Text = plain.Action;
        GuideLinks.ItemsSource = links;
        GuideNoLinks.Visibility = links.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GuideLinkNote.Visibility = links.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        GuideTechnical.Text = $"Technical detail (code {row.RuleId}, {row.Severity}): checks that \"{row.Rule.Title}\". Expected: {row.Rule.Expected}. {help.Meaning} {help.HowToFix}";
        GuideBody.Visibility = Visibility.Visible;
    }

    /// <summary>Opens the Rule guide tab at one rule, clearing any search that would hide it.</summary>
    public void ShowRuleInGuide(string ruleId)
    {
        var row = _guideRows.FirstOrDefault(r => r.RuleId == ruleId);
        if (row is null) return;
        GuideSearch.Text = "";
        GuideCategory.SelectedIndex = 0;
        RefreshGuide();
        ResultTabs.SelectedItem = GuideTab;
        GuideGrid.SelectedItem = row;
        GuideGrid.ScrollIntoView(row);
    }

    private void OnExplorerOpenGuide(object sender, RoutedEventArgs e)
    {
        if (RuleGroupGrid.SelectedItem is RuleGroupRow row) ShowRuleInGuide(row.RuleId);
    }

    /// <summary>
    /// Opens one of the guide's source pages in the user's web browser. Only addresses on Relativity's help
    /// site or Relativity's GitHub account are ever opened; the app itself makes no network connection.
    /// </summary>
    private void OnGuideLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Documents.Hyperlink { Tag: string url } || !RuleGuide.IsTrustedLink(url)) return;
        if (App.SelfTest is not null) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            StatusText.Text = "The link could not be opened. Copy the address shown under it into your browser.";
        }
    }

    /// <summary>Shows one rule in the guide and reports what the tab holds (self-check only).</summary>
    internal string SelfTestGuide()
    {
        ShowRuleInGuide("F-MIME-005");
        var shown = $"rules={GuideGrid.Items.Count} selected=[{GuideTitle.Text}] links={GuideLinks.Items.Count} how-chars={GuideHow.Text.Length} why-chars={GuideWhy.Text.Length}";
        GuideSearch.Text = "calendar";
        var found = GuideGrid.Items.Count;
        GuideSearch.Text = "";
        ShowRuleInGuide("F-MIME-005");
        return shown + $" search-calendar={found} all-again={GuideGrid.Items.Count}";
    }

    private void OnOccurrenceDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (OccurrenceGrid.SelectedItem is not OccurrenceRow row) return;
        if (!row.Occurrence.IsRsmfFile)
        {
            MessageBox.Show(this, "This finding belongs to a folder, not to an RSMF file, so there is nothing to open.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenViewer(row.Occurrence.FullPath, row.Occurrence.Finding);
    }

    /// <summary>Shows the explorer tab and reports what it lists (self-check only).</summary>
    /// <summary>Reports whether the window carries the brick icon and which sizes it holds.</summary>
    internal string SelfTestIcon()
    {
        if (Icon is not System.Windows.Media.Imaging.BitmapFrame frame || frame.Decoder is null) return "missing";
        var sizes = frame.Decoder.Frames.Select(f => f.PixelWidth).Distinct().OrderBy(n => n)
            .Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return "set sizes=" + string.Join(",", sizes);
    }

    internal string SelfTestExplorer()
    {
        ResultTabs.SelectedItem = ExplorerTab;
        return $"rules={RuleGroupGrid.Items.Count} selected=[{ExplorerTitle.Text}] occurrences={OccurrenceGrid.Items.Count} help-shown={ExplorerHelp.Visibility == Visibility.Visible}";
    }

    private ViewerWindow? OpenViewer(string path, Finding? goTo = null)
    {
        if (_lastOptions is null || _lastOutputFolder is null) return null;
        // Large archives spool to a temporary file under the chosen output folder; it is deleted when the viewer closes.
        return OpenViewerWith(path, _lastOptions.With(System.IO.Path.Combine(_lastOutputFolder, ".viewer-tmp")), goTo);
    }

    internal ViewerWindow OpenViewerWith(string path, InspectionOptions options, Finding? goTo = null)
    {
        var viewer = new ViewerWindow(path, options, goTo) { Owner = this };
        viewer.Show();
        return viewer;
    }
}
