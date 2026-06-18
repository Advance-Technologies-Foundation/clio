# How to Add a Translate Toggle Button (`crt.TranslateToggleButton`) to a Freedom UI Page

> Audience: code agent inserting `crt.TranslateToggleButton` into a Creatio Freedom UI page schema.
> `crt.TranslateToggleButton` extends `crt.TranslateToggle` with CRT UI button styling (uses `crt-button`
> instead of Material button) for consistent appearance in the chat panel and similar CRT-styled surfaces.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.TranslateToggleButton"`. **Always present.** |
| 2 | `handlers` | *(only if `translationReady` or `translationError$` outputs need custom logic)* |

> **Important:** Like its base `crt.TranslateToggle`, this component has **no schema-configurable inputs**
> in `ComponentRegistry.json`. The outputs `translationReady` and `translationError$` are the only
> schema-level integration points.

See [translate-toggle.component.md](../../translate-toggle/translate-toggle.component.md) for full
behavioral details — all mechanics are identical; only the button rendering differs (uses `crt-button`).

### 1.1 Naming convention

```
TranslateToggleButton_<id>     // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "TranslateToggleButton_chatMsg",
  "parentName": "ChatActionsContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TranslateToggleButton",
    "translationReady": { "request": "crt.HandleTranslationReadyRequest" },
    "visible": true
  }
}
```

### 2.2 (Optional) Handle translation events in `handlers`

```jsonc
{
  "request": "crt.HandleTranslationReadyRequest",
  "handler": async (request, next) => {
    const { translationKey, translation } = request.event;
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `outputs` and `values` for `crt.TranslateToggleButton` are in `ComponentRegistry.json` under
`componentType: "crt.TranslateToggleButton"`. Only `translationReady` and `translationError$` are
schema-bindable.

---

## 4. Shape of types not in `references.typeDefinitions`

See `crt.TranslateToggle` for the `TranslationReadyPayload` shape — identical here.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "TranslateToggleButton_msg",
  "parentName": "ChatActionsContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TranslateToggleButton",
    "translationReady": { "request": "crt.HandleTranslationReadyRequest" },
    "visible": true
  }
}
```

---

## 6. Driving from page state

Same as `crt.TranslateToggle` — no `propertyBindable` schema inputs. Wire the two outputs to request
handlers to respond to translation events.

---

## 7. Common pitfalls

1. **No schema inputs are configurable.** Properties like `sourceLanguageCode` and `sourceText` must be set by a host component programmatically.
2. **Use `crt.TranslateToggleButton` for CRT-styled surfaces, `crt.TranslateToggle` for Material-styled ones.** Mixing them in the same toolbar may cause visual inconsistencies.
3. **`translationReady` fires on both translate and restore.** Distinguish states via `translationKey`.
4. **Static translation cache is shared across instances.** See `crt.TranslateToggle` pitfalls §7.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.TranslateToggleButton"`, unique `name`.
- [ ] Wire `translationReady` and/or `translationError$` to request handlers if needed.
- [ ] Do not set `sourceLanguageCode`, `targetLanguageCode`, or `sourceText` via schema `values`.
