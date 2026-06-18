# How to Add an Entity Stage Progress Bar (`crt.EntityStageProgressBar`) to a Freedom UI Page

> Audience: code agent inserting a `crt.EntityStageProgressBar` into a Creatio Freedom UI page schema.
>
> A `crt.EntityStageProgressBar` renders the DCM (Dynamic Case Management) stage pipeline for a
> record as a horizontal progress bar. It delegates all data loading and stage-transition logic to
> the page via request-based outputs, so the page schema must wire the outputs to handlers that
> implement the DCM stage API.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`
- **Typical children**: none

---

## 1. Mental model — the 2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.EntityStageProgressBar"`, `entityName`, and `saveOnChange`. **Always present.** |
| 2 | `handlers` | Request handlers for `loadData`, `stageChanged`, `checkIsAnAppropriateProcessSchema`, `setAllowedStages`, and `changeToAppropriateEntityStageSchema`. |

`crt.EntityStageProgressBar` owns no datasource and no viewModel attributes.

### 1.1 Naming convention

```
EntityStageProgressBar_<id>    // view element name; <id> is any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "EntityStageProgressBar_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.EntityStageProgressBar",
    "saveOnChange": false,
    "askUserToChangeSchema": true,
    "entityName": "#DataSourceEntityName()#",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.2 Wire required handlers in `handlers`

The component drives all data operations through outputs. At minimum wire `loadData` and `stageChanged`:

```jsonc
{
  "request": "crt.EntityStageProgressBarLoadDataRequest",
  "handler": async (request, next) => {
    // load stages from DCM API using request.entityName + request.recordId
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.EntityStageProgressBar` are in `ComponentRegistry.json` under
`componentType: "crt.EntityStageProgressBar"`. This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// Step (the stage shape used in stages, currentStage)
interface Step {
  id: string;
  caption: string;
  color?: string;
  isFinal?: boolean;
}

// LookupValue (the shape used for value and stageSchemaFilterByValue)
interface LookupValue {
  value: string;          // Guid
  displayValue: string;
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry (from PackageStore)
{
  "operation": "insert",
  "name": "EntityStageProgressBar_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.EntityStageProgressBar",
    "saveOnChange": false,
    "askUserToChangeSchema": true,
    "entityName": "#DataSourceEntityName()#",
    "layoutConfig": {}
  }
}
```

---

## 6. Driving from page state

`recordId` is bound to the page record ID attribute; when set to a non-empty value it triggers
`loadData` via a `setTimeout(0)` call (debounced to avoid double-loads on init).

`value` is bound to the current stage `LookupValue`; changing it converts the lookup to a `Step`
and re-emits `setAllowedStages`.

---

## 7. Common pitfalls

1. **`recordId` not set** — `loadData` never fires; the bar stays in the loading state indefinitely.
2. **`entityName` wrong or empty** — the `loadData` handler receives an incorrect entity name and cannot load stage definitions.
3. **`saveOnChange: true` without a save handler** — the stage change triggers a silent save; ensure the datasource is in a state that can be saved (record must be open, not in `Add` mode).
4. **`isAppropriateEntityStageSchema` set to `false`** — when `askUserToChangeSchema: true` and the `EntityStageProgressbar-ShowSnackbar` feature flag is on, a snackbar notification is shown; handle `changeToAppropriateEntityStageSchema` to apply the recommended schema.
5. **`stages` array empty after `loadData`** — the component hides itself by setting `display: none` on the parent element; this is by design for records without a stage schema.
6. **`stageRunningProcessUId` set** — while non-null the component suppresses `loadData` re-triggers; clear it to `null` or empty string to unlock data reload after the process completes.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.EntityStageProgressBar"`, unique `name`, valid `parentName`, `propertyName: "items"`, valid `index`.
- [ ] `entityName` set to the entity schema name.
- [ ] `recordId` bound to the page record ID attribute.
- [ ] `loadData` handler implemented (loads DCM stage data).
- [ ] `stageChanged` handler implemented (saves stage selection).
- [ ] `setAllowedStages` handler implemented (evaluates which stages are clickable).
- [ ] `saveOnChange` and `askUserToChangeSchema` set according to use case requirements.
