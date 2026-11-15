# Code-project memory (`.claude/` folder)

Use this when the user works in a real software project. The personal tree and the project folder are linked, so any AI finds both from the start.

## Is it a real project? (gate)
Use it when the directory has a manifest (package.json, pyproject.toml, requirements.txt, go.mod, Cargo.toml, pom.xml, .csproj, Gemfile, build.gradle) or a `.git` folder, and a recognizable structure, and the user's intent is project-level. Skip for one-off tasks ("fix this file", "explain this code", a quick script) and for loose folders (Desktop, Downloads, /tmp). If unsure, ask: "Is this a full project you want me to track, or a one-off task?"

## Link it into the personal tree
When a code project is first met, make or update a branch note in the personal tree, for example `projects/<name>/INDEX.md` (new branch rules from SKILL.md apply). It holds: the project path, one line on what it is, the current status, and a link to `<project-root>/.claude/INDEX.md`. Detail lives in the project's `.claude/`, never copied into the personal tree.

### Location rule (never mix these two places)
- **All project files live in `<project-root>/.claude/`**: rules, sessions, tasks, decisions, architecture, code map, context. Nothing else goes anywhere else.
- **The personal tree holds exactly one file per project**: `projects/<name>/INDEX.md`, a pointer. It has only three things: a one-line summary (with current status), the absolute path to the project root, and a link to `<project-root>/.claude/INDEX.md`. No sessions, tasks, decisions, architecture or code notes, and no other files in that folder.
- **Check before writing**: ask "is this about the code, or about the user?" Code, work progress, tasks, decisions, bugs -> `.claude/`. Personal facts, preferences, people -> the personal tree.
- **Verify after writing**: confirm the new file exists under `<project-root>/.claude/`, and confirm `~/.assistant/memory/projects/<name>/` still holds only `INDEX.md`. Report the check in one line.
- **If files landed in the wrong place**: move them into `<project-root>/.claude/`, fix the links in both `INDEX.md` files, leave only the pointer in the personal tree, and tell the user.

## Folder
```
project-root/.claude/
  INDEX.md          start here: overview + links to all files
  AGENT.md          rules for agents + live state, always current
  SESSION.md        index of sessions   -> sessions/<date-time>_<topic>.md
  TASKS.md          index of tasks      -> tasks/<date>_<name>.md
  ARCHITECTURE.md   system design, components, data flow
  CODEBASE_MAP.md   annotated file tree + key functions + gotchas
  DECISIONS.md      index of decisions  -> DECISIONS/<date>_<decision>.md
  CONTEXT/          optional, made as the project needs: api.md (+ api/<resource>.md),
                    auth.md, database.md, frontend.md, jobs.md, cli.md, infra.md,
                    testing.md, mobile.md ... or any other topic. New topic = new file,
                    linked from INDEX.md at once.
```
Rules: `.claude/` is for AI only, never imported by code. Index files (SESSION, TASKS, DECISIONS, CONTEXT/api.md) hold one row per item and link to its file; detail goes in the item's own file. One file per session, task, decision and resource, never reused. No hard-coded language or framework. No unfilled `{placeholder}`: write "Not found in scan - check manually." Edit sections surgically, never rewrite a whole file (new item files excepted). Bump `> Updated:` on every file touched.

## Start of work in a project
1. Find the project root (walk up to the manifest or `.git`).
2. If `.claude/INDEX.md` exists, read in order: INDEX.md, AGENT.md, SESSION.md (find "Current Session"), that session file, TASKS.md. Tell the user in 3 lines: last session, in progress, next up.
3. If it does not exist: scan the project (file tree, manifest, README, entry points, routes, schema, tests), list the CONTEXT files that the code supports, confirm with the user, then write files in this order: ARCHITECTURE, CODEBASE_MAP, DECISIONS, TASKS, SESSION, first session file, AGENT, INDEX last, then the confirmed CONTEXT files. Formats: `code-templates.md`.
4. Open a fresh session file `sessions/<ISO_DATE_TIME>_<topic>.md`, link back to the previous one, and point SESSION.md "Current Session" at it (move the old one into the history table).
5. Plan the user's request against what the files already say before writing code. Do not re-ask what is already recorded.
6. Load CONTEXT files only for the area being worked on.

## During work: what changed -> what to update
| Change | Update |
|---|---|
| New task | new `tasks/` file + row in TASKS.md Backlog |
| Task started | TASKS row to Active, task file status + progress log, session "In Progress", AGENT.md state |
| Each code change | task progress log + files touched, session files changed, CODEBASE_MAP, relevant CONTEXT file |
| File created, deleted, renamed | CODEBASE_MAP, AGENT.md |
| New endpoint or contract change | CONTEXT/api/<resource>.md + api.md row |
| Schema, migration | CONTEXT/database.md |
| Auth flow | CONTEXT/auth.md |
| New dependency or architecture change | ARCHITECTURE.md; architecture change also a new DECISIONS file + row |
| Decision made | new DECISIONS file (with WHY) + DECISIONS.md row, newest first |
| Bug or gotcha found | CODEBASE_MAP gotchas, session discoveries |
| Task done | TASKS row to Done, task status Done and criteria checked, session "Done This Session" |
| Task blocked | TASKS row to Blocked with exact blocker, task Blocker section, session "Blocked" |
| New topic the project needs (a new area, service, module) | new file in CONTEXT/ (or a deeper branch) and a link in INDEX.md right away |

Do these inline as work happens. The user never has to ask for the update.

## End of session or turn (mandatory)
Fill "Next agent should do" in the session file (ordered, specific), finalize files changed, mark the session Complete, refresh AGENT.md live state and the INDEX.md "Current Status", and refresh the project's note in the personal tree if its status changed. When meaningful work happened in a turn, do a silent checkpoint before replying and mention it in one line.

## Handoff
When the user switches AI tools, or a new AI starts: tell it to open `.claude/INDEX.md`. The files are plain Markdown, so nothing else is needed.