# 🕷️ Spyder — Claude AI Skill Marketplace

> HUD-style UI skills for Claude Code & Cowork by [@masterdeepak15](https://github.com/masterdeepak15)

---

## ⚡ Install Skills

### 🔵 jarvis-ui-react
```
/plugin install https://github.com/masterdeepak15/Spyder/raw/main/dist/jarvis-ui-react.plugin
```

### 🟣 jarvis-ui-blazor
```
/plugin install https://github.com/masterdeepak15/Spyder/raw/main/dist/jarvis-ui-blazor.plugin
```

---

## 📦 Skills

### 🔵 `jarvis-ui-react` — v1.1.0
> HUD-style sci-fi React component library — 50+ components

- **npm:** `@masterdeepak15/jarvis-ui`
- **Components:** JButton, JModal, JTable, JNodeGraph, JRadialMenu, JCommandPalette, JGaugeChart, JBootScreen + 40 more

### 🟣 `jarvis-ui-blazor` — v1.0.0
> Cinematic HUD-style Blazor (.NET/C#/Razor) component library — 57+ components

- **NuGet:** `JarvisUI`
- **Components:** JButton, JModal, JTable, JLeafletMap, JGoogleMap, JNodeGraph, JRadialMenu + 50 more

---

## 🗂️ Repo Structure

```
Spyder/
├── marketplace.json          ← skill index
├── dist/
│   ├── jarvis-ui-react.plugin   ← install this
│   └── jarvis-ui-blazor.plugin  ← install this
└── skills/
    ├── jarvis-ui-react/
    │   ├── SKILL.md
    │   ├── .claude-plugin/plugin.json
    │   └── references/  (50 component docs)
    └── jarvis-ui-blazor/
        ├── SKILL.md
        ├── .claude-plugin/plugin.json
        └── reference/   (57 component docs)
```

---

## 🤝 Add Your Skill

1. Fork this repo
2. Add your skill under `skills/your-skill-name/`
3. Package it: `zip -r dist/your-skill-name.plugin skills/your-skill-name/`
4. Add entry to `marketplace.json`
5. Open a PR!

---

Made with ❤️ by [Deepak Chougale](https://github.com/masterdeepak15)
