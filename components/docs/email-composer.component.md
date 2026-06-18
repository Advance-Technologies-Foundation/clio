# How to Add an Email Composer (`crt.EmailComposer`) to a Freedom UI Page

> Audience: code agent inserting a `crt.EmailComposer` into a Creatio Freedom UI page schema.
>
> `crt.EmailComposer` is a full-featured email authoring widget: it manages subject, from/to/cc/bcc recipient
> fields, body (rich text), attachments, and reply/forward actions. It is a top-level view element inserted via
> a single `viewConfigDiff` `insert` op with no datasource or attribute setup required for the element itself.

## Metadata

- **Category**: interactive
- **Container**: no (content slots `channelsPanel`, `selectionActions` are internal)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.TabContainer`, root page container
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.EmailComposer"` and the required bindings. **Always present.** |
| 2 | `handlers` (optional) | A handler for `defaultSenderRequest` to populate the sender dropdown on load. |

`crt.EmailComposer` owns its internal email state. The page only needs to provide `recordId`,
`entitySchemaName`, `emailId`, `defaultSenderRequest`, and optionally `expandOnLoad`.

### 1.1 Naming convention

```
EmailComposer_<id>         // view element name; <id> is any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "EmailComposer_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.EmailComposer",
    "recordId": "$Id",
    "entitySchemaName": "Activity",
    "defaultSenderRequest": "crt.DefaultSenderComposerRequest",
    "expandOnLoad": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.2 (Optional) Handle `defaultSenderRequest`

The composer fires `defaultSenderRequest` on load to populate the sender mailbox list. The platform ships
`crt.DefaultSenderComposerRequest` — use it directly unless custom sender selection logic is needed.

### 2.3 (Optional) Wire reply/forward via `composerEvent`

```jsonc
// viewConfigDiff.values — bind composer events from a page attribute
"composerEvent": "$EmailComposer_composerEvent"

// viewModelConfigDiff — declare the attribute
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "EmailComposer_composerEvent": { "value": null }
    }
  }
}
```

Set `$EmailComposer_composerEvent` to a `ComposerEvent[]` array with `name: "CrtEmailReply"` to trigger
reply/forward programmatically.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.EmailComposer` are in `ComponentRegistry.json` under `componentType: "crt.EmailComposer"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// ActivityColumnBinding — column binding for activity schema
interface ActivityColumnBinding {
  schemaColumn: string;   // column name in the Activity entity
  attributeName: string;  // page attribute name to sync the value with
}

// ComposerEvent — used with composerEvent input
interface ComposerEvent {
  name: string;          // e.g. 'CrtEmailReply', 'CrtEmailForward'
  params?: Record<string, unknown>;
}
```

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — from Accounts_FormPage.js
{
  "operation": "insert",
  "name": "EmailComposer_mi3v7e8",
  "parentName": "MessageComposerSelector_hjalx2w",
  "propertyName": "items",
  "index": 1,
  "values": {
    "type": "crt.EmailComposer",
    "recordId": "$Id",
    "defaultSenderRequest": "crt.DefaultSenderComposerRequest",
    "entitySchemaName": "Account"
  }
}
```

```jsonc
// Full example from EmailFormPage.js (with emailId and expandOnLoad)
{
  "operation": "insert",
  "name": "EmailComposer_0db39de6",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.EmailComposer",
    "recordId": "$Id",
    "emailId": "$EmailId",
    "defaultSenderRequest": "crt.DefaultSenderComposerRequest",
    "entitySchemaName": "Activity",
    "expandOnLoad": true
  }
}
```

---

## 6. Driving from page state

Most EmailComposer inputs (`subject`, `from`, `to`, `cc`, `bcc`, `body`) are two-way bound through outputs
(`subjectChange`, `fromChange`, etc.). To pre-fill recipients or subject from page state:

```jsonc
// viewConfigDiff.values
"subject": "$Email_subject",
"to": "$Email_recipients"

// viewModelConfigDiff
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Email_subject": { "value": "" },
      "Email_recipients": { "value": [] }
    }
  }
}
```

### 6.1 Forbidding manual subject edits (`subjectReadonly`)

Set `subjectReadonly: true` to make the subject field read-only. The user can read the subject but cannot
edit it manually; system-driven values (reply/forward prefill, drafts, templates) still populate it. Defaults
to `false`. Bind it to a static value or a page attribute for conditional locking.

```jsonc
// viewConfigDiff.values — static lock
"subjectReadonly": true
```

---

## 7. Common pitfalls

1. **Omitting `entitySchemaName`** — the composer needs the entity context to resolve attachments and activity schema; without it drafts cannot be saved.
2. **Omitting `defaultSenderRequest`** — the sender dropdown renders empty; always provide `crt.DefaultSenderComposerRequest` or a custom replacement.
3. **Setting `emailId` without a handler** — `emailId` triggers reply/forward mode; omit it for a fresh compose form.
4. **Using `expandOnLoad: false` inside a tab** — the composer does not auto-expand on tab switch; call `emailComposerCleared` or set `composerEvent` to force expansion when the tab becomes active.
5. **Passing `bindingColumns` without matching activity columns** — each `ActivityColumnBinding` entry must map a real activity schema column; invalid column names are silently ignored.
6. **Forgetting `layoutConfig`** — if the parent is a `crt.GridContainer`, `layoutConfig` with `{ row, column, rowSpan, colSpan }` is required to position the composer in the grid.
7. **Binding `subject`/`body` without two-way sync** — if you bind `subject` but don't handle `subjectChange`, the page attribute drifts from the composer's internal state after the user types.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.EmailComposer"`, unique `name`, valid `parentName`, and `propertyName: "items"`.
- [ ] `entitySchemaName` is set to the activity entity name (e.g. `"Activity"`).
- [ ] `recordId` is bound to the page record GUID attribute (e.g. `"$Id"`).
- [ ] `defaultSenderRequest` is set (use `"crt.DefaultSenderComposerRequest"` unless custom logic is needed).
- [ ] If editing an existing email, `emailId` is bound to the email activity GUID.
- [ ] If the parent is a `crt.GridContainer`, `layoutConfig` is present.
- [ ] Two-way output bindings (`subjectChange`, `toChange`, etc.) are wired if the page needs to read back composer state.
