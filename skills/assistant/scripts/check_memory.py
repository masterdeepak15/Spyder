#!/usr/bin/env python3
"""Optional safety checker for the assistant memory tree. READ ONLY: changes nothing.

Usage: python -I scripts/check_memory.py [memory_root]
Default root: ~/.assistant/memory
Checks:
  1. Every .md note is reachable from the root INDEX.md by links.
  2. No link points to a missing file.
  3. Every INDEX.md below the root has an "Up:" link on its first line.
  4. projects/<name>/ holds only INDEX.md (code memory belongs in <project>/.claude/).
Exit code 0 = clean, 1 = problems found.
"""
import re
import sys
from pathlib import Path

LINK = re.compile(r"\[[^\]]*\]\(([^)#\s]+)(?:#[^)]*)?\)")


def links(md: Path):
    try:
        text = md.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return []
    out = []
    for target in LINK.findall(text):
        if re.match(r"^[a-z]+://", target) or target.startswith("mailto:"):
            continue
        out.append(target)
    return out


def main():
    root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path.home() / ".assistant" / "memory"
    root = root.expanduser().resolve()
    index = root / "INDEX.md"
    if not index.is_file():
        print(f"BAD  no root INDEX.md in {root}")
        return 1

    problems = []
    seen, stack = set(), [index]
    while stack:
        cur = stack.pop()
        if cur in seen:
            continue
        seen.add(cur)
        for t in links(cur):
            dest = (cur.parent / t).resolve()
            if dest.suffix.lower() == ".md":
                if not dest.is_file():
                    problems.append(f"BROKEN  {cur.relative_to(root)} -> {t}")
                else:
                    stack.append(dest)
            elif dest.is_dir():
                idx = dest / "INDEX.md"
                if idx.is_file():
                    stack.append(idx)
                else:
                    problems.append(f"BROKEN  {cur.relative_to(root)} -> {t} (no INDEX.md)")

    for md in sorted(root.rglob("*.md")):
        if md.resolve() not in seen:
            problems.append(f"ORPHAN  {md.relative_to(root)} (no link reaches it)")

    for idx in sorted(root.rglob("INDEX.md")):
        if idx.parent == root:
            continue
        lines = idx.read_text(encoding="utf-8", errors="replace").splitlines()
        first = next((l for l in lines if l.strip()), "")
        if "Up:" not in first:
            problems.append(f"NO-UP  {idx.relative_to(root)} (first line has no Up: link)")

    proj = root / "projects"
    if proj.is_dir():
        for d in sorted(p for p in proj.iterdir() if p.is_dir()):
            extra = [f.relative_to(root) for f in d.rglob("*") if f.is_file() and not (f.parent == d and f.name == "INDEX.md")]
            for f in extra:
                problems.append(f"MISPLACED  {f} (project files belong in <project>/.claude/, keep only INDEX.md here)")

    if problems:
        print("\n".join(problems))
        print(f"\n{len(problems)} problem(s). Nothing was changed.")
        return 1
    print(f"OK  {len(seen)} index/notes reachable, no problems. Nothing was changed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())