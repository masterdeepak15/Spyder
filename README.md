# 🕷️ Spyder — Claude Code Skill Marketplace

> HUD-style Jarvis UI skills for Claude Code by [@masterdeepak15](https://github.com/masterdeepak15)

---

## ⚡ Add as Marketplace (one command)

```
/plugin marketplace add masterdeepak15/Spyder
```

Then install any skill:
```
/plugin install jarvis-ui-react@spyder
/plugin install jarvis-ui-blazor@spyder
```

---

## 📦 Skills

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
│   └── marketplace.json     ← Claude Code reads this
├── skills/
│   ├── jarvis-ui-react/
│   │   ├── SKILL.md
│   │   ├── .claude-plugin/plugin.json
│   │   └── references/  (50 component docs)
│   └── jarvis-ui-blazor/
│       ├── SKILL.md
│       ├── .claude-plugin/plugin.json
│       └── reference/   (57 component docs)
└── dist/
    ├── jarvis-ui-react.plugin
    └── jarvis-ui-blazor.plugin
```

---

## 🤝 Submit Your Plugin to Spyder

1. Fork this repo  
2. Add your plugin under `skills/your-plugin-name/`  
3. Add entry to `.claude-plugin/marketplace.json`  
4. Open a PR!

---

Made with ❤️ by [Deepak Chougale](https://github.com/masterdeepak15)
