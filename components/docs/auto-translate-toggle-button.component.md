# How to Add an Auto-Translate Toggle Button (`crt.AutoTranslateToggleButton`) to a Freedom UI Page

> Audience: code agent inserting `crt.AutoTranslateToggleButton` into a Creatio Freedom UI page schema.
> A styled variant of `crt.AutoTranslateToggle` that renders using the `crt-button` component for visual
> consistency with the Freedom UI system; provides the same automatic message-translation toggle behavior.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.AutoTranslateToggleButton"`. **Always present.** |

`crt.AutoTranslateToggleButton` is **view-only**. It inherits all internal state management
(sys-setting check, user profile read/write) from `crt.AutoTranslateToggle` and only exposes the
`clicked` output.

### 1.1 Naming convention

```
AutoTranslateToggleButton_<id>      // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "AutoTranslateToggleButton_abc123",
  "parentName": "ToolbarFlexContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.AutoTranslateToggleButton",
    "clicked": { "request": "crt.AutoTranslateToggleButtonClickedRequest" }
  }
}
```

### 2.2 (Optional) Handle the clicked output

```jsonc
{
  "request": "crt.AutoTranslateToggleButtonClickedRequest",
  "handler": async (request, next) => {
    // request.autoEnabled — boolean
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.AutoTranslateToggleButton` are in `ComponentRegistry.json` under
`componentType: "crt.AutoTranslateToggleButton"`. This guide covers only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "AutoTranslateToggleButton_chat",
  "parentName": "ChatToolbarContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.AutoTranslateToggleButton",
    "clicked": { "request": "crt.HandleTranslateToggleRequest" }
  }
}
```

```jsonc
// handlers entry
{
  "request": "crt.HandleTranslateToggleRequest",
  "handler": async (request, next) => {
    const autoEnabled = request?.autoEnabled;
    return next?.handle(request);
  }
}
```

---

## 7. Common pitfalls

1. **`clicked` fires on init** — the component emits the loaded translation state immediately on initialization; make sure your handler handles the initial call gracefully.
2. **Visual style difference from `crt.AutoTranslateToggle`** — this variant uses `crt-button` rendering; if you need the Material-button look, use `crt.AutoTranslateToggle` instead.
3. **`translationError$` and `translationReady`** — these outputs are inherited and fire during translation; wire them if you need error feedback or post-translation callbacks.
4. **`EnableChatTranslation` sys-setting controls availability** — same as the base toggle; if the setting is disabled, the button toggles off immediately on init and emits `autoEnabled: false`.
5. **`translateTrigger$` is not a `@CrtInput`** — cannot be set from a page schema `values` block.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.AutoTranslateToggleButton"`, unique `name`, valid `parentName`, `propertyName: "items"`, valid `index`.
- [ ] If you need the toggle state, `clicked` is wired to a request.
- [ ] Handler reads `request.autoEnabled` (boolean).
- [ ] Handler is safe to call on page init (fires once at load with the persisted state).
- [ ] `EnableChatTranslation` sys-setting is enabled in the target environment.
