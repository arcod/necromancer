# Roadmap

This file outlines project goals and a rough sequence of development.

**Start simple!**

---

## Vision

<!-- One or two sentences: what is Necromancer, and what should playing it feel like? -->

## Core pillars

<!-- 3–5 things the game must get right. Use these to decide what's in or out. -->

1.
2.
3.

## Out of scope (for now)

<!-- Ideas you're deliberately not doing yet, so they don't creep in. -->

-

---

## Current focus

<!-- The milestone being worked on right now. Claude will stay inside this. -->

**Milestone:** 1 — Prototype

---

## Milestones

Mark tasks `[x]` when done. Add, remove, or reorder milestones freely.

### Milestone 0 — Project setup
- [x] GitHub repo and Claude setup
- [x] Choose Unity version and render pipeline (Unity 6.3 LTS, URP, Entities 1.4)
- [x] Create the Unity project in this repo

### Bare-bones DOTS RTS ✅
**Goal:** Learn Unity, DOTS and working with Claude by building the smallest playable RTS. Built as `DOTS_Scratch`, then promoted to the real project.
- [x] 1. Units move: a unit entity walks to a target position
- [x] 2. RTS camera: pan (WASD / screen edges) and zoom (scroll wheel)
- [x] 3. Selection: click or drag a box to select units, with a highlight on selected units
- [x] 4. Move orders: right-click the ground to send selected units there
- [x] 5. Hex grid: generate a hex tile map as the ground
- [x] 6. Building placement: preview a building snapped to a hex, click to place it, and block that tile
- [x] 7. Horde: spawn enemies that walk toward the player's units; stress-test with thousands
- [x] 8. Combat: health, attacks and death

### Milestone 1 — The Lich wakes
**Goal:** A playable starting economy: the Phylactery, starting minions, real terrain, and the first buildings and resources, on a working clock.
- [ ] 1. Rename placeholders in code to the GAME_MECHANICS.md terms (`Unit` → `Minion`, `Enemy` → the Church's units, etc.)
- [ ] 2. Game clock: tick = 1 real second, game hour = 1 real minute, UI clock (`Day 2 — 14:30`), day/night lighting
- [ ] 3. Terrain to spec: grassland, desert, forest, cliff, water, swamp; resource features (granite, black basalt, iron, anima fumes, eldritch veins, graveyards)
- [ ] 4. Hex pathfinding: avoid forest, cliffs and water; swamp at 50% speed; wraiths may cross water and forest
- [ ] 5. Multi-hex buildings and placement rules: Phylactery (7 hexes, nothing adjacent), Bone Pit (diamond, joins with neighbors), Soul Font (3 hexes around a vertex, 5-tile spacing), Crypt; terrain requirements
- [ ] 6. Economy: ostite from Bone Pits (per hour, trickled per tick), anima cap from Soul Fonts, building costs, resource bar UI
- [ ] 7. Match start: Phylactery placed mid-map with 6 Cultists and 2 Wraiths; Cultists construct buildings; Crypt trains Skeletal Warriors and Skeletal Archers

### Milestone 2 — The Church notices
**Goal:** An opponent and a way to win or lose.
- [ ] Fog of war and per-minion vision radius
- [ ] Church of the First Flame settlements from 30+ tiles out: isolated farms, chapel hamlets, rare walled cities
- [ ] Townsperson, Acolyte and Flame Priest units
- [ ] Raiding parties that grow as the player claims more of the map, ending in a final onslaught
- [ ] Win (all enemy forces destroyed) / lose (Phylactery destroyed); hard limit around hour 84
- [ ] Full-size map (about 300 × 300 tiles) with the performance work that needs

### Milestone 3 — Growing power
**Goal:** The wider economy and tech tiers.
- [ ] Wood (Sawmill), Granite and Black Basalt (Quarry), Iron (Bloomery), Ichor (Eldritch Veins)
- [ ] Soul Font network multiplier and beams
- [ ] Tiers and upgrades (Phylactery tech tiers, Bone Pit tiers)
- [ ] More minions: revived remains, corrupted mages, abominations

---

## Ideas backlog

<!-- Unsorted ideas. Move them into a milestone when ready. -->

-

## Open questions

<!-- Decisions you haven't made yet. -->

-
