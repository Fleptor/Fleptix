---
trigger: always_on
---

---
name: fleptix-frontend
description: UI/visual design conventions for Fleptix Observer. Load when generating or editing Razor views, layouts, or CSS.
---

# Fleptix Frontend Design Conventions

## Design language: dense data-tool, not marketing site
Fleptix is an operational tool. Never generate hero sections, large
gradient stat cards, oversized centered icons, or marketing-style empty
states. Prioritize information density over whitespace.

## Layout grammar
- Persistent left sidebar: icon + label nav (Dashboard, Containers, Settings).
- Main content: a DATA TABLE for the container list — Name, Image, Status,
  CPU %, Memory, Ports columns. Not a card grid.
- Row-level action icons (start/stop/restart) inline, right-aligned —
  small icon buttons, never full-width colored buttons.
- Top bar: search/filter input only. No hero text.
- Status as a small colored pill (green = running, gray = stopped,
  amber = restarting). Never color the full row/card.

## Typography & color
- System sans-serif for UI text.
- Monospace font for container IDs, image tags, and ports — always.
- Palette: neutral white/light-gray background, near-black text.
  Accent: navy (#1E2761) / ice-blue (#CADCFC) — Fleptix's own identity,
  not any other product's brand color.

## Explicitly avoid
- Bootstrap default `.card` with shadow + centered icon + heading.
- Large rounded gradient stat cards.
- Any hero/jumbotron section.
- Emoji as icons — use Bootstrap Icons (already available with Bootstrap).