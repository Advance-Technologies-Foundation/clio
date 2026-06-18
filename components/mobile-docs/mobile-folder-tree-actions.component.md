# How to Add a Folder Tree Actions Toolbar (`crt.FolderTreeActions`) to a Mobile Page

> Audience: Clio AI agent inserting a `crt.FolderTreeActions` into a mobile page schema.
> Renders a toggle button that opens or closes a paired `crt.FolderTree` panel and shows the
> currently active folder name when a folder is selected.

## Metadata
- **Category**: filtering
- **Container**: false
- **Parent types**: header containers or toolbar rows (e.g. `crt.HeaderContainer`)
- **Typical children**: none

---
## 1. Mental model
`crt.FolderTreeActions` is the action bar companion to `crt.FolderTree`. It shows a single
button labeled with the active folder name (or a default caption when no folder is selected).
Tapping it fires `folderTreeVisibleChanged` to toggle the folder panel. It also exposes a
favorites dropdown and a clear-folder button. At design time the mobile shell uses a button
visual (extends `CrtBaseMobileButtonComponent`) with configurable `color` and a default
`sourceSchemaName` derived from the page's entity.

---
## 2. Clio operation
```jsonc
{
  "operation": "insert",
  "name": "FolderActions",
  "values": {
    "type": "crt.FolderTreeActions",
    "caption": "$Resources.Strings.FolderActions_caption",
    "sourceSchemaName": "Contact",
    "rootSchemaName": "Contact",
    "activeFolderName": "$ActiveFolder"
  },
  "parentName": "HeaderContainer",
  "propertyName": "items",
  "index": 0
}
```

---
## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, and `designerDefaults` for `crt.FolderTreeActions` are in
`ComponentRegistry.json` under `componentType: "crt.FolderTreeActions"`.

Key schema-level properties (configured declaratively in the page schema):

| Property | Type | Description |
|---|---|---|
| `caption` | `string` | Button caption text shown when no folder is active; typically a localizable string resource. |
| `activeFolderName` | `string` | Page attribute binding for the display name of the active folder. |
| `sourceSchemaName` | `string` | Entity schema name used to look up available folders (e.g. `"Contact"`). Defaults to `DEFAULT_FOLDER_TREE_SOURCE_SCHEMA_NAME`. |
| `rootSchemaName` | `string` | Root schema name for the folder hierarchy (usually the same as `sourceSchemaName`). |
| `color` | `MobileButtonColor` | Color scheme for the button (e.g. `"default"`, `"primary"`). Inherited from `CrtBaseMobileButtonComponent`. |

Additional runtime properties:

| Property | Type | Description |
|---|---|---|
| `_filterOptions` | object | Internal filter options wired by the platform. Example: `{ expose: [], from: ['..._active_folder_id', '..._active_folder_filter_data'] }`. Set only when using platform-managed folder filtering. |

---
## 4. Copy-paste minimal example
```jsonc
{
  "operation": "insert",
  "name": "FolderActions",
  "values": {
    "type": "crt.FolderTreeActions",
    "caption": "$Resources.Strings.FolderActions_caption",
    "sourceSchemaName": "Contact",
    "rootSchemaName": "Contact",
    "activeFolderName": "$ActiveFolder"
  },
  "parentName": "HeaderContainer",
  "propertyName": "items",
  "index": 0
}
```

---
## 5. Common pitfalls
- **Missing paired `crt.FolderTree`**: `crt.FolderTreeActions` must be paired with a sibling
  `crt.FolderTree` element on the same page. Without it the toggle button has nothing to show
  or hide.
- **`sourceSchemaName` mismatch**: `sourceSchemaName` must match the entity used by the paired
  `crt.FolderTree`. A mismatch causes the folder list to show folders from the wrong entity.
- **`activeFolderName` not bound**: if `activeFolderName` is not bound to a page attribute the
  button always shows the default caption and the clear-folder button never appears, even after
  the user selects a folder.
