# Phil's Inspector for RSMF files: user guide

Phil's Inspector for RSMF files checks Relativity Short Message Format (`.rsmf`) files on your own PC before you
send them to Relativity. It reads files; it never changes them.

**It performs local QC only. It does not certify files and cannot guarantee that Relativity
will ingest them.** Passing every check here means the file met the rules listed in this guide,
nothing more.

Phil's Inspector for RSMF files is free and open source under the MIT License, copyright (c) 2026 Philip Weldon
(see `LICENSE.txt` in the package; other components are listed in `THIRD-PARTY-NOTICES.md`).
It is an independent project, not affiliated with or endorsed by Relativity.

## 1. Running it

1. Unzip the package into any folder you can write to (for example `Documents\RsmfInspector`).
   Nothing is installed, nothing is written to the registry, and the app makes no network calls.
2. Double-click `RsmfInspector.exe` for the window. The same file is also the command-line
   tool (see "The command line"); there is no separate command-line program any more.

Windows may show a SmartScreen prompt the first time, because the program is not code-signed.

### The window

1. **Settings.** Check these before each scan; they are locked while a scan runs.
   - *Time zone for calendar-day rule*: an IANA zone such as `America/New_York`. Defaults to
     `UTC`, so results are the same on every PC until you choose a zone.
   - *Time-window mode*: Rules A + B (default), or Rule B only.
   - *Size threshold (GB)*: default 2. Decimals such as 1.5 are allowed. 1 GB is counted as
     1,073,741,824 bytes, so 2 GB is 2,147,483,648 bytes.
   - *Participant warning threshold*: blank (default) reports the count without a warning.
   - *Report folder (optional)*: leave it blank. The reports then go into a new folder **beside
     the folder you scan** (in the folder that contains it), named after it:
     `<scanned folder>-rsmf-report-<date>-<time>-<id>`. Nothing is written inside the scanned
     folder. For a single file, the report folder goes beside the folder that holds the file.
     If that place cannot be written to (or you scan a whole drive), the app asks you to pick a
     folder. Fill the box in only if you want reports somewhere else; they then go into a
     `run-...` folder there.
   - *Include message bodies and personal values in reports*: off by default, and never
     remembered.
   - *Remember these settings on this PC*: off by default. When ticked, the time zone, mode,
     thresholds and output folder are saved to `RsmfInspector.settings.json` in your local
     application data folder (`%LOCALAPPDATA%\RsmfInspector`) each time a scan starts, and read
     back at the next start. Untick it to delete that file. (Versions up to 1.22.0 kept the file
     beside the program; one found there is still read, and is deleted when you untick the box.)
     Nothing else is ever written outside the report folder.
2. **Scan Folder...** checks every `.rsmf` file in a folder and its subfolders. The button makes
   a soft click (Windows' own "Navigation Start" sound, at your system volume; silent if
   Windows sounds are muted or that sound file is not on the PC).
   **Open RSMF File...** checks one file and opens it in the viewer. The viewer shows messages
   as rounded chat bubbles: blue on the right for messages the file marks as outgoing or as sent
   by the conversation's custodian, green on the left for everyone else, each with the sender
   and time on it. RSMF files write emoji as short names (`:smile:` in text, `thumbsup` as a
   reaction); known names are shown as the emoji, and unknown ones are left as written.
   Emoji in messages and reactions are shown in colour, in the viewer and in
   the PDFs, using Windows' own emoji font (nothing is bundled). Two limits come from that font
   and the text engine: country flags appear as their two letters, because Windows' emoji font
   has no flags, and emoji built from several joined people (families, couples) appear as the
   separate people side by side. Sender names, conversation titles, notices and attachment
   names show colour emoji too. On a PC without the colour emoji font, emoji fall back to
   black outlines.
   You can also drop a folder or a single `.rsmf` file onto the window. If you drop several
   items, the first folder (or, with no folder, the first file) is used.
   **Cancel** stops a scan; reports are still written for what was done.
   When a folder scan finishes, its `summary.txt` opens in Notepad so you see the result straight
   away. (After **Open RSMF File...** the viewer opens instead.) All reports are plain `.txt`,
   `.csv`, `.jsonl` and `.json` files.
3. **Results.** Summary cards show the headline figures. The **Files** tab has one row per
   file found, with status, metrics and finding counts; use the category and severity boxes to
   show only files with matching findings, and double-click a row to open the viewer. The
   **Findings by rule** tab lists every rule that fired across the whole scan, most serious
   and most widespread first; select a rule to read what it means and see every affected
   file, and double-click one to open that file at the affected record.
4. **Viewer.** Conversations on the left; messages oldest first as chat bubbles with sender,
   time (in the selected time zone), text and attachment names. Type in the search box
   (Ctrl+F) to highlight matching text; Enter and Shift+Enter, or Next and Previous, step
   between matching messages, and "Only matches" hides the rest. Other tabs: Headers, Extracted text, Raw
   manifest, Findings and Metadata coverage. Select a finding to read what it means and what to do
   about it; double-click a finding, or an affected record under Metadata coverage, to jump to
   that message.
   - PNG, JPEG and GIF attachments inside the file are shown inline. Every other attachment is
     listed by name only. Nothing is ever opened or run, and all content is shown as plain text.
   - If a file is damaged, a yellow banner states what could not be read; whatever parsed
     safely is still shown.

### The command line

`RsmfInspector.exe` is also the command-line tool. Started with `scan`, `explain`, `--version`
or `--help` it runs the command and exits without opening a window.

```
RsmfInspector scan <folder-or-file> --out <output-folder> [options]

  --tz <IANA zone>             time zone for the calendar-day rule (default: UTC)
  --mode both|elapsed-only     time-window mode (default: both)
  --size-threshold <GB>        default 2; decimals allowed (before 1.8.0 this was in bytes)
  --participant-threshold <n>  default: off
  --parallel <n>               files inspected at the same time, 1 to 16 (default: automatic, up to 4)
  --include-sensitive          include message bodies and personal values in reports
  --quiet                      no progress lines
RsmfInspector explain [<rule number>]   plain-language help for one rule, or for all of them
RsmfInspector explain --csv         the same for every rule, as a CSV table
RsmfInspector --version
```

Exit codes: 0 no error findings; 1 error findings or exceptions; 2 cancelled or failed;
64 bad arguments.

Because one file is both a window program and a command-line tool, Windows treats it as a
window program, which has two effects:

- **In a batch file (`.cmd`)** it behaves like any command-line tool: the script waits for it,
  and `%ERRORLEVEL%` holds the exit code.
- **Typed at a Command Prompt** the prompt comes back at once and the output appears after it.
  To make the prompt wait, run `start /wait RsmfInspector.exe scan ...`. In PowerShell, add
  `| Out-Host` to the end of the line.
- Sending the output to a file (`> result.txt`) works in every case.

### Converting a folder to PDF

**Convert RSMF Folder to PDF...** makes one PDF for each `.rsmf` file in the folder you choose
(subfolders included). It asks you to confirm first, because the PDFs contain the conversations.

- The PDFs go to a new folder beside the folder you chose, named
  `<chosen folder>-pdf-<date>-<time>-<id>` (or `pdf-...` inside the report folder, if you filled
  that box in), with the
  same subfolder layout and file names as the source (`chat.rsmf` becomes `chat.pdf`).
- **Nothing that is not in the RSMF file is added to a page.** There is no header, footer, page
  number, file name, tool name, date, note or count on the pages, and the PDF carries no title,
  author or other document information. `pdf_summary.txt` and `pdf_conversion_log.csv` are the
  only record of the conversion.
- Pages are US Letter. Each conversation starts a new page with its name, platform and type as
  given in the file. Each message is a bubble with the sender, the timestamp exactly as written
  in the file (not converted to your time zone), the text, inline pictures, attachment names
  and reactions. Events that are not messages show the sender, the file's event type (join,
  leave, ...), any text and the timestamp. The words `parent`, `deleted`, `reactions` and
  `edits` are RSMF's own field names, used as labels; the number after `edits` is how many
  edits the file records. Bubbles are blue on the right for messages the file marks as outgoing or as sent by the
  conversation's custodian, and green on the left for everyone else, with white text. If the file
  marks neither, every bubble is green. Colour and placement are presentation only.
- **The PDFs contain message text, participant names and inline pictures.** This is different
  from the QC reports, which leave those out by default. Handle the PDFs like the source files.
- Every page is a picture of the conversation, so the text in a PDF cannot be searched or
  copied. Long conversations make large files: roughly 0.05 to 0.2 MB per page, more for pages
  with photographs.
- Attachments other than inline pictures (PNG, JPEG, GIF) are listed by name only. Nothing is
  opened or run.
- A file whose conversations cannot be read (damaged ZIP, missing manifest) is not converted.
  `pdf_conversion_log.csv` in the folder lists every source file with its status, page count
  and the reason when it was not converted; `pdf_summary.txt` gives the totals and settings.
- **Cancel** stops after the current page; finished PDFs are kept and the log says which files
  were not reached.
- These are renderings for review made by this tool. They are not certified copies and say
  nothing about how Relativity or any other system would display or accept the files. The pages
  do not say this themselves; `pdf_summary.txt` does.
- The command-line tool does not make PDFs.

### Rule guide

The **Rule guide** tab lists every rule the app has. It needs no scan. Select a rule to read what it
flags, how the app checks it, why it matters, what to do when it appears, and where it comes from.
Type in the search box to find rules by any word or by rule number, or choose a category.

- Rules based on Relativity's documentation show a link to the page, with the part of the page to
  look for. Clicking a link opens that page in your web browser; the address is shown under the
  link so you can copy it instead. The app itself still makes no network connection, and it only
  ever opens pages on help.relativity.com or Relativity's own GitHub account.
- Rules that are this app's own QC checks say so, and have no Relativity page.
- In the Findings by rule tab and in the viewer's Findings tab, the link "How and why this rule is
  checked" opens the guide at that rule.

The same text is in `RULE_GUIDE.csv` (columns `how_it_is_checked`, `why_it_matters` and
`documentation`) and in the `explain` command.

### Sample files

The package includes a `samples` folder: two clean files and one file for each rule that a
file's content can trigger, each with a single defect. `samples\INDEX.md` lists every file
with the rule it raises and what is wrong with it. Click **Try the samples** to scan them
(the reports go beside the `samples` folder, or into `Documents\RsmfInspector` when that place
cannot be written to, as in a Microsoft Store install). That scan uses the time zone America/New_York and a
participant warning threshold of 5, because the samples are built for those settings; your
other settings are used as they are. The samples tell a running joke with a cast borrowed from
well-known cartoons and comics; every message is made up and no real person appears.

## 2. What a scan does

- Looks in the folder and all subfolders. Files ending `.rsmf` (any letter case) are checked.
  Other files are listed as "not an RSMF candidate". Shortcuts, symbolic links and junctions are
  listed but not followed.
- A folder that cannot be listed, or a file that cannot be read or is malformed, is recorded
  as an exception; the scan carries on and the run is marked **coverage incomplete**.
- Up to four files are checked at the same time (fewer on a PC with fewer processors; files of
  64 MB or more are checked alone). The reports come out the same whatever the number.
- Source files are opened read-only. The only things written are the report files in your
  output folder (and, for archives over 128 MB, a temporary working file inside that run
  folder which is deleted when the file is closed).

## 3. Reports

Each run writes a new folder `run-<yyyyMMdd-HHmmss>-<id>`, even if the run failed or was
cancelled. Paths inside the reports are relative to the folder you scanned.

| File | Contents |
|---|---|
| `summary.txt` | Plain-language overview: settings and thresholds used, file counts, findings by severity, category and rule, time-window results, exceptions and skipped checks, and for each rule that fired what it means and what to do. |
| `inventory.csv` | One row per file found: kind, status, size, version, event/message/conversation/participant counts, finding counts, time-window status, earliest and latest message, span, skipped checks. |
| `findings.csv` | One row per finding, led by the file name: file name, path, severity, rule ID, the problem in plain words, observed value, expected value, location and record identifier. Opens in Excel; this is the file to send to whoever produced the RSMF files. |
| `rule_guide.csv` | One row for each rule that fired in this run, most serious and most widespread first: the problem, what it means, what to do, the technical detail, and how many files and findings it covers. Send it with `findings.csv` so the recipient can read what each rule ID means. |
| `findings.jsonl` | The same findings for other programs. One finding per line: path, rule ID, category, severity, message, observed value, expected value, source reference, location (header name or JSON path) and record identifier. |
| `metadata_coverage.csv` | For each expected header and manifest field (file, participant, conversation, event level): how many records are missing, blank, populated or invalid, with the identifiers of affected records (first 25). Custom fields found are listed with requirement `custom`. |
| `time_window_exceptions.csv` | Files that fail the time-window rules or are indeterminate: path, conversation IDs, earliest and latest message, span, time zone, status, reason. |
| `conversation_metrics.csv` | Per conversation: events, message events, participants, earliest and latest message, span. |
| `run.json` | Run ID, start and end time, input path, app and ruleset versions, every setting and threshold, files discovered and checked, findings by severity and category, skipped or failed checks with reasons, final status. |

**Blank** means null, an empty or whitespace-only string, or an empty list. Zero and `false`
count as populated. **Invalid** means the wrong type, a value outside the allowed list, an
unparseable timestamp or count, or an email address without a valid shape.

### What is and is not written by default

Written: record identifiers (participant, conversation and event `id` values, attachment and
avatar file names), counts, versions, timestamps and custom field *names*.

Not written unless you tick the include option (or pass `--include-sensitive`): message
bodies, display names, email addresses, account IDs, custom field values and the values of
free-text headers (To, From, Subject, Custodian, Participants).

If your participant or conversation IDs are themselves personal data (for example phone
numbers), treat the reports accordingly.

## 4. Rules

Every finding has a category:

- **FORMAT**: the file breaks the published RSMF specification or schema.
- **LIMIT**: the file exceeds a limit Relativity documents.
- **LOCAL**: the file breaks one of this organisation's own QC rules.
- **INCOMPLETE**: a check could not be finished, so the result is not a pass.

The full table, with the source of each rule, is in `RULES.md` (ruleset 1.0.0).

### Official rules implemented

Based on Relativity's published specification, schemas and processing documentation:

- The file is a parseable MIME message with an `X-RSMF-Version` header, a supported version
  (1.0.0 or 2.0.0), the version header first, and exactly one base64 attachment named
  `rsmf.zip` that opens as a ZIP.
- `rsmf_manifest.json` is at the ZIP root, is valid JSON, and validates against Relativity's
  published schema for its version. Properties the schema does not define are reported.
- At least one participant, conversation and event; unique IDs; reaction values of 30
  characters or fewer; ISO 8601 timestamps.
- Every reference resolves: conversation, event, reaction, edit and read-receipt participants;
  event conversations and parents; conversation custodians; attachments, avatars and icons to
  files in the ZIP. ZIP files that nothing refers to are reported.
- Optional headers: counts and dates must be parseable; `X-RSMF-EventCount`,
  `X-RSMF-BeginDate` and `X-RSMF-EndDate` are compared with the manifest; RSMF 2.0-only headers
  in a 1.0 file are reported.
- Limits: file larger than the size threshold (Relativity: files over 2 GB are not supported);
  more than 10,000 events (Relativity's viewer limit).

### Local rules

- **Rule A, calendar day (rule 042):** all message timestamps fall on one calendar day in the
  selected time zone. Each timestamp is converted from its own offset. A day with a
  daylight-saving change is still one day.
- **Rule B, elapsed span (rule 043):** latest message minus earliest is 24 hours or less.
  Exactly 24 hours passes.
- A and B are separate findings. Elapsed-only mode checks B alone. Only events of type
  `message` count. A single message, or part of a day, passes.
- A timestamp without an offset is read as UTC and raises a warning (rule 044).
- A message with a missing or unparseable timestamp makes the result **INDETERMINATE** for
  that file (rule 053), with the affected events listed.
- Optional participant-count warning (rule 045), and a warning when the version header and
  the manifest version differ (rule 048).
- **Placeholder sender (rule 046):** a warning for each participant whose display name,
  account name or id contains "Unspecified Sender" (any letter case).
- **Phone number only (rule 047):** a warning for each participant identified by a phone
  number alone: no email address, and nothing in the display name or account name except a
  phone number. A participant with a real name, an account name or an email address is not
  flagged, even if a phone number is also present. The number itself is not written to the
  reports unless you tick the include box.
- Every finding is explained twice: in plain words (the problem, what it means, what to do) and,
  underneath, in technical detail for whoever corrects the files. When the same finding appears
  in nearly every file, the panel says so, because that points to how the files were made
  rather than to individual damaged files.
- **Rule numbers.** Every rule has a three-digit number, 001 to 054, which is what the app and the
  reports show. Numbers are permanent. The older letter codes (such as Q-TIME-A) are kept in the
  `rule_code` column of the CSV reports and in `findings.jsonl` for anyone comparing with
  earlier reports; `RULE_GUIDE.csv` lists both. The sample files still carry the letter code in
  their names.
- `RULE_GUIDE.csv` in the package explains all 54 rules in the same way (problem, what it
  means, what to do, technical detail), whether or not they fired.
- To list every file with either problem after a scan, open the **Findings by rule** tab and
  select rule 046 or rule 047; `findings.jsonl` and `inventory.csv` carry the same
  information.

### What depends on your settings

- The size threshold (default 2 GB, which is 2,147,483,648 bytes). The value used is stated in
  every report, in GB and in bytes.
- The time zone and the time-window mode. Rule A results change with the zone.
- The participant warning threshold (off by default).
- Whether sensitive values are included in reports.

The 10,000-event limit and the 24-hour span are fixed.

### What is unverified

The published sources do not settle these points. The tool behaves as described; treat the
results accordingly. The full list is under "Unverified" in `RULES.md`.

- Whether Relativity accepts short version strings such as `2.0`. Only `1.0.0` and `2.0.0`
  are accepted here.
- The exact byte value of "2 GB" and whether it applies to the `.rsmf` file or to the
  attachments inside it. Here it is the `.rsmf` file size on disk.
- Whether `X-RSMF-EventCount` means all events or only messages. Here it is compared with all
  events.
- Whether the 10,000 limit counts events or messages. Here it counts all events.
- What `X-RSMF-AttachmentCount` counts, and how Relativity compares header participants with
  manifest participants. Neither is checked.
- Relativity publishes no numeric participant limit, so none is enforced.
- Most severities (error or warning) are this tool's own choice; Relativity documents a
  severity for only a few conditions.
- Which timestamp formats Relativity accepts beyond the examples it publishes.
- Whether attachment names are matched case-sensitively. Here they must match exactly.
- Two places where Relativity's specification page and its schema disagree (conversation
  `participants` and `type` in RSMF 2.0); the tool follows the schema for errors and reports
  the specification-only requirement as a warning.

This tool does not predict Relativity's deduplication or slicing results.
