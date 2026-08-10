---
name: agent-memory
description: >
  Creates and maintains a .claude/ folder at the project root: AI memory split into an INDEX,
  AGENT rules/state, a SESSION index (linking to one file per session in sessions/), a TASKS
  index (linking to one file per task in tasks/), ARCHITECTURE, CODEBASE_MAP, a DECISIONS index
  (linking to one file per ADR in DECISIONS/), and optional CONTEXT/ files (api, auth, database,
  frontend). Full templates and workflows live
  in references/ — read only what's needed per turn to keep token use low.
  ALWAYS trigger when: starting a session in a project dir ("start session", "new session",
  "open project"); user says "init project" / "setup claude memory"; any code change, file
  added/deleted, feature/bugfix; switching AI tools or "handoff"; "what's the status?" /
  "where were we?"; a task starts, finishes, or gets blocked. AGENT.md always reflects live
  state — keep .claude/ in sync with the codebase at all times.
---

# Agent Memory — `.claude/` Folder System 🧠

> *"Open .claude/INDEX.md → know everything. No history. No full code scan."*

All AI memory lives inside `.claude/` — separate from project source code, readable by any AI.

**This file is deliberately short.** Everything you need for routine session work is here. Detailed templates and long procedures live in `references/` — go there only when you actually need to write one specific file. Don't preload reference files "just in case."

---

## 0. Is This a Real Project? (Gate — Check Before Anything)

**Run this check first. Do not skip.**

```
SKIP this entire skill (do not create .claude/) if ANY of these are true:
  ✗ Fewer than 5 source files in the directory
  ✗ No manifest file found (package.json, pyproject.toml, requirements.txt,
      go.mod, Cargo.toml, pom.xml, .csproj, Gemfile, build.gradle)
  ✗ No .git directory AND no manifest
  ✗ User request is a one-off task:
      "fix this file", "explain this code", "write a quick script",
      "help me with this function", "temp task", "just this once"
  ✗ Files are isolated with no recognizable folder structure (src/, lib/, app/, etc.)
  ✗ Working in /tmp, Desktop, Downloads with no project indicators

PROCEED only when ALL of these are true:
  ✓ Has a manifest file OR .git directory
  ✓ Has a recognizable folder structure with 10+ files
  ✓ User intent is project-level:
      "project", "codebase", "repo", "init", "start session",
      "open project", "setup memory", "new session"
```

If in doubt — ask: *"Is this a full project you want me to track with .claude/ memory, or a one-off task?"*

---

## 1. Folder Structure

Core files are always created. CONTEXT/ files are optional — detected from code, confirmed with user.

```
project-root/
├── .claude/
│   ├── INDEX.md              ← START HERE — master overview + links to all files
│   ├── AGENT.md              ← Rules for agents + live project state (always kept current)
│   ├── SESSION.md            ← Session INDEX — table of every session, links into sessions/
│   ├── TASKS.md              ← Task INDEX — table of every task, links into tasks/
│   ├── ARCHITECTURE.md       ← System design, components, data flow
│   ├── CODEBASE_MAP.md       ← Annotated file tree + key functions wiki
│   ├── DECISIONS.md          ← ADR INDEX — table of every decision, links into DECISIONS/
│   ├── tasks/                ← One file per task — full detail lives here, not in TASKS.md
│   │   └── {ISO_DATE}_{kebab-task-name}.md
│   ├── sessions/              ← One file per work session — full detail lives here, not in SESSION.md
│   │   └── {ISO_DATE_TIME}_{kebab-session-topic}.md
│   ├── DECISIONS/             ← One file per technical decision — full detail lives here, not in DECISIONS.md
│   │   └── {ISO_DATE}_{kebab-decision}.md
│   └── CONTEXT/              ← OPTIONAL — only if detected in code AND user confirms
│       ├── api.md            ← (if project has API routes/endpoints) — INDEX, links into api/
│       ├── api/               ← One file per controller/resource — full detail lives here, not in api.md
│       │   └── {kebab-resource}.md
│       ├── auth.md           ← (if project has auth/login/jwt/session)
│       ├── database.md       ← (if project has schema/models/migrations)
│       ├── frontend.md       ← (if project has frontend framework)
│       ├── jobs.md           ← (if project has background jobs/queues/workers)
│       ├── cli.md            ← (if project is a CLI tool)
│       ├── infra.md          ← (if project has docker/k8s/terraform/cloud infra)
│       ├── testing.md        ← (if project has 10+ test files)
│       └── mobile.md         ← (if project has mobile code RN/Flutter/Swift/Kotlin)
│
├── src/                      ← your actual project code (untouched)
├── package.json
└── ...
```

**Rule:** `.claude/` is AI-only. Never mix it with source code. Never import from it.
**Rule:** `AGENT.md` is always live — it reflects the current state of all other files.
**Rule:** `INDEX.md` is the entry point — always accurate, always linkable.
**Rule:** CONTEXT/ files are created ONLY after user confirmation. Never assume.
**Rule:** `TASKS.md`, `SESSION.md`, `DECISIONS.md`, and `CONTEXT/api.md` are INDEXES only — one row per task/session/decision/resource, linking to its file in `tasks/`, `sessions/`, `DECISIONS/`, or `CONTEXT/api/`. Never write full detail directly into any index file. This keeps every read cheap: agents load a few index rows instead of one giant file that grows forever and burns tokens.
**Rule:** Every task gets its own file at `tasks/{ISO_DATE}_{kebab-task-name}.md`. Every work session gets its own file at `sessions/{ISO_DATE_TIME}_{kebab-session-topic}.md`. Every technical decision gets its own file at `DECISIONS/{ISO_DATE}_{kebab-decision}.md`. Every API controller/resource gets its own file at `CONTEXT/api/{kebab-resource}.md`. One file per task, session, decision, or resource, forever — never reused or overwritten.
**Rule:** This skill is stack-agnostic. Nothing in `.claude/` — file names, examples, or content — should ever hardcode a specific language, framework, or library. Everything is derived from the actual project being scanned.
**Rule:** Never write any `.claude/` file with an unfilled `{placeholder}`. If a value can't be found, write: `"Not found in scan — check manually."`
**Rule:** Surgical section edits only — never rewrite a whole `.claude/` file (the exception: a brand-new `tasks/{file}`, `sessions/{file}`, `DECISIONS/{file}`, or `CONTEXT/api/{file}`, written fresh from its template).

---

## 2. Session Start Protocol

Run this at the start of EVERY session — no exceptions.

### Step 1 — Find Project Root
```
Scan current directory for: .git  package.json  requirements.txt  pyproject.toml
                             pom.xml  build.gradle  Cargo.toml  go.mod
                             .sln  .csproj  CMakeLists.txt  Makefile  Gemfile
→ Found? PROJECT_ROOT = this directory
→ Not found? Walk UP the tree until found
→ Still not found? Ask user: "What's the project root?"
```

### Step 2 — Does `.claude/` Exist?
```
IF .claude/INDEX.md exists:
  READ (in order):
    1. .claude/INDEX.md                        → get the lay of the land
    2. .claude/AGENT.md                        → rules + current live state
    3. .claude/SESSION.md                      → index — find "📍 Current Session" pointer
    4. sessions/{file linked by Current Session} → exactly what was in progress
    5. .claude/TASKS.md                        → pending work queue

  ANNOUNCE:
    "📖 {ProjectName} loaded.
     Last session: {goal + summary from the linked session file}
     In progress: {active task}
     Next up: {first backlog item}
     Continuing?"

  THEN start this session's own file (Step 3 below) before any work begins.

IF .claude/ does NOT exist:
  → Read references/project-scan.md → run the full scan, detect CONTEXT/ candidates,
    get user confirmation on which CONTEXT/ files to create
  → Read references/templates.md → write each core file in the documented order,
    then any confirmed CONTEXT/ files
  → ANNOUNCE: "🧠 .claude/ initialized for {ProjectName}. All memory files created. Ready."
  → Step 3 still applies — first session still gets its own sessions/ file.
```

### Step 3 — Open This Session's File (every session, no exceptions)
```
→ sessions/: create a NEW file {ISO_DATE_TIME}_{kebab-session-topic}.md — read
             references/templates.md for the exact format. "Prev session" links
             back to the file that was just read in Step 2. Never append to or
             reopen a previous session's file — each run gets a fresh one.
→ SESSION.md: update "📍 Current Session" to point at the new file; move the previous
              "📍 Current Session" entry down into the "🕒 Session History" table as its own row.
```

### Step 4 — Load CONTEXT/ Files On Demand
```
Working on API?          → read .claude/CONTEXT/api.md (index), then the linked CONTEXT/api/{resource}.md
Auth issue?              → read .claude/CONTEXT/auth.md
DB query/schema?         → read .claude/CONTEXT/database.md
UI work?                 → read .claude/CONTEXT/frontend.md
Background jobs?         → read .claude/CONTEXT/jobs.md
CLI commands?            → read .claude/CONTEXT/cli.md
Infra/deploy?            → read .claude/CONTEXT/infra.md
Tests?                   → read .claude/CONTEXT/testing.md
Mobile?                  → read .claude/CONTEXT/mobile.md
Unknown territory?       → read .claude/CODEBASE_MAP.md
Design change?           → read .claude/ARCHITECTURE.md + .claude/DECISIONS.md (index) + linked DECISIONS/{file}
```

---

## 3. During The Session — What To Update, And Where To Look It Up

Every task/session state change and every code change maps to specific `.claude/` file updates. Rather than duplicate that whole map here, use this table to find the right reference **only when the situation applies**:

| Situation | Read this reference | For |
|-----------|---------------------|-----|
| A task is created, started, finished, or blocked | `references/lifecycle-and-updates.md` | Exact sequence of file updates for each state |
| Any file created/deleted/renamed, new endpoint, schema change, dependency added, decision made, bug found | `references/lifecycle-and-updates.md` | "Auto-Update Map" — what changed → which files to touch |
| Writing/updating INDEX.md, AGENT.md, SESSION.md, a `sessions/{file}`, TASKS.md, a `tasks/{file}`, ARCHITECTURE.md, CODEBASE_MAP.md, DECISIONS.md, a `DECISIONS/{file}`, CONTEXT/api.md, a `CONTEXT/api/{file}`, or any other CONTEXT/*.md | `references/templates.md` | The exact markdown format for that one file |
| `.claude/` doesn't exist yet, or user says "refresh wiki" / "rescan project" | `references/project-scan.md` | Bash scan commands + CONTEXT/ detection + confirmation flow |
| User says "handoff", asks "what's the status?", or a brand-new agent needs to get oriented fast | `references/quick-reference.md` | 5-minute onboarding, handoff message template, full command table |

**General update rule:** every `.claude/` file you touch gets its `> Updated:` timestamp bumped. Task/session detail goes in their own files under `tasks/` / `sessions/` — never in the index files themselves.

---

## Quick Commands (most common — full table in `references/quick-reference.md`)

| User says | Claude does |
|-----------|------------|
| `"init project"` / `"setup .claude"` | references/project-scan.md → references/templates.md |
| `"start session"` / `"new session"` | Section 2 above |
| `"end session"` / `"wrap up"` | references/lifecycle-and-updates.md → "Session ENDS" |
| `"what's the status?"` | Open SESSION.md pointer + TASKS.md → 3-line brief |
| `"add task: [X]"` / `"block task [ID]"` | references/templates.md (task file) + TASKS.md row |
| `"handoff to [AI]"` | references/quick-reference.md → handoff template |
