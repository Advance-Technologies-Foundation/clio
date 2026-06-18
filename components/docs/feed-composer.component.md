# How to Add a Feed Composer (`crt.FeedComposer`) to a Freedom UI Page

> Audience: code agent inserting a `crt.FeedComposer` into a Creatio Freedom UI page schema.
>
> A `crt.FeedComposer` is a rich-text message composer tied to a specific record. It lets users
> write, edit, and post feed messages (and child comments) with optional file attachments and
> external-audience toggle. Adding one only requires a single `viewConfigDiff` insert plus
> binding `primaryColumnValue` and `entitySchemaName` to the page datasource.

## Metadata

- **Category**: display
- **Container**: no (uses `contentSlots: ['channelsPanel', 'selectionActions']` for projected content)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`
- **Typical children**: none (channel selector and selection action components projected via content slots)

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.FeedComposer"`, `primaryColumnValue`, `cardState`, and `entitySchemaName`. **Always present.** |
| 2 | `handlers` (optional) | Request handlers for `messagePosted` and/or `messageChangesCanceled` outputs. |

`crt.FeedComposer` owns no datasource and no viewModel attributes. All state is managed internally.

### 1.1 Naming convention

```
FeedComposer_<id>    // view element name; <id> is any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "FeedComposer_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FeedComposer",
    "primaryColumnValue": "$Id",
    "cardState": "$CardState",
    "entitySchemaName": "Account",
    "dataSourceName": "PDS",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.2 (Optional) Handle message posted in `handlers`

```jsonc
{
  "request": "crt.FeedComposerMessagePostedRequest",
  "handler": async (request, next) => {
    // request carries the new message Guid
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.FeedComposer` are in `ComponentRegistry.json` under `componentType: "crt.FeedComposer"`.
This guide covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

`composerEvent` accepts an array of `ComposerEvent` objects:

```ts
interface ComposerEvent {
  name: string;     // e.g. 'BlindZoneWidthChangedEvent'
  params: Record<string, unknown>;
}
```

Currently the only handled event name is `'BlindZoneWidthChangedEvent'` (adjusts footer indent).

`messagePosted` emits a `Guid` string (the ID of the newly created or updated message).
`messageChangesCanceled` emits `void`.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "FeedComposer_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FeedComposer",
    "feedType": "Record",
    "primaryColumnValue": "$Id",
    "cardState": "$CardState",
    "entitySchemaName": "Account",
    "dataSourceName": "PDS"
  }
}
```

---

## 6. Driving from page state

`primaryColumnValue`, `cardState`, and `entitySchemaName` are bound to page datasource attributes.
Bind via `$AttributeName`:

```jsonc
"primaryColumnValue": "$Id",
"cardState": "$CardState",
"entitySchemaName": "Account"
```

The composer is automatically disabled (`disabled: true`) when `cardState` equals
`ModelInPageAction.Add` (new record not yet saved).

---

## 7. Common pitfalls

1. **`primaryColumnValue` empty** — the composer cannot associate messages with a record; always bind to the page record ID attribute.
2. **`entitySchemaName` not set** — mention lookups and external publishing checks fail silently; always provide the entity schema name.
3. **`cardState` not bound** — without `$CardState` the disabled state is not managed correctly for new records.
4. **`loadAttachments: false` with edits** — when editing an existing message with `loadAttachments: false`, existing attachments are cleared; only set to `false` in specific scenarios where attachments must be reset.
5. **`forExternalDefault` and `forExternal` both set** — `forExternalDefault` also sets `forExternal` on write; only set `forExternalDefault` to declare the initial default; let `forExternal` track runtime state.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.FeedComposer"`, unique `name`, valid `parentName`, `propertyName: "items"`, valid `index`.
- [ ] `primaryColumnValue` bound to the page record ID attribute (e.g. `"$Id"`).
- [ ] `cardState` bound to the page card state attribute (e.g. `"$CardState"`).
- [ ] `entitySchemaName` set to the correct entity schema name.
- [ ] `messagePosted` wired to a handler if custom post-publish logic is needed.
