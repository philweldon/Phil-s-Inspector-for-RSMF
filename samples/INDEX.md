# Sample files for Phil's Inspector for RSMF files

Two clean files, then one file per rule. Each rule file is the clean RSMF 2.0 file with a single
defect, so it raises that rule (plus any rule listed under "Also raised", which cannot be avoided).
Scan this folder with the time zone set to `America/New_York`.

The messages tell one running joke: Scrooge McDuck is accused of shady financial dealings, Bruce Wayne is
his general counsel, and a reporter named Clark Kent is asking questions. The cast is borrowed from
well-known cartoons and comics as a parody. Every message is made up, no real person appears, and the
characters belong to their owners, who have nothing to do with this app.

| File | Rule | Category | Severity | What is wrong | Also raised | Setting needed |
|---|---|---|---|---|---|---|
| `00-good-rsmf-2.0.rsmf` |  |  |  | Nothing. A valid RSMF 2.0 file: no findings. |  |  |
| `00-good-rsmf-1.0.rsmf` |  |  |  | Nothing. A valid RSMF 1.0 file: no findings. |  |  |
| `F-MIME-001-not-a-mime-message.rsmf` | F-MIME-001 | FORMAT | error | The file is random bytes, not a MIME message. | I-PARSE-001 |  |
| `F-MIME-002-no-version-header.rsmf` | F-MIME-002 | FORMAT | error | The X-RSMF-Version header is missing. |  |  |
| `F-MIME-003-unsupported-version.rsmf` | F-MIME-003 | FORMAT | error | X-RSMF-Version is 3.0.0, which is not a supported version. | Q-VER-001 |  |
| `F-MIME-004-version-header-not-first.rsmf` | F-MIME-004 | FORMAT | warning | X-RSMF-Version is present but is not the first header line. |  |  |
| `F-MIME-005-second-attachment.rsmf` | F-MIME-005 | FORMAT | error | The file has two attachments; only rsmf.zip is allowed. |  |  |
| `F-MIME-006-truncated-zip.rsmf` | F-MIME-006 | FORMAT | error | The rsmf.zip attachment is cut off half-way, so it is not a readable ZIP. | I-PARSE-001 |  |
| `F-MIME-007-wrong-extension.eml` | F-MIME-007 | FORMAT | warning | A valid RSMF saved with an .eml extension. A folder scan lists it as "not an RSMF candidate"; use Open RSMF File to see the finding. |  |  |
| `F-MIME-008-custom-header-name-too-long.rsmf` | F-MIME-008 | FORMAT | warning | A custom X-RSMF-* header whose name is 255 characters long. |  |  |
| `F-HDR-001-event-count-not-a-number.rsmf` | F-HDR-001 | FORMAT | warning | X-RSMF-EventCount is "three" instead of a whole number. |  |  |
| `F-HDR-002-event-count-mismatch.rsmf` | F-HDR-002 | FORMAT | warning | X-RSMF-EventCount says 99 but the manifest has 3 events. |  |  |
| `F-HDR-003-begin-date-mismatch.rsmf` | F-HDR-003 | FORMAT | warning | X-RSMF-BeginDate is an hour earlier than the earliest event. |  |  |
| `F-HDR-004-end-date-mismatch.rsmf` | F-HDR-004 | FORMAT | warning | X-RSMF-EndDate is an hour later than the latest event. |  |  |
| `F-HDR-005-v2-header-in-v1-file.rsmf` | F-HDR-005 | FORMAT | warning | An RSMF 1.0 file carrying the 2.0-only X-RSMF-Custodian header. |  |  |
| `F-ZIP-001-no-manifest-in-zip.rsmf` | F-ZIP-001 | FORMAT | error | rsmf.zip contains attachments but no rsmf_manifest.json. | I-PARSE-001 |  |
| `F-ZIP-002-unreferenced-file-in-zip.rsmf` | F-ZIP-002 | FORMAT | warning | rsmf.zip contains stray.txt, which nothing in the manifest refers to. |  |  |
| `F-MAN-001-manifest-not-valid-json.rsmf` | F-MAN-001 | FORMAT | error | rsmf_manifest.json is cut off and is not valid JSON. | I-PARSE-001 |  |
| `F-MAN-002-manifest-version-unsupported.rsmf` | F-MAN-002 | FORMAT | error | The manifest's version is 9.9.9. | Q-VER-001, I-PARSE-001 |  |
| `F-MAN-003-schema-violation.rsmf` | F-MAN-003 | FORMAT | error | The third event has no "type", which the schema requires. |  |  |
| `F-MAN-004-undefined-property.rsmf` | F-MAN-004 | FORMAT | warning | The first event has a property "attachment" (misspelt; the schema defines "attachments"). |  |  |
| `F-MAN-005-no-participants.rsmf` | F-MAN-005 | FORMAT | error | The manifest's participants list is empty. |  |  |
| `F-MAN-006-no-conversations.rsmf` | F-MAN-006 | FORMAT | error | The manifest's conversations list is empty. |  |  |
| `F-MAN-007-no-events.rsmf` | F-MAN-007 | FORMAT | error | The manifest's events list is empty. |  |  |
| `F-MAN-008-duplicate-participant-id.rsmf` | F-MAN-008 | FORMAT | error | Two participants share the id P2. |  |  |
| `F-MAN-009-duplicate-conversation-id.rsmf` | F-MAN-009 | FORMAT | error | Two conversations share the id C1. |  |  |
| `F-MAN-010-duplicate-event-id.rsmf` | F-MAN-010 | FORMAT | error | Two events in the same conversation share the id m1. |  |  |
| `F-MAN-011-conversation-without-type.rsmf` | F-MAN-011 | FORMAT | warning | The conversation has no "type" (direct or channel). |  |  |
| `F-MAN-012-reaction-too-long.rsmf` | F-MAN-012 | FORMAT | error | A reaction value is 31 characters long; the limit is 30. |  |  |
| `F-MAN-013-timestamp-not-iso-8601.rsmf` | F-MAN-013 | FORMAT | error | An edit's timestamp is "yesterday at noon". |  |  |
| `F-REF-001-conversation-participant-unknown.rsmf` | F-REF-001 | FORMAT | warning | The conversation lists participant P9, who is not defined. |  |  |
| `F-REF-002-event-participant-unknown.rsmf` | F-REF-002 | FORMAT | warning | The third event's participant is P9, who is not defined. |  |  |
| `F-REF-003-event-conversation-unknown.rsmf` | F-REF-003 | FORMAT | error | The third event's conversation is C9, which is not defined. |  |  |
| `F-REF-004-event-parent-unknown.rsmf` | F-REF-004 | FORMAT | warning | The third event's parent is an event id that does not exist. |  |  |
| `F-REF-005-custodian-not-in-conversation.rsmf` | F-REF-005 | FORMAT | warning | The conversation's custodian is P9, who is not one of its participants. |  |  |
| `F-REF-006-reaction-participant-unknown.rsmf` | F-REF-006 | FORMAT | warning | A reaction is attributed to participant P9, who is not defined. |  |  |
| `F-REF-007-edit-participant-unknown.rsmf` | F-REF-007 | FORMAT | warning | An edit is attributed to participant P9, who is not defined. |  |  |
| `F-REF-008-read-receipt-participant-unknown.rsmf` | F-REF-008 | FORMAT | warning | A read receipt is attributed to participant P9, who is not defined. |  |  |
| `F-REF-009-attachment-missing-from-zip.rsmf` | F-REF-009 | FORMAT | error | The third event refers to photo.png, but rsmf.zip does not contain it. |  |  |
| `F-REF-010-avatar-missing-from-zip.rsmf` | F-REF-010 | FORMAT | warning | A participant's avatar is avatar.png, but rsmf.zip does not contain it. |  |  |
| `F-REF-011-icon-missing-from-zip.rsmf` | F-REF-011 | FORMAT | warning | The conversation's icon is icon.png, but rsmf.zip does not contain it. |  |  |
| `L-EVT-001-10001-events.rsmf` | L-EVT-001 | LIMIT | error | The file has 10,001 events; the viewer limit is 10,000. |  |  |
| `Q-TIME-A-messages-on-two-calendar-days.rsmf` | Q-TIME-A | LOCAL | error | Two messages 20 minutes apart either side of midnight in New York: two calendar days, short span. |  | Time zone America/New_York |
| `Q-TIME-B-span-over-24-hours.rsmf` | Q-TIME-B | LOCAL | error | Messages 24 h 40 min apart on the 25-hour day when clocks go back in New York: one calendar day, but over 24 hours. |  | Time zone America/New_York |
| `Q-TIME-001-timestamps-without-offset.rsmf` | Q-TIME-001 | LOCAL | warning | Message timestamps have no UTC offset (read as UTC, with a warning). |  |  |
| `Q-PART-001-six-participants.rsmf` | Q-PART-001 | LOCAL | warning | The file has 6 participants. Only a finding when the participant warning threshold is set below 6. |  | Participant warning threshold 5 |
| `Q-PART-002-unspecified-sender.rsmf` | Q-PART-002 | LOCAL | warning | One participant's display name is the placeholder "Unspecified Sender". |  |  |
| `Q-PART-003-phone-number-only.rsmf` | Q-PART-003 | LOCAL | warning | One participant has a phone number as its display name and no account name or email. |  |  |
| `Q-VER-001-header-and-manifest-version-differ.rsmf` | Q-VER-001 | LOCAL | warning | X-RSMF-Version says 1.0.0 but the manifest says 2.0.0. |  |  |
| `I-TIME-001-message-without-timestamp.rsmf` | I-TIME-001 | INCOMPLETE | warning | The second message has no timestamp, so the time-window check is INDETERMINATE. |  |  |

## Rules without a sample file

| Rule | How to see it |
|---|---|
| L-SIZE-001 | Needs a file over the size threshold (2 GB by default). To see it, scan any sample with the size threshold set to 0.000001 GB (about 1,000 bytes). |
| I-FILE-001 | Raised when a file cannot be opened. To see it, open a sample in another program that locks it (or remove your read permission) and scan. |
| I-SCAN-001 | Raised when a folder cannot be listed. To see it, scan a folder containing a subfolder you have no permission to open. |
| I-ZIP-001 | Raised when the manifest is larger than the in-memory cap (512 MB). No practical sample file. |
| I-PARSE-001 | Has no file of its own: it accompanies F-MIME-001, F-MIME-006, F-ZIP-001, F-MAN-001 and F-MAN-002 to say which later checks were skipped. |
| I-RUN-001 | Raised for files not yet checked when a scan is cancelled. To see it, cancel a scan part-way. |
