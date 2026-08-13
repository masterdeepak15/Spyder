# Lifecycle & Auto-Update Map

This map is looked up by **SKILL.md Section 0.c** (End-of-Turn memory check) — that gate is what
forces this table to actually get used every turn, not just when the user explicitly asks to
"update memory" or names a trigger phrase below.

## Auto-Update Map — What Happened → What To Touch

| What happened | Update |
|---|---|
| New task mentioned | New `tasks/{date}_{task}.md` (templates.md) + row in `TASKS.md` |
| Task finished / blocked | Edit that task's file status + `TASKS.md` row |
| New project mentioned | Check ownership (SKILL.md Section 3) → new `projects/{name}.md` (delegated or tracked template) + row in `PROJECTS.md` |
| Project status changes | Edit `projects/{name}.md` (one line if delegated, full section if tracked) + `PROJECTS.md` row |
| Reminder needed | Add line to `REMINDERS.md`; remove once resolved |
| New personal fact shared | Add/append `knowledge/{topic}.md` (create if new) + row in `KNOWLEDGE.md` |
| Daily check-in (morning or evening) | See routines below |
| Mood/health shared | Append to today's `daily/{date}.md`; if `health/{month}.md` exists, add a row there too |
| Family update sent | No dedicated file — just note the date in `FAMILY.md` if useful context (e.g. "last update: {date}") |
| Persona change requested | Edit `IDENTITY.md` persona fields immediately |

**Every file touched:** bump its `_Last updated:_` line. Index files get a row added/edited, never full detail.

---

## 🌅 Morning Check-in

Trigger: "good morning", name + "uth gaya/gayi" or equivalent, "start my day", or the first message of a new day.

```
1. Load INDEX.md + IDENTITY.md (already loaded per session start)
2. Skim TASKS.md + REMINDERS.md rows for anything due today
3. Check connected tools if available (Gmail/Calendar/GitHub) — flag anything urgent
4. Warm briefing: today's priorities + anything overdue
5. Ask how they're feeling — log the answer in today's daily/{date}.md once given
```

## 🌙 Evening Check-in

Trigger: "day's done", "wrap up", "I'm done", equivalent phrase in the user's language.

```
1. Ask what happened today (open-ended, not a checklist)
2. Create/update daily/{date}.md — summary, wins, mood, pending-for-tomorrow
3. Add a row to DAILY.md
4. Carry any unfinished items into TASKS.md / REMINDERS.md
5. Ask if a family update is wanted (only if FAMILY.md has update prefs set)
6. Warm closing matching the day's mood
```

## 📬 Family Updates

Trigger: "update family", "send update", equivalent phrase.

```
1. Read FAMILY.md for who/channel/frequency + today's/this week's daily/{date}.md entries
2. Draft using references/family-templates.md, personalized (name, pronoun, language, what happened)
3. ALWAYS show the draft first — never send unseen
4. NEVER include health/stress detail unless the user explicitly says to include it
5. Send only after explicit confirmation
```

---

## Project Ownership Detection (used by SKILL.md Section 3)

Run this whenever a project is mentioned for the first time, or its status changes:

```
Look for (in the project directory, if accessible):
  .claude/INDEX.md        → agent-memory skill owns it
  CLAUDE.md / AGENTS.md   → that tool owns it
  Other recognizable per-project memory convention

Found?     → projects/{name}.md = DELEGATED template. Never copy its internal detail here.
Not found? → Ask once: "Want me to keep light notes on {project} myself, or is
             something else already tracking it?" → TRACKED template if yes.
```

If unsure and can't check the filesystem, ask the user directly rather than guessing.

---

## Growing Files — When To Split Off a Subfolder File

- `daily/` — always one file per day, no threshold.
- `tasks/` — always one file per task, no threshold.
- `health/` — start logging inside `daily/{date}.md`; once there are enough days in a month to make `HEALTH.md` unwieldy, roll that month into `health/{YYYY-MM}.md` and just keep the trend row in `HEALTH.md`.
- `knowledge/{topic}.md` — only create once a topic/person has more than 2-3 facts; a single new fact can live as a fresh row description in `KNOWLEDGE.md` until then.
