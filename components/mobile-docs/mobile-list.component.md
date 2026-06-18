# How to Add a Mobile List (`crt.List`) to a Freedom UI Mobile Designer Page

> Audience: code agent inserting a `crt.List` into a Creatio Freedom UI mobile page schema.
>
> `crt.List` in this library is a **mobile design-time view element**: it configures and previews mobile list
> metadata in the interface designer. Do not treat it as the regular desktop Freedom UI runtime list or grid.

## Metadata

- **Category**: mobile, data, display
- **Container**: yes (row metadata lives in `itemLayout.body` and `itemLayout.subtitles`; do not put arbitrary page children directly into the list)
- **Parent types**: mobile page root container, mobile design-time containers that accept component `items`
- **Typical children**: `crt.ListItem` metadata inside `itemLayout`, with body column objects rather than nested Freedom UI controls

---

## 1. Mental model — the 3 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.List"`, an `items` collection binding, `itemLayout`, and `scrollable`. **Always present.** |
| 2 | `modelConfigDiff` | An entity data source if the list reads records from an object. |
| 3 | `viewModelConfigDiff` | A collection attribute for `items`, plus simple attributes for title, icon, body, subtitle, and primary key columns used by `itemLayout`. |

`crt.List` is configured through the mobile list properties panel (`crt.MobileListPropertiesPanel`). The panel and
`DesignerMobileListStateHandler` keep the datasource, view-model attributes, and `itemLayout` bindings aligned when the
selected object or visible columns change.

### 1.1 Naming convention

```text
MobileList_<id>            // view element name
MobileList_<id>DS          // entity datasource name
MobileList_<id>Items       // collection view-model attribute
MobileList_<id>DS_<Column> // simple attribute projected from the datasource
ListItem_<id>              // itemLayout.name
```

---

## 2. Step-by-step recipe

### 2.1 Add or reuse the entity datasource (`modelConfigDiff`)

Use an entity datasource when the list should show object records. Keep the datasource name stable because the
`itemLayout` bindings usually include it in generated attribute names.

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "dataSources": {
      "MobileList_ContactsDS": {
        "type": "crt.EntityDataSource",
        "scope": "viewElement",
        "config": {
          "entitySchemaName": "Contact"
        }
      }
    }
  }
}
```

### 2.2 Add the collection and projected attributes (`viewModelConfigDiff`)

The list's `items` value points to a collection attribute. The title, icon, body, and subtitles point to simple
attributes projected from the same datasource.

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "MobileList_ContactsItems": {
        "isCollection": true,
        "modelConfig": {
          "path": "MobileList_ContactsDS"
        }
      },
      "MobileList_ContactsDS_Name": {
        "modelConfig": {
          "path": "MobileList_ContactsDS.Name"
        }
      },
      "MobileList_ContactsDS_Email": {
        "modelConfig": {
          "path": "MobileList_ContactsDS.Email"
        }
      },
      "MobileList_ContactsDS_Photo": {
        "modelConfig": {
          "path": "MobileList_ContactsDS.Photo"
        }
      }
    }
  }
}
```

### 2.3 Insert the mobile list (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "MobileList_Contacts",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.List",
    "items": "$MobileList_ContactsItems",
    "itemLayout": {
      "name": "ListItem_Contacts",
      "type": "crt.ListItem",
      "title": "$MobileList_ContactsDS_Name",
      "icon": "$MobileList_ContactsDS_Photo",
      "body": [
        {
          "value": "$MobileList_ContactsDS_Email"
        }
      ],
      "subtitles": [],
      "showEmptyValues": false
    },
    "scrollable": false
  }
}
```

The design-time component changes its placeholder artwork based on whether `items`, `itemLayout.title`, and
`itemLayout.icon` are defined. The default toolbox insertion starts with `itemLayout.name`, `type: "crt.ListItem"`,
empty `body`, and `scrollable: false`.

---

## 3. Property reference

Full registry data for `crt.List` is in `ComponentRegistry.json` under `componentType: "crt.List"`. In the current
registry snapshot the component has no leaf inputs listed, so the important schema shape comes from
`MobileListViewConfig`: `items`, `itemLayout`, and `scrollable`.

Additional runtime properties:

| Property | Type | Description |
|---|---|---|
| `itemSelected` | object | Request descriptor fired when the user taps a list item. Configure as `{ request: '<RequestType>' }`. |
| `useSeparator` | boolean | Show thin separator lines between list items. Default: `false`. |
| `enablePullToRefresh` | boolean | Allow the user to pull down to refresh the list data. Default: `false`. |
| `itemPadding` | object | Padding around each list item. Shape: `{ top, bottom, left, right }` in pixels. |

### Child component: `crt.ListItem`

`crt.ListItem` has no Angular component but is required to configure the list row template.
Insert it via `propertyName: "itemLayout"`:

```jsonc
{
  "operation": "insert",
  "name": "ListItem1",
  "values": {
    "type": "crt.ListItem",
    "title": "$List1DS_Name",
    "body": [{ "value": "$List1DS_Account" }, { "value": "$List1DS_Email" }],
    "subtitles": [],
    "showEmptyValues": true,
    "icon": null
  },
  "parentName": "List1",
  "propertyName": "itemLayout",
  "index": 0
}
```

| Property | Type | Description |
|---|---|---|
| `title` | string | Primary display column binding (e.g. `'$List1DS_Name'`). |
| `body` | array | Array of `{ value: "$DS_Column" }` objects — rendered as labeled fields. |
| `subtitles` | array | Array of subtitle descriptor objects. |
| `icon` | string | Image column binding or `null`. |
| `iconSize` | string | Icon size: `small`, `medium`, `large`. |
| `showEmptyValues` | boolean | Show rows for empty-value body fields. Default: `true`. |
| `actions` | array | Action button configs shown on the item. |

---

## 4. Shape of types not expanded in the registry

```ts
interface MobileListViewConfig {
  items?: string;
  itemLayout: MobileListItemLayout;
  scrollable?: boolean;
}

interface MobileListItemLayout {
  name: string;
  type: 'crt.ListItem';
  title?: string;
  icon?: string;
  body: Array<{ value: string }>;
  subtitles?: Array<{
    value: string;
    label: { visible: boolean };
    name: string;
  }>;
  showEmptyValues?: boolean;
}
```

`title`, `icon`, `body[*].value`, and `subtitles[*].value` should be `$` bindings to attributes from the collection
datasource. The state handler normalizes missing `$` prefixes when mobile-list columns are changed through designer
commands.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — design-time placeholder only, before an object is selected
{
  "operation": "insert",
  "name": "MobileList_New",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.List",
    "itemLayout": {
      "name": "ListItem_New",
      "type": "crt.ListItem",
      "body": []
    },
    "scrollable": false
  }
}
```

```jsonc
// viewConfigDiff entry — configured mobile list
{
  "operation": "insert",
  "name": "MobileList_Contacts",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.List",
    "items": "$MobileList_ContactsItems",
    "itemLayout": {
      "name": "ListItem_Contacts",
      "type": "crt.ListItem",
      "title": "$MobileList_ContactsDS_Name",
      "icon": "$MobileList_ContactsDS_Photo",
      "body": [
        {
          "value": "$MobileList_ContactsDS_Email"
        }
      ],
      "subtitles": [
        {
          "value": "$MobileList_ContactsDS_JobTitle",
          "label": {
            "visible": true
          },
          "name": "ContactJobTitle"
        }
      ],
      "showEmptyValues": false
    },
    "scrollable": true
  }
}
```

---

## 6. Driving from page state

`items` is the main state binding: it must point to a collection attribute, usually declared with `isCollection: true`
and backed by an entity datasource. `scrollable` is a static boolean in designer metadata; the mobile properties panel
defaults it to `true` when absent and writes explicit changes to `itemConfig.scrollable`.

When the selected object changes, the mobile properties panel clears incompatible `itemLayout.body` and
`itemLayout.subtitles`, then rewrites `itemLayout.title` and `itemLayout.icon` to match columns from the new datasource.

---

## 7. Common pitfalls

1. **Using the desktop list mental model.** This `crt.List` is registered with `@CrtMobileViewElement` and belongs to mobile design-time metadata.
2. **Forgetting `items`.** Without a collection binding the designer shows the unconfigured mobile-list placeholder.
3. **Putting child controls directly into `items`.** Row content is described by `itemLayout.body` and subtitles, not by nested page view elements.
4. **Leaving body/title/icon attributes out of `viewModelConfigDiff`.** The bindings can resolve only when the projected attributes exist.
5. **Reusing `itemLayout.name` when copying manually.** The copy command regenerates `ListItem_*`; manual inserts should also keep each layout name unique.
6. **Binding title or icon to unsupported columns.** The properties panel offers text columns for title and image columns for icon.
7. **Assuming `scrollable` defaults to false everywhere.** Toolbox metadata sets `false`, while the properties panel normalizes missing values to `true`.

---

## 8. Quick checklist

- [ ] Insert `type: "crt.List"` only into a mobile design-time page/container.
- [ ] Add or reuse an entity datasource when records should come from an object.
- [ ] Declare the collection attribute used by `items`.
- [ ] Declare every projected title, icon, body, subtitle, and primary-key attribute used by the list.
- [ ] Set `itemLayout.type` to `crt.ListItem` and use a unique `itemLayout.name`.
- [ ] Keep row fields as `$` bindings in `itemLayout`.
- [ ] Set `scrollable` deliberately instead of relying on mixed design-time defaults.
