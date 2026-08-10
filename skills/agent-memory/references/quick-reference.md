# Reference: Onboarding, Handoff & Commands

> Read this file when a new agent needs a fast orientation, when switching AI tools mid-project, or to look up what a specific user phrase should trigger.

**Contents:**
- Easy Start — New Agent in 5 Minutes
- Cross-Agent Handoff Message (template to generate on "handoff")
- Quick Commands (full phrase → action table)

---

## Easy Start — New Agent in 5 Minutes

```
1. Open .claude/INDEX.md          → understand the project + see all files   (1 min)
2. Read .claude/AGENT.md          → rules + current live state                (2 min)
3. Read .claude/SESSION.md        → find "📍 Current Session", open that file (1 min)
4. Read .claude/TASKS.md          → what to work on next                      (1 min)
5. Load relevant CONTEXT/ file    → for the specific task you're picking up
6. Start working                  → create your own sessions/{new file} immediately (Step 3)
```

No history. No full code scan. No "can you explain the project?" questions.
**The .claude/ folder is the answer.**

---

## Cross-Agent Handoff Message

Generate this when switching AI tools:

```
📋 {ProjectName} — Agent Handoff

Read in order (5 min total):
1. .claude/INDEX.md              → overview
2. .claude/AGENT.md              → rules + current state
3. .claude/SESSION.md            → index — find "📍 Current Session"
4. sessions/{linked file}        → what's in progress right now
5. .claude/TASKS.md              → task queue

Currently: {task from sessions/{current file} "🔄 In Progress"}
Next action: {step 1 from that file's "Next Agent Should Do"}
Watch out: {any blocker or gotcha}

Full context: .claude/ARCHITECTURE.md, CODEBASE_MAP.md, CONTEXT/
```

---

## Quick Commands

| User says | Claude does |
|-----------|------------|
| `"init project"` / `"setup .claude"` | Full scan → create all files in `.claude/`, including first `sessions/` file |
| `"start session"` / `"new session"` | Read INDEX + AGENT + SESSION (index) + linked `sessions/{file}` + TASKS, brief user, then create a fresh `sessions/{new file}` |
| `"end session"` / `"wrap up"` | Complete session-end steps (mandatory) — finalize `sessions/{current file}`, update SESSION.md history row |
| `"what's the status?"` | Read SESSION.md pointer → linked `sessions/{file}` + TASKS.md → give 3-line brief |
| `"update wiki"` / `"sync .claude"` | Scan for changes → update all stale files |
| `"handoff to [AI]"` | Generate cross-agent handoff message |
| `"refresh codebase map"` | Rescan source files → update CODEBASE_MAP.md |
| `"log decision: [X]"` | Create `DECISIONS/{ISO_DATE}_{kebab-decision}.md`; add row to top of DECISIONS.md index |
| `"add task: [X]"` | Create `tasks/{ISO_DATE}_{kebab-name}.md`; add row to TASKS.md backlog with priority |
| `"block task [ID]: [reason]"` | Move task to Blocked in TASKS.md; update `tasks/{file}` Blocker section; note it in `sessions/{current file}` |
| `"show task [ID]"` / `"open task [ID]"` | Look up ID in TASKS.md → read + summarize its `tasks/{file}` |
| `"show session [N]"` / `"what happened in session [N]"` | Look up # in SESSION.md history → read + summarize its `sessions/{file}` |
