# Reference: `.claude/` File Templates

> Read this file when you need the exact format for any `.claude/` file. Don't preload the whole thing — jump to the one section (`###` heading) you actually need for the file you're about to write. Every file is written from real project data; never leave a `{placeholder}` unfilled — if a value can't be found, write `"Not found in scan — check manually."`

**Table of Contents**
- File Creation — Write In This Order
- `.claude/INDEX.md` — Master Overview
- `.claude/AGENT.md` — Rules + Live State
- `.claude/SESSION.md` — Session INDEX (links into `sessions/`)
- `.claude/sessions/{ISO_DATE_TIME}_{kebab-session-topic}.md` — Individual Session File
- `.claude/TASKS.md` — Task INDEX (links into `tasks/`)
- `.claude/tasks/{ISO_DATE}_{kebab-task-name}.md` — Individual Task File
- `.claude/ARCHITECTURE.md` — System Design
- `.claude/CODEBASE_MAP.md` — LLM Code Wiki
- `.claude/DECISIONS.md` — ADR Log
- `.claude/CONTEXT/api.md`
- `.claude/CONTEXT/auth.md`
- `.claude/CONTEXT/database.md`
- `.claude/CONTEXT/frontend.md`

---

## File Creation — Write In This Order

```
CORE (always):
1. ARCHITECTURE.md  (system design — read actual code first)
2. CODEBASE_MAP.md  (code wiki — built from real file tree + read source files)
3. DECISIONS.md     (empty log, ready for entries)
4. TASKS.md         (index — start empty or with whatever tasks are known)
5. SESSION.md       (index — written just before the first sessions/ file)
6. sessions/{first session file}  (this session's own file — written last among core)
7. AGENT.md         (rules + state — reflects everything above)
8. INDEX.md         (written last — links to all files that actually exist)

CONTEXT/ (only confirmed ones, after core):
9-N. Each confirmed CONTEXT/ file — populated from actual code, not templates
```


---

### `.claude/INDEX.md` — Master Overview

```markdown
# {ProjectName} — AI Memory Index
> Updated: {ISO_DATETIME} | Session #{N}

## What Is This?
{1-2 sentences: what the project does — derived from README or entry point}
**Stack:** {actual lang} | {actual framework} | {actual database} | {actual hosting}

---

## 🗂️ Memory Files

| File | Purpose | Read When |
|------|---------|-----------|
| [AGENT.md](AGENT.md) | Rules + live project state | Every session — mandatory |
| [SESSION.md](SESSION.md) | Session INDEX — links to `sessions/*.md` | Every session — mandatory |
| [TASKS.md](TASKS.md) | Task INDEX — links to `tasks/*.md` | Picking up work |
| [ARCHITECTURE.md](ARCHITECTURE.md) | System design + components | Understanding the system |
| [CODEBASE_MAP.md](CODEBASE_MAP.md) | Annotated code tree + key functions | Navigating code |
| [DECISIONS.md](DECISIONS.md) | ADR log — technical decisions | Before changing architecture |
{CONTEXT_TABLE_ROWS — add rows only for files that actually exist}

---

## 📋 Current Status
**Active Task:** {task from the current session file, linked via SESSION.md}
**Last Session:** {date} — {1-line summary, from SESSION.md Current Session pointer}
**Open Tasks:** {count} backlog | {count} active | {count} blocked

---

## ⚡ Quick Start For New Agents
1. Read this file ✓
2. Read `AGENT.md` → understand rules + current state
3. Read `SESSION.md` → find "📍 Current Session", then open that linked `sessions/{file}`
4. Read `TASKS.md` → pick a row, open its linked `tasks/{file}` for full detail
5. Load relevant `CONTEXT/` file for your task (if it exists)
6. Start — create your own `sessions/{new file}` immediately (Step 3)
```

---

### `.claude/AGENT.md` — Rules + Live State

```markdown
# AGENT.md — {ProjectName}
> Updated: {ISO_DATETIME} | Session #{N} | By: {AI_NAME}
> ⚠️ This file is always live — it reflects the current state of the .claude/ folder


---

## Project Overview
{2-3 sentences: what it does, who uses it — from README or actual code}

**Stack:** {actual language} | {actual framework} | {actual database} | {actual hosting}
**Repo:** {git remote URL — from git remote -v}
**Status:** {Active / In Development / Maintenance}

---
## How To Run
```bash
# Install
{actual install command from package.json scripts or README}

# Start dev
{actual dev command}

# Run tests
{actual test command}

# Build
{actual build command}
```

## Required Environment Variables
```
{VAR_NAME}=    # {what it's for — derived from .env.example or config files}
```
If no .env.example found: "Check with team — no .env.example found in repo"

---
## Rules For All Agents

### Mandatory (every session):
1. Read `.claude/INDEX.md` first — get oriented
2. Read `.claude/SESSION.md` (index) before writing any code — find "📍 Current Session", open its linked `sessions/{file}` for what was in progress
3. Create this session's own `sessions/{ISO_DATE_TIME}_{kebab-topic}.md` at the start (Step 3); update it as work happens; finalize it when work ends
4. Move tasks in `.claude/TASKS.md` as status changes; keep task detail in the linked `tasks/{file}`, not in TASKS.md itself
5. Log every technical decision in `.claude/DECISIONS.md` with WHY

### Code rules:
6. Update `.claude/CODEBASE_MAP.md` when any file is created, deleted, or renamed
7. Update relevant `CONTEXT/` file when its area changes (ONLY if that file exists)
8. Never hardcode secrets — use environment variables
9. Surgical section edits only — never rewrite a whole `.claude/` file (the one exception: a brand-new `tasks/{file}` or `sessions/{file}`, which is written fresh, not edited-in-place from a template)

### Never do:
- Skip updating `.claude/` files after making changes
- Leave a session file's "In Progress" / "Next Agent Should Do" stale when a session ends
- Write task or session detail directly into `TASKS.md` or `SESSION.md` — those are indexes only
- Make architecture changes without a DECISIONS.md entry
- Create or update a CONTEXT/ file that was not confirmed by the user

---

## Key Source Files
<!-- The 6-10 most important files — what an agent needs to navigate confidently -->

| File | What It Does |
|------|-------------|
| `{src/entry/point}` | {entry point — server start / main function} |
| `{src/key/service}` | {most important business logic} |
| `{src/middleware}` | {auth / error handling middleware} |
| `{src/utils/db}` | {database client — always import from here} |
| `{config/file}` | {main configuration} |

---

## .claude/ Folder Live State

| File | Last Updated | Summary |
|------|-------------|---------|
| SESSION.md | {datetime} | Current: `sessions/{file}` — {1-line: what's in progress} |
| TASKS.md | {datetime} | {N} active, {N} backlog, {N} blocked |
| CODEBASE_MAP.md | {datetime} | {N} modules mapped |
| ARCHITECTURE.md | {datetime} | {last change summary} |
| DECISIONS.md | {datetime} | {N} decisions logged |
{CONTEXT rows — only list files that actually exist}
```

---

### `.claude/SESSION.md` — Session INDEX (wiki-style — links only, no detail)

`SESSION.md` never holds session detail. It is a lookup table: a pointer to the current session's file, plus a history table of every past session. Full goal/progress/discoveries for any session live inside that session's own file, not here. This is what keeps it cheap to read — the index stays a handful of lines forever, no matter how many sessions the project has had.

```markdown
# SESSION.md — Session Index
> Updated: {ISO_DATETIME}
> Full detail for any session lives in `sessions/{filename}` — this file only points to it.

---

## 📍 Current Session
**File:** [sessions/{ISO_DATE_TIME}_{kebab-topic}.md](sessions/{ISO_DATE_TIME}_{kebab-topic}.md)
**Goal:** {one-line goal — enough to recognize it, not explain it}
**Status:** {🔄 In Progress / ✅ Complete}

---

## 🕒 Session History
<!-- Newest first. One row per session, ever — added when a session ends (or a new one starts). -->
| # | Date | Goal | File | Agent | Status |
|---|------|------|------|-------|--------|
| {N} | {date} | {one-line goal} | [sessions/{file}](sessions/{file}) | {AI name} | ✅ Complete |
```

**Rule:** The "Goal" text stays one line. Anything more — what was done, what's blocked, discoveries — belongs in the linked file.
**Rule:** Every time Step 3 runs (a new session starts), the previous "📍 Current Session" becomes a new row at the top of "🕒 Session History", and "📍 Current Session" is replaced with the new file.

---

### `.claude/sessions/{ISO_DATE_TIME}_{kebab-session-topic}.md` — Individual Session File

One file per work session, created fresh every time the Session Start Protocol runs (Step 3) — never reopened or appended to across sessions. This is where all real detail lives — SESSION.md only points here.

**Filename rule:** `{YYYY-MM-DD_HHMM}_{kebab-case-topic}.md` (24h time, no colons) — e.g. a session started on 21 July 2026 at 2:30 PM working on some topic would be `2026-07-21_1430_{topic}.md`. The time component matters here (unlike tasks) because a project can easily have more than one session in a day.

```markdown
# Session #{N}: {short topic}
> Date: {ISO_DATETIME} | Agent: {AI_NAME} | Status: {🔄 In Progress / ✅ Complete}
> Prev session: [sessions/{prev-file}](sessions/{prev-file}) — {1-line summary}, or "First session" if new

---

## 🎯 Goal This Session
{Specific, concrete goal — what "done" looks like}

## ✅ Done This Session
- {completed item — reference actual file path}

## 🔄 In Progress
- **{Task ID} {task name}** — {exact current state}

## 🚫 Blocked
- **{Task ID}** — {blocker} | Needs: {what to unblock}

## 📁 Files Changed This Session
| File | What Changed |
|------|-------------|
| `{actual/path/file}` | {Created / Modified / Deleted} — {what and why} |

## 💡 Discoveries / Gotchas
- {anything surprising, edge cases, warnings for future agents}

## 🔜 Next Agent Should Do
1. {exact first action with actual file reference}
2. {second action}
3. {third action}
```

**Rule:** Update this file throughout the session, not just at the end — treat it the same way you'd treat a task's Progress Log.


---

### `.claude/TASKS.md` — Task INDEX (wiki-style — links only, no detail)

`TASKS.md` never holds task detail. It is a lookup table: one row per task, each row linking to its full file in `tasks/`. Full history, notes, and discoveries live inside that task's own file, not here.

```markdown
# TASKS.md — Task Index
> Updated: {ISO_DATETIME}
> Full detail for any task lives in `tasks/{filename}` — this file only indexes and describes.

---

## 🔥 Active
| ID | Task | File | Started | Agent |
|----|------|------|---------|-------|
| T-{N} | {one-line task description} | [tasks/{ISO_DATE}_{kebab-name}.md](tasks/{ISO_DATE}_{kebab-name}.md) | {date} | {AI name} |

## 📋 Backlog
| ID | Task | File | Priority | Notes |
|----|------|------|----------|-------|
| T-{N} | {one-line task description} | [tasks/{ISO_DATE}_{kebab-name}.md](tasks/{ISO_DATE}_{kebab-name}.md) | 🔴 High / 🟡 Med / 🟢 Low | {context or dependency} |

## ✅ Done
| ID | Task | File | Completed | Session |
|----|------|------|-----------|---------|
| T-{N} | {one-line task description} | [tasks/{ISO_DATE}_{kebab-name}.md](tasks/{ISO_DATE}_{kebab-name}.md) | {date} | #{N} |

## 🚫 Blocked
| ID | Task | File | Blocker | Since |
|----|------|------|---------|-------|
| T-{N} | {one-line task description} | [tasks/{ISO_DATE}_{kebab-name}.md](tasks/{ISO_DATE}_{kebab-name}.md) | {exact blocker} | {date} |
```

**Rule:** The "Task" column stays one line — enough to recognize the task, not explain it. Anything more belongs in the linked file.
**Rule:** When a task moves between sections (Backlog → Active → Done/Blocked), move its *row*, keep its *file* — never rename or recreate the file on a status change, just update the file's own status field (Section 6).

---

### `.claude/tasks/{ISO_DATE}_{kebab-task-name}.md` — Individual Task File

One file per task. Created the moment a task is added to `TASKS.md` (even in Backlog). This is where all real detail lives — TASKS.md only points here.

**Filename rule:** `{YYYY-MM-DD}_{kebab-case-task-name}.md` — date is the day the task was *created*, not started or finished. Example: `2026-07-21_setup-auth-middleware.md`.

```markdown
# T-{N}: {Task Title}
> Created: {ISO_DATETIME} | Status: {Backlog / Active / Done / Blocked}
> File: `tasks/{ISO_DATE}_{kebab-task-name}.md`

---

## Description
{What needs to happen — 2-5 sentences, concrete and specific}

## Why / Context
{What triggered this task — user request, bug report, dependency on another task}

## Acceptance Criteria
- [ ] {concrete, checkable condition for "done"}
- [ ] {another one}

## Progress Log
<!-- Newest entry at the TOP. One entry per work session touching this task. -->
- **{ISO_DATETIME}** ({Agent}): {what was done, exact file refs, current state}

## Files Touched
| File | What Changed |
|------|-------------|
| `{actual/path/file}` | {Created / Modified / Deleted} — {what and why} |

## Blocker (if status = Blocked)
{Exact blocker + what's needed to unblock}

## Related
- Depends on: {T-{N} or "none"}
- Relates to: {ADR-{N}, CONTEXT/{file}.md, or "none"}
```

**Rule:** Update a task's own file every time you touch it — the Progress Log is the task's memory. TASKS.md is just the map.

---

### `.claude/ARCHITECTURE.md` — System Design

```markdown
# ARCHITECTURE.md
> Updated: {ISO_DATETIME}

## System Overview
{2-3 sentences: type of system, how it's structured, key design goals}

## Component Diagram
```
{ASCII diagram — show components and how they connect}

Example:
[Client Browser]
      ↓ HTTPS
[React Frontend :3000]
      ↓ REST / JSON
[Express API :4000]
    ↙         ↘
[PostgreSQL]  [Redis]
[port 5432]   [cache]
      ↑
[BullMQ workers] ← background jobs
```

## Layers
| Layer | Responsibility | Location |
|-------|---------------|----------|
| {layer} | {what it handles — not what it is} | `{path/}` |

## Request Flow
How a typical request moves through the system:
1. `{entry}` receives request
2. `{middleware}` validates auth + input
3. `{controller}` routes to correct handler
4. `{service}` executes business logic
5. `{repository/db}` reads/writes data
6. Response returned as `{shape}`

## External Services
| Service | Why Used | Config |
|---------|---------|--------|
| {name} | {purpose} | `{env var name}` |

## Known Constraints / Limits
- {e.g. "Rate limit: 100 req/15min per IP"}
- {e.g. "Max file upload: 10MB"}
```

---

### `.claude/CODEBASE_MAP.md` — LLM Code Wiki

The most important file for code navigation. Annotated tree + key functions.

```markdown
# CODEBASE_MAP.md
> Updated: {ISO_DATETIME}

---

## Annotated File Tree
<!-- Every folder has a comment. Key files explained inline. Skip generated/vendor dirs. -->

```
{project-name}/
├── src/
│   ├── controllers/              # HTTP handlers — thin, just parse req/res, call services
│   │   ├── auth.controller.ts    # Login, logout, refresh token endpoints
│   │   ├── users.controller.ts   # User CRUD endpoints
│   │   └── tasks.controller.ts   # Task CRUD endpoints
│   │
│   ├── services/                 # ALL business logic — never put logic in controllers
│   │   ├── auth.service.ts       # JWT creation, validation, refresh logic
│   │   ├── users.service.ts      # User operations: create, update, soft delete
│   │   └── tasks.service.ts      # Task state machine, assignment, notifications
│   │
│   ├── middleware/
│   │   ├── auth.middleware.ts    # JWT decode → attaches req.user — applied to all /api routes
│   │   └── error.middleware.ts   # Global error handler — catches all thrown errors
│   │
│   └── utils/
│       ├── db.ts                 # ← ALWAYS import Prisma from here, never directly
│       └── logger.ts             # Winston logger — use instead of console.log
│
├── .claude/                      # AI memory — do not import in source code
├── prisma/
│   ├── schema.prisma             # Source of truth for DB schema
│   └── migrations/               # Run in order — never edit after applied to prod
└── tests/
    ├── unit/                     # No DB needed — fast
    └── integration/              # Needs test DB — set DATABASE_URL in .env.test
```

---

## Key Functions / Classes
<!-- 10-15 most important. Where they live and exactly what they do. -->

| Name | File | What It Does |
|------|------|-------------|
| `AuthService.login()` | `src/services/auth.service.ts` | Validates credentials, returns JWT pair |
| `authMiddleware` | `src/middleware/auth.middleware.ts` | Decodes JWT, attaches `req.user = {id, role}` |
| `db` (singleton) | `src/utils/db.ts` | Prisma client — always import from here |
| `TaskService.transition()` | `src/services/tasks.service.ts` | Handles all task state changes + triggers notifications |

---

## Naming Conventions
| Thing | Convention | Example |
|-------|-----------|---------|
| Files | kebab-case | `auth-service.ts` |
| Classes | PascalCase | `AuthService` |
| Functions / vars | camelCase | `getUserById` |
| Constants | SCREAMING_SNAKE | `MAX_RETRY_COUNT` |
| DB tables | snake_case | `task_comments` |
| Env vars | SCREAMING_SNAKE | `DATABASE_URL` |

---

## Gotchas / Watch Out For
- {e.g. "Never import PrismaClient directly — always use `src/utils/db.ts`"}
- {e.g. "Soft delete pattern: set `deletedAt`, never hard delete"}
- {e.g. "Tests need .env.test — copy from .env and change DATABASE_URL"}
```

---

### `.claude/DECISIONS.md` — ADR Log

```markdown
# DECISIONS.md — Architecture Decision Records
> Every significant technical decision logged here with context and reason.
> Future agents: read before changing architecture or patterns.

---

## ADR-{N}: {Short Decision Title}
**Date:** {date} | **Session:** #{N} | **Status:** Accepted

**Context:** {What situation or problem forced this decision?}

**Decision:** {What was decided — one clear sentence}

**Reason:** {Why this over alternatives? What was rejected and why?}

**Impact:**
- ✅ {positive outcome}
- ⚠️ {constraint or tradeoff this creates — be honest}

---
<!-- New ADRs go at the TOP — newest first -->
```

---

### `.claude/CONTEXT/api.md`

```markdown
# API Reference
> Updated: {ISO_DATETIME}
> Base URL: `{http://localhost:PORT/api}` | Auth: `Authorization: Bearer {token}`

---

## Authentication
All endpoints require JWT bearer token unless marked **Public**.
Get token via `POST /api/auth/login`.

---

## Endpoints

### {Resource} — `{/api/resource}`

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| GET | `/api/{resource}` | List all (paginated) | Required |
| POST | `/api/{resource}` | Create new | Required |
| GET | `/api/{resource}/:id` | Get single | Required |
| PATCH | `/api/{resource}/:id` | Partial update | Required |
| DELETE | `/api/{resource}/:id` | Soft delete | Required |

**POST body:**
```json
{ "field": "string — description" }
```
**Response:**
```json
{ "id": "uuid", "field": "value", "createdAt": "ISO datetime" }
```
**Errors:** `400` validation | `401` unauthorized | `404` not found | `409` conflict
```

---

### `.claude/CONTEXT/auth.md`

```markdown
# Auth System
> Updated: {ISO_DATETIME}

## Flow
1. `POST /api/auth/login` → send `{email, password}`
2. `AuthService.login()` validates credentials with bcrypt
3. Returns `{ accessToken (15min), refreshToken (7d) }`
4. Client sends `Authorization: Bearer {accessToken}` on every request
5. `authMiddleware` decodes token → attaches `req.user = { id, email, role }`
6. Token expired? → `POST /api/auth/refresh` with refresh token → get new access token

## Tokens
| Token | Expiry | Storage recommendation |
|-------|--------|----------------------|
| Access JWT | 15 min | Memory / Authorization header |
| Refresh JWT | 7 days | HttpOnly cookie |

## Roles
| Role | Permissions |
|------|------------|
| `admin` | {all actions} |
| `member` | {limited actions} |

## Key Files
| File | Does |
|------|------|
| `src/services/auth.service.ts` | All auth logic |
| `src/middleware/auth.middleware.ts` | JWT validation per request |
| `src/controllers/auth.controller.ts` | Login/logout/refresh endpoints |
```

---

### `.claude/CONTEXT/database.md`

```markdown
# Database
> Updated: {ISO_DATETIME}
> Type: {PostgreSQL} | ORM: {Prisma 5} | Migration tool: {prisma migrate}

---

## Schema

### `{table_name}`
| Column | Type | Notes |
|--------|------|-------|
| `id` | uuid | PK, auto-generated |
| `{col}` | {type} | {constraint, FK, description} |
| `created_at` | timestamp | auto-set on insert |
| `deleted_at` | timestamp | NULL = not deleted (soft delete pattern) |

**Relations:**
- `{table}` → `{table}` via `{fk_column}` ({one-to-many})

---

## Query Patterns

```typescript
// Always use the db utility
import { db } from '@/utils/db'

// Soft delete (never hard delete)
await db.user.update({ where: { id }, data: { deletedAt: new Date() } })

// Always filter soft-deleted
await db.user.findMany({ where: { deletedAt: null } })
```

## Migration Rules
1. Never edit a migration file after it's been applied to production
2. Always run `npx prisma migrate deploy` before starting dev server
3. Add `deleted_at` column to any table that supports soft deletes
```

---

### `.claude/CONTEXT/frontend.md` (create only if project has a frontend)

```markdown
# Frontend
> Updated: {ISO_DATETIME}
> Framework: {React 18} | Styling: {Tailwind CSS} | State: {Zustand}

## Structure
```
src/
├── pages/           # Route-level components (one per route)
├── components/
│   ├── ui/          # Generic: Button, Input, Modal, Card
│   └── features/    # Feature-specific: TaskCard, UserAvatar, LoginForm
├── hooks/           # Custom hooks: useAuth, useTasks, useToast
├── store/           # Zustand stores — global state only
├── services/        # All API calls — never fetch() directly in components
└── utils/           # Pure helper functions
```

## Patterns
- All API calls go through `src/services/` — never raw `fetch()` in components
- Global state → Zustand store | Local state → `useState`
- Shared UI → `components/ui/` | Feature-specific → `components/features/`

## Key Components
| Component | File | What It Does |
|-----------|------|-------------|
| `{Name}` | `{path}` | {description} |
```

---

