# Phil's Inspector for RSMF files: rules

Ruleset version: **1.2.0** (changes only when this file changes).

Sources. The rules were written from copies of these pages and repositories saved on 2026-11-18;
the saved copies are kept by the author and are not part of the public repository:

| ID | Source | URL |
|----|--------|-----|
| SPEC | RSMF specification | https://help.relativity.com/RelativityOne/Content/System_Guides/Relativity_Short_Message_Format/Relativity_short_message_format.htm |
| CREATE | Creating an RSMF file | https://help.relativity.com/RelativityOne/Content/System_Guides/Relativity_Short_Message_Format/Creating_an_RSMF_File.htm |
| PROC | Processing an RSMF file | https://help.relativity.com/RelativityaiR/Content/System_Guides/Relativity_Short_Message_Format/Processing_an_RSMF_file.htm |
| SLICE | RSMF slicing reference | https://help.relativity.com/RelativityaiR/Content/Relativity/Processing/SupportedFileTypes-RSMFSlicingReference.htm |
| DEDUP | Deduplication considerations | https://help.relativity.com/RelativityaiR/Content/Relativity/Processing/Deduplication_considerations.htm |
| VLRW | Very large workspaces | https://help.relativity.com/RelativityaiR/Content/Solutions/Workflow_solutions_for_very_large_workspaces.htm |
| VALID | Validator samples repo @ c717cd3 (schema 2.0.0, walkthrough PDF); schema 1.0.0 @ ce90d80 | https://github.com/relativitydev/rsmf-validator-samples |
| GEN | Generation sample repo @ eccd83b | https://github.com/relativitydev/relativity-rsmf-gen-sample |
| BRIEF | Project brief (owner's QC rules) | not published |

Categories: **FORMAT** (spec violation), **LIMIT** (documented product limit), **LOCAL** (owner's
QC rules), **INCOMPLETE** (check could not be finished). Severities: error, warning, info.

## Rule numbers

The app and the reports show each rule by number. The letter codes used in the tables below are the
original identifiers; they remain in `findings.jsonl` (`ruleId`) and in the `rule_code` column of the
CSV reports. Numbers are permanent: a rule keeps its number, and a new rule takes the next one.

| No. | Code | Problem reported |
|-----|------|------------------|
| 001 | F-MIME-001 | File cannot be read as an RSMF file |
| 002 | F-MIME-002 | Version label is missing |
| 003 | F-MIME-003 | Version label is not one Relativity documents |
| 004 | F-MIME-004 | Version label is not on the first line |
| 005 | F-MIME-005 | The file does not hold exactly one message package |
| 006 | F-MIME-006 | The message package inside the file is damaged |
| 007 | F-MIME-007 | File name does not end in .rsmf |
| 008 | F-MIME-008 | A custom label has a name that is too long |
| 009 | F-HDR-001 | A summary value at the top of the file is the wrong kind |
| 010 | F-HDR-002 | Stated number of events does not match the content |
| 011 | F-HDR-003 | Stated start date does not match the first message |
| 012 | F-HDR-004 | Stated end date does not match the last message |
| 013 | F-HDR-005 | File says version 1.0 but uses a version 2.0 feature |
| 014 | F-ZIP-001 | The list of messages is missing from the file |
| 015 | F-ZIP-002 | The file contains an item that no message refers to |
| 016 | F-MAN-001 | The list of messages cannot be read |
| 017 | F-MAN-002 | The list of messages does not state a valid version |
| 018 | F-MAN-003 | Content breaks Relativity's published format rules |
| 019 | F-MAN-004 | Content includes a field Relativity's format does not define |
| 020 | F-MAN-005 | No participants are listed |
| 021 | F-MAN-006 | No conversations are listed |
| 022 | F-MAN-007 | The file contains no messages or events |
| 023 | F-MAN-008 | Two participants share the same ID |
| 024 | F-MAN-009 | Two conversations share the same ID |
| 025 | F-MAN-010 | Two messages in one conversation share the same ID |
| 026 | F-MAN-011 | A conversation does not say whether it is a direct chat or a channel |
| 027 | F-MAN-012 | A reaction's name is too long |
| 028 | F-MAN-013 | A date and time is not written in the standard way |
| 029 | F-REF-001 | A conversation lists someone who is not in the participant list |
| 030 | F-REF-002 | A message's sender is not in the participant list |
| 031 | F-REF-003 | A message belongs to a conversation that does not exist |
| 032 | F-REF-004 | A reply answers a message that is not in this file |
| 033 | F-REF-005 | The custodian is not a member of the conversation |
| 034 | F-REF-006 | A reaction is credited to someone not in the participant list |
| 035 | F-REF-007 | An edit is credited to someone not in the participant list |
| 036 | F-REF-008 | A read receipt is credited to someone not in the participant list |
| 037 | F-REF-009 | A message's attachment is missing from the file |
| 038 | F-REF-010 | A participant's picture is missing from the file |
| 039 | F-REF-011 | A conversation's icon is missing from the file |
| 040 | L-SIZE-001 | File is larger than the size limit |
| 041 | L-EVT-001 | File has more than 10,000 messages and events |
| 042 | Q-TIME-A | Messages fall on more than one calendar day |
| 043 | Q-TIME-B | Messages span more than 24 hours |
| 044 | Q-TIME-001 | Message times do not say which time zone they are in |
| 045 | Q-PART-001 | More participants than the limit you set |
| 046 | Q-PART-002 | A participant is called "Unspecified Sender" |
| 047 | Q-PART-003 | A participant shows only as a phone number |
| 048 | Q-VER-001 | The file gives two different version numbers |
| 049 | I-FILE-001 | The file could not be opened |
| 050 | I-SCAN-001 | A folder could not be opened |
| 051 | I-ZIP-001 | Part of the file was too large to check |
| 052 | I-PARSE-001 | Some checks were skipped because of an earlier problem |
| 053 | I-TIME-001 | The one-day and 24-hour checks could not be decided |
| 054 | I-RUN-001 | The file was not checked because the scan stopped |

## FORMAT — outer file (MIME layer)

| ID | Sev | What is checked | Expected | Source |
|----|-----|-----------------|----------|--------|
| F-MIME-001 | error | File parses as an Internet Message Format / MIME message | Parseable headers and MIME structure | CREATE §RSMF file support in Processing; SPEC §Overview |
| F-MIME-002 | error | `X-RSMF-Version` header present | Present, non-blank | SPEC §Header; CREATE §support (2); PROC §Checklist |
| F-MIME-003 | error | `X-RSMF-Version` value is a supported version | `1.0.0` or `2.0.0` (versions with a published schema) | SPEC §Header, §Example; VALID schemas |
| F-MIME-004 | warning | `X-RSMF-Version` is the first header line | First header in the file | SPEC §Header; CREATE §support (2) ("should") |
| F-MIME-005 | error | Number of attachments that are base64-encoded with `Content-Disposition: attachment; filename="rsmf.zip"` | Exactly one; no other attachment parts | CREATE §support (3); VALID PDF Sample 3 |
| F-MIME-006 | error | The `rsmf.zip` part decodes from base64 and opens as a ZIP archive | Valid base64, readable ZIP central directory | CREATE §support (3); SPEC §RSMF structure |
| F-MIME-007 | warning | File name extension | `.rsmf` (case-insensitive) | CREATE §support (1) |
| F-MIME-008 | warning | Length of the custom part of any non-standard `X-RSMF-*` header name | Fewer than 255 characters | PROC §RSMF Header |
| F-HDR-001 | warning | `X-RSMF-EventCount`, `X-RSMF-AttachmentCount` parse as non-negative whole numbers; `X-RSMF-BeginDate`, `X-RSMF-EndDate` parse as ISO 8601 | Parseable | SPEC §Header; PROC §RSMF Header (field types) |
| F-HDR-002 | warning | `X-RSMF-EventCount` vs number of events in the manifest | Equal | SPEC §Header; GEN `RSMFGenerator.cs` (header = `events.Count`) |
| F-HDR-003 | warning | `X-RSMF-BeginDate` vs earliest event timestamp in the manifest | Same instant | SPEC §Header; GEN `RSMFGenerator.cs` |
| F-HDR-004 | warning | `X-RSMF-EndDate` vs latest event timestamp in the manifest | Same instant | SPEC §Header; GEN `RSMFGenerator.cs` |
| F-HDR-005 | warning | 2.0-only header (`Application`, `Custodian`, `Participants`, `AttachmentCount`, `EventCollectionID`) in a file whose version is 1.0.0 | Absent in 1.0 files | SPEC §Header table |

## FORMAT — ZIP and manifest

| ID | Sev | What is checked | Expected | Source |
|----|-----|-----------------|----------|--------|
| F-ZIP-001 | error | `rsmf_manifest.json` exists at the ZIP root | Present at root | SPEC §RSMF structure |
| F-ZIP-002 | warning | ZIP member (other than the manifest) not referenced by any attachment id, avatar or icon | Every member referenced | PROC §RSMF.zip, §Troubleshooting attachments |
| F-MAN-001 | error | Manifest is well-formed JSON with an object root | Parseable JSON object | SPEC §rsmf_manifest.json |
| F-MAN-002 | error | Manifest `version` is a supported version | `1.0.0` or `2.0.0` | SPEC §rsmf_manifest.json; VALID schemas |
| F-MAN-003 | error | Manifest validates against the bundled schema for its version (one finding per violation) | No schema violations | VALID `rsmf_schema_1_0_0.json` / `rsmf_schema_2_0_0.json`; VALID PDF Intro, Samples 1 and 4 |
| F-MAN-004 | warning | Property not defined by the schema for the manifest's version (includes 2.0-only fields in a 1.0 manifest) | Only defined properties; extras go in `custom` | VALID PDF Sample 4 (ManifestUndefinedProperty); SPEC field tables |
| F-MAN-005 | error | `participants` has at least one entry | ≥ 1 | SPEC §Participants |
| F-MAN-006 | error | `conversations` has at least one entry | ≥ 1 | SPEC §Conversations |
| F-MAN-007 | error | `events` has at least one entry | ≥ 1 | SPEC §Events |
| F-MAN-008 | error | Participant `id` values are unique in the manifest | No duplicates | SPEC §Participants (id) |
| F-MAN-009 | error | Conversation `id` values are unique in the manifest | No duplicates | SPEC §Conversations (id) |
| F-MAN-010 | error | Event `id` values (where present) are unique within their conversation | No duplicates | SPEC §Events (id) |
| F-MAN-011 | warning | Conversation `type` present in a 2.0.0 manifest (SPEC requires it; the 2.0.0 schema does not) | `direct` or `channel` | SPEC §Conversations (type) |
| F-MAN-012 | error | Reaction `value` length (the schemas express this with a keyword that has no effect on strings) | ≤ 30 characters | SPEC §Reaction (value) |
| F-MAN-013 | error | Event, edit and read-receipt `timestamp` values are ISO 8601 date-times | Parseable ISO 8601 date-time | SPEC §Events, §Edits, §Read_receipts; VALID schemas (`format: date-time`) |

## FORMAT — references

| ID | Sev | What is checked | Expected | Source |
|----|-----|-----------------|----------|--------|
| F-REF-001 | warning | Each conversation `participants` entry | Id of a root participant | SPEC §Conversations; VALID PDF Sample 4 (ManifestInvalidConversationParticipant, warning) |
| F-REF-002 | warning | Event `participant` | Id of a root participant | SPEC §Events; VALID PDF Sample 4 (ManifestInvalidEventParticipant, warning) |
| F-REF-003 | error | Event `conversation` | Id of a conversation | SPEC §Events (conversation) |
| F-REF-004 | warning | Event `parent` | Id of another event in the same conversation | SPEC §Events (parent); VALID schema 2.0.0 (parent description); SLICE §Orphaned messages |
| F-REF-005 | warning | Conversation `custodian` | One of that conversation's participants | SPEC §Conversations (custodian) |
| F-REF-006 | warning | Reaction `participants` entries | Ids of root participants | SPEC §Reaction |
| F-REF-007 | warning | Edit `participant` | Id of a root participant | SPEC §Edits |
| F-REF-008 | warning | Read-receipt `participant` | Id of a root participant | SPEC §Read_receipts |
| F-REF-009 | error | Event attachment `id` | Name of a file in rsmf.zip | SPEC §Attachment; PROC §Troubleshooting attachments |
| F-REF-010 | warning | Participant `avatar` | Name of a file in rsmf.zip | SPEC §Participants; VALID PDF Sample 2 (warning) |
| F-REF-011 | warning | Conversation `icon` | Name of a file in rsmf.zip | SPEC §Conversations (icon) |

## LIMIT

| ID | Sev | What is checked | Expected | Source |
|----|-----|-----------------|----------|--------|
| L-SIZE-001 | error | Size of the .rsmf file in bytes vs the size threshold (default 2,147,483,648; configurable; threshold stated in every report) | Size ≤ threshold | PROC §Checklist, §Troubleshooting; SLICE §Splitting to meet RSMF file limits; VLRW §RSMF; BRIEF Phase 1 Limits |
| L-EVT-001 | error | Total events in the manifest (all event types) | ≤ 10,000 | PROC §Checklist; SLICE §Splitting to meet RSMF file limits; CREATE §creation process; VLRW §RSMF |

Participant count is reported as a metric. No vendor participant cap is enforced: VLRW §RSMF
gives no number.

## LOCAL

| ID | Sev | What is checked | Expected | Source |
|----|-----|-----------------|----------|--------|
| Q-TIME-A | error | Calendar day: every message-event timestamp, converted from its own offset to the selected IANA zone (default: UTC), falls on one local date. Skipped in elapsed-only mode | One calendar day | BRIEF Phase 1 Time-window QC, Rule A |
| Q-TIME-B | error | Elapsed span: latest minus earliest message-event timestamp | ≤ 24 h (exactly 24 h passes) | BRIEF Phase 1 Time-window QC, Rule B |
| Q-TIME-001 | warning | Message-event timestamp has no UTC offset; it is treated as UTC | Explicit offset or `Z` | BRIEF Phase 1 Time-window QC |
| Q-PART-001 | warning | Unique participant count vs the user-set warning threshold (off unless set) | ≤ threshold | BRIEF Phase 1 Limits; VLRW §RSMF |
| Q-PART-002 | warning | A participant's `display`, `account_id` or `id` contains the placeholder "Unspecified Sender" (any letter case). One finding per participant | A real participant name | Owner request 2026-11-19 |
| Q-PART-003 | warning | A participant is identified by a phone number only: no `email`, and `display` and `account_id` are each blank or a phone number, with a phone number in `display`, `account_id` or (failing those) `id`. A phone number is 7 to 15 digits with only spaces and `+ - ( ) . /` between them, optionally after `tel:` or before an extension. One finding per participant | A display name, account name or email address | Owner request 2026-11-19 |
| Q-VER-001 | warning | `X-RSMF-Version` header vs manifest `version` | Equal | BRIEF Phase 1 Parsing; GEN `RSMFGenerator.cs` (header copied from manifest) |

A single message, or messages covering part of a day, pass A and B. Both are evaluated per
file; per-conversation earliest, latest and span are reported without findings.

## INCOMPLETE

| ID | Sev | What is checked | Expected | Source |
|----|-----|-----------------|----------|--------|
| I-FILE-001 | error | File could not be opened or read (access denied, I/O error, locked) | Readable | BRIEF Phase 1 Folder scan |
| I-SCAN-001 | error | Directory could not be listed; scan coverage marked incomplete | Listable | BRIEF Phase 1 Folder scan |
| I-ZIP-001 | warning | A ZIP member (manifest or inline image) exceeded the in-memory size cap, so checks needing its content were not run | Member within cap | BRIEF Phase 1 Folder scan |
| I-PARSE-001 | info | Checks skipped because an earlier layer failed (e.g. manifest rules when the ZIP is damaged); lists the skipped rule groups | All layers parse | BRIEF Phase 1 Folder scan, Audit output |
| I-TIME-001 | warning | Time-window check is INDETERMINATE: one or more message events have a missing or unparseable timestamp; lists event locations | All message timestamps parseable | BRIEF Phase 1 Time-window QC |
| I-RUN-001 | info | Scan was cancelled before this file was checked | Run completes | BRIEF Phase 1 Folder scan |

## Conflicts between sources

| # | Topic | SPEC says | Schema says | Handling |
|---|-------|-----------|-------------|----------|
| C1 | 2.0 conversation `participants` | Optional | Required (2.0.0 schema) | Schema applies: absence is F-MAN-003 (error). VALID PDF states 2.0.0 manifests must adhere to the schema |
| C2 | 2.0 conversation `type` | Required | Not required | Absence is F-MAN-011 (warning) |
| C3 | Event `type` values | message, disclaimer, join, leave | 2.0.0 schema also allows `history`, `unknown` | Schema applies: both accepted in 2.0.0 (SPEC §RSMF 2.0 functionality mentions history events) |
| C4 | BRIEF vs sources: "2.0 headers" | BRIEF asks for count/date comparison "where the spec gives a comparable meaning" | Only EventCount, BeginDate, EndDate are comparable | AttachmentCount and Participants are reported, not compared (see Unverified 5, 6) |

## Unverified

Not settled by the saved sources. Each is implemented as stated here, or not implemented, and
reported as unverified in the user guide.

1. **Short version strings.** Whether Relativity accepts `1.0` / `2.0` in the header or manifest. Both schemas require `version` length ≥ 5 and every example uses `x.y.z`. Implemented: only `1.0.0` and `2.0.0` are supported values.
2. **Exact 2 GB figure and what it measures.** Sources say "greater than 2 GB" without a byte value, and DEDUP says "2 GB of attachments" for converted data. Implemented: .rsmf file size on disk, finding when size > threshold, default 2,147,483,648 (from BRIEF).
3. **EventCount semantics.** SPEC says "short message events"; PROC maps the header to `Rsmf/MessageCount`; GEN writes the count of all events. Implemented: compared with the count of all events.
4. **10,000 limit unit.** PROC/SLICE/VLRW say events; CREATE once says "message" cap. Implemented: all events; 10,000 passes, 10,001 fails.
5. **`X-RSMF-AttachmentCount` meaning.** "Sum of all attachments present" could mean attachment references or ZIP files. Not compared; value reported.
6. **Header participants vs manifest participants.** SLICE says Relativity warns when they differ but not which header (`To`, `From`, `X-RSMF-Participants`) or how names are matched. Not implemented.
7. **Participant cap.** No number published. Count reported; only the optional LOCAL threshold applies.
8. **Severities.** Sources give severity only for: schema violation and extra attachment (error), missing avatar, invalid conversation/event participant, undefined property (warning). All other severities above are this tool's assignments.
9. **Timestamp profile.** Schemas say `format: date-time` yet their own examples omit the offset. Which ISO 8601 variants Relativity accepts is not stated. Implemented: ISO 8601 extended date-time, optional fractional seconds, optional `Z`/offset; offset-less is accepted with Q-TIME-001.
10. **`custom` value type.** Schemas require a non-empty string; their description text mentions numbers and booleans. Implemented: schema applies.
11. **Email format.** Schemas mark `email` as `format: idn-email`; whether Relativity enforces it is unknown. Not a finding; shown as "invalid" in metadata coverage only.
12. **ZIP name matching.** Case sensitivity, and whether referenced files may sit in subfolders, are not stated. Implemented: exact, case-sensitive match on the full entry name.
13. **Icon size (≤ 28×28 px).** A recommendation only; not checked.
14. **First-header rule strictness.** "Should" in both sources; implemented as a warning.
15. **Deduplication.** DEDUP describes Relativity's hashing; this tool does not predict duplicates. `X-RSMF-DuplicatedEventCount` is recognised as a known header.
