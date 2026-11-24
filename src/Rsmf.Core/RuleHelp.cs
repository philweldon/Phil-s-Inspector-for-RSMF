namespace Rsmf.Core;

/// <summary>The technical explanation of a rule: what a finding means and how the file should be corrected.</summary>
public sealed record RuleExplanation(string Meaning, string HowToFix);

/// <summary>The non-technical explanation: the problem in a few words, what it means, and what the reader can do.</summary>
public sealed record PlainExplanation(string Problem, string Meaning, string Action);

/// <summary>
/// Reader-facing help for every rule. This explains the rules in docs/RULES.md; it does not change
/// what any rule checks. A test fails if a rule has no entry.
/// </summary>
public static class RuleHelp
{
    private const string Regenerate = "Regenerate the file with the tool that created it, or ask the party who supplied it for a corrected file.";

    private static readonly Dictionary<string, RuleExplanation> Help = new(StringComparer.Ordinal)
    {
        ["F-MIME-001"] = new(
            "The file could not be read as an email-style (MIME) message at all. An RSMF file is an .eml file with a different extension, so this usually means the file is truncated, corrupted, or is not an RSMF file.",
            "Check that the file transferred completely (compare its size with the source). If it did, the file is not a valid RSMF. " + Regenerate),
        ["F-MIME-002"] = new(
            "The X-RSMF-Version header is missing or empty. Relativity requires it in both RSMF 1.0 and 2.0.",
            "Add an X-RSMF-Version header (for example 2.0.0) as the first header line, matching the version in rsmf_manifest.json. " + Regenerate),
        ["F-MIME-003"] = new(
            "The X-RSMF-Version header holds a value other than 1.0.0 or 2.0.0, the two versions Relativity has published schemas for.",
            "Set X-RSMF-Version to 1.0.0 or 2.0.0 to match the manifest. Whether Relativity accepts shortened forms such as 2.0 is not documented, so use the full form."),
        ["F-MIME-004"] = new(
            "X-RSMF-Version is present but is not the first header line. Relativity's documentation says it should be first.",
            "Move X-RSMF-Version to the top of the headers. The documentation says \"should\", so treat this as a warning to correct at the source, not proof the file will be rejected."),
        ["F-MIME-005"] = new(
            "The file does not have exactly one attachment named rsmf.zip, base64-encoded and marked as an attachment. There may be none, more than one, a different name or letter case, or a different encoding.",
            "The file must contain a single attachment: Content-Disposition: attachment; filename=\"rsmf.zip\", Content-Transfer-Encoding: base64. Remove any other attachments; put every message attachment inside rsmf.zip instead."),
        ["F-MIME-006"] = new(
            "The rsmf.zip attachment could not be decoded or opened: it contains characters that are not base64, or the decoded data is not a readable ZIP archive (often because it was cut short).",
            "Check that the file transferred completely. If it did, the archive was damaged when the file was built. " + Regenerate),
        ["F-MIME-007"] = new(
            "The file name does not end in .rsmf. Relativity Processing identifies RSMF files by this extension.",
            "Rename the file so it ends in .rsmf."),
        ["F-MIME-008"] = new(
            "A custom X-RSMF-* header has a name of 255 characters or more. Relativity documents that a custom header name must be shorter than 255 characters.",
            "Shorten the custom header's name at the source."),
        ["F-HDR-001"] = new(
            "An optional roll-up header holds a value of the wrong kind: X-RSMF-EventCount or X-RSMF-AttachmentCount is not a whole number, or X-RSMF-BeginDate or X-RSMF-EndDate is not an ISO 8601 date-time.",
            "Correct the header value, or remove the header: these headers are optional. Relativity maps them to fields, so a bad value gives a wrong or empty field."),
        ["F-HDR-002"] = new(
            "X-RSMF-EventCount does not equal the number of events in the manifest. The header is mapped to a field in Relativity, so the field would be wrong.",
            "Set X-RSMF-EventCount to the number of events in rsmf_manifest.json. This tool compares it with all events (not only messages); the published sources are not explicit on that point."),
        ["F-HDR-003"] = new(
            "X-RSMF-BeginDate is not the same moment as the earliest event timestamp in the manifest. Relativity can map this header to the sent date.",
            "Set X-RSMF-BeginDate to the earliest event timestamp. Differences of time zone notation are fine; the two must be the same instant."),
        ["F-HDR-004"] = new(
            "X-RSMF-EndDate is not the same moment as the latest event timestamp in the manifest. Relativity can map this header to the last-modified date.",
            "Set X-RSMF-EndDate to the latest event timestamp. Differences of time zone notation are fine; the two must be the same instant."),
        ["F-HDR-005"] = new(
            "The file declares RSMF 1.0.0 but carries a header that only exists in RSMF 2.0 (Application, Custodian, Participants, AttachmentCount or EventCollectionID).",
            "Either produce the file as RSMF 2.0.0 (header and manifest), or remove the 2.0-only header."),
        ["F-ZIP-001"] = new(
            "rsmf.zip opened, but there is no rsmf_manifest.json at its top level. The manifest holds every message, so without it there is nothing to review. It may be missing, inside a subfolder, or named with different letter case.",
            "Rebuild rsmf.zip so that rsmf_manifest.json sits at the root of the archive, not inside a folder."),
        ["F-ZIP-002"] = new(
            "rsmf.zip contains a file that no attachment, avatar or icon in the manifest refers to. Relativity processes such a file as a loose document under an rsmf.zip folder, not as part of a message.",
            "If the file belongs to a message, add an attachment entry for it to that event (the id must equal the file name). If it does not belong, remove it from the archive."),
        ["F-MAN-001"] = new(
            "rsmf_manifest.json is not valid JSON (or its top level is not an object), so no message could be read from it.",
            "Open the manifest from the Raw manifest tab to see where it breaks; the finding gives the line and position. " + Regenerate),
        ["F-MAN-002"] = new(
            "The manifest's \"version\" is missing or is not 1.0.0 or 2.0.0, so the tool cannot tell which schema applies and schema validation was skipped.",
            "Set \"version\" in rsmf_manifest.json to 1.0.0 or 2.0.0, matching the X-RSMF-Version header."),
        ["F-MAN-003"] = new(
            "The manifest breaks Relativity's published schema for its version: a required field is missing, a value has the wrong type, or a value is not one of the allowed choices. The message and location say exactly what and where.",
            "Correct the field named in the location. Relativity's own validator reports schema violations as errors. Note that RSMF 1.0 requires more fields than 2.0 (for example display names, event participant, conversation and timestamp)."),
        ["F-MAN-004"] = new(
            "The manifest contains a property the schema for its version does not define. It is often a misspelling (for example \"attachment\" for \"attachments\") or an RSMF 2.0 field used in a 1.0 file. Relativity's validator reports this as a warning.",
            "Rename the property to the documented name, or move the value into the record's \"custom\" list, which is the supported place for extra metadata."),
        ["F-MAN-005"] = new(
            "The manifest has no participants. Relativity requires at least one for a file to be valid.",
            "Add every person or account that takes part to the \"participants\" list. " + Regenerate),
        ["F-MAN-006"] = new(
            "The manifest has no conversations. Relativity requires at least one for a file to be valid.",
            "Add the conversation the events belong to. " + Regenerate),
        ["F-MAN-007"] = new(
            "The manifest has no events, so the file contains no messages.",
            "Check the export covered the intended date range. " + Regenerate),
        ["F-MAN-008"] = new(
            "Two or more participants share the same id. Events refer to participants by id, so messages could be attributed to the wrong person.",
            "Give every participant a unique id and update the references that use it."),
        ["F-MAN-009"] = new(
            "Two or more conversations share the same id, so events cannot be tied to one conversation unambiguously.",
            "Give every conversation a unique id and update the events that refer to it."),
        ["F-MAN-010"] = new(
            "Two or more events in the same conversation share an id. Replies refer to their parent by id, so threads could attach to the wrong message.",
            "Give each event in a conversation a unique id (ids are only needed for events that are part of a thread)."),
        ["F-MAN-011"] = new(
            "A conversation in an RSMF 2.0 file has no \"type\". Relativity's specification page lists it as required (direct or channel) although the 2.0 schema does not enforce it.",
            "Add \"type\": \"direct\" (fixed set of participants) or \"channel\" (participants can join and leave)."),
        ["F-MAN-012"] = new(
            "A reaction's value is longer than 30 characters, the documented maximum.",
            "Use a short code or the emoji character itself for the reaction value."),
        ["F-MAN-013"] = new(
            "A timestamp on an event, edit or read receipt is not an ISO 8601 date-time (for example 2026-03-03T14:05:00Z).",
            "Write the timestamp in ISO 8601 form with a time zone offset or Z. If it is a message's own timestamp, the time-window check for the file will also be indeterminate until it is fixed."),
        ["F-REF-001"] = new(
            "A conversation lists a participant id that is not defined in the manifest's participants. Relativity's validator reports this as a warning.",
            "Add the missing participant to the participants list, or correct the id in the conversation."),
        ["F-REF-002"] = new(
            "An event's participant (the sender) is not defined in the manifest's participants, so the message has no known sender. Relativity's validator reports this as a warning.",
            "Add the missing participant, or correct the id on the event."),
        ["F-REF-003"] = new(
            "An event refers to a conversation id that does not exist, so it belongs to no conversation.",
            "Correct the event's \"conversation\" to an existing conversation id, or add the missing conversation."),
        ["F-REF-004"] = new(
            "A reply's \"parent\" is not the id of another event in the same conversation. This also happens legitimately when a long conversation is split into several files and the original message is in a different file.",
            "If the parent should be in this file, correct the id. If the file is one slice of a longer conversation, this may be expected: note it and move on."),
        ["F-REF-005"] = new(
            "A conversation's custodian is not one of that conversation's participants. The custodian is the person the data was collected from.",
            "Set \"custodian\" to the id of a participant in the conversation, or add that participant to the conversation."),
        ["F-REF-006"] = new(
            "A reaction is attributed to a participant id that is not defined in the manifest.",
            "Add the missing participant, or correct the id in the reaction."),
        ["F-REF-007"] = new(
            "An edit is attributed to a participant id that is not defined in the manifest.",
            "Add the missing participant, or correct the id in the edit."),
        ["F-REF-008"] = new(
            "A read receipt is attributed to a participant id that is not defined in the manifest.",
            "Add the missing participant, or correct the id in the read receipt."),
        ["F-REF-009"] = new(
            "An event lists an attachment whose id is not the name of any file in rsmf.zip, so the attachment cannot be shown or linked to the message. Relativity's validator reports the same condition.",
            "Add the file to rsmf.zip under exactly that name (including extension), or correct the attachment id. Names are compared exactly, including letter case."),
        ["F-REF-010"] = new(
            "A participant's avatar names a file that is not in rsmf.zip. The message still displays, without the picture. Relativity's validator reports this as a warning.",
            "Add the image to rsmf.zip under that name, or remove the avatar value."),
        ["F-REF-011"] = new(
            "A conversation's icon names a file that is not in rsmf.zip.",
            "Add the image to rsmf.zip under that name, or remove the icon value. Relativity notes that custom icons are not currently shown in its viewer."),
        ["L-SIZE-001"] = new(
            "The file is larger than the size threshold. Relativity documents that RSMF files greater than 2 GB are not supported and may be unable to process.",
            "Split the conversation into smaller files (for example smaller time periods) with the tool that generated it. The threshold used is stated in the reports; the default is 2,147,483,648 bytes."),
        ["L-EVT-001"] = new(
            "The file has more than 10,000 events. Relativity documents a viewer limit of 10,000 events per RSMF file and says larger files may cause errors when opened.",
            "Split the conversation into files of 10,000 events or fewer, typically by a shorter time period."),
        ["Q-TIME-A"] = new(
            "Local QC rule A: the messages in this file fall on more than one calendar day in the selected time zone. Each timestamp is converted from its own offset first.",
            "If one-day files are required, regenerate with daily slicing in the intended time zone. Check the time zone setting first: the same file can pass in one zone and fail in another."),
        ["Q-TIME-B"] = new(
            "Local QC rule B: the latest message is more than 24 hours after the earliest. Exactly 24 hours passes.",
            "Regenerate with a shorter time period per file. On the day clocks go back, one calendar day lasts 25 hours, so a file can pass rule A and still fail this rule."),
        ["Q-TIME-001"] = new(
            "One or more message timestamps have no time zone offset. The tool reads them as UTC, which may not be what the source system meant, so day boundaries could be wrong.",
            "Have the generating tool write timestamps with an offset or Z. Until then, treat the time-window result for this file with caution."),
        ["Q-PART-001"] = new(
            "The file has more unique participants than the warning threshold you set. Relativity says files with a significant number of participants cause performance problems but publishes no number.",
            "Decide whether the conversation should be split or the threshold raised. This is a local warning, not a Relativity limit."),
        ["Q-PART-002"] = new(
            "A participant is named \"Unspecified Sender\". That is a placeholder some export tools write when they could not work out who sent a message, so messages from this participant are not attributed to a real person.",
            "Go back to the source data or the export settings and get the real sender identified, then regenerate the file. If the sender truly cannot be known, record that decision."),
        ["Q-PART-003"] = new(
            "A participant is identified by a phone number alone: there is no display name, account name or email address. Reviewers will see only the number.",
            "If the name behind the number is known (for example from the device's contacts), add it as the participant's display name and regenerate the file. Otherwise record that the number could not be resolved."),
        ["Q-VER-001"] = new(
            "The X-RSMF-Version header and the manifest's \"version\" disagree, so it is unclear which version of the format the file follows.",
            "Make the header and the manifest state the same version."),
        ["I-FILE-001"] = new(
            "The file could not be opened or read (it may be locked by another program, you may lack permission, or the disk reported an error). Nothing in it was checked, so this is not a pass.",
            "Close any program that has the file open, check your access to it, and scan again."),
        ["I-SCAN-001"] = new(
            "A folder could not be listed, usually because of permissions. Any RSMF files inside it were not seen, so the scan is incomplete.",
            "Get access to the folder, or scan a copy of it, and run the scan again."),
        ["I-ZIP-001"] = new(
            "Part of the file was larger than the tool's in-memory safety cap, so the checks that need it were not run. This is not a pass.",
            "For rsmf.zip, scan through the app or CLI with an output folder so the archive can be spooled to disk. A manifest over the cap usually means the conversation should be split into smaller files."),
        ["I-PARSE-001"] = new(
            "An earlier layer of the file failed, so later checks could not run. The message says which checks were skipped. This is information to read alongside the error that caused it.",
            "Fix the error reported for the same file first (damaged MIME, ZIP or manifest); the skipped checks will run once that is resolved."),
        ["I-TIME-001"] = new(
            "The time-window check could not be decided because one or more messages have a missing or unreadable timestamp. The file neither passed nor failed rules A and B.",
            "Fix the timestamps listed in the finding at the source, then scan again."),
        ["I-RUN-001"] = new(
            "The scan was cancelled (or failed) before this file was checked. Nothing is known about it.",
            "Run the scan again and let it finish."),
    };

    /// <summary>
    /// The same rules explained for someone who does not work with the file format: the problem in a few
    /// words, what it means without jargon, and what a reviewer or project manager can actually do.
    /// </summary>
    private static readonly Dictionary<string, PlainExplanation> PlainHelp = new(StringComparer.Ordinal)
    {
        ["F-MIME-001"] = new(
            "File cannot be read as an RSMF file",
            "The program could not read this file at all. It is probably damaged, incomplete, or not really an RSMF file.",
            "Copy the file again from where it came from and scan it again. If it still fails, ask whoever produced the file for a new copy."),
        ["F-MIME-002"] = new(
            "Version label is missing",
            "Every RSMF file is meant to start with a label saying which version of the format it uses. This file has none, and Relativity requires it.",
            "Ask whoever produced the files to make them again with the version label included."),
        ["F-MIME-003"] = new(
            "Version label is not one Relativity documents",
            "The file's version label says something other than 1.0.0 or 2.0.0, the only two versions Relativity documents.",
            "Ask whoever produced the files to make them again as version 1.0.0 or 2.0.0."),
        ["F-MIME-004"] = new(
            "Version label is not on the first line",
            "The file has its version label, but another line comes before it, usually the name of the tool that made the file. Relativity's instructions say the version label should come first. They say \"should\", not \"must\", and this program cannot tell you whether Relativity will mind.",
            "There is nothing to fix by hand inside each file. Load a small test batch into Relativity to confirm the files are accepted, and tell whoever runs the export tool so they can change the order if needed."),
        ["F-MIME-005"] = new(
            "The file does not hold exactly one message package",
            "An RSMF file must carry one package inside it, named rsmf.zip, which holds the messages. This file has none, more than one, or one with the wrong name.",
            "Ask whoever produced the file to make it again. This cannot be repaired by hand."),
        ["F-MIME-006"] = new(
            "The message package inside the file is damaged",
            "The package of messages inside this file cannot be opened, usually because the file was cut short or damaged while being copied.",
            "Copy the file again from where it came from and scan it again. If it still fails, ask whoever produced the file for a new copy."),
        ["F-MIME-007"] = new(
            "File name does not end in .rsmf",
            "Relativity recognises these files by the .rsmf ending on the file name, and this file has a different ending.",
            "Rename the file so that its name ends in .rsmf."),
        ["F-MIME-008"] = new(
            "A custom label has a name that is too long",
            "The file carries an extra, custom label whose name is 255 characters or longer. Relativity says such names must be shorter.",
            "Ask whoever produced the file to shorten that label's name."),
        ["F-HDR-001"] = new(
            "A summary value at the top of the file is the wrong kind",
            "The top of the file can carry summary values such as counts and dates. One of them holds something that is not a number, or not a date. Relativity copies these values into its own fields, so that field would be wrong or empty.",
            "Ask whoever produced the file to correct the value or leave it out; these summary values are optional."),
        ["F-HDR-002"] = new(
            "Stated number of events does not match the content",
            "The summary at the top of the file says it holds a certain number of events, but the content holds a different number. Relativity can copy the stated number into a field, so that field would be wrong.",
            "Compare the Observed column with what you expect from the source, in case events are missing. Then ask whoever produced the file to correct the count."),
        ["F-HDR-003"] = new(
            "Stated start date does not match the first message",
            "The summary at the top of the file gives a start date and time, but the earliest message in the file is at a different time. Relativity can use the stated start date as the document's sent date, so sorting and filtering by date could be off for this file.",
            "Look at the Observed column to see how far apart the two times are. A small gap is often rounding or a time-zone slip in the export tool. A large gap deserves a question to whoever produced the file. Correcting it means making the file again."),
        ["F-HDR-004"] = new(
            "Stated end date does not match the last message",
            "The summary at the top of the file gives an end date and time, but the latest message in the file is at a different time. Relativity can use the stated end date as the document's last-modified date, so sorting and filtering by date could be off for this file.",
            "Look at the Observed column to see how far apart the two times are. A small gap is often rounding or a time-zone slip in the export tool. A large gap deserves a question to whoever produced the file. Correcting it means making the file again."),
        ["F-HDR-005"] = new(
            "File says version 1.0 but uses a version 2.0 feature",
            "The file is labelled as version 1.0, but the top of the file includes a summary value that only exists in version 2.0.",
            "Ask whoever produced the file to make it again as version 2.0, or to leave that value out."),
        ["F-ZIP-001"] = new(
            "The list of messages is missing from the file",
            "Inside every RSMF file there is one list that holds all the messages. In this file it is missing, misnamed, or tucked inside a subfolder, so there is nothing to review.",
            "Ask whoever produced the file to make it again. This cannot be repaired by hand."),
        ["F-ZIP-002"] = new(
            "The file contains an item that no message refers to",
            "The package inside this file holds an extra item, such as a picture or document, that no message, profile picture or icon points to. Relativity would load it as a separate loose document, not as part of a message.",
            "Decide whether the item belongs to a message. If it does, ask whoever produced the file to link it to that message. If it does not, ask them to leave it out."),
        ["F-MAN-001"] = new(
            "The list of messages cannot be read",
            "The list of messages inside this file is damaged or not written correctly, so no messages could be read from it.",
            "Ask whoever produced the file to make it again. The Observed column says where the list breaks, which will help them."),
        ["F-MAN-002"] = new(
            "The list of messages does not state a valid version",
            "The list of messages inside the file should say it is version 1.0.0 or 2.0.0. It says something else, or nothing, so the detailed format checks could not be run.",
            "Ask whoever produced the file to make it again with the version stated."),
        ["F-MAN-003"] = new(
            "Content breaks Relativity's published format rules",
            "Something Relativity's format requires is missing, or is written in the wrong way. The Observed and Location columns say what and where. Relativity's own checker treats these as errors.",
            "Send the finding to whoever produced the file and ask them to correct it and make the file again."),
        ["F-MAN-004"] = new(
            "Content includes a field Relativity's format does not define",
            "The file contains a piece of information under a name the format does not know. It is often a misspelt name, or a version 2.0 feature used in a version 1.0 file. Relativity's checker warns about this, and the information may be ignored.",
            "Ask whoever produced the file to use the documented name, or to move the information into the format's place for custom values."),
        ["F-MAN-005"] = new(
            "No participants are listed",
            "The file does not list anyone as taking part in the conversation. Relativity requires at least one participant.",
            "Ask whoever produced the file to make it again with the participants included."),
        ["F-MAN-006"] = new(
            "No conversations are listed",
            "The file does not contain any conversation. Relativity requires at least one.",
            "Ask whoever produced the file to make it again."),
        ["F-MAN-007"] = new(
            "The file contains no messages or events",
            "The file is well formed but empty: it holds no messages at all.",
            "Check that the export covered the dates you intended. If it did, this file may simply be empty and not needed."),
        ["F-MAN-008"] = new(
            "Two participants share the same ID",
            "Each participant should have an ID of their own. Two or more share one here, so messages could be shown under the wrong person.",
            "Ask whoever produced the file to give each participant a separate ID and make the file again."),
        ["F-MAN-009"] = new(
            "Two conversations share the same ID",
            "Each conversation should have an ID of its own. Two or more share one here, so messages cannot be tied to the right conversation.",
            "Ask whoever produced the file to give each conversation a separate ID and make the file again."),
        ["F-MAN-010"] = new(
            "Two messages in one conversation share the same ID",
            "Replies point to the message they answer by its ID. Two or more messages share an ID here, so a reply could attach to the wrong message.",
            "Ask whoever produced the file to give each message a separate ID and make the file again."),
        ["F-MAN-011"] = new(
            "A conversation does not say whether it is a direct chat or a channel",
            "Version 2.0 files are meant to say, for each conversation, whether it is a direct chat between fixed people or a channel people can join and leave. This one does not say.",
            "Ask whoever produced the file to add that information."),
        ["F-MAN-012"] = new(
            "A reaction's name is too long",
            "A reaction, such as a thumbs-up, is recorded with a name longer than the 30 characters Relativity allows.",
            "Ask whoever produced the file to use a short name or the emoji itself."),
        ["F-MAN-013"] = new(
            "A date and time is not written in the standard way",
            "A date and time in the file cannot be read as a date. If it is the time of a message, this program also cannot judge the one-day and 24-hour checks for the file.",
            "Ask whoever produced the file to write dates in the standard international form, including the time zone."),
        ["F-REF-001"] = new(
            "A conversation lists someone who is not in the participant list",
            "A conversation names a participant that the file's list of participants does not include. Relativity's checker warns about this.",
            "Ask whoever produced the file to add the missing person to the participant list."),
        ["F-REF-002"] = new(
            "A message's sender is not in the participant list",
            "A message is recorded as sent by someone the file's list of participants does not include, so the message has no known sender. Relativity's checker warns about this.",
            "Ask whoever produced the file to add the missing sender to the participant list."),
        ["F-REF-003"] = new(
            "A message belongs to a conversation that does not exist",
            "A message points to a conversation that is not in the file, so it does not belong anywhere.",
            "Ask whoever produced the file to correct the message or add the missing conversation."),
        ["F-REF-004"] = new(
            "A reply answers a message that is not in this file",
            "A reply points to an earlier message that this file does not contain. That is normal when one long conversation has been split into several files and the earlier message is in another file.",
            "If your files are slices of longer conversations, this usually needs no action. Otherwise ask whoever produced the file to check it."),
        ["F-REF-005"] = new(
            "The custodian is not a member of the conversation",
            "The custodian is the person the data was collected from. This conversation names a custodian who is not one of its participants.",
            "Ask whoever produced the file to correct the custodian or add them to the conversation."),
        ["F-REF-006"] = new(
            "A reaction is credited to someone not in the participant list",
            "A reaction is recorded as coming from someone the file's list of participants does not include.",
            "Ask whoever produced the file to add the missing person to the participant list."),
        ["F-REF-007"] = new(
            "An edit is credited to someone not in the participant list",
            "An edit to a message is recorded as made by someone the file's list of participants does not include.",
            "Ask whoever produced the file to add the missing person to the participant list."),
        ["F-REF-008"] = new(
            "A read receipt is credited to someone not in the participant list",
            "A read receipt is recorded for someone the file's list of participants does not include.",
            "Ask whoever produced the file to add the missing person to the participant list."),
        ["F-REF-009"] = new(
            "A message's attachment is missing from the file",
            "A message says it has an attachment, but that attachment is not inside the RSMF file, so reviewers will not be able to open it.",
            "Check whether the attachment exists in the source data. If it does, ask whoever produced the file to make it again with the attachment included."),
        ["F-REF-010"] = new(
            "A participant's picture is missing from the file",
            "A participant is given a profile picture that is not inside the RSMF file. Their messages still display, without the picture.",
            "This affects appearance only. If the pictures matter, ask whoever produced the file to include them."),
        ["F-REF-011"] = new(
            "A conversation's icon is missing from the file",
            "A conversation is given an icon picture that is not inside the RSMF file.",
            "This affects appearance only, and Relativity says it does not currently show these icons."),
        ["L-SIZE-001"] = new(
            "File is larger than the size limit",
            "The file is bigger than the size limit set in Settings. Relativity says RSMF files over 2 GB are not supported and may fail to process.",
            "Ask whoever produced the file to split the conversation into smaller files, for example by shorter time periods."),
        ["L-EVT-001"] = new(
            "File has more than 10,000 messages and events",
            "Relativity says its viewer handles up to 10,000 messages and events in one RSMF file, and that larger files may cause errors when opened.",
            "Ask whoever produced the file to split the conversation into files of 10,000 or fewer, usually by shorter time periods."),
        ["Q-TIME-A"] = new(
            "Messages fall on more than one calendar day",
            "This is your own team's check, not a Relativity requirement. The messages in this file fall on more than one calendar day in the time zone chosen in Settings.",
            "First check the time zone in Settings, because the same file can pass in one zone and fail in another. If the zone is right and you need one day per file, ask for the files to be split by day."),
        ["Q-TIME-B"] = new(
            "Messages span more than 24 hours",
            "This is your own team's check, not a Relativity requirement. The last message in this file is more than 24 hours after the first.",
            "If you need each file to cover 24 hours or less, ask for the files to be split into shorter periods."),
        ["Q-TIME-001"] = new(
            "Message times do not say which time zone they are in",
            "Some message times in this file do not say which time zone they are in. This program treated them as UTC, which may be several hours away from what was meant, so the one-day check could be wrong.",
            "Treat the one-day and 24-hour results for this file with caution, and ask whoever produced the file to include the time zone with each time."),
        ["Q-PART-001"] = new(
            "More participants than the limit you set",
            "The file has more participants than the warning number you entered in Settings. Relativity says files with very many participants can be slow, but gives no number.",
            "Decide whether the conversation should be split, or raise your warning number. This is your own check, not a Relativity limit."),
        ["Q-PART-002"] = new(
            "A participant is called \"Unspecified Sender\"",
            "\"Unspecified Sender\" is a placeholder that an export tool writes when it could not tell who sent a message. Messages from this participant are not tied to a real person.",
            "Ask whoever ran the export whether the real sender can be identified; often it is the owner of the device. If not, make a note that these messages have no named sender."),
        ["Q-PART-003"] = new(
            "A participant shows only as a phone number",
            "No name is recorded for this person, so reviewers will see a phone number where a name would normally be. The file is not damaged.",
            "If names matter for the review, ask whoever ran the export whether contact names can be included, for example from the phone's address book, and the files made again. If the numbers cannot be matched to names, this is simply how the data is."),
        ["Q-VER-001"] = new(
            "The file gives two different version numbers",
            "The version label at the top of the file and the version stated inside it disagree, so it is unclear which version of the format the file follows.",
            "Ask whoever produced the file to make the two agree."),
        ["I-FILE-001"] = new(
            "The file could not be opened",
            "The program could not open or read this file. It may be open in another program, you may not have permission, or the disk reported a problem. Nothing in the file was checked, so this is not a pass.",
            "Close any program that has the file open, check that you can open the folder, and scan again."),
        ["I-SCAN-001"] = new(
            "A folder could not be opened",
            "The program could not look inside one folder, usually because of permissions. Any RSMF files in it were not seen, so the scan is incomplete.",
            "Get access to the folder, or scan a copy of it, and run the scan again."),
        ["I-ZIP-001"] = new(
            "Part of the file was too large to check",
            "Part of this file was larger than the program's safety limit, so some checks were not run. This is not a pass.",
            "A file this large usually means the conversation should be split into smaller files. Ask whoever produced it."),
        ["I-PARSE-001"] = new(
            "Some checks were skipped because of an earlier problem",
            "Another problem in the same file stopped the later checks from running. This line is here so you know those checks did not happen.",
            "Deal with the other error reported for this file first. The skipped checks will run once it is resolved."),
        ["I-TIME-001"] = new(
            "The one-day and 24-hour checks could not be decided",
            "One or more messages have a missing or unreadable time, so the program could not say whether the file passes the one-day and 24-hour checks. It neither passed nor failed.",
            "Ask whoever produced the file to correct the times listed in the finding, then scan again."),
        ["I-RUN-001"] = new(
            "The file was not checked because the scan stopped",
            "The scan was cancelled, or failed, before it reached this file. Nothing is known about it.",
            "Run the scan again and let it finish."),
    };

    public static PlainExplanation Plain(string ruleId) =>
        PlainHelp.TryGetValue(ruleId, out var p) ? p : new PlainExplanation("No plain-language explanation is available", "No explanation is available for this rule.", "See RULES.md.");

    public static IReadOnlyCollection<string> PlainRuleIds => PlainHelp.Keys;

    public static RuleExplanation Get(string ruleId) =>
        Help.TryGetValue(ruleId, out var h) ? h : new RuleExplanation("No explanation is available for this rule.", "See RULES.md.");

    public static bool Has(string ruleId) => Help.ContainsKey(ruleId);

    public static IReadOnlyCollection<string> RuleIds => Help.Keys;

    /// <summary>A plain-text block for one rule, used by the CLI and summary.txt.</summary>
    public static string Format(string ruleId, int wrap = 96)
    {
        var def = RuleCatalog.Get(ruleId);
        var help = Get(ruleId);
        var sb = new System.Text.StringBuilder();
        var plain = Plain(ruleId);
        sb.AppendLine($"Rule {RuleCatalog.Label(def.Id)}  [{def.Category}, {def.Severity.ToString().ToLowerInvariant()}]  {plain.Problem}");
        AppendWrapped(sb, "What it means: " + plain.Meaning, wrap);
        AppendWrapped(sb, "What to do:    " + plain.Action, wrap);
        var guide = RuleGuide.Get(ruleId);
        AppendWrapped(sb, "How checked:   " + guide.How, wrap);
        AppendWrapped(sb, "Why:           " + guide.Why, wrap);
        AppendWrapped(sb, "Origin:        " + RuleGuide.Origin(def), wrap);
        AppendWrapped(sb, "Read more:     " + RuleGuide.LinksText(ruleId), wrap);
        AppendWrapped(sb, "Technical:     Code " + def.Id + ". Checks that: " + def.Title + ". " + help.Meaning + " " + help.HowToFix, wrap);
        AppendWrapped(sb, "Source:        " + def.Source, wrap);
        return sb.ToString();
    }

    private static void AppendWrapped(System.Text.StringBuilder sb, string text, int wrap)
    {
        const string indent = "    ";
        var line = new System.Text.StringBuilder(indent);
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > indent.Length && line.Length + 1 + word.Length > wrap)
            {
                sb.AppendLine(line.ToString());
                line.Clear().Append(indent).Append("  ");
            }
            if (line.Length > indent.Length && line[^1] != ' ') line.Append(' ');
            line.Append(word);
        }
        sb.AppendLine(line.ToString());
    }
}
