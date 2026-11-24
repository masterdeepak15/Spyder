# 🕷️ Spyder — Claude Code Skill Marketplace

> HUD-style Jarvis UI skills for Claude Code by [@masterdeepak15](https://github.com/masterdeepak15)

---

## ⚡ How to Use This Marketplace

### Step 1 — Add Spyder as a marketplace source

Open Claude Code and run:

```
/plugin marketplace add masterdeepak15/Spyder
```

> This registers Spyder as a known marketplace called `spyder`. You only need to do this once.

---

### Step 2 — Install skills

Pick whichever skills you need:

```
/plugin install jarvis-ui-react@spyder
```
```
/plugin install jarvis-ui-blazor@spyder
```
```
/plugin install teamhub-team@spyder
```
```
/plugin install agent-memory@spyder
```
```
/plugin install assistant@spyder
```
```
/plugin install dotnet-auth-standards@spyder
```

---

### Step 3 — Verify installation

After installing, confirm the skill is active:

```
/plugin
```

You should see `jarvis-ui-react` or `jarvis-ui-blazor` listed under installed plugins. Claude will now automatically use the skill when you work with Jarvis UI components.

---

### Alternative — Direct install (no marketplace step)

If you just want one skill without adding the full marketplace:

```
/plugin install https://github.com/masterdeepak15/Spyder/raw/main/dist/jarvis-ui-react.plugin
```
```
/plugin install https://github.com/masterdeepak15/Spyder/raw/main/dist/jarvis-ui-blazor.plugin
```
```
/plugin install https://github.com/masterdeepak15/Spyder/raw/main/dist/teamhub-team.plugin
```

---

## 📦 Available Skills

### 🔵 `jarvis-ui-react` — v1.1.0
> HUD-style sci-fi React component library — 50+ components

- **npm:** `@masterdeepak15/jarvis-ui`
- **Demo:** https://jarvis-ui-docs.vercel.app/
- **Components:** JButton, JModal, JTable, JNodeGraph, JRadialMenu, JCommandPalette, JGaugeChart, JBootScreen + 40 more

### 🟣 `jarvis-ui-blazor` — v1.0.0
> Cinematic HUD-style Blazor (.NET/C#/Razor) component library — 57+ components

- **NuGet:** `JarvisUI`
- **Components:** JButton, JModal, JTable, JLeafletMap, JGoogleMap, JNodeGraph, JRadialMenu + 50 more

### 🟢 `teamhub-team` — v1.2.0
> Multi-agent Team Lead / Developer / Tester / Project Planner skills,
> backed by a self-hosted TeamHub MCP server (no Jira required)

- **Bundles:** `team-lead`, `team-developer`, `tester`, `project-planner`
- **Requires:** the `teamhub` MCP server running and wired into `.mcp.json`
  — see `skills/teamhub-team/README.md` after installing

### 🧠 `agent-memory` — v1.0.0
> Persistent, stack-agnostic project memory for AI coding sessions

- Creates a `.claude/` folder at the project root: INDEX, AGENT rules/state,
  per-session files (`sessions/`), per-task files (`tasks/`), ARCHITECTURE,
  CODEBASE_MAP, DECISIONS, and optional `CONTEXT/` files
- Progressive-disclosure design — slim core `SKILL.md` + on-demand `references/`
  for low token use
- Works across C#, C++, Python, Go, React, Next.js, Blazor, Ruby, Java, Rust

---

### 🤖 `assistant` — v2.1.0
> Personal AI memory in one skill: a living tree of linked notes about you, plus a `.claude/` folder for every coding project

- **Automatic** — no slash command needed. It loads at the start of every conversation, saves lasting facts as they happen, and runs an end-of-turn check
- **Dynamic memory tree** in `~/.assistant/memory/`: no fixed folders. New topics get their own folder and `INDEX.md`, linked from the parent at once with an `Up:` link back, so every note is reachable from the root `INDEX.md`
- **Coding projects** (built in): a `.claude/` folder at the project root with INDEX, AGENT rules/state, sessions, tasks, ARCHITECTURE, CODEBASE_MAP, DECISIONS and optional `CONTEXT/`. Project files stay in the project; the personal tree keeps only one pointer note per project
- **Optional safety checker**: `scripts/check_memory.py` (read only, Python) reports orphan notes, broken links and misplaced project files
- Works in Claude Code, Claude Desktop or any AI that can read and write files. Plain Markdown, no runtime needed
- `agent-memory` stays available for existing users; `assistant` v2 includes the same `.claude/` flow

---

### `dotnet-auth-standards` - v1.0.0
> Opinionated .NET / ASP.NET Core standards for auth, APIs, webhooks, MCP and production code

- **Covers:** OAuth 2.0/2.1, OpenID Connect, JWT and bearer tokens, DPoP, REST/HTTP semantics, API design, webhooks, MCP servers and clients, TLS/mTLS, API security hardening, production operations, and C# coding standards (naming, OOP, SOLID)
- **Automatic** - loads when you write or review C# / ASP.NET Core code that touches these areas, even if you do not name an RFC
- **Contents:** 10 reference guides in `reference/`, a review checklist in `checklists/`, and compiled, self-tested samples in `examples/`
- **Requires:** nothing to install; the samples need the .NET SDK only if you want to build them

---

## 🗂️ Repo Structure

```
Spyder/
├── .claude-plugin/
│   └── marketplace.json     ← Claude Code reads this to discover plugins
├── skills/
│   ├── jarvis-ui-react/
│   │   ├── SKILL.md
│   │   ├── .claude-plugin/plugin.json
│   │   └── references/      (50 per-component docs)
│   ├── jarvis-ui-blazor/
│   │   ├── SKILL.md
│   │   ├── .claude-plugin/plugin.json
│   │   └── reference/       (57 per-component docs)
│   ├── teamhub-team/
│   │   ├── README.md
│   │   ├── .claude-plugin/plugin.json
│   │   └── skills/           (team-lead, team-developer, tester, project-planner)
│   ├── agent-memory/
│   │   ├── SKILL.md
│   │   ├── .claude-plugin/plugin.json
│   │   └── references/       (templates, quick-reference, project-scan, lifecycle)
│   ├── assistant/
│   │   ├── SKILL.md
│   │   ├── .claude-plugin/plugin.json
│   │   ├── references/       (format, templates, onboarding, code-projects, code-templates)
│   │   └── scripts/          (check_memory.py, optional read-only checker)
│   └── dotnet-auth-standards/
│       ├── SKILL.md
│       ├── .claude-plugin/plugin.json
│       ├── reference/      (10 guides: OAuth/OIDC, tokens, REST, API design, webhooks, MCP, TLS, security, operations, coding standards)
│       ├── checklists/      (code-review.md)
│       └── examples/        (compiled, self-tested C# samples)
└── dist/
    ├── jarvis-ui-react.plugin
    ├── jarvis-ui-blazor.plugin
    └── teamhub-team.plugin
```

---

## 🤝 Submit Your Plugin to Spyder

Want to add your own skill to this marketplace?

1. Fork this repo
2. Add your skill folder under `skills/your-skill-name/` with a `SKILL.md` and `.claude-plugin/plugin.json`
3. Add an entry for it in `.claude-plugin/marketplace.json`
4. Open a PR — once merged, your skill is installable via `@spyder`

---

## License

MIT — see `LICENSE`.

---

Made with ❤️ by [Deepak Chougale](https://github.com/masterdeepak15)
