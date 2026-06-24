# 🕷️ Spyder — Claude AI Skill Marketplace

> HUD-style UI skills for Claude Code & Cowork by [@masterdeepak15](https://github.com/masterdeepak15)

---

## ⚡ Quick Install

### Add this entire marketplace (one command)
```
/plugin install https://github.com/masterdeepak15/Spyder
```

### Install individual skills
```bash
# React (50+ components)
/plugin install https://raw.githubusercontent.com/masterdeepak15/Spyder/main/skills/jarvis-ui-react/SKILL.md

# Blazor (.NET)
/plugin install https://raw.githubusercontent.com/masterdeepak15/Spyder/main/skills/jarvis-ui-blazor/SKILL.md
```

---

## 📦 Skills

### 🔵 `jarvis-ui-react` — v1.1.0
> HUD-style sci-fi React component library with 50+ components

- **npm:** `@masterdeepak15/jarvis-ui`
- **Demo:** https://jarvis-ui-docs.vercel.app/
- **Components:** JButton, JModal, JTable, JNodeGraph, JRadialMenu, JCommandPalette, JGaugeChart, JBootScreen, and 40+ more
- **Install:**
  ```
  /plugin install https://raw.githubusercontent.com/masterdeepak15/Spyder/main/skills/jarvis-ui-react/SKILL.md
  ```

---

### 🟣 `jarvis-ui-blazor` — v1.0.0
> Cinematic HUD-style Blazor (.NET/C#/Razor) component library

- **NuGet:** `JarvisUI`
- **Components:** JButton, JModal, JTable, JLeafletMap, JNodeGraph, JRadialMenu, JCommandPalette, and 50+ more
- **Install:**
  ```
  /plugin install https://raw.githubusercontent.com/masterdeepak15/Spyder/main/skills/jarvis-ui-blazor/SKILL.md
  ```

---

## 🗂️ Repo Structure

```
Spyder/
├── marketplace.json          ← machine-readable skill index
├── README.md
└── skills/
    ├── jarvis-ui-react/
    │   ├── SKILL.md          ← main skill file
    │   ├── .claude-plugin/
    │   │   └── plugin.json
    │   └── references/       ← per-component docs (50 files)
    └── jarvis-ui-blazor/
        ├── SKILL.md
        ├── .claude-plugin/
        │   └── plugin.json
        └── reference/        ← per-component docs (57 files)
```

---

## 🤝 Add Your Skill to This Marketplace

1. Fork this repo
2. Add your skill under `skills/your-skill-name/`
   ```
   skills/your-skill-name/
   ├── SKILL.md
   └── .claude-plugin/
       └── plugin.json
   ```
3. Add your entry to `marketplace.json`
4. Open a PR!

**`plugin.json` format:**
```json
{
  "name": "your-skill-name",
  "description": "What your skill does",
  "version": "1.0.0",
  "author": {
    "name": "Your Name",
    "email": "you@example.com"
  }
}
```

---

Made with ❤️ by [Deepak Chougale](https://github.com/masterdeepak15)
