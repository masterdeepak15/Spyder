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
│   └── jarvis-ui-blazor/
│       ├── SKILL.md
│       ├── .claude-plugin/plugin.json
│       └── reference/       (57 per-component docs)
└── dist/
    ├── jarvis-ui-react.plugin
    └── jarvis-ui-blazor.plugin
```

---

## 🤝 Submit Your Plugin to Spyder

Want to add your own skill to this marketplace?

1. Fork this repo
2. Add your skill folder under `skills/your-skill-name/` with a `SKILL.md` and `.claude-plugin/plugin.json`
3. Add an entry for it in `.claude-plugin/marketplace.json`
4. Open a PR — once merged, your skill is installable via `@spyder`

---

Made with ❤️ by [Deepak Chougale](https://github.com/masterdeepak15)
