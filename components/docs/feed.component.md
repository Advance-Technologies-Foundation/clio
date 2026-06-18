# How to Add a Feed (`crt.Feed`) to a Freedom UI Page

> Audience: code agent inserting a `crt.Feed` into a Creatio Freedom UI page schema.
>
> `crt.Feed` renders the activity stream for either a single record (record feed) or the
> current user (user feed). It internally manages `SocialMessage` loading, posting, and
> the rich-text composer.

For the underlying schema mechanics (diff operations, attribute binding), see
crt.Input guide.
This document focuses on feed-specific configuration.

## Metadata

- **Category**: interactive
- **Container**: yes
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`, `crt.TabContainer`
- **Typical children**: `crt.MenuItem`, `crt.MenuLabel`, `crt.MenuDivider` (in the `selectionActions` slot)

---

## 1. Mental model — the 2-3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.Feed"`, `feedType`, and (for record feeds) `entitySchemaName` + `primaryColumnValue`. |
| 2 | `viewModelConfigDiff` | Page attributes for the bound state — `feedMessages`, `offsetDate`, `sortColumn`, and optionally `cardState`. |
| 3 | `modelConfigDiff` | Required for **record feeds** — declare a `crt.EntityDataSource` (the page's `PDS`) so the feed has the record context. Not required for **user feeds**. |

### 1.1 Naming convention

```
Feed_<id>                    // view element name
Feed_<id>_messages           // page attribute carrying the loaded message array
Feed_<id>_offsetDate         // pagination cursor
Feed_<id>_sortColumn         // current sort selection
```

---

## 2. Step-by-step recipe

### 2.1 Declare the bound attributes (`viewModelConfigDiff` entry)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Feed_xkp4r_messages":   { "value": [] },
      "Feed_xkp4r_offsetDate": { "value": null },
      "Feed_xkp4r_sortColumn": { "value": "CreatedOn" }
    }
  }
}
```

For a record feed, the page's primary `$Id` and `$CardState` attributes (provided by the platform on edit pages) are already available — no extra declaration needed.

### 2.2 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Feed_xkp4r",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Feed",
    "feedType": "Record",
    "dataSourceName": "PDS",
    "entitySchemaName": "Contact",
    "primaryColumnValue": "$Id",
    "cardState": "$CardState",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 6 }
  }
}
```

A record feed typically uses just these fields. The feed manages its own message loading, pagination, and sort internally — bound attributes (`feedMessages`, `offsetDate`, `sortColumn`) are only declared when other elements on the page need to react to feed state.

For a **user feed** (My Feed), set `feedType: "User"`, omit `entitySchemaName` and `primaryColumnValue`, and set `dataSourceName: null`:

```jsonc
"feedType": "User",
"dataSourceName": null,
"entitySchemaName": null
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Feed` are in `ComponentRegistry.json` under `componentType: "crt.Feed"` (or via
`get-component-info`). This guide covers only the assembly mechanics — what to put where in
`viewConfigDiff` / `viewModelConfigDiff` / `modelConfigDiff` — plus the real value combinations and
runtime behavior the registry schema does not capture.

Component declares `contentSlots: ['selectionActions']` — inject custom action items for the in-feed text selection popover.

**No `dataValueType` mapping** — feeds bind to the platform's SocialMessage service, not to an entity column.

### 3.1 Typical `values` combinations

- **Record feed**: `feedType: "Record"`, `primaryColumnValue: "$Id"`, `cardState: "$CardState"`,
  `dataSourceName: "PDS"`, `entitySchemaName: "<Entity>"`. The `dataSourceName` may be any custom
  **existing** data source on the page (not only `"PDS"`) — the feed never creates it.
- **User feed**: `feedType: "User"`, `entitySchemaName: null`, `dataSourceName: null`
  (any `primaryColumnValue`/`cardState` are ignored).
- **Customize an inherited feed** via `merge` instead of re-inserting (e.g. enable external posts):

  ```jsonc
  { "operation": "merge", "name": "Feed",
    "values": { "dataSourceName": "PDS", "entitySchemaName": "Case", "allowExternalPost": true } }
  ```

### 3.2 Load gate (`canLoadFeed`)

The feed renders messages only when: not in designer mode, **and** `feedType === "User"`, **or**
`feedType === "Record"` with **both** `entitySchemaName` and `primaryColumnValue` set. Otherwise it
shows only a static placeholder — the #1 cause of "feed appears empty."

---

## 4. Copy-paste minimal example — record feed

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "ContactRecordFeed",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Feed",
    "feedType": "Record",
    "dataSourceName": "PDS",
    "entitySchemaName": "Contact",
    "primaryColumnValue": "$Id",
    "cardState": "$CardState",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 4, "rowSpan": 6 }
  }
}
```

No `viewModelConfigDiff` entries are required for a basic record feed — the feed manages its own state.

> When the feed is placed inside a tab (`crt.TabContainer` — the most common case), omit
> `layoutConfig`: the tab sizes the feed.

---

## 5. Common pitfalls

1. **`feedType: "Record"` without `entitySchemaName`/`primaryColumnValue`** — `canLoadFeed()` returns `false`, the feed silently does nothing. Always provide both on record feeds.
2. **Using `feedType: "User"` on a record page** — works (it shows the current user's stream), but is rarely what the page wants. Use `"Record"` to tie to the open record.
3. **Missing `cardState: "$CardState"` on edit pages** — the composer stays open while the record is being added, which fails when the user tries to post against an unpersisted id.
4. **Designer placeholder shows but runtime feed empty** — usually missing `entitySchemaName`, `primaryColumnValue`, or wrong record id. Inspect `canLoadFeed()` in the runtime context.
5. **Re-binding `feedMessages` to a transient value** — the feed manages its own pagination/loading; setting `feedMessages` from outside resets the internal state. Treat the bound attribute as read-only output of the feed.
6. **Embedding inside a tight grid cell** — feeds need vertical room. Less than `rowSpan: 4` typically clips the message list and forces scroll-inside-scroll.
7. **Declaring a new data source for a record feed** — unnecessary. Reuse the page's existing `"PDS"` (or an existing custom DS); no new `crt.EntityDataSource` is required for the feed.

---

## 6. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Feed"`, unique `name`, `propertyName: "items"`.
- [ ] `feedType` set explicitly (`"Record"` or `"User"`).
- [ ] For `"Record"` feeds: `entitySchemaName` and `primaryColumnValue: "$Id"` set.
- [ ] For `"User"` feeds: `entitySchemaName: null` and `dataSourceName: null`.
- [ ] `cardState: "$CardState"` set on edit pages (so the composer hides until the record is persisted).
- [ ] For record feeds: `dataSourceName: "PDS"` (the page's primary data source).
- [ ] `feedMessages` / `offsetDate` / `sortColumn` bound to page attributes **only when other elements need to react** to feed state. Not required for the feed itself.
- [ ] `readingMode` (optional) — if set, use the numeric `FeedReadingMode` value (`0` = All, `1` = External, `2` = Internal), not a string literal.
- [ ] `layoutConfig` provides generous `rowSpan` (omit when the feed sits in a tab container).
