# teamhub-team

Three skills for running a multi-agent Claude Code team — a human-driven
Team Lead session that plans and assigns work, and one or more Developer
sessions that do the coding — all coordinated through **TeamHub**, a
self-hosted, project-aware MCP server (SQLite-backed, no Jira required).

This plugin bundles:

- **`team-lead`** — plans a project brief into projects/sprints/tasks,
  assigns work to developer handles, answers blockers.
- **`team-developer`** — pulls assigned tasks, works the code with your
  machine's own git/tools, reports status back to the Lead.
- **`project-planner`** — admin/reporting skill for setting up a new
  TeamHub project or getting a task-status health summary.

## Prerequisite: the TeamHub MCP server

These skills call tools like `mcp__teamhub__create_task`,
`mcp__teamhub__check_inbox`, etc. — they assume a `teamhub` MCP server is
running and wired into your `.mcp.json`:

```json
{
  "mcpServers": {
    "teamhub": { "type": "http", "url": "http://<host-lan-ip>:8787/mcp" }
  }
}
```

TeamHub is a small Node/TypeScript MCP server (one process, one SQLite
file) that any team can self-host on one always-on machine. Without it
running, these skills have no tools to call. Set it up once per team, then
every member's Claude Code session — Lead or Developer, on any machine —
points at the same TeamHub URL.

## Installing

```
/plugin install teamhub-team@spyder
```

Installs all three skills at once, namespaced as `teamhub-team:team-lead`,
`teamhub-team:team-developer`, and `teamhub-team:project-planner`.

## Using it

- On the Lead's machine: start `claude`, and either paste a project brief
  ("set this up as project X and break it into tasks") or give direct
  instructions ("create a task called Y and assign it to dev-A").
- On each Developer's machine: start `claude`, register, and check the
  inbox for assigned work.

Any human, on either side, can call the underlying TeamHub tools directly
at any time — the skills are a default playbook, not a restriction.
