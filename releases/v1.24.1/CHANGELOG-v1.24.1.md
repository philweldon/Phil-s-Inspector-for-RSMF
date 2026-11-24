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
