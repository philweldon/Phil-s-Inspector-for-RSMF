using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Rsmf.Core;

namespace Rsmf.App;

public partial class ViewerWindow : Window
{
    private readonly string _path;
    private readonly InspectionOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<ConversationView, List<MessageVm>> _messageCache = new();
    private ViewerDocument? _document;
    private List<MessageVm> _currentMessages = new();
    private readonly TaskCompletionSource<string> _loadCompleted = new();

    /// <summary>Completes when loading ends: "loaded ..." on success, otherwise the reason.</summary>
    public Task<string> LoadCompleted => _loadCompleted.Task;

    private readonly Finding? _goTo;

    /// <param name="goTo">A finding to show once the file has loaded (from the findings explorer), or null.</param>
    public ViewerWindow(string path, InspectionOptions options, Finding? goTo = null)
    {
        InitializeComponent();
        _path = path;
        _options = options;
        _goTo = goTo;
        Title = $"{System.IO.Path.GetFileName(path)} - {AppInfo.Name} {AppInfo.Version}";
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // All parsing happens off the UI thread.
            var token = _cts.Token;
            var document = await Task.Run(() => ViewerDocument.Load(_path, _options, token));
            if (_cts.IsCancellationRequested)
            {
                document.Dispose();
                return;
            }
            _document = document;
            Populate(document);
            if (_goTo is not null) GoToFinding(_goTo);
            _loadCompleted.TrySetResult(
                $"loaded conversations={document.Conversations.Count} messages-shown={_currentMessages.Count} " +
                $"findings={document.Result.Findings.Count} headers={document.Headers.Count} unavailable={document.Unavailable.Count}");
        }
        catch (OperationCanceledException)
        {
            // Window closed while loading.
            _loadCompleted.TrySetResult("cancelled");
        }
        catch (Exception ex)
        {
            InfoText.Text = "The file could not be loaded: " + ex.GetType().Name + ". " + ex.Message;
            _loadCompleted.TrySetResult("failed " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _cts.Cancel();
        _document?.Dispose();
        _document = null;
    }

    private void Populate(ViewerDocument d)
    {
        var r = d.Result;
        var m = r.Metrics;
        string N(int? n) => n.HasValue ? n.Value.ToString("N0", CultureInfo.InvariantCulture) : "n/a";
        var timeWindow = r.TimeWindow.Status switch
        {
            TimeWindowStatus.Pass => "PASS",
            TimeWindowStatus.Fail => "FAIL - " + r.TimeWindow.Reason,
            TimeWindowStatus.Indeterminate => r.TimeWindow.Reason,
            TimeWindowStatus.NoMessages => "no message events",
            _ => "not evaluated",
        };
        InfoText.Text =
            $"{d.Path}\n" +
            $"Version {(r.RsmfVersion.Length > 0 ? r.RsmfVersion : "n/a")}  ·  {r.SizeBytes.ToString("N0", CultureInfo.InvariantCulture)} bytes  ·  " +
            $"{N(m.TotalEvents)} events ({N(m.MessageEvents)} messages)  ·  {N(m.ConversationCount)} conversations  ·  {N(m.UniqueParticipantCount)} participants\n" +
            $"Findings: {r.Count(Severity.Error)} error, {r.Count(Severity.Warning)} warning, {r.Count(Severity.Info)} info  ·  " +
            $"Time window ({d.ZoneId}): {timeWindow}";

        if (d.Unavailable.Count > 0)
        {
            UnavailableText.Text = "Not everything in this file could be read.\n" + string.Join("\n", d.Unavailable.Select(u => "• " + u));
            UnavailableBanner.Visibility = Visibility.Visible;
        }

        HeaderGrid.ItemsSource = d.Headers;
        ExtractedTextBox.Text = d.ExtractedText;
        ManifestBox.Text = d.ManifestText;
        FindingGrid.ItemsSource = r.Findings.Select(f => new FindingRow(f)).ToList();
        FindingsTab.Header = $"Findings ({r.Findings.Count})";
        CoverageGrid.ItemsSource = r.Coverage;
        ConversationList.ItemsSource = d.Conversations;
        Tabs.IsEnabled = true;

        if (d.Conversations.Count > 0) ConversationList.SelectedIndex = 0;
        else
        {
            MessageCountText.Text = "No conversations are available for this file.";
            // Nothing to show on the first tab: start on the findings instead.
            Tabs.SelectedItem = FindingsTab;
        }
    }

    // ------------------------------------------------------------------ conversations and search

    private void OnConversationChanged(object sender, SelectionChangedEventArgs e) => RefreshMessages();

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RefreshMessages();

    private void OnClearSearch(object sender, RoutedEventArgs e) => SearchBox.Text = "";

    private List<MessageVm> _matches = new();
    private int _matchIndex = -1;

    private void OnOnlyMatchesChanged(object sender, RoutedEventArgs e) => RefreshMessages();

    private void OnNextMatch(object sender, RoutedEventArgs e) => StepMatch(+1);

    private void OnPreviousMatch(object sender, RoutedEventArgs e) => StepMatch(-1);

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            StepMatch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : +1);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SearchBox.Text = "";
            e.Handled = true;
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && Tabs.IsEnabled)
        {
            Tabs.SelectedIndex = 0;
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    /// <summary>Selects the next or previous message that contains the search text, wrapping at the ends.</summary>
    private void StepMatch(int direction)
    {
        if (_matches.Count == 0) return;
        _matchIndex = ((_matchIndex + direction) % _matches.Count + _matches.Count) % _matches.Count;
        ShowMatch();
    }

    private void ShowMatch()
    {
        if (_matchIndex < 0 || _matchIndex >= _matches.Count) return;
        var vm = _matches[_matchIndex];
        MessageList.SelectedItem = vm;
        MessageList.ScrollIntoView(vm);
        UpdateSearchCaption();
    }

    private int _allCount;
    private int _hitCount;

    private void UpdateSearchCaption()
    {
        string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);
        var term = SearchBox.Text.Trim();
        if (term.Length == 0)
        {
            MessageCountText.Text = $"{N(_allCount)} events, oldest first";
        }
        else if (_matches.Count == 0)
        {
            MessageCountText.Text = $"No events contain \"{term}\"";
        }
        else
        {
            var position = _matchIndex >= 0 ? $"Match {N(_matchIndex + 1)} of {N(_matches.Count)} events" : $"{N(_matches.Count)} events match";
            MessageCountText.Text = $"{position}  \u00B7  {N(_hitCount)} occurrence(s) of \"{term}\" in {N(_allCount)} events";
        }
        PreviousMatchButton.IsEnabled = NextMatchButton.IsEnabled = _matches.Count > 0;
    }

    private void RefreshMessages()
    {
        if (!IsInitialized) return; // raised while the window is still being built
        if (_document is null || ConversationList.SelectedItem is not ConversationView conversation)
        {
            _currentMessages = new List<MessageVm>();
            _matches = new List<MessageVm>();
            _matchIndex = -1;
            _allCount = _hitCount = 0;
            MessageList.ItemsSource = _currentMessages;
            return;
        }
        if (!_messageCache.TryGetValue(conversation, out var all))
        {
            var document = _document;
            all = conversation.Messages.Select(v => v.IsMessage ? new MessageVm(document, v) : new NoticeVm(document, v)).ToList();
            _messageCache[conversation] = all;
        }

        var term = SearchBox.Text.Trim();
        _allCount = all.Count;
        _hitCount = 0;
        _matches = new List<MessageVm>();
        foreach (var vm in all)
        {
            vm.SearchTerm = term;
            if (term.Length == 0) continue;
            var hits = vm.View.HitCount(term);
            if (hits == 0) continue;
            _hitCount += hits;
            _matches.Add(vm);
        }

        _currentMessages = term.Length > 0 && OnlyMatchesBox.IsChecked == true ? _matches : all;
        MessageList.ItemsSource = WithDaySeparators(_currentMessages);
        _matchIndex = _matches.Count > 0 ? 0 : -1;
        UpdateSearchCaption();
        if (_matchIndex == 0) ShowMatch();
    }

    /// <summary>Runs a search through the normal path and reports the caption (self-check only).</summary>
    /// <summary>Scrolls to the first message whose text starts with the given words (self-check only).</summary>
    internal string SelfTestShowText(string bodyStart)
    {
        var vm = MessageList.Items.OfType<MessageVm>().FirstOrDefault(m => m.Body.StartsWith(bodyStart, StringComparison.Ordinal));
        if (vm is null) return "not-found";
        MessageList.SelectedItem = vm;
        MessageList.ScrollIntoView(vm);
        return $"shown=[{vm.View.RecordId}] has-emoji={Rsmf.Core.EmojiText.HasEmoji(vm.Body)}";
    }

    internal string SelfTestSearch(string term)
    {
        SearchBox.Text = term;
        return $"term={term} caption=[{MessageCountText.Text}] matches={_matches.Count} shown={_currentMessages.Count}";
    }

    /// <summary>Inserts a separator before the first event of each calendar day (in the selected zone).</summary>
    private static List<object> WithDaySeparators(List<MessageVm> messages)
    {
        var items = new List<object>(messages.Count + 8);
        string? day = null;
        foreach (var vm in messages)
        {
            var d = vm.View.DayText;
            if (d != day)
            {
                items.Add(new DayVm(d.Length > 0 ? d : "No usable timestamp"));
                day = d;
            }
            items.Add(vm);
        }
        return items;
    }

    // ------------------------------------------------------------------ drill-down

    private void OnFindingSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized) return;
        if (FindingGrid.SelectedItem is not FindingRow row)
        {
            HelpTitle.Text = "Select a finding to see what it means and what to do about it. Double-click to go to the affected record.";
            HelpBody.Visibility = Visibility.Collapsed;
            return;
        }
        var rule = RuleCatalog.Get(row.RuleId);
        var help = RuleHelp.Get(row.RuleId);
        var plain = RuleHelp.Plain(row.RuleId);
        HelpTitle.Text = $"Rule {RuleCatalog.Label(rule.Id)}  \u00B7  {plain.Problem}";
        HelpMeaning.Text = plain.Meaning;
        HelpFix.Text = plain.Action;
        HelpDetail.Text = $"Technical detail (code {rule.Id}): checks that \"{rule.Title}\". {help.Meaning} {help.HowToFix}\n" +
                          $"Observed: {row.Observed}\nExpected: {row.Expected}\nLocation: {row.Location}" +
                          (row.RecordId.Length > 0 ? $"   Record: {row.RecordId}" : "") + $"\nSource: {row.Source}";
        HelpBody.Visibility = Visibility.Visible;
    }

    private void OnOpenRuleGuide(object sender, RoutedEventArgs e)
    {
        if (FindingGrid.SelectedItem is not FindingRow row || Application.Current.MainWindow is not MainWindow main) return;
        main.ShowRuleInGuide(row.RuleId);
        if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
        main.Activate();
    }

    /// <summary>Reports where the viewer is showing (self-check only).</summary>
    internal string SelfTestState() =>
        $"tab={Tabs.SelectedIndex} selected-message=[{(MessageList.SelectedItem as MessageVm)?.View.RecordId}] " +
        $"selected-finding=[{(FindingGrid.SelectedItem as FindingRow)?.RuleId} {(FindingGrid.SelectedItem as FindingRow)?.Location}]";

    /// <summary>Shows the findings tab with the first finding selected and reports the help shown (self-check only).</summary>
    internal string SelfTestShowFirstFinding()
    {
        Tabs.SelectedItem = FindingsTab;
        if (FindingGrid.Items.Count == 0) return "no findings";
        FindingGrid.SelectedIndex = 0;
        return $"findings={FindingGrid.Items.Count} help-title=[{HelpTitle.Text}] help-shown={HelpBody.Visibility == Visibility.Visible} meaning-chars={HelpMeaning.Text.Length}";
    }

    /// <summary>Selects the matching finding (so its help shows) and goes to the affected record when there is one.</summary>
    private void GoToFinding(Finding finding)
    {
        if (_document is null) return;
        var row = (FindingGrid.ItemsSource as IEnumerable<FindingRow>)?.FirstOrDefault(r =>
            r.RuleId == finding.RuleId && r.Location == finding.Location && r.RecordId == finding.RecordId);
        if (row is not null)
        {
            FindingGrid.SelectedItem = row;
            FindingGrid.ScrollIntoView(row);
        }
        var hit = _document.FindEvent(finding.Location) ?? _document.FindRecord(finding.RecordId);
        if (hit is not null) ShowMessage(hit.Value.Conversation, hit.Value.Message);
        else Tabs.SelectedItem = FindingsTab;
    }

    private void OnFindingDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindingGrid.SelectedItem is FindingRow row) DrillDown(row.Location, row.RecordId);
    }

    private void OnCoverageSelected(object sender, SelectionChangedEventArgs e)
    {
        if (CoverageGrid.SelectedItem is not CoverageRow row)
        {
            AffectedList.ItemsSource = null;
            return;
        }
        var items = row.MissingIds.Select(id => new AffectedRecord("missing", id))
            .Concat(row.BlankIds.Select(id => new AffectedRecord("blank", id)))
            .Concat(row.InvalidIds.Select(id => new AffectedRecord("invalid", id)))
            .ToList();
        AffectedList.ItemsSource = items;
        var total = row.Missing + row.Blank + row.Invalid;
        AffectedCaption.Text = total == 0
            ? $"{row.Level} / {row.Field}: no missing, blank or invalid records."
            : $"{row.Level} / {row.Field}: {total} affected record(s)" + (total > items.Count ? $"; the first {items.Count} are listed." : ". Double-click an event record to go to it.");
    }

    private void OnAffectedDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AffectedList.SelectedItem is AffectedRecord record) DrillDown("", record.RecordId);
    }

    private void DrillDown(string location, string recordId)
    {
        if (_document is null) return;

        var hit = _document.FindEvent(location) ?? _document.FindRecord(recordId);
        if (hit is not null)
        {
            ShowMessage(hit.Value.Conversation, hit.Value.Message);
            return;
        }

        if (recordId.StartsWith("conversation:", StringComparison.Ordinal))
        {
            var id = recordId["conversation:".Length..];
            var conversation = _document.Conversations.FirstOrDefault(c => c.Id == id);
            if (conversation is not null)
            {
                Tabs.SelectedIndex = 0;
                ConversationList.SelectedItem = conversation;
                ConversationList.ScrollIntoView(conversation);
                return;
            }
        }

        var header = _document.Headers.FirstOrDefault(h => string.Equals(h.Name, location, StringComparison.OrdinalIgnoreCase));
        if (header is not null)
        {
            Tabs.SelectedIndex = 1;
            HeaderGrid.SelectedItem = header;
            HeaderGrid.ScrollIntoView(header);
            return;
        }

        var where = string.IsNullOrEmpty(recordId) ? location : $"{recordId}\n{location}";
        MessageBox.Show(this,
            "This item is not tied to a single event or header.\n\nLocation:\n" + where +
            "\n\nUse the Raw manifest tab to inspect it.",
            Title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowMessage(ConversationView conversation, MessageView message)
    {
        Tabs.SelectedIndex = 0;
        if (SearchBox.Text.Length > 0) SearchBox.Text = "";
        if (!ReferenceEquals(ConversationList.SelectedItem, conversation)) ConversationList.SelectedItem = conversation;
        else RefreshMessages();
        ConversationList.ScrollIntoView(conversation);

        var vm = _currentMessages.FirstOrDefault(x => x.View.EventIndex == message.EventIndex);
        if (vm is null) return;
        MessageList.SelectedItem = vm;
        MessageList.ScrollIntoView(vm);
        MessageList.Focus();
    }
}
