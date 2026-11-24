namespace Rsmf.Core;

/// <summary>How a rule is checked and why it matters, in plain words.</summary>
public sealed record RuleGuideEntry(string How, string Why);

/// <summary>A published page a rule is based on: its name, the part of it to read, and its address.</summary>
public sealed record RuleLink(string Label, string Section, string Url);

/// <summary>
/// The rule guide: for every rule, how the app checks it, why the check exists, and the published
/// Relativity pages it is based on. This explains the rules; it does not change what any rule checks.
/// A test fails if a rule has no entry.
/// </summary>
public static class RuleGuide
{
    private const string Help = "https://help.relativity.com/RelativityOne/Content/";

    /// <summary>The published sources, by the short names used in <see cref="RuleDefinition.Source"/>.</summary>
    private static readonly Dictionary<string, (string Label, string Url)> Sources = new(StringComparer.Ordinal)
    {
        ["SPEC"] = ("Relativity help: The Relativity Short Message Format (RSMF)", Help + "System_Guides/Relativity_Short_Message_Format/Relativity_short_message_format.htm"),
        ["CREATE"] = ("Relativity help: Creating an RSMF file", Help + "System_Guides/Relativity_Short_Message_Format/Creating_an_RSMF_File.htm"),
        ["PROC"] = ("Relativity help: Processing an RSMF file", Help + "System_Guides/Relativity_Short_Message_Format/Processing_an_RSMF_file.htm"),
        ["SLICE"] = ("Relativity help: RSMF slicing configuration reference", Help + "Relativity/Processing/SupportedFileTypes-RSMFSlicingReference.htm"),
        ["VLRW"] = ("Relativity help: Workflow solutions for very large workspaces", Help + "Solutions/Workflow_solutions_for_very_large_workspaces.htm"),
        ["VALID"] = ("Relativity on GitHub: RSMF validator samples and schema", "https://github.com/relativitydev/rsmf-validator-samples"),
        ["GEN"] = ("Relativity on GitHub: RSMF generation sample", "https://github.com/relativitydev/relativity-rsmf-gen-sample"),
    };

    private static readonly Dictionary<string, RuleGuideEntry> Entries = new(StringComparer.Ordinal)
    {
        ["F-MIME-001"] = new(
            "The app reads the file the way an email program reads an .eml file: a block of labelled lines at the top, followed by one or more parts. If that structure cannot be read, this rule fires.",
            "An RSMF file is an email file with a different ending. If it cannot be read as one, neither this app nor Relativity can reach the messages inside it."),
        ["F-MIME-002"] = new(
            "The app looks through the labelled lines at the top of the file for one named X-RSMF-Version and checks that it has a value.",
            "Relativity's specification makes this the one required label. It says which version of the format the rest of the file follows."),
        ["F-MIME-003"] = new(
            "The app reads the value of the X-RSMF-Version label and compares it with the two documented versions, 1.0.0 and 2.0.0.",
            "Relativity has published the format for those two versions only. With any other value it is not clear which set of rules the file is meant to follow."),
        ["F-MIME-004"] = new(
            "The app checks which labelled line comes first in the file and whether that line is X-RSMF-Version.",
            "Relativity's instructions for creating a file say the version label should be the first line. They do not say what happens when it is not, which is why this is a warning and not an error."),
        ["F-MIME-005"] = new(
            "The app counts the attached parts of the file and checks that there is exactly one, that it is named rsmf.zip, that it is marked as an attachment and that it is base64-encoded.",
            "Relativity expects all of a file's messages and attachments inside that single package. Relativity's own validator fails a file that carries a second attachment."),
        ["F-MIME-006"] = new(
            "The app decodes the rsmf.zip attachment from base64 text back into a ZIP archive and then tries to open the archive.",
            "Everything a reviewer would see is inside that archive. If it cannot be opened, there are no messages to load or view."),
        ["F-MIME-007"] = new(
            "The app looks at the ending of the file's name.",
            "Relativity's documentation lists the .rsmf ending first among the things Processing needs in order to treat a file as RSMF."),
        ["F-MIME-008"] = new(
            "The app measures the name of every extra X-RSMF label that is not one of the standard ones.",
            "Relativity documents that the name of a custom label must be shorter than 255 characters."),
        ["F-HDR-001"] = new(
            "The app reads the optional count and date labels at the top of the file and tries to read each one as a whole number or as a standard date and time.",
            "Relativity copies these values into document fields. A value of the wrong kind leaves that field wrong or empty."),
        ["F-HDR-002"] = new(
            "The app counts the events in the list of messages and compares the total with the number stated in the X-RSMF-EventCount label.",
            "Relativity can copy the stated count into a field, and a mismatch can also be a sign that events were lost when the file was made."),
        ["F-HDR-003"] = new(
            "The app finds the earliest event time in the file and compares it, as a moment in time, with the X-RSMF-BeginDate label.",
            "Relativity can use the stated start date as the document's sent date, so a wrong value affects sorting and filtering by date."),
        ["F-HDR-004"] = new(
            "The app finds the latest event time in the file and compares it, as a moment in time, with the X-RSMF-EndDate label.",
            "Relativity can use the stated end date as the document's last-modified date, so a wrong value affects sorting and filtering by date."),
        ["F-HDR-005"] = new(
            "When the file says it is version 1.0.0, the app looks for the labels that exist only in version 2.0.",
            "The specification's table of labels marks these as version 2.0 only, so the file is mixing two versions of the format."),
        ["F-ZIP-001"] = new(
            "The app lists the contents of rsmf.zip and looks for a file named exactly rsmf_manifest.json at the top level, not inside a folder.",
            "The specification puts the list of messages in that one file at the top level of the archive. Without it there is nothing to show."),
        ["F-ZIP-002"] = new(
            "The app lists every item in rsmf.zip and checks whether any attachment, profile picture or conversation icon refers to it by name.",
            "An item that nothing refers to is not shown as part of any message. Relativity's processing documentation covers such items under troubleshooting attachments."),
        ["F-MAN-001"] = new(
            "The app reads rsmf_manifest.json as JSON, the structured text format it must be written in, and checks that the whole file is a single object.",
            "Every message is stored in this one file. If it is not valid JSON, none of the messages can be read."),
        ["F-MAN-002"] = new(
            "The app reads the version value inside the list of messages and compares it with 1.0.0 and 2.0.0.",
            "The version decides which of Relativity's two published format definitions the content is checked against."),
        ["F-MAN-003"] = new(
            "The app checks the list of messages against the format definition (the schema) that Relativity publishes for the file's version. Each thing the definition does not allow is reported separately, with its location.",
            "The schema is Relativity's own statement of which fields are required and how they must be written. Relativity's validator reports these problems as failures."),
        ["F-MAN-004"] = new(
            "The app compares every field name in the list of messages with the names the schema defines for that version of the format.",
            "A name the format does not define is usually a misspelling or a field from the other version. Relativity's validator warns about it, and the information may be ignored."),
        ["F-MAN-005"] = new(
            "The app counts the entries in the participants list.",
            "The specification requires at least one participant, because every message has to belong to someone."),
        ["F-MAN-006"] = new(
            "The app counts the entries in the conversations list.",
            "The specification requires at least one conversation, because every message has to belong to one."),
        ["F-MAN-007"] = new(
            "The app counts the entries in the events list, which holds the messages and other activity.",
            "A file with no events has nothing to review, which usually means the export covered the wrong dates or went wrong."),
        ["F-MAN-008"] = new(
            "The app collects every participant ID in the file and looks for any value that is used more than once.",
            "Messages name their sender by ID. If two people share an ID, a message could be shown under the wrong person."),
        ["F-MAN-009"] = new(
            "The app collects every conversation ID in the file and looks for any value that is used more than once.",
            "Messages name their conversation by ID. If two conversations share an ID, messages cannot be tied to the right one."),
        ["F-MAN-010"] = new(
            "Within each conversation, the app collects the event IDs that are present and looks for any value that is used more than once.",
            "Replies point to the message they answer by its ID. If two messages share an ID, a reply could attach to the wrong one."),
        ["F-MAN-011"] = new(
            "For version 2.0.0 files, the app checks that each conversation states a type.",
            "The specification lists the type (direct or channel) as required in version 2.0, although the published schema does not enforce it."),
        ["F-MAN-012"] = new(
            "The app measures the length of each reaction's value.",
            "The specification limits a reaction's value to 30 characters. The published schema states this limit in a way that has no effect, so the app checks it directly."),
        ["F-MAN-013"] = new(
            "The app tries to read the time on every event, edit and read receipt as a standard ISO 8601 date and time.",
            "Messages are put in order and displayed by these times, and this app's one-day and 24-hour checks depend on them too."),
        ["F-REF-001"] = new(
            "The app takes each participant ID listed on a conversation and looks it up in the file's participant list.",
            "A conversation member who is not in the participant list has no name or details to show. Relativity's validator warns about this."),
        ["F-REF-002"] = new(
            "The app takes the sender ID on each event and looks it up in the file's participant list.",
            "A message whose sender is not in the participant list has no known author. Relativity's validator warns about this."),
        ["F-REF-003"] = new(
            "The app takes the conversation ID on each event and looks it up in the file's list of conversations.",
            "A message is displayed inside its conversation. One that points to a conversation that is not there has nowhere to appear."),
        ["F-REF-004"] = new(
            "The app takes the parent ID on each reply and looks for an event with that ID in the same file.",
            "Replies are shown linked to the message they answer. Relativity's slicing documentation notes that splitting a conversation into files can separate a reply from its parent."),
        ["F-REF-005"] = new(
            "The app takes each conversation's custodian and checks that the same ID appears among that conversation's participants.",
            "The custodian is the person the data was collected from, so the specification expects them to be a member of the conversation."),
        ["F-REF-006"] = new(
            "The app takes each participant ID credited with a reaction and looks it up in the file's participant list.",
            "A reaction from someone who is not in the participant list cannot be shown with a name."),
        ["F-REF-007"] = new(
            "The app takes the participant ID on each edit and looks it up in the file's participant list.",
            "An edit by someone who is not in the participant list cannot be shown with a name."),
        ["F-REF-008"] = new(
            "The app takes the participant ID on each read receipt and looks it up in the file's participant list.",
            "A read receipt from someone who is not in the participant list cannot be shown with a name."),
        ["F-REF-009"] = new(
            "The app takes the ID of each attachment listed on an event and looks for an item with that name inside rsmf.zip.",
            "The message says it has an attachment, but the attachment's content is not in the file, so it cannot be reviewed."),
        ["F-REF-010"] = new(
            "The app takes each participant's avatar name and looks for an item with that name inside rsmf.zip.",
            "The person's messages still display, without their picture. Relativity's validator reports this as a warning."),
        ["F-REF-011"] = new(
            "The app takes each conversation's icon name and looks for an item with that name inside rsmf.zip.",
            "The conversation still displays, without its icon. This affects appearance only."),
        ["L-SIZE-001"] = new(
            "The app reads the file's size in bytes and compares it with the size limit in Settings, which is 2 GB unless you change it.",
            "Relativity's documentation states that RSMF files greater than 2 GB are not supported."),
        ["L-EVT-001"] = new(
            "The app counts every event in the list of messages, of any type, and compares the total with 10,000.",
            "Relativity documents a viewer limitation of 10,000 events per RSMF file."),
        ["Q-TIME-A"] = new(
            "The app converts every message time into the time zone chosen in Settings and checks whether they all fall on the same calendar date.",
            "This is a local QC rule for teams that expect one file per day. It is not a Relativity requirement, and it can be switched off with the time-window mode in Settings."),
        ["Q-TIME-B"] = new(
            "The app subtracts the earliest message time in the file from the latest and checks whether the difference is more than 24 hours.",
            "This is a local QC rule for teams that expect each file to cover no more than a day. It is not a Relativity requirement."),
        ["Q-TIME-001"] = new(
            "The app checks whether each message time states its offset from UTC, for example Z or -05:00.",
            "A time without an offset is ambiguous. The app treats it as UTC, which can move a message onto a different calendar day."),
        ["Q-PART-001"] = new(
            "The app counts the distinct participants in the file and compares the total with the warning threshold in Settings. The check is off while that box is blank.",
            "This is a local QC rule for spotting unusually large conversations. It is not a Relativity requirement."),
        ["Q-PART-002"] = new(
            "The app looks at each participant's display name, account ID and ID for the words Unspecified Sender, in any letter case.",
            "That placeholder means the export tool could not tell who sent the messages, so reviewers will not be able to tell either."),
        ["Q-PART-003"] = new(
            "The app looks for participants with no email address whose display name and account ID are each blank or a phone number.",
            "Reviewers see a number where a name should be, which makes it hard to tell who was speaking."),
        ["Q-VER-001"] = new(
            "The app compares the version stated in the X-RSMF-Version label with the version stated inside the list of messages.",
            "If the two disagree, it is unclear which version of the format the file follows, and so which rules apply to it."),
        ["I-FILE-001"] = new(
            "The app tries to open and read the file. If Windows refuses, this rule records the reason.",
            "A file that could not be read was not checked, so it must not be mistaken for a clean file."),
        ["I-SCAN-001"] = new(
            "The app tries to list the contents of every folder under the one you chose. If Windows refuses, this rule records the folder.",
            "Any files in that folder were never seen, so the scan does not cover everything you pointed it at."),
        ["I-ZIP-001"] = new(
            "The app measures each item inside the archive before loading it and skips any that is larger than its memory safety limit.",
            "Content that was skipped was not checked, and the report says so instead of passing the file silently."),
        ["I-PARSE-001"] = new(
            "When an earlier check fails in a way that blocks later ones, the app lists which groups of checks did not run.",
            "Without this note, a short list of findings could be misread as a nearly clean file."),
        ["I-TIME-001"] = new(
            "Before judging the one-day and 24-hour checks, the app confirms that every message has a readable time.",
            "With a time missing, either answer could be wrong, so the app reports the checks as undecided instead of guessing."),
        ["I-RUN-001"] = new(
            "When a scan is cancelled or fails, the app records each file it had not yet reached.",
            "The reports then show exactly which files were never checked."),
    };

    public static IReadOnlyCollection<string> RuleIds => Entries.Keys;

    public static RuleGuideEntry Get(string ruleId) =>
        Entries.TryGetValue(ruleId, out var e) ? e : new RuleGuideEntry("No description is available for this rule.", "No description is available for this rule.");

    /// <summary>Only Relativity's help site and Relativity's own GitHub account are ever opened from the app.</summary>
    public static bool IsTrustedLink(string? url) =>
        url is not null && (url.StartsWith("https://help.relativity.com/", StringComparison.Ordinal)
                            || url.StartsWith("https://github.com/relativitydev/", StringComparison.Ordinal));

    /// <summary>
    /// The published pages a rule is based on, in the order the rule lists them. Sources that are not
    /// published (the owner's own QC rules) give no link, so a rule can have none.
    /// </summary>
    public static IReadOnlyList<RuleLink> Links(string ruleId)
    {
        var links = new List<RuleLink>();
        if (!RuleCatalog.TryResolve(ruleId, out var rule)) return links;
        foreach (var part in rule.Source.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var space = part.IndexOf(' ');
            var key = space < 0 ? part : part[..space];
            if (!Sources.TryGetValue(key, out var source)) continue;
            var section = (space < 0 ? "" : part[(space + 1)..]).Replace("\u00A7", "").Trim();
            // The rule sources use short notes; spell them out for readers.
            for (var point = 1; point <= 3; point++)
                section = section.Replace($"support ({point})", $"RSMF file support in Processing, point {point}");
            section = section.Replace("PDF", "the walkthrough PDF");
            var at = links.FindIndex(l => l.Url == source.Url);
            if (at < 0) links.Add(new RuleLink(source.Label, section, source.Url));
            else if (section.Length > 0)
                links[at] = links[at] with { Section = links[at].Section.Length == 0 ? section : links[at].Section + ", " + section };
        }
        return links;
    }

    /// <summary>Where a rule comes from, in one line: Relativity's format, a Relativity limit, or this app's own checks.</summary>
    public static string Origin(RuleDefinition rule) => rule.Category switch
    {
        FindingCategory.FORMAT => "Relativity format rule: based on Relativity's published RSMF documentation.",
        FindingCategory.LIMIT => "Relativity product limit: based on limits Relativity documents for RSMF files.",
        FindingCategory.LOCAL => "Local QC rule: this app's own check, not a Relativity requirement.",
        _ => "Scan status: reports something the app could not check. Not a Relativity requirement.",
    };

    /// <summary>The links as one line of text, for the CSV guide and the explain command.</summary>
    public static string LinksText(string ruleId)
    {
        var links = Links(ruleId);
        return links.Count == 0
            ? "No Relativity page: this is the app's own check."
            : string.Join("; ", links.Select(l => (l.Section.Length > 0 ? $"{l.Label} (see: {l.Section})" : l.Label) + " " + l.Url));
    }
}
