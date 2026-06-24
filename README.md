# 🕷️ Spyder — Claude AI Skill Marketplace

A community skill marketplace for Claude AI (Claude Code / Cowork). Browse and install skills to supercharge your Claude sessions.

---

## 📦 Available Skills

| Skill | Description | Install |
|-------|-------------|---------|
| `jarvis-ui-react` | Build beautiful Jarvis-style AI UIs with React + Tailwind | `/plugin install jarvis-ui-react` |
| `jarvis-ui-blazor` | Build Jarvis-style AI UIs with Blazor (.NET) | `/plugin install jarvis-ui-blazor` |

---

## 🚀 How to Install

### Option 1 — Claude Code (recommended)
```bash
/plugin install https://raw.githubusercontent.com/masterdeepak15/Spyder/main/skills/jarvis-ui-react/SKILL.md
```

### Option 2 — Add as a marketplace source
In your Claude Code config or `CLAUDE.md`, add:
```
Marketplace: https://github.com/masterdeepak15/Spyder
```

### Option 3 — Manual install
Copy any `SKILL.md` from the `skills/` folder into your project's `.claude/skills/` directory.

---

## 🛠️ Skill Structure

```
skills/
├── jarvis-ui-react/
│   └── SKILL.md
├── jarvis-ui-blazor/
│   └── SKILL.md
└── ...
```

Each skill is a self-contained `SKILL.md` file with trigger conditions, instructions, and references.

---

## 🤝 Contributing

1. Fork this repo
2. Create your skill under `skills/your-skill-name/SKILL.md`
3. Follow the skill format (see any existing skill as a template)
4. Open a PR!

---

Made with ❤️ by [Deepak](https://github.com/masterdeepak15)
