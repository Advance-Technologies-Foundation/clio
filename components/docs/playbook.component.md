# How to Add a Playbook (`crt.Playbook`) to a Freedom UI Page

> Audience: code agent inserting `crt.Playbook` into a Creatio Freedom UI page schema.
>
> `crt.Playbook` is a read-only knowledge-base gallery that renders a list of playbook articles from the
> `KnowledgeBase` entity. Each article card shows title, author, modification date, and type. The component
> requires a `modelConfigDiff` datasource entry plus a `viewModelConfigDiff` attribute to feed the `items` input.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.TabContainer`
- **Typical children**: none

---

## 1. Mental model — the 3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.Playbook"` and `items` bound to a `$`-attribute. **Always present.** |
| 2 | `modelConfigDiff` | A datasource entry pointing to the `KnowledgeBase` entity (or a filtered variant). |
| 3 | `viewModelConfigDiff` | An attribute declaration of type `BaseViewModelCollection` bound to the datasource. |

No `handlers` are needed for a read-only playbook.

### 1.1 Naming convention

```
Playbook_<id>             // view element name; <id> = any short unique slug
Playbook_<id>DS           // datasource key in modelConfigDiff
$Playbook_<id>_Items      // viewModel attribute bound to the datasource collection
```

> **Note:** The conventional naming from the PackageStore uses `KnowledgeBaseDS` as the datasource name
> regardless of the widget element name.

---

## 2. Step-by-step recipe

### 2.1 Add the datasource (`modelConfigDiff`)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "dataSources": {
      "Playbook_KnowledgeBaseDS": {
        "type": "crt.EntityDataSource",
        "config": {
          "entitySchemaName": "KnowledgeBase",
          "attributes": {
            "Id":         { "path": "Id" },
            "Name":       { "path": "Name" },
            "Notes":      { "path": "Notes" },
            "CreatedBy":  { "path": "CreatedBy" },
            "ModifiedOn": { "path": "ModifiedOn" },
            "Type":       { "path": "Type" }
          }
        }
      }
    }
  }
}
```

### 2.2 Declare the attribute (`viewModelConfigDiff`)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Playbook_Items": {
        "type": "crt.BaseViewModelCollection",
        "isCollection": true,
        "dataSources": ["Playbook_KnowledgeBaseDS"]
      }
    }
  }
}
```

### 2.3 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Playbook_main",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Playbook",
    "items": "$Playbook_Items",
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Playbook` are in `ComponentRegistry.json` under `componentType: "crt.Playbook"`. This guide covers
only the assembly mechanics.

**Key input:**

| Input | Type | Description |
|---|---|---|
| `items` | `BaseViewModelCollection` | Collection of playbook article view models, bound to a datasource attribute. |

---

## 5. Copy-paste minimal example

Real PackageStore usage from `Opportunities_FormPage`:

```jsonc
// viewConfigDiff entry
{
  "operation": "insert",
  "name": "Playbook_main",
  "parentName": "RightContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Playbook",
    "_designOptions": {
      "sourceName": "KnowledgeBase",
      "templateValuesMapping": {
        "id":         "Playbook_KnowledgeBaseDS_Id",
        "caption":    "Playbook_KnowledgeBaseDS_Name",
        "content":    "Playbook_KnowledgeBaseDS_Notes",
        "author":     "Playbook_KnowledgeBaseDS_CreatedBy",
        "modifiedOn": "Playbook_KnowledgeBaseDS_ModifiedOn",
        "type":       "Playbook_KnowledgeBaseDS_Type"
      }
    }
  }
}
```

---

## 7. Common pitfalls

1. **Missing `_designOptions.templateValuesMapping`** — the component maps datasource attribute paths to gallery card columns; without this mapping the card fields render empty.
2. **Attribute names must follow the `<datasourceName>_<columnPath>` pattern** — `Playbook_KnowledgeBaseDS_Name` means datasource `Playbook_KnowledgeBaseDS`, column `Name`; deviating from this pattern breaks the internal template mapping.
3. **Using a non-`KnowledgeBase` entity** — the component is purpose-built for the `KnowledgeBase` schema; connecting it to a different entity schema requires the same column paths (`Name`, `Notes`, `CreatedBy`, `ModifiedOn`, `Type`).
4. **`items` bound to a non-collection attribute** — `items` expects a `BaseViewModelCollection`; binding to a scalar attribute silently produces an empty list.
5. **No `@CrtInterfaceDesignerItem` create command** — unlike chart widgets, `crt.Playbook` has no automated create command; you must wire all three diff sections manually.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Playbook"`, unique `name`, valid `parentName`, `propertyName: "items"`, and valid `index`.
- [ ] `items` bound to a `$Attribute` that is declared as a `BaseViewModelCollection` in `viewModelConfigDiff`.
- [ ] `modelConfigDiff` contains a `crt.EntityDataSource` for `KnowledgeBase` with the required attribute columns.
- [ ] `_designOptions.templateValuesMapping` present with all six column bindings (`id`, `caption`, `content`, `author`, `modifiedOn`, `type`).
- [ ] `layoutConfig` present when the parent is a `crt.GridContainer`.
