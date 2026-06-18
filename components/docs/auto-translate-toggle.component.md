# How to Add an Auto-Translate Toggle (`crt.AutoTranslateToggle`) to a Freedom UI Page

> Audience: code agent inserting `crt.AutoTranslateToggle` into a Creatio Freedom UI page schema.
> A toggle button for automatic message translation in omnichannel messaging; on first render it
> reads the `EnableChatTranslation` system setting and the user's saved profile state, then emits
> `clicked` so the page can act on the initial translation preference.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.AutoTranslateToggle"`. **Always present.** |

`crt.AutoTranslateToggle` is **view-only** — no model, no attribute needed. It manages its own
state internally (reads `EnableChatTranslation` sys-setting + user profile on init) and emits
`clicked` when the translation state changes.

### 1.1 Naming convention

```
AutoTranslateToggle_<id>      // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "AutoTranslateToggle_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.AutoTranslateToggle",
    "clicked": { "request": "crt.AutoTranslateToggleClickedRequest" }
  }
}
```

### 2.2 (Optional) Handle the clicked output

```jsonc
{
  "request": "crt.AutoTranslateToggleClickedRequest",
  "handler": async (request, next) => {
    // request.autoEnabled — boolean, true when auto-translate is on
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.AutoTranslateToggle` are in `ComponentRegistry.json` under
`componentType: "crt.AutoTranslateToggle"`. This guide covers only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "AutoTranslateToggle_main",
  "parentName": "ToolbarFlexContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.AutoTranslateToggle",
    "clicked": { "request": "crt.HandleAutoTranslateToggleRequest" }
  }
}
```

```jsonc
// handlers entry
{
  "request": "crt.HandleAutoTranslateToggleRequest",
  "handler": async (request, next) => {
    const autoEnabled = request?.autoEnabled;
    // react to translation mode change
    return next?.handle(request);
  }
}
```

---

## 6. Driving from page state

This component manages its own active/inactive toggle state internally. The only
bindable output is `clicked`. To synchronize external attributes, declare a viewModel attribute and
bind it in your `clicked` handler.

---

## 7. Common pitfalls

1. **Do not wire `clicked` if you don't need the toggle state** — the component still works, but `clicked` fires on init (emitting the loaded state), so make sure your handler is prepared for the initial call at page load, not only on user interaction.
2. **`EnableChatTranslation` sys-setting controls availability** — if the setting is `false`, the component sets `autoEnabled = false` and emits immediately on init; no further toggle is possible.
3. **User profile persistence is built-in** — the component reads and writes `OmnichannelMessagingProfileData` automatically; do not manually sync this profile in your handler.
4. **`translateTrigger$` is not a `@CrtInput`** — it is set programmatically by a parent component that wires the observable; do not attempt to bind it from a page schema `values` block.
5. **Outputs `translationError$` and `translationReady`** — these fire during the translation flow driven by `translateTrigger$`; wire them if you need to react to translation completion or errors.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.AutoTranslateToggle"`, unique `name`, valid `parentName`, `propertyName: "items"`, valid `index`.
- [ ] If you need to react to the toggle state, `clicked` is wired to a request in `handlers`.
- [ ] Handler code checks `request.autoEnabled` (boolean) to determine the new state.
- [ ] Aware that `clicked` fires once on init with the loaded state — handler must be idempotent.
- [ ] `EnableChatTranslation` sys-setting is enabled in the target environment if auto-translate functionality is needed.
