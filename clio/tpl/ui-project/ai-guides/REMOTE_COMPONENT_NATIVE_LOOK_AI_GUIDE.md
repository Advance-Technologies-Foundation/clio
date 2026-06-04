# Remote Component Native Look AI Guide

**ATTENTION AI AGENT:** This is a directive instruction set, not documentation. Follow rules exactly.

**Audience:** AI assistant styling the **runtime** part of a Creatio Freedom UI custom (remote) component that must **reproduce the look of a built-in Creatio UI element** (button, input, dropdown, …).

**Scope:** Per-element **visual recipes** — anatomy, dimensions, spacing, variants, and interaction states — that make a custom element visually match its native Creatio counterpart.

**Related guides:**
- **Token / theming styling:** See ./REMOTE_COMPONENT_STYLING_AI_GUIDE.md (co-located in this folder) — read FIRST; every recipe here builds on its token and Shadow DOM rules.
- **Design tokens catalog:** See ./DESIGN_TOKENS_AI_GUIDE.md (co-located in this folder) — names and default values of every `--crt-*` token used in the recipes below.
- **Runtime behavior:** See REMOTE_COMPONENT_RUNTIME_AI_GUIDE.md from @creatio-devkit/common package.
- **Accessibility:** See WCAG_ACCESSIBILITY_AI_GUIDE.md from @creatio-devkit/common package.

---

## Component recipes

> Each recipe documents the type taxonomy, the `--crt-*` tokens per type and state, shared chrome, and sizes.

### Button

A Creatio button is defined by two independent axes — pick one value from each.

**Axis 1 — display type** (how it is drawn):
- `contained` — solid fill. **Default.** Uses the `--crt-button-*` typography role.
- `text` — no fill; the color shows as the foreground color (label + icon) — ghost / link-like action. Uses the `--crt-button-small-*` typography role.

**Axis 2 — color** (intent / visual style; the designer caption is in parentheses):
- `primary` — blue filled brand action (Save / Submit).
- `default` (*Plain*) — transparent with subtle text; the neutral action. **Default color.**
- `outline` — light fill + border; a secondary action with more emphasis than Plain.
- `accent` (*Focus*) — green filled positive action.
- `warn` (*Warning*) — red filled destructive / error action.
- `plain-white` / `outline-white` — white text for placement over dark or glassmorphic backgrounds. Use only over dark / image surfaces.

> A button uses **one foreground color per state** for both the label and the icon — the icon inherits it via `currentColor`; the button never sets a separate icon color. The token in each cell is the design system's fixed pick, so copy it exactly. It is **not** a choice between the `--crt-color-text-*` and `--crt-color-icon-*` families — that is why some cells use a `text-*` token and others an `icon-*` token.

#### Color tokens — `contained` (filled)

Background = fill. For the solid colors (`primary`, `accent`, `warn`, `outline`) the disabled fill is `--crt-color-background-disabled` `#ededed` and the disabled foreground `--crt-color-text-disabled` `#757575`.

| color | background | hover | active | foreground (label + icon) | border |
|---|---|---|---|---|---|
| primary | `--crt-color-background-primary` `#004fd6` | `--crt-color-background-primary-hover` `#0041b5` | `--crt-color-background-primary-selected` `#003495` | `--crt-color-text-on-primary` `#ffffff` | — |
| default (Plain) | `transparent` | `--crt-color-background-primary-subtle-hover` `#eff4fb` | `--crt-color-background-primary-subtle-selected` `#e3ebfa` | `--crt-color-text-on-primary-subtle` `#001c5a` | — |
| outline | `--crt-color-background-primary-subtle` `#f7f8fb` | `--crt-color-background-primary-subtle-hover` `#eff4fb` | `--crt-color-background-primary-subtle-selected` `#e3ebfa` | `--crt-color-text-on-primary-subtle` `#001c5a` | `1px solid` `--crt-color-border-primary-subtle` `#cbdbf8` · disabled `--crt-color-border-disabled` `#dfdfdf` |
| accent (Focus) | `--crt-color-background-success` `#0b8500` | `--crt-color-background-success-hover` `#076e00` | `--crt-color-background-success-selected` `#045900` | `--crt-color-text-on-success` `#ffffff` | — |
| warn (Warning) | `--crt-color-background-error` `#d2310d` | `--crt-color-background-error-hover` `#b12200` | `--crt-color-background-error-selected` `#8e1900` | `--crt-color-text-on-error` `#ffffff` | — |
| plain-white | `transparent` | `--crt-glass-color-light-100` | `--crt-glass-color-light-200` | `#ffffff` | — |
| outline-white | `--crt-glass-color-light-50` | `--crt-glass-color-light-100` | `--crt-glass-color-light-200` | `#ffffff` | `1px solid` `--crt-glass-color-light-800` · hover `-600` · active `-400` · disabled `-200` |

> White colors (`plain-white`, `outline-white`) keep a transparent / glass fill when disabled and use `--crt-color-text-glassmorphic-disabled` for disabled text. Take their fallback values (rgba) from ./DESIGN_TOKENS_AI_GUIDE.md.

#### Color tokens — `text` (no fill)

The color token becomes the **foreground** color; the background stays transparent in every state. Disabled foreground = `--crt-color-text-disabled` `#757575`.

| color | foreground (label + icon) | hover | active |
|---|---|---|---|
| default | `--crt-color-icon-action` `#0d2e4e` | `--crt-color-icon-action-hover` `#004fd6` | `--crt-color-icon-action-hover` `#004fd6` |
| primary | `--crt-color-background-primary` `#004fd6` | `--crt-color-background-primary-hover` `#0041b5` | `--crt-color-background-primary-selected` `#003495` |
| accent | `--crt-color-background-success` `#0b8500` | `--crt-color-background-success-hover` `#076e00` | `--crt-color-background-success-selected` `#045900` |
| warn | `--crt-color-background-error` `#d2310d` | `--crt-color-background-error-hover` `#b12200` | `--crt-color-background-error-selected` `#8e1900` |

> `outline`, `plain-white`, and `outline-white` are designed for `contained`; pair `text` with `default` / `primary` / `accent` / `warn`.

#### Shared chrome (all buttons)

- **Typography:** `contained` → `--crt-button-*` role (14 / 500, line-height 16px); `text` → `--crt-button-small-*` role (12 / 500, line-height 16px). `text-transform: none`.
- **Radius:** `--crt-radius-150` (6px) standard; `--crt-radius-infinite` (999px) for a pill / rounded button.
- **Icon ↔ label gap:** `--crt-spacing-100` (4px).
- **Focus:** `outline: 2px solid var(--crt-color-border-selected, #004fd6); outline-offset: 2px;` (see WCAG_ACCESSIBILITY_AI_GUIDE.md).

#### Sizes

`height` is fixed per size; horizontal padding and font-size follow. `large` is the default.

| size | height | padding (inline) | font-size | icon |
|---|---|---|---|---|
| small | 18px | `0 var(--crt-spacing-100, 4px)` | 10px | 8px |
| medium | 24px | `0 var(--crt-spacing-300, 12px)` | 12px | 12px |
| large (default) | 32px | `0 var(--crt-spacing-400, 16px)` | 14px | 16px |
| extra-large | 40px | `0 var(--crt-spacing-400, 16px)` | 14px | 24px |

> Icon-only button uses tighter inline padding: small `0 5px`, medium `0 6px`, large / extra-large `0 8px`.

#### Recipe — primary, contained, large (the most common button)

```scss
.button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--crt-spacing-100, 4px);

  height: 32px;
  padding: 0 var(--crt-spacing-400, 16px);
  border: none;
  border-radius: var(--crt-radius-150, 6px);
  cursor: pointer;

  font-family: var(--crt-button-font-family, 'Montserrat', sans-serif);
  font-size: var(--crt-button-font-size, 14px);
  font-weight: var(--crt-button-font-weight, 500);
  line-height: var(--crt-button-line-height, 16px);
  text-transform: none;

  color: var(--crt-color-text-on-primary, #ffffff);
  background: var(--crt-color-background-primary, #004fd6);
}
.button:hover { background: var(--crt-color-background-primary-hover, #0041b5); }
.button:active { background: var(--crt-color-background-primary-selected, #003495); }
.button:disabled {
  background: var(--crt-color-background-disabled, #ededed);
  color: var(--crt-color-text-disabled, #757575);
  cursor: not-allowed;
}
.button:focus-visible {
  outline: 2px solid var(--crt-color-border-selected, #004fd6);
  outline-offset: 2px;
}
```

For another color, swap only `background` / `:hover` / `:active` / `color` from the contained table — keep chrome, size, and focus identical.

#### Recipe — default, text (ghost action)

```scss
.button-text {
  display: inline-flex;
  align-items: center;
  gap: var(--crt-spacing-100, 4px);

  height: 32px;
  padding: 0 var(--crt-spacing-400, 16px);
  border: none;
  background: transparent;
  border-radius: var(--crt-radius-150, 6px);
  cursor: pointer;

  font-family: var(--crt-button-small-font-family, 'Montserrat', sans-serif);
  font-size: var(--crt-button-small-font-size, 12px);
  font-weight: var(--crt-button-small-font-weight, 500);
  line-height: var(--crt-button-small-line-height, 16px);
  text-transform: none;

  color: var(--crt-color-icon-action, #0d2e4e);
}
.button-text:hover { color: var(--crt-color-icon-action-hover, #004fd6); }
.button-text:active { color: var(--crt-color-icon-action-hover, #004fd6); }
.button-text:disabled { color: var(--crt-color-text-disabled, #757575); cursor: not-allowed; }
.button-text:focus-visible {
  outline: 2px solid var(--crt-color-border-selected, #004fd6);
  outline-offset: 2px;
}
```

### Text input

Two appearances — like the button's display type:
- `fill` (default) — no box; a single **bottom underline**. The Freedom UI default.
- `outline` — a full 1px **border box** with corner radius.

Both share the same text, label, placeholder, and state colors; only the border treatment differs. **Anatomy:** an optional caption label + the input control; typed text uses the `--crt-body-2-*` role, the label uses `--crt-caption-*`.

#### Text, label & placeholder (both appearances)

| part | token | typography |
|---|---|---|
| typed text | `--crt-color-text-body` `#181818` | `--crt-body-2-*` (13 / 500 / 20) |
| placeholder | `--crt-color-text-muted` `#606060` | `--crt-body-2-*`, weight 400 |
| label | `--crt-color-text-muted` `#606060` | `--crt-caption-*` (12 / 500 / 16) |
| disabled text | `--crt-color-text-disabled` `#757575` | — |
| required mark / error text | `--crt-color-text-error` `#d2310d` | — |

#### Border — by appearance & state

| state | `fill` (underline) | `outline` (box) |
|---|---|---|
| resting | 1px dotted `--crt-color-border-base` `#dfdfdf` | 1px solid `--crt-color-border-base` `#dfdfdf` |
| hover | 1px solid `--crt-color-border-base` `#dfdfdf` | `--crt-color-border-muted` `#ededed` (2px) |
| focus | `--crt-color-border-selected` `#004fd6` | `--crt-color-border-selected` `#004fd6` |
| invalid | `--crt-color-border-error` `#d2310d` | `--crt-color-border-error` `#d2310d` |

> `fill` draws only the bottom border — dotted at rest, solid on hover, `border-selected` on focus. `outline` is a full box with radius `--crt-radius-100` (4px).

#### Recipe — fill (underline, default)

```scss
.field {
  display: flex;
  flex-direction: column;
  gap: var(--crt-spacing-100, 4px);
}
.field__label {
  font-family: var(--crt-caption-font-family, 'Montserrat', sans-serif);
  font-size: var(--crt-caption-font-size, 12px);
  font-weight: var(--crt-caption-font-weight, 500);
  line-height: var(--crt-caption-line-height, 16px);
  color: var(--crt-color-text-muted, #606060);
}
.input {
  font-family: var(--crt-body-2-font-family, 'Montserrat', sans-serif);
  font-size: var(--crt-body-2-font-size, 13px);
  font-weight: var(--crt-body-2-font-weight, 500);
  line-height: var(--crt-body-2-line-height, 20px);
  color: var(--crt-color-text-body, #181818);

  padding: 0 0 var(--crt-spacing-100, 4px);
  border: none;
  border-bottom: 1px dotted var(--crt-color-border-base, #dfdfdf);
  background: transparent;
}
.input::placeholder { color: var(--crt-color-text-muted, #606060); font-weight: 400; }
.input:hover { border-bottom-style: solid; }
.input:focus-visible { outline: none; border-bottom: 1px solid var(--crt-color-border-selected, #004fd6); }
.input:disabled { color: var(--crt-color-text-disabled, #757575); }
.input[aria-invalid='true'] { border-bottom: 1px solid var(--crt-color-border-error, #d2310d); }
```

#### Recipe — outline (box)

Same label and text; replace the border block:

```scss
.input--outline {
  border: 1px solid var(--crt-color-border-base, #dfdfdf);
  border-radius: var(--crt-radius-100, 4px);
  padding: var(--crt-spacing-200, 8px) var(--crt-spacing-300, 12px);
  background: var(--crt-color-background-base, #ffffff);
}
.input--outline:hover { border-color: var(--crt-color-border-muted, #ededed); }
.input--outline:focus-visible { outline: none; border-color: var(--crt-color-border-selected, #004fd6); }
.input--outline:disabled { border-color: var(--crt-color-border-disabled, #dfdfdf); color: var(--crt-color-text-disabled, #757575); }
.input--outline[aria-invalid='true'] { border-color: var(--crt-color-border-error, #d2310d); }
```

### Select / combobox

Two parts: the **trigger** (the closed field) and the **dropdown panel** of options.

**The trigger is a Text input field.** The closed select/combobox reuses the *Text input* field exactly — same appearances (`fill` / `outline`), border, text, label, and state tokens. It only adds a trailing expander icon and shows the selected value as the field text. For border / focus / disabled / invalid, follow *Text input*; the table below lists only what the trigger adds.

#### Trigger — added tokens

| part | token |
|---|---|
| selected value text | `--crt-color-text-body` `#181818` (`--crt-body-2-*`) |
| placeholder (empty) | `--crt-color-text-muted` `#606060` |
| expander icon — closed | `--crt-color-icon-action` `#0d2e4e` |
| expander icon — open | `--crt-color-icon-action-hover` `#004fd6` |
| clear icon | `--crt-color-icon-muted` `#606060` |

#### Dropdown panel & options

The panel is a bordered surface (square corners) that scrolls. Options use the `--crt-body-2-*` role; an optional secondary line uses `--crt-caption-*` muted.

| part | background | text |
|---|---|---|
| panel | `--crt-color-background-base` `#ffffff` (border `--crt-color-border-base` `#dfdfdf`, shadow `--crt-color-shadow`) | — |
| option (default) | `transparent` | `--crt-color-text-body` `#181818` |
| option · hover / active | `--crt-color-background-interaction-hover` `#eff4fb` | `--crt-color-text-body` `#181818` |
| option · selected | `--crt-color-background-interaction-selected` `#e3ebfa` | `--crt-color-text-body` `#181818` |
| option · disabled / empty / "no items" | — | `--crt-color-text-muted` `#606060` |
| option secondary line | — | `--crt-color-text-muted` `#606060` (`--crt-caption-*`) |

> Option row ≈ 32px tall, panel min-width 240px, max-height ≈ 400px (scrolls). Multi-select: each option row pairs the Checkbox (see *Checkbox*) with the label.

#### Recipe — panel + options

For the trigger, use the *Text input* recipe and add a trailing chevron coloured `--crt-color-icon-action` (open: `--crt-color-icon-action-hover`).

```scss
.select-panel {
  background: var(--crt-color-background-base, #ffffff);
  border: 1px solid var(--crt-color-border-base, #dfdfdf);
  box-shadow: 0 0 4px var(--crt-color-shadow, rgba(24, 24, 24, 0.1));
  min-width: 240px;
  max-height: 400px;
  overflow-y: auto;
  padding: 0;
}
.select-option {
  display: flex;
  align-items: center;
  min-height: 32px;
  padding: 0 var(--crt-spacing-300, 12px);
  cursor: pointer;

  font-family: var(--crt-body-2-font-family, 'Montserrat', sans-serif);
  font-size: var(--crt-body-2-font-size, 13px);
  font-weight: var(--crt-body-2-font-weight, 500);
  line-height: var(--crt-body-2-line-height, 20px);
  color: var(--crt-color-text-body, #181818);
  background: transparent;
}
.select-option:hover,
.select-option--active { background: var(--crt-color-background-interaction-hover, #eff4fb); }
.select-option--selected { background: var(--crt-color-background-interaction-selected, #e3ebfa); }
.select-option:focus-visible {
  outline: 2px solid var(--crt-color-border-selected, #004fd6);
  outline-offset: -2px;
}
.select-option--disabled,
.select-option__empty { color: var(--crt-color-text-muted, #606060); }
.select-option__secondary {
  color: var(--crt-color-text-muted, #606060);
  font-size: var(--crt-caption-font-size, 12px);
  line-height: var(--crt-caption-line-height, 16px);
}
```

### Checkbox

A checkbox is driven by its **state**, not by color variants. Three visual states — `unchecked`, `checked`, `indeterminate` — each combine with `hover`, `focus`, and `disabled`. `checked` and `indeterminate` share the same box colors; only the mark differs (✓ vs a dash).

**Anatomy:** a 16×16 box (radius 4px) + a caption label. Unchecked = bordered empty box; checked / indeterminate = filled box with a white mark. The mark is drawn with `currentColor`, so the box's `color` is the mark color.

#### Color tokens — box

| state | box fill | box border | mark |
|---|---|---|---|
| unchecked | `transparent` | `--crt-color-icon-muted` `#606060` | — |
| unchecked · disabled | `transparent` | `--crt-color-border-disabled` `#dfdfdf` | — |
| checked / indeterminate | `--crt-color-background-primary` `#004fd6` | same as fill | `--crt-color-icon-on-primary` `#ffffff` |
| checked · hover | `--crt-color-background-primary-hover` `#0041b5` | same as fill | `--crt-color-icon-on-primary` `#ffffff` |
| checked · disabled | `--crt-color-background-disabled` `#ededed` | same as fill | `--crt-color-icon-on-primary` `#ffffff` |

> Unchecked hover keeps the same border; only the checked fill darkens on hover.

#### Label & chrome

- **Label:** `--crt-caption-*` role (12 / 500 / 16), color `--crt-color-text-muted` `#606060`; disabled `--crt-color-text-disabled` `#757575`.
- **Box:** 16×16, `2px solid` border, radius `--crt-radius-100` (4px).
- **Box ↔ label gap:** `--crt-spacing-200` (8px).
- **Focus:** `outline: 2px solid var(--crt-color-border-selected, #004fd6); outline-offset: 2px;` on the box (see WCAG_ACCESSIBILITY_AI_GUIDE.md).

#### Recipe

Keep the native `<input type="checkbox">` for behavior and accessibility; visually hide it and render a styled box next to it. HTML: `<label class="checkbox"><input class="checkbox__input" type="checkbox"><span class="checkbox__box"><svg viewBox="0 0 16 16" fill="currentColor">…</svg></span><span class="checkbox__label">…</span></label>`.

```scss
.checkbox {
  display: inline-flex;
  align-items: center;
  gap: var(--crt-spacing-200, 8px);
  cursor: pointer;
}
.checkbox__input {            /* visually hidden, still focusable */
  position: absolute;
  width: 1px;
  height: 1px;
  margin: 0;
  opacity: 0;
}
.checkbox__box {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  box-sizing: border-box;
  width: 16px;
  height: 16px;
  border: 2px solid var(--crt-color-icon-muted, #606060);
  border-radius: var(--crt-radius-100, 4px);
  background: transparent;
  color: var(--crt-color-icon-on-primary, #ffffff);   /* mark color (currentColor) */
}
.checkbox__box svg { width: 12px; height: 12px; visibility: hidden; }

.checkbox__input:checked + .checkbox__box,
.checkbox__input:indeterminate + .checkbox__box {
  background: var(--crt-color-background-primary, #004fd6);
  border-color: var(--crt-color-background-primary, #004fd6);
}
.checkbox__input:checked + .checkbox__box svg,
.checkbox__input:indeterminate + .checkbox__box svg { visibility: visible; }

.checkbox:hover .checkbox__input:checked + .checkbox__box {
  background: var(--crt-color-background-primary-hover, #0041b5);
  border-color: var(--crt-color-background-primary-hover, #0041b5);
}

.checkbox__input:focus-visible + .checkbox__box {
  outline: 2px solid var(--crt-color-border-selected, #004fd6);
  outline-offset: 2px;
}

.checkbox__input:disabled + .checkbox__box {
  border-color: var(--crt-color-border-disabled, #dfdfdf);
}
.checkbox__input:disabled:checked + .checkbox__box {
  background: var(--crt-color-background-disabled, #ededed);
  border-color: var(--crt-color-background-disabled, #ededed);
}
.checkbox__input:disabled ~ .checkbox__label {
  color: var(--crt-color-text-disabled, #757575);
}

.checkbox__label {
  font-family: var(--crt-caption-font-family, 'Montserrat', sans-serif);
  font-size: var(--crt-caption-font-size, 12px);
  font-weight: var(--crt-caption-font-weight, 500);
  line-height: var(--crt-caption-line-height, 16px);
  color: var(--crt-color-text-muted, #606060);
}
```

### Menu

A dropdown panel of **action items** opened from a trigger (button / icon). Unlike Select, items are commands — there is no persistent "selected" state; `active` is the pressed state.

#### Panel
| part | token |
|---|---|
| background | `--crt-color-background-base` `#ffffff` |
| border | `--crt-color-border-base` `#dfdfdf` |
| shadow | `--crt-color-shadow` `rgba(24,24,24,0.1)` |

#### Item
| state | token |
|---|---|
| text | `--crt-color-text-body` `#181818` (`--crt-body-2-*`) |
| leading icon | `--crt-color-icon-base` `#0d2e4e` |
| hover | bg `--crt-color-background-interaction-hover` `#eff4fb` |
| active (pressed) | bg `--crt-color-background-interaction-selected` `#e3ebfa` |
| disabled | text + icon `--crt-color-text-disabled` `#757575` |

Item height 40px; items reserve a leading-icon column so labels align. Optional intent icons by action meaning: primary `--crt-color-icon-primary` `#004fd6`, destructive `--crt-color-icon-error` `#d2310d`, success `--crt-color-icon-success` `#0b8500`.

#### Group label & divider
- **Group label:** `--crt-color-text-muted` `#606060` (`--crt-caption-*`).
- **Divider:** 1px top border `--crt-color-border-base` `#dfdfdf`, `--crt-spacing-200` (8px) vertical margin.

#### Recipe

```scss
.menu-panel {
  background: var(--crt-color-background-base, #ffffff);
  border: 1px solid var(--crt-color-border-base, #dfdfdf);
  box-shadow: 0 0 4px var(--crt-color-shadow, rgba(24, 24, 24, 0.1));
  padding: var(--crt-spacing-200, 8px) 0;
  min-width: 180px;
}
.menu-item {
  display: flex;
  align-items: center;
  gap: var(--crt-spacing-200, 8px);
  width: 100%;
  height: 40px;
  padding: 0 var(--crt-spacing-400, 16px);
  border: none;
  background: transparent;
  cursor: pointer;
  text-align: start;

  font-family: var(--crt-body-2-font-family, 'Montserrat', sans-serif);
  font-size: var(--crt-body-2-font-size, 13px);
  font-weight: var(--crt-body-2-font-weight, 500);
  line-height: var(--crt-body-2-line-height, 20px);
  color: var(--crt-color-text-body, #181818);
}
.menu-item__icon {            /* 16×16, currentColor */
  width: 16px;
  height: 16px;
  color: var(--crt-color-icon-base, #0d2e4e);
}
.menu-item:hover { background: var(--crt-color-background-interaction-hover, #eff4fb); }
.menu-item:active { background: var(--crt-color-background-interaction-selected, #e3ebfa); }
.menu-item:focus-visible {
  outline: 2px solid var(--crt-color-border-selected, #004fd6);
  outline-offset: -2px;
}
.menu-item:disabled {
  color: var(--crt-color-text-disabled, #757575);
  cursor: not-allowed;
}
.menu-item:disabled .menu-item__icon { color: var(--crt-color-text-disabled, #757575); }

.menu-label {
  padding: var(--crt-spacing-100, 4px) var(--crt-spacing-400, 16px);
  font-family: var(--crt-caption-font-family, 'Montserrat', sans-serif);
  font-size: var(--crt-caption-font-size, 12px);
  font-weight: var(--crt-caption-font-weight, 500);
  line-height: var(--crt-caption-line-height, 16px);
  color: var(--crt-color-text-muted, #606060);
}
.menu-divider {
  margin: var(--crt-spacing-200, 8px) 0;
  border: none;
  border-top: 1px solid var(--crt-color-border-base, #dfdfdf);
}
```

---
Last updated: 2026-06-04
