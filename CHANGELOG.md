# Changelog

Every release is one entry: version, date and time, what changed, why, tests added.

## 1.25.0 - 2026-11-24 07:10 (America/New_York)

Owner request of 2026-11-24 07:06: the default time zone should be UTC, not the PC's own zone.

**What**
- With no time zone chosen, the calendar-day rule (042) now uses **UTC**. Before, it used the
  zone of the PC running the scan, so the same files could pass on one PC and fail on another.
  This applies to the Settings box in the app, to the command line when `--tz` is left out, and
  to the viewer.
- Ruleset version is now **1.2.0**, because a default that rule 042 depends on changed. No rule's
  wording, severity or number changed.
- `summary.txt` and `run.json` describe an unchosen zone as "default" / "default (UTC)" instead
  of "PC default".

**Not changed**
- A zone you choose, or one saved with "Remember these settings", is used exactly as before.
- **Try the samples** still scans with America/New_York, because the samples are built for it.

**Tests**
- One existing test tightened: with no zone chosen the zone is UTC.

## 1.24.1 - 2026-11-24 07:05 (America/New_York)

Owner request of 2026-11-24 06:58: the sample files should be funnier.

**What**
- The 51 sample files now tell one running joke. Scrooge McDuck is accused of shady financial
  dealings, Bruce Wayne is his general counsel, a reporter named Clark Kent is asking questions,
  and assorted Looney Tunes characters turn up as witnesses. Names, email addresses (all ending
  in `.invalid`), the conversation name, the subject line and the message text changed.
- The clean samples gain a third participant (the reporter, who says nothing).
- Nothing a rule looks at changed: every sample has the same ids, timestamps, counts, defect
  and file name as before, and raises exactly the same rules.
- `samples\INDEX.md` and the user guide say that the cast is borrowed as a parody and that the
  characters belong to their owners.
- Build runner: new `samples` command that writes the sample set to a new folder.

**Not changed**
- No rule, report or behaviour changed. The test fixtures (a separate set) are untouched.

**Tests**
- No new tests. The existing tests that every sample raises exactly its rule, and that a scan of
  the sample folder matches the index, pass against the new content.
