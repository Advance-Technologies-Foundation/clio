# How to Add a Feed Item (`crt.FeedItem`) to a Freedom UI Page

> Audience: code agent inserting a `crt.FeedItem` into a Creatio Freedom UI page schema.
>
> A `crt.FeedItem` renders a single feed post or comment with its metadata, attachments,
> likes/comments controls, and action menu. It is typically instantiated programmatically by a
> parent `crt.Feed` component rather than added directly to a page schema.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer` (or rendered by parent `crt.Feed`)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.FeedItem"` and an `item` binding. **Always present.** |

`crt.FeedItem` is **view-only** — no model, no datasource, no viewModel attribute. The `item`
input supplies the entire message data.

### 1.1 Naming convention

```
FeedItem_<id>        // view element name; <id> is any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "FeedItem_abc123",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FeedItem",
    "item": "$FeedItem_data",
    "schemaName": "Account",
    "feedType": "Record",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.FeedItem` are in `ComponentRegistry.json` under `componentType: "crt.FeedItem"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

`item` accepts a `FeedMessage` object (`{ Id, Message, CreatedBy, CreatedOn, CommentCount, UserAccess, FileSchemaName, Parent, ...}`).

`itemActionItems` accepts `CrtMenuItemViewElementConfig[]`:

```ts
interface CrtMenuItemViewElementConfig {
  type: 'crt.MenuItem';
  caption: string;
  name?: string;
  icon?: string;
  handleItemClick?: (event: Event) => void;
}
```

`readingMode` is a `FeedReadingMode` enum (`All` | `Unread`).

---

## 5. Copy-paste minimal example

```jsonc
// viewModelConfigDiff
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "FeedItem_data": { "value": null }
    }
  }
}
```

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "FeedItem_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FeedItem",
    "item": "$FeedItem_data",
    "schemaName": "Account",
    "feedType": "Record"
  }
}
```

---

## 6. Driving from page state

`item` is `propertyBindable` — bind to a `$Attribute` that holds the `FeedMessage` object.

---

## 7. Common pitfalls

1. **`item` is `null` or empty** — the component renders empty; always ensure the bound attribute is populated before the component renders.
2. **`schemaName` not set** — entity link navigation (`data-link="mention"`) and child comment operations will silently fail.
3. **`disableLikesComments: true`** — both the like action and the comment toggle are hidden; use only when embedding in a read-only context.
4. **`loadAttachments: false`** — attachments array is set to `[]` immediately; existing files on the message will not be shown.
5. **`customVisibleItemActionsMenu`** — by default only the message author sees edit/delete actions (checks `userInfo.contactId === item.CreatedBy.value`); if you override `itemActionItems`, also handle visibility separately.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.FeedItem"`, unique `name`, valid `parentName`, `propertyName: "items"`, valid `index`.
- [ ] `item` bound to an attribute holding a valid `FeedMessage` object.
- [ ] `schemaName` set to the owning entity schema name.
- [ ] `messageDeleted` and `messageEdited` wired if the parent list needs to remove/update the item.
