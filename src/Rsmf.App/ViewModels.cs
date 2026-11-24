using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rsmf.Core;

namespace Rsmf.App;

/// <summary>Frozen brushes shared by the grids and summary cards.</summary>
public static class Palette
{
    public static readonly Brush TextPrimary = Make(0x1F, 0x29, 0x33);
    public static readonly Brush TextSecondary = Make(0x5F, 0x6B, 0x7A);
    public static readonly Brush Error = Make(0xB7, 0x1C, 0x1C);
    public static readonly Brush ErrorSoft = Make(0xFD, 0xE7, 0xE5);
    public static readonly Brush Warning = Make(0x9A, 0x5B, 0x00);
    public static readonly Brush WarningSoft = Make(0xFF, 0xF1, 0xD6);
    public static readonly Brush Ok = Make(0x1B, 0x6E, 0x35);
    public static readonly Brush OkSoft = Make(0xE2, 0xF3, 0xE6);
    public static readonly Brush Neutral = Make(0x4B, 0x55, 0x63);
    public static readonly Brush NeutralSoft = Make(0xEC, 0xEF, 0xF2);
    public static readonly Brush Info = Make(0x1A, 0x56, 0xA8);
    public static readonly Brush InfoSoft = Make(0xE1, 0xEE, 0xFC);

    private static Brush Make(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>One row of the results grid.</summary>
public sealed class FileRow
{
    public FileRow(FileResult result) => Result = result;

    public FileResult Result { get; }
    public string RelativePath => Result.RelativePath;
    public string FullPath => Result.Path;
    public bool IsCandidate => Result.Kind == FileKind.RsmfCandidate;

    public string Status => Result.Status switch
    {
        FileStatus.Ok => Result.Findings.Count == 0 ? "OK" : "OK (info only)",
        FileStatus.Warnings => "Warnings",
        FileStatus.Errors => "Errors",
        FileStatus.Unreadable => "Unreadable",
        FileStatus.NotChecked => "Not checked",
        FileStatus.NotCandidate => "Not an RSMF candidate",
        _ => "Link (not followed)",
    };

    public string Version => Result.RsmfVersion;
    public string Size => Result.SizeBytes.ToString("N0", CultureInfo.InvariantCulture);
    public int? Events => Result.Metrics.TotalEvents;
    public int? Messages => Result.Metrics.MessageEvents;
    public int? Conversations => Result.Metrics.ConversationCount;
    public int? Participants => Result.Metrics.UniqueParticipantCount;
    public int? Errors => IsCandidate ? Result.Count(Severity.Error) : null;
    public int? Warnings => IsCandidate ? Result.Count(Severity.Warning) : null;
    public int? Info => IsCandidate ? Result.Count(Severity.Info) : null;
    public int? Format => CountOf(FindingCategory.FORMAT);
    public int? Limit => CountOf(FindingCategory.LIMIT);
    public int? Local => CountOf(FindingCategory.LOCAL);
    public int? Incomplete => CountOf(FindingCategory.INCOMPLETE);

    public string TimeWindow => !IsCandidate || !Result.Checked ? "" : Result.TimeWindow.Status switch
    {
        TimeWindowStatus.Pass => "PASS",
        TimeWindowStatus.Fail => "FAIL",
        TimeWindowStatus.Indeterminate => "INDETERMINATE",
        TimeWindowStatus.NoMessages => "No messages",
        _ => "Not evaluated",
    };

    public Brush StatusBackground => Result.Status switch
    {
        FileStatus.Ok => Palette.OkSoft,
        FileStatus.Warnings => Palette.WarningSoft,
        FileStatus.Errors or FileStatus.Unreadable => Palette.ErrorSoft,
        _ => Palette.NeutralSoft,
    };

    public Brush StatusForeground => Result.Status switch
    {
        FileStatus.Ok => Palette.Ok,
        FileStatus.Warnings => Palette.Warning,
        FileStatus.Errors or FileStatus.Unreadable => Palette.Error,
        _ => Palette.Neutral,
    };

    public Brush ErrorsForeground => Errors > 0 ? Palette.Error : Palette.TextSecondary;
    public FontWeight ErrorsWeight => Errors > 0 ? FontWeights.SemiBold : FontWeights.Normal;
    public Brush WarningsForeground => Warnings > 0 ? Palette.Warning : Palette.TextSecondary;
    public FontWeight WarningsWeight => Warnings > 0 ? FontWeights.SemiBold : FontWeights.Normal;

    public Visibility TimeWindowVisibility => TimeWindow.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Brush TimeWindowBackground => Result.TimeWindow.Status switch
    {
        TimeWindowStatus.Pass => Palette.OkSoft,
        TimeWindowStatus.Fail => Palette.ErrorSoft,
        TimeWindowStatus.Indeterminate => Palette.WarningSoft,
        _ => Palette.NeutralSoft,
    };

    public Brush TimeWindowForeground => Result.TimeWindow.Status switch
    {
        TimeWindowStatus.Pass => Palette.Ok,
        TimeWindowStatus.Fail => Palette.Error,
        TimeWindowStatus.Indeterminate => Palette.Warning,
        _ => Palette.Neutral,
    };

    private int? CountOf(FindingCategory c) => IsCandidate ? Result.Findings.Count(f => f.Category == c) : null;

    public bool Matches(FindingCategory? category, Severity? severity) =>
        (category is null && severity is null)
        || Result.Findings.Any(f => (category is null || f.Category == category) && (severity is null || f.Severity == severity));
}

/// <summary>One rule in the findings explorer, with how widely it fired.</summary>
/// <summary>One rule in the Rule guide tab.</summary>
public sealed class GuideRow
{
    public GuideRow(RuleDefinition rule) => Rule = rule;

    public RuleDefinition Rule { get; }
    public string RuleId => Rule.Id;
    public string RuleNumber => RuleCatalog.Label(Rule.Id);
    public string Problem => RuleHelp.Plain(Rule.Id).Problem;
    public string Category => Rule.Category.ToString();
    public string Severity => Rule.Severity.ToString().ToLowerInvariant();
    public int SeverityRank => (int)Rule.Severity;

    public Brush SeverityBackground => Rule.Severity switch
    {
        Rsmf.Core.Severity.Error => Palette.ErrorSoft,
        Rsmf.Core.Severity.Warning => Palette.WarningSoft,
        _ => Palette.InfoSoft,
    };

    public Brush SeverityForeground => Rule.Severity switch
    {
        Rsmf.Core.Severity.Error => Palette.Error,
        Rsmf.Core.Severity.Warning => Palette.Warning,
        _ => Palette.Info,
    };

    /// <summary>Everything the search box looks through.</summary>
    public string SearchText
    {
        get
        {
            var plain = RuleHelp.Plain(Rule.Id);
            var guide = RuleGuide.Get(Rule.Id);
            return string.Join(' ', RuleNumber, Rule.Id, Category, Severity, plain.Problem, plain.Meaning, plain.Action, guide.How, guide.Why, Rule.Title);
        }
    }
}

/// <summary>One source link shown in the Rule guide tab.</summary>
public sealed class GuideLinkRow
{
    public GuideLinkRow(RuleLink link) => Link = link;

    public RuleLink Link { get; }
    public string Label => Link.Label;
    public string Url => Link.Url;
    public string Detail => Link.Section.Length > 0 ? "Look for: " + Link.Section : "";
    public Visibility DetailVisibility => Link.Section.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class RuleGroupRow
{
    public RuleGroupRow(RuleGroup group) => Group = group;

    public RuleGroup Group { get; }
    public string RuleId => Group.Rule.Id;
    /// <summary>The rule's number as shown to people, for example "004".</summary>
    public string RuleNumber => RuleCatalog.Label(Group.Rule.Id);
    public string Title => Group.Rule.Title;
    /// <summary>The problem in plain words, for the list.</summary>
    public string Problem => RuleHelp.Plain(Group.Rule.Id).Problem;
    public string Category => Group.Rule.Category.ToString();
    public string Severity => Group.Rule.Severity.ToString().ToLowerInvariant();
    public int SeverityRank => (int)Group.Rule.Severity;
    public int Files => Group.FileCount;
    public int Findings => Group.FindingCount;

    public Brush SeverityBackground => Group.Rule.Severity switch
    {
        Rsmf.Core.Severity.Error => Palette.ErrorSoft,
        Rsmf.Core.Severity.Warning => Palette.WarningSoft,
        _ => Palette.InfoSoft,
    };

    public Brush SeverityForeground => Group.Rule.Severity switch
    {
        Rsmf.Core.Severity.Error => Palette.Error,
        Rsmf.Core.Severity.Warning => Palette.Warning,
        _ => Palette.Info,
    };
}

/// <summary>One occurrence of a rule in the findings explorer.</summary>
public sealed class OccurrenceRow
{
    public OccurrenceRow(FindingOccurrence occurrence) => Occurrence = occurrence;

    public FindingOccurrence Occurrence { get; }
    public string File => Occurrence.RelativePath;
    public string Observed => Occurrence.Finding.Observed;
    public string Location => Occurrence.Finding.Location;
    public string RecordId => Occurrence.Finding.RecordId;
}

/// <summary>A finding row for the viewer's findings tab.</summary>
public sealed class FindingRow
{
    public FindingRow(Finding f) => Finding = f;

    public Finding Finding { get; }
    public string Severity => Finding.Severity.ToString().ToLowerInvariant();

    public Brush SeverityBackground => Finding.Severity switch
    {
        Rsmf.Core.Severity.Error => Palette.ErrorSoft,
        Rsmf.Core.Severity.Warning => Palette.WarningSoft,
        _ => Palette.InfoSoft,
    };

    public Brush SeverityForeground => Finding.Severity switch
    {
        Rsmf.Core.Severity.Error => Palette.Error,
        Rsmf.Core.Severity.Warning => Palette.Warning,
        _ => Palette.Info,
    };
    public string Category => Finding.Category.ToString();
    public string RuleId => Finding.RuleId;
    public string Message => Finding.Message;
    public string RuleNumber => RuleCatalog.Label(Finding.RuleId);
    /// <summary>The problem in plain words, for the list.</summary>
    public string Problem => RuleHelp.Plain(Finding.RuleId).Problem;
    public string Observed => Finding.Observed;
    public string Expected => Finding.Expected;
    public string Location => Finding.Location;
    public string RecordId => Finding.RecordId;
    public string Source => Finding.Source;
}

/// <summary>An attachment reference under a message. Images load lazily off the UI thread.</summary>
public sealed class AttachmentVm : INotifyPropertyChanged
{
    private readonly ViewerDocument _document;
    private readonly AttachmentView _view;
    private ImageSource? _image;
    private bool _started;
    private string _note = "";

    public AttachmentVm(ViewerDocument document, AttachmentView view)
    {
        _document = document;
        _view = view;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label
    {
        get
        {
            var size = _view.DeclaredSize.HasValue ? $" ({_view.DeclaredSize.Value.ToString("N0", CultureInfo.InvariantCulture)} bytes)" : "";
            var missing = _view.InArchive ? "" : "  [not in rsmf.zip]";
            return $"Attachment: {_view.Display}{size}{missing}{_note}";
        }
    }

    public Visibility ImageVisibility => _view.IsInlineImage ? Visibility.Visible : Visibility.Collapsed;

    public ImageSource? Image
    {
        get
        {
            if (_view.IsInlineImage && !_started)
            {
                _started = true;
                _ = Task.Run(Load);
            }
            return _image;
        }
    }

    private void Load()
    {
        ImageSource? image = null;
        try
        {
            var bytes = _document.ReadInlineImage(_view);
            if (bytes is not null)
            {
                image = ImageLoader.Decode(bytes, 640);
            }
        }
        catch (Exception)
        {
            // Undecodable image data: fall through and list the attachment by name only.
            image = null;
        }

        _image = image;
        if (image is null) _note = "  [image could not be displayed]";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
    }
}

/// <summary>Decodes image bytes that were already checked to be PNG, JPEG or GIF data.</summary>
public static class ImageLoader
{
    /// <summary>Decodes to a frozen bitmap, scaling down to <paramref name="maxWidth"/> pixels but never up.</summary>
    public static ImageSource Decode(byte[] bytes, int maxWidth)
    {
        int width;
        using (var probe = new MemoryStream(bytes, writable: false))
        {
            width = BitmapFrame.Create(probe, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).PixelWidth;
        }
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (width > maxWidth) bmp.DecodePixelWidth = maxWidth;
        bmp.StreamSource = new MemoryStream(bytes, writable: false);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}

/// <summary>
/// Attached properties that show a TextBlock's text with every occurrence of a search term highlighted.
/// The text is added as plain runs; nothing in it is interpreted.
/// </summary>
public static class Highlight
{
    private static readonly Brush HitBackground = MakeBrush(0xFF, 0xE0, 0x66);

    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Highlight), new PropertyMetadata("", OnChanged));

    public static readonly DependencyProperty TermProperty = DependencyProperty.RegisterAttached(
        "Term", typeof(string), typeof(Highlight), new PropertyMetadata("", OnChanged));

    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);
    public static string GetTerm(DependencyObject d) => (string)d.GetValue(TermProperty);
    public static void SetTerm(DependencyObject d, string value) => d.SetValue(TermProperty, value);

    private static Brush MakeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block) return;
        var text = GetText(block) ?? "";
        var term = GetTerm(block) ?? "";
        var hits = Rsmf.Core.TextSearch.FindMatches(text, term, 200);
        if (hits.Count == 0)
        {
            block.Text = text;
            return;
        }
        block.Inlines.Clear();
        var at = 0;
        foreach (var (start, length) in hits)
        {
            if (start > at) block.Inlines.Add(new Run(text.Substring(at, start - at)));
            block.Inlines.Add(new Run(text.Substring(start, length)) { Background = HitBackground, Foreground = BubblePalette.HitText });
            at = start + length;
        }
        if (at < text.Length) block.Inlines.Add(new Run(text.Substring(at)));
    }
}

/// <summary>Bubble colours shared by the viewer and the PDF pages.</summary>
public static class BubblePalette
{
    public static readonly Brush Blue = Make(0xFF, 0x00, 0x7A, 0xFF);
    public static readonly Brush Green = Make(0xFF, 0x2E, 0xB2, 0x51);
    public static readonly Brush Text = Make(0xFF, 0xFF, 0xFF, 0xFF);
    /// <summary>White at 85%: timestamps and small labels on a bubble.</summary>
    public static readonly Brush Subtle = Make(0xD9, 0xFF, 0xFF, 0xFF);
    public static readonly Brush HitText = Make(0xFF, 0x1F, 0x29, 0x33);

    private static Brush Make(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>One colour per sender, stable within a file.</summary>
public static class SenderPalette
{
    private static readonly (byte R, byte G, byte B, byte Sr, byte Sg, byte Sb)[] Colours =
    {
        (0x1A, 0x56, 0xA8, 0xE1, 0xEE, 0xFC), // blue
        (0x1B, 0x6E, 0x35, 0xE2, 0xF3, 0xE6), // green
        (0x6B, 0x3F, 0xA0, 0xEF, 0xE6, 0xFA), // purple
        (0x9A, 0x5B, 0x00, 0xFF, 0xF1, 0xD6), // amber
        (0x0F, 0x6B, 0x6B, 0xDD, 0xF3, 0xF2), // teal
        (0xA3, 0x24, 0x5E, 0xFB, 0xE4, 0xEE), // pink
        (0x7A, 0x4A, 0x21, 0xF5, 0xE9, 0xDD), // brown
        (0x3F, 0x4E, 0x9E, 0xE6, 0xE9, 0xF8), // indigo
    };

    private static readonly (Brush Strong, Brush Soft, Brush Border)[] Brushes = Colours.Select(c =>
        (Freeze(Color.FromRgb(c.R, c.G, c.B)), Freeze(Color.FromRgb(c.Sr, c.Sg, c.Sb)), Freeze(Color.FromArgb(0x55, c.R, c.G, c.B)))).ToArray();

    private static readonly (Brush Strong, Brush Soft, Brush Border) Unknown =
        (Palette.Neutral, Palette.NeutralSoft, Freeze(Color.FromArgb(0x55, 0x4B, 0x55, 0x63)));

    private static Brush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static (Brush Strong, Brush Soft, Brush Border) For(int senderIndex) =>
        senderIndex < 0 ? Unknown : Brushes[senderIndex % Brushes.Length];
}

/// <summary>A day separator in the message list.</summary>
public sealed record DayVm(string Text);

/// <summary>A message prepared for the virtualized message list, shown as a chat bubble.</summary>
public class MessageVm : INotifyPropertyChanged
{
    private readonly ViewerDocument _document;
    private readonly (Brush Strong, Brush Soft, Brush Border) _colours;
    private ImageSource? _avatar;
    private bool _avatarStarted;

    public MessageVm(ViewerDocument document, MessageView view)
    {
        _document = document;
        View = view;
        _colours = SenderPalette.For(view.SenderIndex);
        Attachments = view.Attachments.Select(a => new AttachmentVm(document, a)).ToList();
        Reactions = view.Reactions.Select(r => r.Display).ToList();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private string _searchTerm = "";

    /// <summary>The current search text; occurrences are highlighted in the bubble.</summary>
    public string SearchTerm
    {
        get => _searchTerm;
        set
        {
            if (_searchTerm == value) return;
            _searchTerm = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchTerm)));
        }
    }

    public MessageView View { get; }
    public string SenderName => View.SenderName;
    public string Initials => View.SenderInitials;
    public string TimeText => View.TimeText.Length > 0 ? View.TimeText : View.TimestampText;
    public string Body => View.Body;
    /// <summary>The body as shown: emoji short names such as ":smile:" appear as the emoji.</summary>
    public string DisplayBody => EmojiShortNames.Expand(View.Body);
    public IReadOnlyList<AttachmentVm> Attachments { get; }
    public IReadOnlyList<string> Reactions { get; }

    public HorizontalAlignment Alignment => View.AlignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    public Brush SenderBrush => _colours.Strong;

    // Chat-app look: the custodian's (or "outgoing") messages are blue on the right, everyone else's green
    // on the left, with white text. The sender and time stay on the bubble.
    public Brush BubbleBackground => View.AlignRight ? BubblePalette.Blue : BubblePalette.Green;
    public Brush BubbleBorder => BubbleBackground;
    public Brush BubbleText => BubblePalette.Text;
    public Brush BubbleSubtle => BubblePalette.Subtle;

    public Visibility BodyVisibility => View.Body.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DeletedVisibility => View.Deleted ? Visibility.Visible : Visibility.Collapsed;
    public string ReplyText => View.ParentId.Length > 0 ? "\u21AA reply to " + View.ParentId : "";
    public Visibility ReplyVisibility => View.ParentId.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string EditedText => View.EditCount switch { 0 => "", 1 => "edited", _ => $"edited {View.EditCount} times" };
    public Visibility FooterVisibility => Reactions.Count > 0 || View.EditCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Record details shown when hovering over the bubble.</summary>
    public string ToolTipText
    {
        get
        {
            var direction = View.Direction.Length > 0 ? $"\nDirection: {View.Direction}" : "";
            return $"{View.TimestampText}\n{View.Type}  \u00B7  {View.RecordId}{direction}";
        }
    }

    public string Meta => $"{View.Type}  \u00B7  {View.RecordId}";

    /// <summary>The sender's avatar from the archive, loaded off the UI thread; null shows the initials.</summary>
    public ImageSource? AvatarImage
    {
        get
        {
            if (View.SenderAvatar.Length > 0 && !_avatarStarted)
            {
                _avatarStarted = true;
                _ = Task.Run(LoadAvatar);
            }
            return _avatar;
        }
    }

    private void LoadAvatar()
    {
        try
        {
            var bytes = _document.ReadAvatar(View.SenderAvatar);
            if (bytes is null) return;
            _avatar = ImageLoader.Decode(bytes, 64);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvatarImage)));
        }
        catch (Exception)
        {
            // Undecodable avatar: keep the initials.
        }
    }
}

/// <summary>A non-message event (join, leave, disclaimer, history...) shown as a centred notice.</summary>
public sealed class NoticeVm : MessageVm
{
    public NoticeVm(ViewerDocument document, MessageView view) : base(document, view) { }

    public string NoticeText
    {
        get
        {
            var what = View.Type switch
            {
                "join" => "joined",
                "leave" => "left",
                _ => View.Type,
            };
            var body = View.Body.Length > 0 ? ": " + View.Body : "";
            var time = View.TimeText.Length > 0 ? "  \u00B7  " + View.TimeText : "";
            return $"{View.SenderName} {what}{body}{time}";
        }
    }
}

/// <summary>An affected record shown under the metadata coverage grid.</summary>
public sealed record AffectedRecord(string Kind, string RecordId)
{
    public string Display => $"{Kind}: {RecordId}";
}
