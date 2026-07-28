# teamhub-team

Four skills for running a multi-agent Claude Code team — a human-driven
Team Lead session that plans and assigns work, Developer sessions that
write the code, and Tester sessions that verify it and report bugs — all
coordinated through **TeamHub**, a self-hosted, project-aware MCP server
(SQLite-backed, no Jira required).

This plugin bundles:

- **`team-lead`** — plans a project brief into projects/sprints/tasks,
  assigns work to developer/tester handles, answers blockers.
- **`team-developer`** — pulls assigned tasks, works the code with your
  machine's own git/tools, reports status back to the Lead.
- **`tester`** — pulls assigned test tasks, runs or writes tests against a
  developer's work, files bugs, reports test results back to the Lead.
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

Installs all four skills at once, namespaced as `teamhub-team:team-lead`,
`teamhub-team:team-developer`, `teamhub-team:tester`, and
`teamhub-team:project-planner`.

## Using it

- On the Lead's machine: start `claude`, and either paste a project brief
  ("set this up as project X and break it into tasks") or give direct
  instructions ("create a task called Y and assign it to dev-A").
- On each Developer's machine: start `claude`, register, and check the
  inbox for assigned work.
- On each Tester's machine: start `claude`, register with `role="tester"`,
  and check the inbox for assigned test tasks.

Any human, on either side, can call the underlying TeamHub tools directly
at any time — the skills are a default playbook, not a restriction.

## Operating mode: manual (default) vs auto

Every registration has a `mode`, set at registration or anytime via
`set_mode`:

- **`manual`** (default) — human-supervised, nothing can stop a session
  mid-turn.
- **`auto`** — meaningful only for a headless Developer or Tester, run via
  `teamhub agent --mode auto` (install
  [`@masterdeepak15/teamhub-cli`](https://www.npmjs.com/package/@masterdeepak15/teamhub-cli)
  — no repo checkout needed) or `agents/runner.ts --mode auto` from a
  [claude-team-protocol](https://github.com/masterdeepak15/claude-team-protocol)
  checkout: file edits are auto-approved, and the Lead can genuinely
  interrupt and redirect its in-flight work mid-task via
  `interrupt_developer` (works for any handle, despite the name) — a
  watchdog polls for it and kills/restarts the current turn within seconds.
  Bash commands still require confirmation in both modes, by design —
  TeamHub has no authentication, so removing that gate too would make a
  crafted interrupt reason a real code-execution risk.
