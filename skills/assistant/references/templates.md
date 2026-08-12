# File Templates

Exact format for every file under `~/.assistant/`. Write these fresh when a file doesn't exist yet; for existing files, edit only the relevant section/row — never rewrite the whole file.

---

## INDEX.md

```markdown
# {AssistantName} — Index
_Last updated: {ISO_DATETIME}_

I'm {AssistantName} ({AssistantPronoun}), {UserName}'s personal assistant.

## Quick Links
- 🪪 [Identity](./IDENTITY.md)
- 👨‍👩‍👧 [Family](./FAMILY.md)
- 📁 [Projects](./PROJECTS.md) — {N} tracked
- ✅ [Tasks](./TASKS.md) — {N} open
- 📅 [Daily Log](./DAILY.md) — last entry {date}
- 💊 [Health & Mood](./HEALTH.md)
- ⏰ [Reminders](./REMINDERS.md) — {N} active
- 🧠 [Knowledge](./KNOWLEDGE.md)

## Right Now
- Current focus: {one line}
- Last talked: {date/time}
```

---

## IDENTITY.md

```markdown
# Identity
_Last updated: {ISO_DATETIME}_

## Assistant Persona
- Name: {AssistantName}
- Pronoun: {AssistantPronoun}
- Tone preference: {TonePreference}

## User
- Name: {UserName}
- Gender: {Gender}
- Location: {City, State/Country}
- Profession / Work: {Work}
- Preferred language / style: {Language}
```

---

## FAMILY.md

```markdown
# Family
_Last updated: {ISO_DATETIME}_

## Members
- {Name} — {relation}
- ...

## Update Preferences
- Who gets updates: {names}
- Channel: {WhatsApp / Email / other}
- Frequency: {as requested / weekly / daily}
```

---

## PROJECTS.md (INDEX — rows only)

```markdown
# Projects
_Last updated: {ISO_DATETIME}_

| Project | Managed By | Status | Last Touched | File |
|---|---|---|---|---|
| {Name} | {agent-memory / self / other skill} | {active/paused/done} | {date} | [→](./projects/{kebab-name}.md) |
```

### `projects/{kebab-name}.md` — DELEGATED case (another skill owns memory)
```markdown
# Project: {Name}
_Last updated: {ISO_DATETIME}_

**Managed by:** {skill name}
**Full detail:** {absolute path to that skill's memory, e.g. {project-path}/.claude/INDEX.md}
**Assistant's note:** Pointer only — no duplication. Read the owning file directly for real detail.
**Last known status (one line):** {status}
```

### `projects/{kebab-name}.md` — TRACKED case (assistant is the only memory)
```markdown
# Project: {Name}
_Last updated: {ISO_DATETIME}_

**Path:** {path, if known}
**Status:** {active/paused/done}

## Goal
{one or two lines}

## Current Focus
{what's happening now}

## Recent Activity
- {date}: {what happened}

## Next Steps
- {item}
```

---

## TASKS.md (INDEX — rows only)

```markdown
# Tasks
_Last updated: {ISO_DATETIME}_

| Task | Project | Status | Due | File |
|---|---|---|---|---|
| {short title} | {project or "personal"} | {open/blocked/done} | {date or —} | [→](./tasks/{ISO_DATE}_{kebab-task}.md) |
```

### `tasks/{ISO_DATE}_{kebab-task}.md`
```markdown
# Task: {Title}
_Created: {ISO_DATE}_ · _Status: {open/blocked/done}_

**Project:** {name or "personal"}
**Due:** {date or —}

## Detail
{what needs doing}

## Updates
- {date}: {progress note}
```

---

## DAILY.md (INDEX — rows only)

```markdown
# Daily Log
_Last updated: {ISO_DATETIME}_

| Date | Mood | Summary | File |
|---|---|---|---|
| {date} | {1-10 or word} | {one line} | [→](./daily/{date}.md) |
```

### `daily/{ISO_DATE}.md`
```markdown
# {ISO_DATE}

**Mood:** {rating/word}

## What Happened
- {item}

## Wins
- {item}

## Pending / Carried to Tomorrow
- {item}
```

---

## HEALTH.md (INDEX — trend table, rows only)

```markdown
# Health & Mood
_Last updated: {ISO_DATETIME}_

| Period | Avg Mood | Notes | File |
|---|---|---|---|
| {YYYY-MM} | {value} | {one line} | [→](./health/{YYYY-MM}.md) |
```

### `health/{YYYY-MM}.md` (monthly rollup, created once daily notes get heavy)
```markdown
# Health & Mood — {YYYY-MM}

| Date | Mood | Notes |
|---|---|---|
| {date} | {value} | {note} |
```

---

## REMINDERS.md (flat list — pruned as items resolve)

```markdown
# Active Reminders
_Last updated: {ISO_DATETIME}_

- [ ] {reminder} — due {date}
- [ ] {reminder} — due {date}
```

---

## KNOWLEDGE.md (INDEX — rows only)

```markdown
# Knowledge — Things {AssistantName} Knows About {UserName}
_Last updated: {ISO_DATETIME}_

| Topic | Summary | File |
|---|---|---|
| {topic/person} | {one line} | [→](./knowledge/{kebab-topic}.md) |
```

### `knowledge/{kebab-topic}.md` (only created once a topic has enough to say)
```markdown
# {Topic}
_Last updated: {ISO_DATETIME}_

- {fact}
- {fact}
```
