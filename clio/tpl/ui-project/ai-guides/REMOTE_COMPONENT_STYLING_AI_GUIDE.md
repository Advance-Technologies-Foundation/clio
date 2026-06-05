# Remote Component Styling AI Guide

**ATTENTION AI AGENT:** This is a directive instruction set, not documentation. Follow rules exactly.

**Audience:** AI assistant styling the **runtime** part of a Creatio Freedom UI custom (remote) component so it looks native — visually consistent with built-in Creatio components and correct in every theme.

**Scope:** Make every generated runtime component consume Creatio **design tokens** (CSS custom properties) for colors, typography, spacing, and radius — instead of hardcoded values — so the component inherits the system look and adapts to theme switching automatically.

**Related guides:**
- **Runtime behavior guide:** See REMOTE_COMPONENT_RUNTIME_AI_GUIDE.md from @creatio-devkit/common package — read FIRST. It owns component structure, inputs/outputs, registration, tests.
- **Design tokens catalog:** See ./DESIGN_TOKENS_AI_GUIDE.md (co-located in this folder) — catalog of all `--crt-*` token names and default values.
- **Native component look (recipes):** See ./REMOTE_COMPONENT_NATIVE_LOOK_AI_GUIDE.md (co-located in this folder). Use it when the component must reproduce a built-in Creatio element (button, input, dropdown, …).
- **Accessibility guide:** See WCAG_ACCESSIBILITY_AI_GUIDE.md from @creatio-devkit/common package — focus-visible, color-contrast, and "color not sole indicator" rules interact with styling.
- **Design-time panel styling:** See PROPERTIES_PANEL_AI_STYLES_TEMPLATE.md from @creatio/interface-designer package. **Do NOT apply that template to runtime components** — see the Shadow DOM section below for why.

---

## The One Rule That Changes Everything: Shadow DOM

Runtime remote components are generated with **`encapsulation: ViewEncapsulation.ShadowDom`** (a hard requirement of the runtime guide). This is the single fact that dictates how you must style them.

**What does NOT cross the shadow boundary into your component:**
- ❌ Creatio global stylesheets and theme classes' rule sets.
- ❌ Shared SCSS partials such as `@use '@creatio/interface-designer/styles/properties-panel-styles'`. These compile to ordinary CSS rules that stop at the shadow boundary. Importing them does nothing useful in a runtime component.
- ❌ System BEM/utility classes (`.input-control`, `.action-button`, `mat-*`, etc.). They are invisible inside your shadow root.
- ❌ Element/attribute selectors written outside the component.

**What DOES cross the shadow boundary:**
- ✅ **CSS Custom Properties (design tokens).** Custom properties inherit down the DOM tree and pierce shadow boundaries: a token declared on an ancestor (`:root`, or the active theme's root class on the app root) is readable inside your shadow root via `var(--crt-...)`. The active theme redefines these tokens, so your `var(--crt-...)` values adapt to theme switching for free — conversely, a hardcoded hex stays fixed and breaks when the theme changes.
- ✅ Inherited typed properties such as `font-family` and `color` **if** you let them inherit onto `:host`.

**Therefore the only way to be visually consistent is to consume `--crt-*` design tokens via `var(...)`.** This is mandatory, not stylistic.

> Why the panel template is different: the properties panel uses `ViewEncapsulation.None`, so its styles live in the light DOM and CAN `@use` shared SCSS and use system classes. A runtime component cannot. Never copy the panel approach into a runtime component.

---

## `:host` baseline (ALWAYS include this block first)

Styling assumes the runtime component is generated with `encapsulation: ViewEncapsulation.ShadowDom` (per the runtime guide) — that is the boundary tokens pierce. Set the system font and base text color on `:host` once; everything inside the shadow tree then inherits the correct typography by default, exactly like native components.

```scss
:host {
  display: block;

  /* Inherit the system UI typography — body-2 is the default UI text role */
  font-family: var(--crt-body-2-font-family, 'Montserrat', sans-serif);
  font-size: var(--crt-body-2-font-size, 13px);
  font-weight: var(--crt-body-2-font-weight, 500);
  line-height: var(--crt-body-2-line-height, 20px);
  color: var(--crt-color-text-body, #181818);

  -webkit-font-smoothing: antialiased;
  box-sizing: border-box;
}

:host *,
:host *::before,
:host *::after {
  box-sizing: inherit;
}
```

**Rules:**
- ALWAYS declare the `:host` typography + color baseline. This is what makes the component "look like Creatio" with zero extra work.
- ALWAYS provide a **fallback** in every `var(--crt-..., <fallback>)` — the second argument. It is what renders in isolated unit tests or any host that is not wrapped in a Creatio theme. Use the documented default value as the fallback.

---

## Choosing tokens

All token **names, default-theme values, naming conventions, and scope** live in one place: **./DESIGN_TOKENS_AI_GUIDE.md** (co-located in this folder). Use it as the catalog; this guide only covers how to apply tokens in a Shadow DOM runtime component.

- **Prefer semantic tokens** (named by role: `--crt-color-text-*`, `--crt-color-background-*`, `--crt-color-border-*`, `--crt-color-icon-*`, and the `--crt-<role>-*` typography roles). They encode the right shade per theme and adapt automatically. Reach for raw palette/primitive tokens only when no semantic role fits.
- **Use only tokens listed in the tokens guide.** Those are defined globally (`:root` or the active theme's root) and therefore inherit into your shadow root. A `--crt-*` token that is not listed there may exist only in another scope (e.g. the design-time properties panel's `--crt-size-*`) and will resolve to nothing inside a runtime shadow root.
- **Every `var(--crt-..., <fallback>)` needs a fallback** — use the token's default-theme value from the tokens guide as the second argument.
- **Spacing & radius are fixed scales, not arbitrary px — never invent a step; confirm the exact `--crt-spacing-*` / `--crt-radius-*` value in the tokens guide before use.**

---

## PATTERNS (copy, then adapt)

For a complete per-element recipe — button, text input, select, checkbox, menu, or field label, with its default appearance, geometry, and states — use ./REMOTE_COMPONENT_NATIVE_LOOK_AI_GUIDE.md.

### Pattern A — Text / value display

```scss
.value {
  color: var(--crt-color-text-body, #181818);
}
.value--muted {
  color: var(--crt-color-text-muted, #606060);
  font-size: var(--crt-caption-font-size, 12px);
  line-height: var(--crt-caption-line-height, 16px);
}
```

### Pattern B — Surface / card

```scss
.card {
  background: var(--crt-color-background-base, #ffffff);
  border: 1px solid var(--crt-color-border-base, #dfdfdf);
  border-radius: var(--crt-radius-200, 8px);
  padding: var(--crt-spacing-400, 16px);
}
```

---

## HARD FAIL

Generation MUST be rejected and regenerated if any of these appear in a runtime component:

| Rule | ❌ Wrong | ✅ Correct |
|------|---------|-----------|
| Hardcoded UI text/surface color | `color: #444;` / `background: #fff;` | `color: var(--crt-color-text-body, #181818);` |
| Hardcoded UI font-family | `font-family: 'Roboto';` | `font-family: var(--crt-body-2-font-family, 'Montserrat', sans-serif);` |
| Raw px font-size for a known role | `font-size: 13px;` | `font-size: var(--crt-body-2-font-size, 13px);` |
| `var()` without fallback | `color: var(--crt-color-text-body);` | `color: var(--crt-color-text-body, #181818);` |
| Presentational `@Input` defaults to a literal | `public color = 'black';` | `public accentColor = 'var(--crt-color-text-action, #0d2e4e)';` |
| Color as the only state signal | red text only for error | icon/text + `--crt-color-text-error` (see WCAG guide) |
| Boxed text input when `fill` is the default | `border: 1px solid` + `border-radius` on the input | `border-bottom` underline (`fill`); a box only on explicit request |
| Secondary button with a border instead of Plain | `border: 1px solid` on the secondary button | transparent `default` (Plain), no border |

**Exception to _Hardcoded UI font-family_:** `'Courier New', monospace` is allowed ONLY for code/raw-data display (the design system has no monospace token).

### Exception — presentational value exposed as a component @Input

The row above is about **UI chrome**. If the component exposes a user-configurable presentational value (a color, font size, spacing, radius…), that is a **data value**, not chrome, so binding it inline is correct. But default it to the matching **token** (not a literal like `'black'` or `'12px'`), and still use tokens for all surrounding chrome.

```typescript
@Input() @CrtInput()
public accentColor = 'var(--crt-color-text-action, #0d2e4e)';
```

```html
<span class="status" [style.color]="accentColor">{{ text }}</span>
```

---

## Styling checklist (add to the runtime generation checklist)

```markdown
- [ ] :host declares font-family + font-size + font-weight + line-height + color from --crt-body-2-* / --crt-color-text-body
- [ ] Every UI color uses a --crt-* color token (semantic preferred); no hardcoded hex for chrome
- [ ] Every text element uses a --crt-<role>-* typography group, not raw px
- [ ] Spacing/radius use --crt-spacing-* / --crt-radius-*
- [ ] Presentational @Input defaults to a token, not a literal
- [ ] Every var(--crt-*) has a fallback second argument
- [ ] Hover / disabled / :focus-visible states use tokens
- [ ] Text input uses the fill underline by default (a box only on explicit request)
- [ ] Secondary button is Plain (transparent, no border), not bordered
- [ ] Component verified across themes (colors adapt correctly when the active theme changes)
- [ ] Color is never the sole state indicator (WCAG)
```

---
Last updated: 2026-06-04
