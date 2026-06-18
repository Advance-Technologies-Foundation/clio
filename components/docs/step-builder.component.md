# How to Add a Step Builder (`crt.StepBuilder`) to a Freedom UI Page

> Audience: code agent inserting `crt.StepBuilder` into a Creatio Freedom UI page schema.
>
> A `crt.StepBuilder` renders editable sequence steps; its runtime preprocessor creates the step collection, datasource dependency, and default action request binding.

## Metadata

- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, root page container
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.StepBuilder"` and `sequenceId`. **Always present.** |

`StepBuilderPreprocessor` handles the rest on metadata initialization: it creates the generated collection
attribute, embedded `SequenceStep` datasource, dependency relation, default sorting by `Index`, a paging window
(`pagingConfig`, default 10 rows), `items` binding, `stepEvent` request binding, and the end-of-list
`paginationChange` request. Steps and their per-step analytics aggregations load one page at a time; the next
page is fetched only when the user scrolls to the last loaded step.

### 1.1 Naming convention

```text
StepBuilder_<id>                  // view element name; <id> = any short unique slug
StepBuilder_<id>_Steps            // generated collection attribute
StepBuilder_<id>_StepsDS          // generated embedded datasource name
StepBuilder_<id>_StepsSorting     // generated sorting attribute
```

Generated names come from the view element `name`, so keep it stable.

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "StepBuilder_Main",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.StepBuilder",
    "sequenceId": "$Id"
  }
}
```

### 2.2 Let the preprocessor add generated wiring

At runtime, the preprocessor adds the effective equivalent of:

```jsonc
"items": "$StepBuilder_Main_Steps",
"stepEvent": {
  "request": "crt.StepBuilderStepEventRequest",
  "params": {
    "itemsAttributeName": "StepBuilder_Main_Steps",
    "sequenceId": "$Id",
    "action": "@event.action",
    "item": "@event.item",
    "eventData": "@event.eventData"
  },
  "useRelativeContext": true
},
"paginationChange": {
  "request": "crt.LoadDataRequest",
  "params": { "dataSourceName": "StepBuilder_Main_StepsDS", "parameters": [] }
}
```

The generated collection attribute also carries `modelConfig.pagingConfig` (`{ "rowCount": 10 }` by default).

Do not duplicate this wiring unless you intentionally need to override the default behavior.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.StepBuilder` are in `ComponentRegistry.json` under `componentType: "crt.StepBuilder"`. This guide covers
only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

`sequenceId` is consumed by the preprocessor even though it is not listed as a leaf component input. It may be a
literal sequence id or a `$` binding to a page attribute. The generated `stepEvent` request payload has this shape:

```ts
interface StepBuilderStepEventRequest {
  action: string;
  item: unknown;
  eventData: Record<string, unknown>;
  itemsAttributeName: string;
  sequenceId: string;
}
```

---

## 5. Copy-paste minimal example

No PackageStore page schema currently contains `crt.StepBuilder`, so this example follows the runtime preprocessor
contract.

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "StepBuilder_Main",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.StepBuilder",
    "sequenceId": "$Id",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 2, "rowSpan": 1 }
  }
}
```

---

## 6. Driving from page state

Bind `sequenceId` to the sequence record id when the builder is placed on a sequence page. The preprocessor uses
that binding to filter generated `SequenceStep` records through the dependency relation.

---

## 7. Common pitfalls

1. **Omitting `sequenceId`.** Add/copy/edit actions need the sequence context.
2. **Renaming the view element after wiring exists.** Generated attribute and datasource names are derived from `name`.
3. **Manually duplicating generated attributes.** Let the preprocessor create the collection and datasource unless you have a tested override.
4. **Overriding `stepEvent` without forwarding required params.** Add, copy, delete, and edit handlers require action, item, items attribute, and sequence id.
5. **Expecting static children.** Steps come from the generated collection, not from child view elements.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.StepBuilder"`.
- [ ] Stable, unique `name` because generated wiring depends on it.
- [ ] `sequenceId` points to the current sequence id or a valid id binding.
- [ ] Do not manually add generated `items` and `stepEvent` unless overriding preprocessor behavior.
- [ ] Parent container and layout config match the surrounding page.
