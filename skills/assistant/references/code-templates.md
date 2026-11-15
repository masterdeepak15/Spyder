# `.claude/` file formats

Fill every field from real project data. Keep each file short.

## INDEX.md
```
# <Project> - AI Memory Index
> Updated: <ISO_DATETIME> | Session #<N>

What it is: <1-2 sentences>. Stack: <lang> | <framework> | <db> | <hosting>
Personal memory note: <path in ~/.assistant/memory/>

| File | Purpose | Read when |
|---|---|---|
| [AGENT.md](AGENT.md) | rules + live state | every session |
| [SESSION.md](SESSION.md) | session index | every session |
| [TASKS.md](TASKS.md) | task index | picking up work |
| [ARCHITECTURE.md](ARCHITECTURE.md) | design | understanding the system |
| [CODEBASE_MAP.md](CODEBASE_MAP.md) | code tree | navigating code |
| [DECISIONS.md](DECISIONS.md) | decision index | before changing design |
(one row per CONTEXT file that exists)

Current status: active task, last session (date, one line), open tasks (backlog/active/blocked counts).
```

## AGENT.md
Overview, repo URL, status; how to run (install, dev, test, build from the real scripts); required environment variables (names and purpose only, never values); the rules (read INDEX, SESSION first; fresh session file; move tasks; log decisions; keep CODEBASE_MAP current; update CONTEXT files); and a live-state table: one row per `.claude/` file with its last-updated time.

## SESSION.md (index)
```
# Sessions
> Updated: <ISO_DATETIME>
Current session: [<topic>](sessions/<file>.md) - status
| # | Date | Topic | Status | Link |
```

## sessions/<ISO_DATE_TIME>_<topic>.md
Header: goal, status (Active/Complete), previous session link. Sections: In Progress (exact state, task link), Done This Session, Blocked (what unblocks it), Files Changed (table), Discoveries, Next Agent Should Do (ordered, specific).

## TASKS.md (index)
Tables Backlog, Active, Blocked, Done. Row: id, title, date, agent, link to `tasks/<file>.md`.

## tasks/<ISO_DATE>_<name>.md
Header: title, status (Backlog/Active/Blocked/Done), started, agent. Sections: Description, Acceptance Criteria (checkboxes), Progress Log (newest first), Files Touched (table), Blocker.

## ARCHITECTURE.md
Components and what each does, data flow, external services and dependencies, deployment shape. A small text diagram is fine.

## CODEBASE_MAP.md
Annotated file tree, key functions or classes (name, file, what it does), gotchas.

## DECISIONS.md (index) and DECISIONS/<ISO_DATE>_<decision>.md
Index: table, newest first: date, decision, status, link. Decision file: context, options considered, choice, WHY, consequences.

## CONTEXT/<topic>.md
Facts for one area, taken from the actual code: for api.md an index of resources linking to `api/<resource>.md` (endpoints, methods, auth, request and response shapes); database.md the schema and migrations; auth.md the flow; and so on. A topic that does not exist in the project gets no file.