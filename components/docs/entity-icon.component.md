# How to Add an Entity Icon (`crt.EntityIcon`) to a Freedom UI Page

> Audience: code agent inserting a `crt.EntityIcon` into a Creatio Freedom UI page schema.
>
> `crt.EntityIcon` displays a visual identity for a record: it renders either a user photo (from
> `photoImageId`), a tile icon image (from `tileIconId`), or a fallback icon glyph derived from `tileKey`.
> It is used in record tiles, avatars, and catalog cards. No datasource is needed.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`, tile/card containers
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.EntityIcon"` and image/icon bindings. **Always present.** |

`crt.EntityIcon` is display-only. No `modelConfigDiff`, `viewModelConfigDiff`, or `handlers` changes are
needed unless you want to bind the icon to a datasource attribute.

### 1.1 Icon resolution priority

The component resolves the displayed icon in this order:
1. `photoImageId` — shows the user/contact photo if the GUID is valid and non-empty.
2. `tileIconId` — shows a custom tile icon image if the GUID is valid and non-empty.
3. `icon` — shows the explicit icon glyph name if set.
4. `tileKey` — derives a schema-specific icon from the built-in `ENTITY_SCHEMA_ICONS` map; falls back to
   `default-entity-icon` if the key is not found.

### 1.2 Naming convention

```
EntityIcon_<id>         // view element name; <id> is any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "EntityIcon_abc123",
  "parentName": "TileContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.EntityIcon",
    "tileKey": "$ContactDS_TileKey",
    "tileIconId": "$ContactDS_TileIconId",
    "authorName": "$ContactDS_Name",
    "authorId": "$ContactDS_Id",
    "photoImageId": "$ContactDS_PhotoId",
    "iconSize": "large",
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.EntityIcon` are in `ComponentRegistry.json` under `componentType: "crt.EntityIcon"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

All inputs are primitive strings or `SizeEnum` keyword. No custom types.

```ts
// SizeEnum keywords accepted by iconSize:
'none' | 'default' | 'xs' | 'small' | 'medium' | 'large' | 'xl' | 'xxl'
```

`iconSize: 'default'` (the initial value) renders the icon at the component's base size (no CSS modifier class).

---

## 5. Copy-paste minimal example

No direct PackageStore schema match found. Based on the component inputs and typical tile usage:

```jsonc
// viewConfigDiff entry — tile icon with photo fallback
{
  "operation": "insert",
  "name": "ContactIcon",
  "parentName": "ContactTileContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.EntityIcon",
    "tileKey": "contact",
    "authorName": "$ContactDS_FullName",
    "authorId": "$ContactDS_Id",
    "photoImageId": "$ContactDS_PhotoId",
    "iconSize": "large"
  }
}
```

---

## 6. Driving from page state

All inputs accept `$AttributeName` bindings. Bind `photoImageId` and `tileIconId` to datasource attributes
for dynamic record-based icons:

```jsonc
"photoImageId": "$DetailDS_AuthorPhoto",
"authorId": "$DetailDS_AuthorId",
"authorName": "$DetailDS_AuthorName"
```

---

## 7. Common pitfalls

1. **Setting `iconSize: 'default'`** — this is the zero-value and adds no CSS class; the icon renders at
   the base size. Use a named keyword (`'small'`, `'large'`) when a specific size is needed.
2. **Setting `tileKey` to a value not in the icon registry** — falls back to `default-entity-icon`; check
   available keys in `ENTITY_SCHEMA_ICONS` if the expected icon does not appear.
3. **Providing `photoImageId` as an empty GUID** — the component checks `isGuid && !isEmptyGuid`; an empty
   GUID string is treated as absent, so the photo is not shown.
4. **Setting `icon` and `tileKey` together** — `icon` takes priority over the `tileKey` fallback; if the
   explicit icon name is unknown to the icon library, the slot renders blank.
5. **Missing `layoutConfig` inside a `crt.GridContainer`** — the icon needs `{ row, column, rowSpan, colSpan }`
   when the parent is a grid container.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.EntityIcon"`, unique `name`, valid `parentName`, `propertyName: "items"`, and valid `index`.
- [ ] At least one of `photoImageId`, `tileIconId`, `icon`, or `tileKey` is set.
- [ ] `iconSize` is set to a named keyword (`'small'`, `'medium'`, `'large'`) when non-default size is needed.
- [ ] If inside a `crt.GridContainer`, `layoutConfig` is present.
- [ ] If `authorId` / `authorName` are set, they are bound to valid datasource attributes (used by accessibility labels or avatar initials).
