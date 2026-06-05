# Design Tokens AI Guide

**ATTENTION AI AGENT:** Reference catalog of Creatio `--crt-*` design tokens. Use these token names and default values exactly.

**Audience:** Any AI guide or task that needs Creatio `--crt-*` design token names and values.

**Scope:** All `--crt-*` design tokens: names, default-theme values, naming conventions, and where each token is defined (its scope).

---

## How tokens are scoped

These tokens are provided by the active Creatio theme at runtime: **primitives** (spacing, radius, font scales, weights, base + glass colors) are declared on `:root`; **palettes**, **semantic**, and **typography** tokens are declared per theme on the active theme's root class. Both `:root` and the theme root are ancestors of every component, so all tokens here inherit down the DOM tree.

The **Default** column lists each token's literal value under Creatio's default theme. Use it as the `var(--crt-..., <fallback>)` second argument so the value still renders where no Creatio theme is applied (isolated tests, non-themed hosts).

> A `--crt-*` token NOT listed in this catalog may be defined only in some other scope (for example, the design-time properties panel uses panel-only `--crt-size-*`). Outside that scope — e.g. inside a runtime shadow root — it resolves to nothing. Use only the tokens documented here.

## Naming conventions

- **Intents:** `primary`, `secondary`, `accent`, `error`, `success`.
- **`on-<intent>`** = text/icon color to place **on top of** that intent's fill (ensures contrast).
- **`subtle`** = lightest tint, **`soft`** = light tint; `-hover` / `-selected` = interaction states.
- Most components only need `text-body`, `text-muted`, `background-base`, `border-base`; reach for intent variants for emphasis, states, and messaging.
- **Prefer semantic tokens over raw palette/primitive tokens** — semantic tokens already encode the right shade per theme and adapt automatically.

---

## Semantic tokens

### Colors · Text

| Purpose | Token | Default |
|---------|-------|---------|
| Default body text | `--crt-color-text-body` | `#181818` |
| Secondary / muted text | `--crt-color-text-muted` | `#606060` |
| Heading text | `--crt-color-text-heading` | `#0d2e4e` |
| Disabled text | `--crt-color-text-disabled` | `#757575` |
| Link text | `--crt-color-text-link` | `#004fd6` |
| Link text (hover) | `--crt-color-text-link-hover` | `#0041b5` |
| Actionable text | `--crt-color-text-action` | `#0d2e4e` |
| Actionable text (hover) | `--crt-color-text-action-hover` | `#004fd6` |
| Primary-intent text | `--crt-color-text-primary` | `#004fd6` |
| Text on primary fill | `--crt-color-text-on-primary` | `#ffffff` |
| Text on primary subtle fill | `--crt-color-text-on-primary-subtle` | `#001c5a` |
| Text on primary soft fill | `--crt-color-text-on-primary-soft` | `#001c5a` |
| Secondary-intent text | `--crt-color-text-secondary` | `#0d2e4e` |
| Text on secondary fill | `--crt-color-text-on-secondary` | `#ffffff` |
| Text on secondary subtle fill | `--crt-color-text-on-secondary-subtle` | `#001b37` |
| Text on secondary soft fill | `--crt-color-text-on-secondary-soft` | `#001b37` |
| Accent-intent text | `--crt-color-text-accent` | `#ff4013` |
| Text on accent fill | `--crt-color-text-on-accent` | `#ffffff` |
| Text on accent subtle fill | `--crt-color-text-on-accent-subtle` | `#550b00` |
| Text on accent soft fill | `--crt-color-text-on-accent-soft` | `#550b00` |
| Error text | `--crt-color-text-error` | `#d2310d` |
| Text on error fill | `--crt-color-text-on-error` | `#ffffff` |
| Text on error subtle fill | `--crt-color-text-on-error-subtle` | `#4d0900` |
| Text on error soft fill | `--crt-color-text-on-error-soft` | `#4d0900` |
| Success text | `--crt-color-text-success` | `#0b8500` |
| Text on success fill | `--crt-color-text-on-success` | `#ffffff` |
| Text on success subtle fill | `--crt-color-text-on-success-subtle` | `#013000` |
| Text on success soft fill | `--crt-color-text-on-success-soft` | `#013000` |

### Colors · Background

| Purpose | Token | Default |
|---------|-------|---------|
| Component / page background | `--crt-color-background-base` | `#ffffff` |
| Canvas (translucent base) | `--crt-color-background-canvas` | `rgba(255,255,255,0.9)` |
| Hover surface (neutral) | `--crt-color-background-interaction-hover` | `#eff4fb` |
| Selected surface (neutral) | `--crt-color-background-interaction-selected` | `#e3ebfa` |
| Disabled surface | `--crt-color-background-disabled` | `#ededed` |
| Primary fill | `--crt-color-background-primary` | `#004fd6` |
| Primary fill (hover) | `--crt-color-background-primary-hover` | `#0041b5` |
| Primary fill (selected) | `--crt-color-background-primary-selected` | `#003495` |
| Primary subtle | `--crt-color-background-primary-subtle` | `#f7f8fb` |
| Primary subtle (hover) | `--crt-color-background-primary-subtle-hover` | `#eff4fb` |
| Primary subtle (selected) | `--crt-color-background-primary-subtle-selected` | `#e3ebfa` |
| Primary soft | `--crt-color-background-primary-soft` | `#e3ebfa` |
| Primary soft (hover) | `--crt-color-background-primary-soft-hover` | `#cbdbf8` |
| Primary soft (selected) | `--crt-color-background-primary-soft-selected` | `#9bbaf2` |
| Secondary fill | `--crt-color-background-secondary` | `#0d2e4e` |
| Secondary fill (hover) | `--crt-color-background-secondary-hover` | `#072949` |
| Secondary fill (selected) | `--crt-color-background-secondary-selected` | `#032443` |
| Secondary subtle | `--crt-color-background-secondary-subtle` | `#f6f7f8` |
| Secondary subtle (hover) | `--crt-color-background-secondary-subtle-hover` | `#eef0f2` |
| Secondary subtle (selected) | `--crt-color-background-secondary-subtle-selected` | `#e1e5e9` |
| Secondary soft | `--crt-color-background-secondary-soft` | `#e1e5e9` |
| Secondary soft (hover) | `--crt-color-background-secondary-soft-hover` | `#c8cfd7` |
| Secondary soft (selected) | `--crt-color-background-secondary-soft-selected` | `#96a4b3` |
| Accent fill | `--crt-color-background-accent` | `#ff4013` |
| Accent fill (hover) | `--crt-color-background-accent-hover` | `#d42d00` |
| Accent fill (selected) | `--crt-color-background-accent-selected` | `#a72100` |
| Accent subtle | `--crt-color-background-accent-subtle` | `#fdf8f7` |
| Accent subtle (hover) | `--crt-color-background-accent-subtle-hover` | `#fef4f1` |
| Accent subtle (selected) | `--crt-color-background-accent-subtle-selected` | `#ffece7` |
| Accent soft | `--crt-color-background-accent-soft` | `#ffece7` |
| Accent soft (hover) | `--crt-color-background-accent-soft-hover` | `#ffddd5` |
| Accent soft (selected) | `--crt-color-background-accent-soft-selected` | `#ffbeaf` |
| Error fill | `--crt-color-background-error` | `#d2310d` |
| Error fill (hover) | `--crt-color-background-error-hover` | `#b12200` |
| Error fill (selected) | `--crt-color-background-error-selected` | `#8e1900` |
| Error subtle | `--crt-color-background-error-subtle` | `#fcf8f7` |
| Error subtle (hover) | `--crt-color-background-error-subtle-hover` | `#fbf2f0` |
| Error subtle (selected) | `--crt-color-background-error-subtle-selected` | `#fbe9e5` |
| Error soft | `--crt-color-background-error-soft` | `#fbe9e5` |
| Error soft (hover) | `--crt-color-background-error-soft-hover` | `#f9d7cf` |
| Error soft (selected) | `--crt-color-background-error-soft-selected` | `#f4b2a3` |
| Success fill | `--crt-color-background-success` | `#0b8500` |
| Success fill (hover) | `--crt-color-background-success-hover` | `#076e00` |
| Success fill (selected) | `--crt-color-background-success-selected` | `#045900` |
| Success subtle | `--crt-color-background-success-subtle` | `#f7f9f7` |
| Success subtle (hover) | `--crt-color-background-success-subtle-hover` | `#f1f6f0` |
| Success subtle (selected) | `--crt-color-background-success-subtle-selected` | `#e6f0e5` |
| Success soft | `--crt-color-background-success-soft` | `#e6f0e5` |
| Success soft (hover) | `--crt-color-background-success-soft-hover` | `#d1e4ce` |
| Success soft (selected) | `--crt-color-background-success-soft-selected` | `#a6cda2` |

### Colors · Border

| Purpose | Token | Default |
|---------|-------|---------|
| Default border | `--crt-color-border-base` | `#dfdfdf` |
| Muted border | `--crt-color-border-muted` | `#ededed` |
| Selected / focused border | `--crt-color-border-selected` | `#004fd6` |
| Disabled border | `--crt-color-border-disabled` | `#dfdfdf` |
| Primary border | `--crt-color-border-primary` | `#004fd6` |
| Primary border (subtle) | `--crt-color-border-primary-subtle` | `#cbdbf8` |
| Primary border (soft) | `--crt-color-border-primary-soft` | `#9bbaf2` |
| Secondary border | `--crt-color-border-secondary` | `#0d2e4e` |
| Secondary border (subtle) | `--crt-color-border-secondary-subtle` | `#c8cfd7` |
| Secondary border (soft) | `--crt-color-border-secondary-soft` | `#96a4b3` |
| Accent border | `--crt-color-border-accent` | `#ff4013` |
| Accent border (subtle) | `--crt-color-border-accent-subtle` | `#ffddd5` |
| Accent border (soft) | `--crt-color-border-accent-soft` | `#ffbeaf` |
| Error border | `--crt-color-border-error` | `#d2310d` |
| Error border (subtle) | `--crt-color-border-error-subtle` | `#f9d7cf` |
| Error border (soft) | `--crt-color-border-error-soft` | `#f4b2a3` |
| Success border | `--crt-color-border-success` | `#0b8500` |
| Success border (subtle) | `--crt-color-border-success-subtle` | `#d1e4ce` |
| Success border (soft) | `--crt-color-border-success-soft` | `#a6cda2` |

### Colors · Icon

| Purpose | Token | Default |
|---------|-------|---------|
| Base icon | `--crt-color-icon-base` | `#0d2e4e` |
| Actionable icon | `--crt-color-icon-action` | `#0d2e4e` |
| Actionable icon (hover) | `--crt-color-icon-action-hover` | `#004fd6` |
| Muted icon | `--crt-color-icon-muted` | `#606060` |
| Disabled icon | `--crt-color-icon-disabled` | `#757575` |
| Primary icon | `--crt-color-icon-primary` | `#004fd6` |
| Icon on primary fill | `--crt-color-icon-on-primary` | `#ffffff` |
| Icon on primary subtle fill | `--crt-color-icon-on-primary-subtle` | `#001c5a` |
| Icon on primary soft fill | `--crt-color-icon-on-primary-soft` | `#001c5a` |
| Secondary icon | `--crt-color-icon-secondary` | `#0d2e4e` |
| Icon on secondary fill | `--crt-color-icon-on-secondary` | `#ffffff` |
| Icon on secondary subtle fill | `--crt-color-icon-on-secondary-subtle` | `#001b37` |
| Icon on secondary soft fill | `--crt-color-icon-on-secondary-soft` | `#001b37` |
| Accent icon | `--crt-color-icon-accent` | `#ff4013` |
| Icon on accent fill | `--crt-color-icon-on-accent` | `#ffffff` |
| Icon on accent subtle fill | `--crt-color-icon-on-accent-subtle` | `#550b00` |
| Icon on accent soft fill | `--crt-color-icon-on-accent-soft` | `#550b00` |
| Error icon | `--crt-color-icon-error` | `#d2310d` |
| Icon on error fill | `--crt-color-icon-on-error` | `#ffffff` |
| Icon on error subtle fill | `--crt-color-icon-on-error-subtle` | `#4d0900` |
| Icon on error soft fill | `--crt-color-icon-on-error-soft` | `#4d0900` |
| Success icon | `--crt-color-icon-success` | `#0b8500` |
| Icon on success fill | `--crt-color-icon-on-success` | `#ffffff` |
| Icon on success subtle fill | `--crt-color-icon-on-success-subtle` | `#013000` |
| Icon on success soft fill | `--crt-color-icon-on-success-soft` | `#013000` |

### Colors · Effect

| Purpose | Token | Default |
|---------|-------|---------|
| Drop shadow color | `--crt-color-shadow` | `rgba(24,24,24,0.1)` |

### Typography — semantic roles

Each role exposes `-font-family`, `-font-size`, `-font-weight`, `-line-height`, and `-letter-spacing` tokens. Use the role that matches the UI element.

| Role | When to use | size / weight / line-height |
|------|-------------|------------------------------|
| `--crt-body-1-*` | Primary body text | 14 / 500 / 20 |
| `--crt-body-2-*` | **Default UI text** (labels, values, table cells) | 13 / 500 / 20 |
| `--crt-caption-*` | Captions, helper text, field labels | 12 / 500 / 16 |
| `--crt-headline-4-*` | Small section title | 16 / 500 / 20 |
| `--crt-headline-3-*` | Section title | 18 / 500 / 24 |
| `--crt-headline-2-*` | Card / widget title | 20 / 500 / 26 |
| `--crt-headline-1-*` | Page-level title | 24 / 600 / 32 |
| `--crt-button-*` | Button label | 14 / 500 / 16 |
| `--crt-overline-*` | Overline / tiny uppercase label | 10 / 400 / 12 |

### Spacing & radius — primitives (on `:root`)

> Use `--crt-spacing-*` for gaps/padding/margins.

| Token | Default | | Token | Default |
|-------|---------|---|-------|---------|
| `--crt-spacing-100` | 4px | | `--crt-radius-100` | 4px |
| `--crt-spacing-200` | 8px | | `--crt-radius-150` | 6px |
| `--crt-spacing-300` | 12px | | `--crt-radius-200` | 8px |
| `--crt-spacing-400` | 16px | | `--crt-radius-300` | 12px |
| `--crt-spacing-500` | 20px | | `--crt-radius-infinite` | 999px |
| `--crt-spacing-600` | 24px | | | |

---

## Full reference

The semantic set above covers most components. Use this section when you need a shade or role not listed there.

### Base colors (on `:root`)

| Token | Default |
|-------|---------|
| `--crt-color-base-light` | `#ffffff` |
| `--crt-color-base-dark` | `#181818` |

### Palettes — `--crt-palette-<hue>-<shade>`

Hues: `primary`, `secondary`, `accent`, `neutral`, `error`, `success`.
Shades: `10, 25, 50, 100, 200, 300, 400, 500, 600, 700, 800, 900` (`500` is the base).

Prefer a semantic token over a raw palette token. Use palette only for bespoke visuals (charts, custom badges) where no semantic role fits. Default values per `--crt-palette-<hue>-<shade>`:

| Shade | primary | secondary | accent | neutral | error | success |
|-------|---------|-----------|--------|---------|-------|---------|
| 10 | `#f7f8fb` | `#f6f7f8` | `#fdf8f7` | `#f9f9f9` | `#fcf8f7` | `#f7f9f7` |
| 25 | `#eff4fb` | `#eef0f2` | `#fef4f1` | `#f4f4f4` | `#fbf2f0` | `#f1f6f0` |
| 50 | `#e3ebfa` | `#e1e5e9` | `#ffece7` | `#ededed` | `#fbe9e5` | `#e6f0e5` |
| 100 | `#cbdbf8` | `#c8cfd7` | `#ffddd5` | `#dfdfdf` | `#f9d7cf` | `#d1e4ce` |
| 200 | `#9bbaf2` | `#96a4b3` | `#ffbeaf` | `#c4c4c4` | `#f4b2a3` | `#a6cda2` |
| 300 | `#6c99ea` | `#667b91` | `#ff9c86` | `#a9a9a9` | `#eb8c77` | `#7cb575` |
| 400 | `#3d76e1` | `#39536f` | `#ff7659` | `#8e8e8e` | `#e0634a` | `#4f9d47` |
| 500 | `#004fd6` | `#0d2e4e` | `#ff4013` | `#757575` | `#d2310d` | `#0b8500` |
| 600 | `#0041b5` | `#072949` | `#d42d00` | `#606060` | `#b12200` | `#076e00` |
| 700 | `#003495` | `#032443` | `#a72100` | `#4c4c4c` | `#8e1900` | `#045900` |
| 800 | `#002877` | `#001f3e` | `#7d1600` | `#393939` | `#6d1100` | `#024400` |
| 900 | `#001c5a` | `#001b37` | `#550b00` | `#272727` | `#4d0900` | `#013000` |

### Glass / glassmorphic primitives (on `:root`)

For translucent / glassmorphic surfaces only.

| Shade | `--crt-glass-color-light-<shade>` | `--crt-glass-color-dark-<shade>` |
|-------|-----------------------------------|----------------------------------|
| 50 | `rgba(255,255,255,0.05)` | `rgba(24,24,24,0.05)` |
| 100 | `rgba(255,255,255,0.1)` | `rgba(24,24,24,0.1)` |
| 200 | `rgba(255,255,255,0.2)` | `rgba(24,24,24,0.2)` |
| 300 | `rgba(255,255,255,0.3)` | `rgba(24,24,24,0.3)` |
| 400 | `rgba(255,255,255,0.4)` | `rgba(24,24,24,0.4)` |
| 500 | `rgba(255,255,255,0.5)` | `rgba(24,24,24,0.5)` |
| 600 | `rgba(255,255,255,0.6)` | `rgba(24,24,24,0.6)` |
| 700 | `rgba(255,255,255,0.7)` | `rgba(24,24,24,0.7)` |
| 800 | `rgba(255,255,255,0.8)` | `rgba(24,24,24,0.8)` |
| 900 | `rgba(255,255,255,0.9)` | `rgba(24,24,24,0.9)` |

| Token | Default |
|-------|---------|
| `--crt-color-text-glassmorphic-base` | `#ffffff` |
| `--crt-color-text-glassmorphic-muted` | `#bababa` |
| `--crt-color-text-glassmorphic-disabled` | `rgba(255,255,255,0.6)` |
| `--crt-color-icon-glassmorphic-base` | `#ffffff` |
| `--crt-color-icon-glassmorphic-muted` | `#bababa` |

### Full typography roles

Each role exposes `-font-family`, `-font-size`, `-font-weight`, `-line-height`, `-letter-spacing`.

| Role | size / weight / line-height |
|------|-----------------------------|
| `--crt-large-1-*` | 54 / 500 / 70 |
| `--crt-large-2-*` | 36 / 500 / 44 |
| `--crt-large-3-*` | 32 / 500 / 40 |
| `--crt-large-4-*` | 28 / 500 / 36 |
| `--crt-headline-1-*` | 24 / 600 / 32 |
| `--crt-headline-2-*` | 20 / 500 / 26 |
| `--crt-headline-3-*` | 18 / 500 / 24 |
| `--crt-headline-4-*` | 16 / 500 / 20 |
| `--crt-headline-5-*` | 14 / 500 / 18 |
| `--crt-body-1-*` | 14 / 500 / 20 |
| `--crt-body-2-*` | 13 / 500 / 20 |
| `--crt-caption-*` | 12 / 500 / 16 |
| `--crt-button-*` | 14 / 500 / 16 |
| `--crt-button-small-*` | 12 / 500 / 16 |
| `--crt-overline-*` | 10 / 400 / 12 |

- `-letter-spacing`: `0` for every role except `--crt-overline-*` (`0.05em`).
- `-font-family`: `--crt-font-family-heading` for `large-*` and `headline-*`; `--crt-font-family-body` for `body-*`, `caption`, `button`, `button-small`, `overline`. Both default to `'Montserrat', sans-serif`.

### Primitive scales (on `:root`)

`--crt-spacing-<n>` and `--crt-radius-<n>` (a dash means that step does not exist):

| n | spacing | radius |
|------|---------|--------|
| 0 | 0 | 0 |
| 50 | 2px | 2px |
| 100 | 4px | 4px |
| 150 | 6px | 6px |
| 200 | 8px | 8px |
| 250 | 10px | 10px |
| 300 | 12px | 12px |
| 400 | 16px | 16px |
| 500 | 20px | — |
| 600 | 24px | 24px |
| 700 | 28px | — |
| 800 | 32px | 32px |
| 900 | 36px | — |
| 1000 | 40px | — |
| infinite | — | 999px |

`--crt-font-size-<n>` and `--crt-line-height-<n>`:

| n | font-size | line-height |
|------|-----------|-------------|
| 50 | 10px | 12px |
| 100 | 12px | 16px |
| 200 | 13px | 18px |
| 300 | 14px | 20px |
| 400 | 16px | 24px |
| 500 | 18px | 26px |
| 600 | 20px | 32px |
| 700 | 24px | 36px |
| 800 | 28px | 40px |
| 900 | 32px | 44px |
| 1000 | 36px | 70px |
| 1100 | 54px | — |

### Font weights

`--crt-font-weight-<name>`:

| Token | Default |
|-------|---------|
| `--crt-font-weight-thin` | 200 |
| `--crt-font-weight-light` | 300 |
| `--crt-font-weight-regular` | 400 |
| `--crt-font-weight-medium` | 500 |
| `--crt-font-weight-semi-bold` | 600 |
| `--crt-font-weight-bold` | 700 |
| `--crt-font-weight-extra-bold` | 800 |
| `--crt-font-weight-black` | 900 |

---
Last updated: 2026-06-04
