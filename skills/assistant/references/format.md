# Note and index format

## A note
File: `<branch>/<short-name>.md`, lowercase with dashes.

```
---
title: Agent-memory skill
summary: One line saying what the note is about.
updated: 2026-10-25
source: user
related: projects/friend/INDEX.md, people/priya.md
---

Short body, plain sentences, under about 150 words. Date facts that can go
stale ("checked 2026-10-25").
```

- `source`: `user` (user said it) or `agent` (you worked it out or read it).
- `related`: paths from the memory root. Add the link on both notes.

## An index
File: `<branch>/INDEX.md`. First line is the way up, then one line per note or sub-branch.

```
# Projects
Up: [Memory](../INDEX.md)

- [Friend](friend/INDEX.md) - the Friend AI assistant project
- [Agent-memory skill](agent-memory.md) - skill that keeps project decisions in files
```

Line = `[title](relative path) - summary`. Sub-branch links point to that branch's `INDEX.md`.

## The root index
`INDEX.md` in the memory root has no `Up:` line and lists the top branches. It starts almost empty and grows as topics appear.