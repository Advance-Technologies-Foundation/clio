# How to Add a Search Filter (`crt.SearchFilter`) to a Freedom UI Page

> Audience: code agent inserting a `crt.SearchFilter` into a Creatio Freedom UI page schema.
>
> `crt.SearchFilter` is a search input bound declaratively to one or more collection
> attributes (typically `Items` on a list page). The canonical wiring is via
> `targetAttributes` (simple form) or `_filterOptions.expose` (newer form) — both
> consumed by `SearchFilterPreprocessor`, which generates the value attribute, the
> filtered-columns attribute, the converter, and the handler that pushes the typed text
> into the target collection's filtration API. No manual `valueChange` handler is needed.

## Metadata

- **Category**: filtering
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.SearchFilter"` and either `targetAttributes` (legacy, simpler) or `_filterOptions` (newer, more flexible). |

The preprocessor (`SearchFilterPreprocessor`, in `libs/studio-enterprise/feature/components-preprocessors/src/lib/search-filter/`) reads the declarative wiring and at page-load time:

- Generates the value attribute (`<name>_SearchValue`) and the filtered-columns attribute (`<name>_FilteredColumnsGroups`).
- Generates the runtime `valueChange` binding to a built-in request.
- Generates the `crt.SearchFilterAttributeConverter` that maps the typed text onto the target collection attribute (e.g. `Items`) — which is consumed by any `compatibleAPIs: { Filtration: true }` collection (`crt.DataGrid`, `crt.Gallery`, `crt.FileList`, …).
- Computes the column-picker UI when `columnsGroupsConfig` is set.

You do **not** need to declare a value attribute or a handler manually.

### 1.1 Naming convention

```
SearchFilter_<id>                            // view element name (e.g. "AddressSearchFilter")
<element>_SearchValue                        // generated value attribute
<element>_FilteredColumnsGroups              // generated columns-group attribute
<element>_<target>                           // generated converter attribute (per target)
```

The platform-recommended caption resource key is `<elementName>_placeholder` for the placeholder string.

The default `name` used by stock list pages is the literal `SearchFilter`.
A second SearchFilter on the same page **must** use a different `name` —
every auto-generated artefact above is keyed by `<name>_…`, so duplicate
names overwrite each other's bindings.

---

## 1.2 Prerequisite — the target collection attribute must exist

`crt.SearchFilter` does **not** talk to a data source directly. It writes
through a **collection view-model attribute** declared in
`viewModelConfigDiff` (`isCollection: true`, `modelConfig.path` → a key in
`modelConfigDiff.dataSources`). The collection attribute is **never
auto-generated** by any data-source type — there is no implicit
`<DSName>_List`, `<DSName>_Items`, or `Items` attribute. If
`targetAttributes` names something that is not under
`viewModelConfigDiff[].values.attributes`, the preprocessor silently skips
the converter and the input may render `disabled`.

**Decision flow.** If some element on the page already binds
`items: "$<Name>"`, reuse that `<Name>` verbatim — no new diffs needed.
Otherwise declare the data source + collection attribute first:

```jsonc
// modelConfigDiff — register the data source
{ "operation": "merge", "path": [], "values": {
    "dataSources": {
      "MyDS": {
        "type": "crt.EntityDataSource",
        "scope": "viewElement",
        "config": { "entitySchemaName": "Account" }
      }
    }
} }
```

```jsonc
// viewModelConfigDiff — declare the collection attribute SearchFilter targets
{ "operation": "merge", "path": [], "values": {
    "attributes": {
      "MyCollection": {
        "isCollection": true,
        "modelConfig": { "path": "MyDS" }
      }
    }
} }
```

The string passed to `targetAttributes` (here `"MyCollection"`) must match
the key under `attributes`. The data-source key (`"MyDS"`) appears only
inside `modelConfig.path`. Per-column nested `attributes` are only required
when the collection consumer reads specific column codes.

For a stock list page, the implicit collection is `"Items"` — use
`targetAttributes: ["Items"]` without any new diffs.

---

## 2. Step-by-step recipe — two equivalent wirings

**Decision rule.** Default to `targetAttributes` (§ 2.1). Switch to
`_filterOptions.expose` (§ 2.2) **only** when you need to (a) name the
calculated attribute explicitly, or (b) chain multiple converters on the
same SearchFilter.

### 2.1 Simple form: `targetAttributes`

Use this when the search applies to one or more collection attributes by name. The preprocessor builds the converter and value attributes automatically.

**What to put in `targetAttributes`.** The value is the **name of a collection
view-model attribute** that some other component on the page already binds to
via `items: "$<Name>"`. It is **not** the entity name, **not** the data-source
name, **not** a column code. Example: if the page exposes a collection
attribute `Account_List` (bound to `AccountDS` in `viewModelConfigDiff`) and a
list/gallery/file-list element renders it via `"items": "$Account_List"`,
then `targetAttributes: ["Account_List"]`. The preprocessor pushes the
generated filter into that collection's filtration API — no further wiring is
needed.

```jsonc
{
  "operation": "insert",
  "name": "SearchFilter_xkp4r",
  "parentName": "RightFilterContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.SearchFilter",
    "placeholder": "#ResourceString(SearchFilter_xkp4r_placeholder)#",
    "targetAttributes": ["Items"]
  }
}
```

### 2.2 Newer form: `_filterOptions.expose`

Use this when you need explicit control over the generated attribute or want to chain multiple converters. Typical on detail pages with embedded lists (e.g. a contact's address list).

```jsonc
{
  "operation": "insert",
  "name": "AddressSearchFilter",
  "parentName": "AddressToolsFlexContainer",
  "propertyName": "items",
  "index": 3,
  "values": {
    "type": "crt.SearchFilter",
    "placeholder": "#ResourceString(AddressSearchFilter_placeholder)#",
    "iconOnly": true,
    "_filterOptions": {
      "expose": [
        {
          "attribute": "AddressSearchFilter_AddressList",
          "converters": [
            {
              "converter": "crt.SearchFilterAttributeConverter",
              "args": ["AddressList"]
            }
          ]
        }
      ],
      "from": [
        "AddressSearchFilter_SearchValue",
        "AddressSearchFilter_FilteredColumnsGroups"
      ]
    }
  }
}
```

Field meanings (`_filterOptions`):

- `expose[].attribute` — page attribute the converter writes into; consumed by the target collection's filtration API.
- `expose[].converters[0].converter` — must be `"crt.SearchFilterAttributeConverter"`.
- `expose[].converters[0].args` — array containing the target collection attribute name (e.g. `"AddressList"`, `"Items"`).
- `from` — the two value attributes the converter reads from: `<name>_SearchValue` and `<name>_FilteredColumnsGroups`. The preprocessor declares both.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.SearchFilter` are in `ComponentRegistry.json` under `componentType: "crt.SearchFilter"`. This guide covers
only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 4. Copy-paste minimal example — page-level list search

```jsonc
// viewConfigDiff entry — single insert, no other diffs needed
{
  "operation": "insert",
  "name": "ListSearchFilter",
  "parentName": "RightFilterContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.SearchFilter",
    "placeholder": "#ResourceString(ListSearchFilter_placeholder)#",
    "targetAttributes": ["Items"]
  }
}
```

The bound collection consumer (any element with `items: "$<CollectionAttr>"` whose component supports `compatibleAPIs: { Filtration: true }`) receives the resulting filter through its filtration API — no extra wiring needed.

---

## 5. Common pitfalls

> **Anti-patterns — do NOT do any of these to "make the search filter the list".**
> If you find yourself writing any of these, you missed `targetAttributes`.
> Delete the manual wiring and add `targetAttributes: ["<collection-attr>"]`
> (or `_filterOptions.expose`) — the preprocessor generates everything else.
>
> - A `handlers` entry on `crt.HandleViewModelAttributeChangeRequest`
>   listening to a search-related attribute.
> - A custom `FilterGroup` built inside a handler
>   (`new sdk.FilterGroup()` + `addSchemaColumnFilterWithParameter`).
> - Manual dispatch of `crt.LoadDataRequest` with `customFilters` from a
>   handler.
> - A view-model attribute that stores the typed search string
>   (`<anything>SearchValue`, `searchText`, `query`, …).
> - `value: "$<anything>"` inside the SearchFilter `values` block.
> - A `valueChange.request` binding on the SearchFilter.
> - Manually declared `<name>_SearchValue` / `<name>_FilteredColumnsGroups`
>   attributes (the preprocessor declares them and duplicates collide).

1. **`targetAttributes` references an attribute the page doesn't expose** — see § 1.2. Preprocessor skips the converter; input may render `disabled`.
2. **`_filterOptions.expose[].attribute` colliding with another component's exposed attribute** — last write wins. Use unique `<elementName>_<target>` suffixes.
3. **`columnsGroupsConfig.columns` set with no entries** — column picker stays hidden. Each `columnsGroups[i].columns` must be non-empty.
4. **`iconOnly: true` on a wide layout cell** — input collapses to one icon, wasting cell width. Pair with a narrow `colSpan` or a tools flex container.
5. **Expecting `instant: true` to change debounce** — the converter enforces its own debounce. `instant` only affects emit-on-clear vs emit-on-keystroke at the component level.

---

## 6. Quick checklist

- [ ] **Exactly one of** `targetAttributes: ["<CollectionAttr>"]` **OR** `_filterOptions.expose[]` is present. If both are absent → stop.
- [ ] The string in `targetAttributes` (or in `_filterOptions.expose[].converters[0].args[0]`) equals the **name of a collection view-model attribute** declared under `viewModelConfigDiff[].values.attributes` with `isCollection: true` and `modelConfig.path` pointing at a key in `modelConfigDiff.dataSources`. Not the entity name. Not the data-source name. Not a column code. (See § 1.2.)
- [ ] `placeholder` localized via `#ResourceString(<elementName>_placeholder)#`.
- [ ] `iconOnly` chosen intentionally (`true` for detail-tools, `false` for the main search bar).
- [ ] **Zero** manual entries in `handlers` / `viewModelConfigDiff` / `modelConfigDiff` for the search itself — no `*SearchValue` attribute, no `valueChange.request` binding, no `crt.HandleViewModelAttributeChangeRequest` handler, no manual `FilterGroup` + `crt.LoadDataRequest`.
- [ ] `layoutConfig` provided if the parent is a grid container (omit for flex tools containers).
