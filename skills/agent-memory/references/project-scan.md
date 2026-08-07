# Reference: Project Scan & CONTEXT/ Detection

> Read this file when `.claude/INDEX.md` does NOT exist and you need to initialize memory for a project (referenced from SKILL.md Section 2, Step 2).

**Contents:**
- Full Project Scan — the bash commands to run and what to read before writing anything
- Project Type Detection + CONTEXT/ User Confirmation — how to decide which optional `CONTEXT/*.md` files to propose, and the mandatory confirmation flow

---

## Full Project Scan

Run when `.claude/` doesn't exist, or user says "refresh wiki" / "rescan project".

### Step 0 — Prefer A Connected Tool Over Manual Scanning, If One Exists

Before running the manual bash scan below, check the available tool list for anything purpose-built for this — a code-memory MCP server, a project/codebase indexer, a "claudemem"/"cortex"-style memory tool, or similar (names vary by setup; this skill doesn't assume any specific one is installed). If such a tool is connected:
- Prefer it for gathering file structure, dependencies, and code context — it's typically faster and more accurate than grep/find heuristics.
- Still write the output into `.claude/` using the templates in `references/templates.md` — this skill's `.claude/` format is the point, not the scanning method.
- If the connected tool only covers *part* of the scan (e.g. dependency graph but not DB schema), fall back to the relevant manual steps below for the rest.

If no such tool is connected, or none fits, run the full manual scan below. Don't suggest connecting one unprompted — that's a separate call, not part of this skill.

**Read actual source files before writing any `.claude/` file. This is mandatory, regardless of scan method.**

```bash
# 1. Full file tree (exclude noise)
find . -type f \
  -not -path '*/.claude/*' \
  -not -path '*/.git/*' \
  -not -path '*/node_modules/*' \
  -not -path '*/dist/*' \
  -not -path '*/build/*' \
  -not -path '*/bin/*' \
  -not -path '*/obj/*' \
  -not -path '*/__pycache__/*' \
  -not -path '*/.next/*' \
  -not -path '*/vendor/*' \
  -not -path '*/target/*' \
  | sort | head -300

# 2. Dependencies / package manifest
cat package.json 2>/dev/null \
  || cat requirements.txt 2>/dev/null \
  || cat pyproject.toml 2>/dev/null \
  || cat Cargo.toml 2>/dev/null \
  || cat go.mod 2>/dev/null \
  || cat pom.xml 2>/dev/null \
  || cat build.gradle 2>/dev/null \
  || cat Gemfile 2>/dev/null \
  || find . -maxdepth 3 -name "*.csproj" -not -path '*/node_modules/*' -exec cat {} \; 2>/dev/null \
  || find . -maxdepth 2 \( -name "CMakeLists.txt" -o -name "Makefile" -o -name "configure.ac" \) -exec cat {} \; 2>/dev/null

# 3. Entry points
find . \( -name "index.*" -o -name "main.*" -o -name "app.*" -o -name "server.*" \
          -o -name "Program.cs" -o -name "Startup.cs" \) \
  -not -path '*/node_modules/*' -not -path '*/.git/*' -not -path '*/bin/*' -not -path '*/obj/*' | head -10

# 4. API routes (web/JS/Python/Java/Go/C#)
grep -rl "router\.\|app\.get\|app\.post\|@GetMapping\|@PostMapping\|@app\.route\|@router\.\|FastAPI\|express\|\[ApiController\]\|\[HttpGet\|\[HttpPost\|: ControllerBase\|app\.MapGet\|app\.MapPost" \
  --include="*.ts" --include="*.js" --include="*.py" --include="*.java" --include="*.go" --include="*.cs" . 2>/dev/null \
  | grep -v node_modules | grep -v '/bin/\|/obj/' | head -15

# 5. DB schema / models
find . \( -name "schema.prisma" -o -name "*.sql" -o -name "models.py" \
          -o -name "*.entity.ts" -o -name "schema.rb" -o -name "*.model.ts" \
          -o -name "*.model.go" -o -name "*.schema.ts" -o -iname "*dbcontext*.cs" \
          -o -path "*Migrations*" \) \
  -not -path '*/node_modules/*' -not -path '*/bin/*' -not -path '*/obj/*' | head -10

# 6. Auth files
find . \( -name "*auth*" -o -name "*jwt*" -o -name "*login*" -o -name "*passport*" \
          -o -name "*session*" -o -name "*oauth*" \) \
  -not -path '*/node_modules/*' -not -path '*/.git/*' -not -path '*/bin/*' -not -path '*/obj/*' | head -10

# 7. Frontend framework
grep -r "from 'react'\|from 'vue'\|from '@angular\|from 'svelte'\|from 'next'\|from 'nuxt'" \
  --include="*.ts" --include="*.tsx" --include="*.js" --include="*.jsx" . 2>/dev/null \
  | grep -v node_modules | head -5
find . -name "*.razor" -not -path '*/bin/*' -not -path '*/obj/*' | head -5   # Blazor

# 8. Background jobs / queues
find . \( -name "*worker*" -o -name "*queue*" -o -name "*job*" -o -name "*celery*" -o -iname "*hangfire*" \) \
  -not -path '*/node_modules/*' -not -path '*/.git/*' | head -10

# 9. CLI entry points
grep -rl "click\|argparse\|cobra\|clap\|commander\|yargs\|oclif\|System.CommandLine" \
  --include="*.py" --include="*.go" --include="*.rs" --include="*.ts" --include="*.cs" . 2>/dev/null \
  | grep -v node_modules | head -5

# 10. Infra / DevOps
find . \( -name "Dockerfile" -o -name "docker-compose*" -o -name "*.tf" \
          -o -name "*.yaml" -o -name "*.yml" \) \
  -not -path '*/.git/*' -not -path '*/node_modules/*' | grep -i "k8s\|helm\|deploy\|infra\|docker\|compose\|terraform" | head -10

# 11. Test files count
find . -type f \( -name "*.test.*" -o -name "*.spec.*" -o -name "test_*.py" -o -name "*_test.go" -o -iname "*tests.cs" \) \
  -not -path '*/node_modules/*' -not -path '*/bin/*' -not -path '*/obj/*' | wc -l

# 12. Mobile
find . \( -name "*.swift" -o -name "*.kt" -o -name "*.dart" \) | head -5
grep -r "react-native\|flutter\|expo" package.json 2>/dev/null | head -3
```

Notes on coverage:
- These `find`/`grep` heuristics work reasonably across mainstream ecosystems (JS/TS, Python, Go, Java, C#/.NET incl. Blazor, Ruby, Rust) but C/C++ projects rarely have "API routes," "DB schema," or "auth files" in this sense — for those, steps 4-6 will legitimately return nothing, and that's correct, not a bug. Lean on steps 1-2 (file tree + build manifest: `CMakeLists.txt`/`Makefile`) and actual source reading for C/C++.
- If a language/framework isn't covered above, don't skip the scan — read the actual manifest and file tree (steps 1-2 always work) and reason from there instead of pattern-matching blind.

---

## Project Type Detection + CONTEXT/ User Confirmation

### Step 1 — Detect What the Codebase Has

After the scan, build a detection table:

```
CONTEXT DETECTION RULES — check each against scan results:

| Candidate File     | Evidence Needed                                               |
|--------------------|---------------------------------------------------------------|
| CONTEXT/api.md     | Route files found, or router./app.get/app.post/controller    |
| CONTEXT/auth.md    | Files named *auth*, *jwt*, *login*, *passport*, *oauth*       |
| CONTEXT/database.md| schema.prisma / *.sql / models.py / *.entity.ts / migrations/|
| CONTEXT/frontend.md| React/Vue/Angular/Svelte/Next in deps or .tsx/.vue files     |
| CONTEXT/jobs.md    | workers/ / queues/ / celery / bull / sidekiq / *worker* files|
| CONTEXT/cli.md     | click/argparse/cobra/clap/commander in deps or entry files   |
| CONTEXT/infra.md   | Dockerfile / docker-compose / *.tf / k8s/ / helm/            |
| CONTEXT/testing.md | 10+ test files (.test.ts / .spec.py / test_*.py)             |
| CONTEXT/mobile.md  | .swift / .kt / .dart / react-native / flutter in deps        |

Only add a candidate if evidence exists. No guessing.
```

### Step 2 — Ask User Before Creating CONTEXT/ Files (MANDATORY)

After detection, present the candidates to the user. Do not create CONTEXT/ files without asking.

Message format:
```
I found evidence for these CONTEXT/ docs in your codebase:

  ✅ CONTEXT/api.md       — found route files (src/routes/, controllers/)
  ✅ CONTEXT/database.md  — found schema.prisma + migrations/
  ✅ CONTEXT/auth.md      — found auth.service.ts, jwt.middleware.ts
  ✅ CONTEXT/frontend.md  — found React + src/components/

Should I create these? I'll populate them from your actual code.
(Say "yes", "skip", or name specific ones like "yes api and database only")
```

**Rules:**
- If user says **"yes"** or **"all"** → create all detected candidates
- If user says **"skip"** or **"no"** → skip CONTEXT/ entirely, create only core files
- If user names specific ones → create only those
- If user says **"yes but not frontend"** → exclude what they named
- Never create a CONTEXT/ file the user didn't confirm
- Never create a CONTEXT/ file with no evidence in the codebase

---


