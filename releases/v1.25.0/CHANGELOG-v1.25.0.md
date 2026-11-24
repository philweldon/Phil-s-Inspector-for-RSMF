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
