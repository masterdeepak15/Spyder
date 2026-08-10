# Reference: Task/Session Lifecycle & Auto-Update Map

> Read this file when a task or session changes state (created, started, done, blocked) or when any change is made to the codebase and you need to know which `.claude/` files to touch.

**Contents:**
- Task Lifecycle — Non-Negotiable Flow (the exact sequence of file updates for each state change)
- Auto-Update Map — a lookup table: "what changed" → "which files to update" → "what to write"

---

## Task Lifecycle — Non-Negotiable Flow

Every task follows this exact path:

```
BACKLOG → ACTIVE → DONE
              ↓
           BLOCKED → ACTIVE (when unblocked)
```

### Task CREATED (added to Backlog):
```
→ tasks/:      create {ISO_DATE}_{kebab-name}.md using the task file template
→ TASKS.md:    add row to Backlog, linking to the new file
```

### Task STARTS:
```
→ TASKS.md:     move row from Backlog to Active (add Started date + Agent) — file link unchanged
→ tasks/{file}: update Status header to Active; add Progress Log entry
→ sessions/{current file}: add to "🔄 In Progress" with current state + link to tasks/{file}
→ AGENT.md:     update ".claude/ Folder Live State" table (TASKS.md row)
```

### While WORKING (after every code change):
```
→ tasks/{file}:            add newest-first Progress Log entry with exact state
→ tasks/{file}:            add changed file to "Files Touched" table
→ sessions/{current file}: update "🔄 In Progress" with latest exact state
→ sessions/{current file}: add changed file to "📁 Files Changed" table
→ Relevant CONTEXT/:       update if API/auth/DB/frontend changed
→ CODEBASE_MAP.md:         add/update/remove any files from tree
→ AGENT.md:                bump Updated timestamp + TASKS.md state row
```

### Task DONE:
```
→ tasks/{file}: update Status header to Done; final Progress Log entry; check off Acceptance Criteria
→ TASKS.md:     move row from Active to Done (add Completed date + session #) — file link unchanged
→ sessions/{current file}: move from "🔄 In Progress" to "✅ Done This Session"
→ AGENT.md:     update ".claude/ Folder Live State" table
```

### Task BLOCKED:
```
→ tasks/{file}: update Status header to Blocked; fill "Blocker" section with exact blocker
→ TASKS.md:     move row from Active to Blocked (write exact blocker + date) — file link unchanged
→ sessions/{current file}: move to "🚫 Blocked" with what's needed to unblock
```

### Session ENDS (mandatory — do not skip):
```
→ sessions/{current file}: fill "🔜 Next Agent Should Do" — ordered, specific, immediately actionable
→ sessions/{current file}: finalize "📁 Files Changed" — every file touched this session
→ sessions/{current file}: update Status header to "✅ Complete"
→ SESSION.md:   update "📍 Current Session" Status to "✅ Complete" (still points at this file — it only moves into "🕒 Session History" when Step 3 runs for the next session); bump Updated timestamp
→ TASKS.md:     bump timestamp
→ AGENT.md:     update ".claude/ Folder Live State" — all rows current
→ INDEX.md:     update "Current Status" section
```

**Note:** `SESSION.md`'s "📍 Current Session" pointer is only replaced with a *new* file when the *next* session starts (Step 3) — at session end it's fine for the index to still point at the just-finished file, now marked Complete. This means a session can end without a session immediately following it, and the index always has a valid current pointer.

---

## Auto-Update Map — Every Change Has a Doc Update

| What changed | Files to update | What to write |
|-------------|----------------|--------------|
| Session started | `sessions/{new file}`, `SESSION.md` | Create session file (Step 3); update index pointer + history row |
| New task identified | `tasks/{new file}`, `TASKS.md` | Create task file; add row to Backlog |
| Task picked up | `TASKS.md`, `tasks/{file}`, `sessions/{current file}`, `AGENT.md` | Move row to Active; update file Status + Progress Log; In Progress entry |
| Task detail/progress updated | `tasks/{file}` | Append Progress Log entry; update Files Touched |
| **Any file created** | `CODEBASE_MAP.md`, `AGENT.md` | Add to tree with annotation; bump state |
| **Any file deleted** | `CODEBASE_MAP.md`, `AGENT.md` | Remove from tree; bump state |
| **Any file renamed** | `CODEBASE_MAP.md`, `AGENT.md` | Update path; bump state |
| Function/class added | `CODEBASE_MAP.md` | Add to Key Functions table |
| New API endpoint | `CONTEXT/api/{resource file}`, `CONTEXT/api.md`, `AGENT.md` | Add endpoint row in resource file; add/update index row; bump state |
| API contract changed | `CONTEXT/api/{resource file}` | Update that endpoint |
| DB table/column added | `CONTEXT/database.md`, `AGENT.md` | Add to schema; bump state |
| DB migration written | `CONTEXT/database.md` | Note migration name + what it does |
| Auth flow changed | `CONTEXT/auth.md`, `AGENT.md` | Update flow section; bump state |
| New dependency added | `ARCHITECTURE.md` | Add to External Services |
| Architecture changed | `ARCHITECTURE.md` + `DECISIONS/{new file}` + `DECISIONS.md` | Update diagram + create ADR file + add index row |
| Bug/gotcha found | `CODEBASE_MAP.md`, `sessions/{current file}` | Add to Gotchas; add to Discoveries |
| Technical decision made | `DECISIONS/{new file}`, `DECISIONS.md`, `AGENT.md` | Create ADR file; add index row (newest first); bump state |
| Task done | `TASKS.md`, `tasks/{file}`, `sessions/{current file}`, `AGENT.md` | Move row to Done; update file Status + check Acceptance Criteria; ✅ Done; update state |
| Task blocked | `TASKS.md`, `tasks/{file}`, `sessions/{current file}`, `AGENT.md` | Move row to Blocked; fill file's Blocker section; update state |
| Session ended | `sessions/{current file}`, `SESSION.md`, `TASKS.md`, `AGENT.md`, `INDEX.md` | Finalize session file; index history row; timestamps; live state |

**Update rule:** Surgical section edits only. Never rewrite a whole file.
**Timestamp rule:** Every file you touch gets its `> Updated:` bumped.

---

