# How to Add a Translate Toggle (`crt.TranslateToggle`) to a Freedom UI Page

> Audience: code agent inserting `crt.TranslateToggle` into a Creatio Freedom UI page schema.
> `crt.TranslateToggle` is a **toggle action button** that switches between the original and translated
> version of a text value, caching translations across toggles and emitting events when ready.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.TranslateToggle"`. **Always present.** |
| 2 | `handlers` | *(only if `translationReady` or `translationError$` outputs need custom logic)* |

> **Important:** `crt.TranslateToggle` has **no schema-configurable inputs** in `ComponentRegistry.json`
> (the `inputs` array is empty). All configuration (`iconPosition`, `size`, `sourceLanguageCode`, etc.)
> is handled at the Angular `@Input` level and is wired programmatically when embedding this component
> inside a host that passes values directly — not via Freedom UI schema `values`.
> Only the two `@CrtOutput` events (`translationReady`, `translationError$`) are bindable from the schema.

### 1.1 Naming convention

```
TranslateToggle_<id>     // view element name; <id> is any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "TranslateToggle_chatMsg",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TranslateToggle",
    "translationReady": { "request": "crt.TranslationReadyRequest" },
    "visible": true
  }
}
```

### 2.2 (Optional) Handle translation events in `handlers`

```jsonc
{
  "request": "crt.TranslationReadyRequest",
  "handler": async (request, next) => {
    // request.event contains { translationKey, translation }
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs` (empty), `outputs`, and `values` for `crt.TranslateToggle` are in `ComponentRegistry.json`
under `componentType: "crt.TranslateToggle"`. Only `translationReady` and `translationError$` are
schema-bindable outputs.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// translationReady event payload
interface TranslationReadyPayload {
  translationKey: string;   // the key identifying this translation pair
  translation: string;      // the translated (or restored original) text
}
```

The component maintains a per-class static cache (`Map<string, string>`) keyed by a compound hash of
`translationKey | sourceLanguageCode | targetLanguageCode | contentHash`. On toggle-back, the original
text is restored from cache without a server call.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "TranslateToggle_msg",
  "parentName": "ActionsContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.TranslateToggle",
    "translationReady": { "request": "crt.HandleTranslationReadyRequest" },
    "visible": true
  }
}
```

```jsonc
// handlers entry
{
  "request": "crt.HandleTranslationReadyRequest",
  "handler": async (request, next) => {
    const { translationKey, translation } = request.event;
    // apply translation to the bound attribute
    return next?.handle(request);
  }
}
```

---

## 6. Driving from page state

`crt.TranslateToggle` has no `propertyBindable` schema inputs. It is driven by its `@Input` properties
which must be set by a host component that mounts it programmatically, not via Freedom UI schema bindings.
The outputs `translationReady` and `translationError$` fire regardless and are the only schema-level
integration points.

---

## 7. Common pitfalls

1. **No schema inputs are configurable.** Properties like `sourceLanguageCode`, `targetLanguageCode`, and `sourceText` must be passed through Angular's host-embedding mechanism, not via Freedom UI schema `values`.
2. **`translationReady` fires on both translate and restore.** The `translation` field on restore is the original text retrieved from cache; distinguish states via `translationKey`.
3. **Static translation cache is shared across all instances.** If two `crt.TranslateToggle` components share the same `translationKey` + language combo, they read from the same cached entry.
4. **`translationError$` fires on network failure or empty response.** Clicking the button again retries the translation.
5. **`mousedown` is suppressed (preventDefault).** This is intentional to keep keyboard focus on the editable field while clicking the toggle; the button is still keyboard-accessible via tab.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.TranslateToggle"`, unique `name`, valid `parentName`.
- [ ] If translation events need custom logic, wire `translationReady` and/or `translationError$` to request handlers.
- [ ] Do not attempt to set `sourceLanguageCode`, `targetLanguageCode`, `sourceText` via schema `values` — they have no effect.
- [ ] Ensure a host or parent component sets the Angular `@Input` properties programmatically when needed.
