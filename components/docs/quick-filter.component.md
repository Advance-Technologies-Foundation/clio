# How to Add a Quick Filter (`crt.QuickFilter`) to a Freedom UI Page

> Audience: code agent inserting a `crt.QuickFilter` into a Creatio Freedom UI page schema.
>
> `crt.QuickFilter` is a chip-style filter whose internal control is swapped at runtime by
> the `filterType` (`custom`, `date-range`, `lookup`). The canonical wiring is declarative:
> a `_filterOptions.expose` block tells the platform how the filter's value maps onto the
> target collection's view-model attribute, and the platform preprocessors generate the
> value/converter attributes and handlers that move values between the chip and the
> collection (the generic `FilterOptionsPreprocessor` consumes `_filterOptions`;
> `QuickFilterPreprocessor` wires the chip's value/valueChange behavior).
>
> **The chip alone does not filter the list.** The preprocessing produces the filter
> expression but does **not** register it on the target collection. You must also add the
> chip's `<ChipName>_Items` attribute to the collection's `modelConfig.filterAttributes`
> array — otherwise the chip renders and accepts values but the record set is never
> filtered, with no console error. See §1 and §2.2.
>
> **Repairing a filter that was already added but doesn't work?** Do **not** delete and
> re-create the chip — the chip config is almost always correct, and a freshly inserted
> chip has the exact same gap. The missing piece is the §2.2 registration. Jump to **§2.3**
> for the diagnose-and-repair steps and the repair anti-patterns (the wrong-path and
> wire-it-onto-the-DataTable mistakes that look plausible but never fix it).

## Metadata

- **Category**: filtering
- **Container**: no
- **Parent types**: `crt.GridContainer`, `crt.FlexContainer`
- **Typical children**: none (filter UI is internal to the chosen `filterType`)

---

## 1. Mental model — the 2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.QuickFilter"`, `filterType`, `config`, and a `_filterOptions` block that points at the target collection's filter column. |
| 2 | `viewModelConfigDiff` | A `merge` op that registers the chip's `<ChipName>_Items` attribute in the target collection's `modelConfig.filterAttributes` array (`{ "name": "<ChipName>_Items", "loadOnChange": true }`). |

Two preprocessors split the work: the generic `FilterOptionsPreprocessor` reads
`_filterOptions` and synthesizes the calculated filter attribute (`<ChipName>_Items`) plus
the `crt.QuickFilterAttributeConverter` wiring, while `QuickFilterPreprocessor` creates the
chip's value attribute (`<ChipName>_Value`) and its `valueChange` handler — no handler code
or value-attribute declaration needed at the page level.

What the preprocessors do **not** do is register the produced filter on the target
collection. The collection only applies attributes listed in its
`modelConfig.filterAttributes`; if `<ChipName>_Items` is missing from that array the filter
expression is computed but never consumed. Step 2 is mandatory and is the single most
common reason a clio-MCP-added quick filter renders but does not filter (Page Designer adds
this entry automatically; the documented chip insert does not).

### 1.1 Naming convention

```
QuickFilterBy<X>            // view element name, e.g. QuickFilterByDate, QuickFilterByOwner
QuickFilterBy<X>_Items      // attribute the converter writes into (filter expression for the collection)
QuickFilterBy<X>_Value      // attribute the converter reads from (current chip value)
```

`FilterOptionsPreprocessor` names the filter attribute after `expose[].attribute`
(`<ChipName>_Items`), while `QuickFilterPreprocessor` derives the value attribute name from
the chip's `name` (`<ChipName>_Value`) regardless of `_filterOptions.from` — so `from` must
point at that generated `<ChipName>_Value` attribute; deviating from the naming convention
breaks the wiring. The platform-recommended caption resource key is
`<elementName>_config_caption`.

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "QuickFilterByDate",
  "parentName": "LeftFilterContainerInner",
  "propertyName": "items",
  "index": 1,
  "values": {
    "type": "crt.QuickFilter",
    "filterType": "date-range",
    "config": {
      "caption": "#ResourceString(QuickFilterByDate_config_caption)#",
      "hint": "",
      "icon": "date",
      "iconPosition": "left-icon"
    },
    "_filterOptions": {
      "expose": [
        {
          "attribute": "QuickFilterByDate_Items",
          "converters": [
            {
              "converter": "crt.QuickFilterAttributeConverter",
              "args": [
                {
                  "target": {
                    "viewAttributeName": "Items",
                    "filterColumn": "CreatedOn"
                  },
                  "quickFilterType": "date-range"
                }
              ]
            }
          ]
        }
      ],
      "from": "QuickFilterByDate_Value"
    },
    "visible": true
  }
}
```

The target collection (any `compatibleAPIs: { Filtration: true }` component like
`crt.DataGrid`, `crt.Gallery`, or `crt.FileList`) is the one named in
`target.viewAttributeName` — typically `Items`. Inserting the chip is **not** sufficient on
its own: continue with step 2.2 to register the filter on that collection.

### 2.2 Register the filter on the target collection (`viewModelConfigDiff` entry)

The collection only applies the attributes listed in its `modelConfig.filterAttributes`.
Add the chip's exposed `<ChipName>_Items` attribute to that array via a `merge` op on
`viewModelConfigDiff`:

```jsonc
{
  "operation": "merge",
  "path": ["attributes", "Items", "modelConfig"],
  "values": {
    "filterAttributes": [
      { "name": "QuickFilterByDate_Items", "loadOnChange": true }
    ]
  }
}
```

Rules:

- `path[1]` (`"Items"` above) is the collection's view-model attribute — it **must** match
  `target.viewAttributeName` from step 2.1.
- `name` **must** equal the chip's `_filterOptions.expose[].attribute` (`<ChipName>_Items`),
  exactly.
- `loadOnChange: true` reloads the collection when the chip value changes — always set it
  for a quick filter.
- A `merge` op **replaces** the `filterAttributes` array. If the collection already
  registers other filter attributes (e.g. a predefined filter or another quick filter),
  list all of them in `values.filterAttributes` together with the new entry, or the
  existing ones are dropped.

Without this entry the chip renders and accepts values but the list is not filtered — a
silent failure with no console error or validation warning. This is exactly what the
preprocessors do **not** do for you.

### 2.3 Repair an already-added quick filter that renders but doesn't filter

A quick filter added earlier (an older agent run, a previous plan, a copied page) very
often has the chip in place — it renders, opens, and accepts a value — but the list never
filters. The chip config is almost always fine; the missing piece is the §2.2 registration.
**Add only what's missing — do not re-create the chip.**

**Step 1 — diagnose (read the current page body, don't guess):**

1. Find the chip's `viewConfigDiff` insert. Note its `name` (`<ChipName>`) and the
   `target.viewAttributeName` inside `_filterOptions.expose[].converters[].args[]` — the
   target collection attribute, usually `Items`.
2. In `viewModelConfigDiff`, look for an operation whose `path` is **exactly**
   `["attributes", "<Collection>", "modelConfig"]` and whose `values.filterAttributes`
   array contains `{ "name": "<ChipName>_Items", "loadOnChange": true }`.
3. If that entry is absent — or it exists but at the wrong path / with a mismatched
   `name` — the filter expression is computed but never consumed. **That is the bug.**

**Step 2 — repair (add the one missing registration, nothing else):**

```jsonc
// viewModelConfigDiff — the entry that was missing; substitute <Collection> and
// <ChipName> with the actual values you read in Step 1 (e.g. "Items" and
// "QuickFilterByStatus") — the placeholders here are not literal.
{
  "operation": "merge",
  "path": ["attributes", "<Collection>", "modelConfig"],
  "values": {
    "filterAttributes": [
      { "name": "<ChipName>_Items", "loadOnChange": true }
    ]
  }
}
```

If the collection already lists other filter attributes, include all of them here — a
`merge` **replaces** the array (pitfall #6).

**Step 3 — repair anti-patterns (these look plausible, do NOT fix it, and often make it worse):**

- ❌ **Wiring `_filterOptions` / `_filterOptions.from` (or `filterAttributes`) onto the
  DataTable / collection view element itself** in `viewConfigDiff`. The collection does
  not consume the filter from its own view element; the registration lives in
  `viewModelConfigDiff` under the collection **attribute's** `modelConfig` (Step 2), not on
  the `crt.DataGrid` / `crt.DataTable` element.
- ❌ **Registering one level too high** at `["attributes", "Items"]`. The runtime reads
  `filterAttributes` from `modelConfig`, so the path **must** end with `"modelConfig"`:
  `["attributes", "Items", "modelConfig"]`. This is the single most common repair miss.
- ❌ **Delete-and-re-add the chip "from scratch"** hoping a preprocessor will register it.
  It will not — a brand-new chip starts with the very same gap. Only the Step 2 entry makes
  it filter.
- ❌ **Adding a predefined/static filter** or touching the entity model. The data is fine;
  this is purely a view-model registration gap.

**Step 4 — verify:** reload the page, pick a chip value, and confirm the **record count
changes** (selecting one option should drop the total). If the count is unchanged, the
registration is still missing, at the wrong path, or its `name` doesn't byte-for-byte match
the chip's `_filterOptions.expose[].attribute`.

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.QuickFilter` are in `ComponentRegistry.json` under `componentType: "crt.QuickFilter"`. This guide covers
only the assembly mechanics — what to put where in `viewConfigDiff` /
`viewModelConfigDiff` / `modelConfigDiff`.

---

## 4. `config` shape per `filterType`

All configs extend `BaseQuickFilterConfig`:

```ts
interface BaseQuickFilterConfig<TValue> {
  readonly defaultValue?: TValue;
  readonly caption: string;
  readonly hint?: string;
  readonly icon?: string;
  readonly iconPosition?: "left-icon" | "right-icon" | "only-icon" | "only-text";
}
```

`config.icon` is an optional platform icon-registry name (a `string`). **Whether it renders depends
on `filterType`:** the `date-range` chip binds it directly to `mat-icon [svgIcon]`; the `lookup`
chip forwards it to the chip's `crt.Button` (so it follows the button's icon rules); the `custom`
chip renders a checkbox from `caption`/`hint` only and **ignores `config.icon`** entirely. Where the
icon does render, any name registered in the platform `MatIconRegistry` works, including names
outside `crt.Button`'s curated set (e.g. `calendar-icon`). For a list of **known-good, common**
names, the `crt.Button` registry entry's `references.typeDefinitions.ButtonIcon.values` is the
handiest reference (e.g. `"date"`, `"person-button-icon"`, `"filter-column-icon"`). The name **must
be an exact registered name** — short aliases like `"calendar"` are not registered and render an
empty icon (with an `Error retrieving icon :<name>!` console message, no UI/platform validation).
Set `iconPosition` to control placement relative to the chip caption (for the chip types that render
an icon).

### 4.1 `filterType: "custom"` — boolean toggle filter

```jsonc
"config": {
  "caption": "#ResourceString(ShowArchived_config_caption)#",
  "icon": "filter-column-icon",   // ignored by the custom chip — it renders a checkbox from caption/hint only
  "iconPosition": "left-icon",    // ignored by the custom chip
  "defaultValue": false,
  "approachState": true
}
```

`CustomQuickFilterConfig` adds:

- `approachState: boolean` — required; controls how the chip's "approached" UI state is rendered.

Value: `boolean`.

### 4.2 `filterType: "date-range"` — date range filter

```jsonc
"config": {
  "caption": "#ResourceString(QuickFilterByDate_config_caption)#",
  "icon": "date",
  "iconPosition": "left-icon",
  "showFiscalPeriods": true,
  "showTime": false
}
```

`DateRangeQuickFilterConfig` adds:

- `showFiscalPeriods?: boolean` — show fiscal-period presets (e.g. Q1, Q2).
- `showTime?: boolean` — include time pickers alongside dates.

Value: `DateRangeQuickFilterValue` (see source).

### 4.3 `filterType: "lookup"` — lookup-based filter

```jsonc
"config": {
  "caption": "#ResourceString(QuickFilterByOwner_config_caption)#",
  "icon": "person-button-icon",
  "iconPosition": "left-icon",
  "defaultValue": [],
  "entitySchemaName": "Contact",
  "recordsFilter": null
}
```

`LookupQuickFilterConfig` adds (source: `lookup-quick-filter-config.model.ts`):

- `entitySchemaName: string` — **required**. The lookup entity (e.g. `"Contact"`, `"Country"`).
- `showRecordsInWindows: boolean` — open the selection in a modal window (`true`) instead of an inline list (`false`). Optional in practice: the runtime reads it via optional chaining, so omitting it (as most real lookup filters do) behaves as `false`, even though the interface currently types it as non-optional.
- `recordsFilter?: unknown` — static filter applied to the lookup query (often `null`).
- `comboboxViewElementConfig?: ViewElementConfig` — override the inner combobox's view config.

Value: `LookupQuickFilterValue` — array of selected `LookupValue`s.

Source TypeScript models (in-repo, for reference): `custom-quick-filter-config.model.ts`, `date-range-quick-filter-config.model.ts`, `lookup-quick-filter-config.model.ts` under `libs/studio-enterprise/ui/quick-filter/src/runtime/models/`.

---

## 5. `_filterOptions` deep-dive

`_filterOptions` is consumed by the generic `FilterOptionsPreprocessor` at page-load time
and turned into runtime attributes and converters; `QuickFilterPreprocessor` separately
wires the chip's value/valueChange behavior. Shape:

```jsonc
"_filterOptions": {
  "expose": [
    {
      "attribute": "<ChipName>_Items",
      "converters": [
        {
          "converter": "crt.QuickFilterAttributeConverter",
          "args": [
            {
              "target": {
                "viewAttributeName": "Items",
                "filterColumn": "<EntityColumnPath>"
              },
              "quickFilterType": "<custom | date-range | lookup>"
            }
          ]
        }
      ]
    }
  ],
  "from": "<ChipName>_Value"
}
```

Field meanings:

- `expose[].attribute` — page attribute the converter **writes** the filter expression into. By
  convention `<ChipName>_Items`.
- `expose[].converters[0].converter` — must be `"crt.QuickFilterAttributeConverter"`.
- `target.viewAttributeName` — the **target collection's** view-model attribute that the
  filter applies to — the attribute the list is bound to, typically `"Items"`. It must
  match the collection attribute you register in step 2.2 (`path[1]` of the
  `viewModelConfigDiff` merge).
- `target.filterColumn` — the entity column that the filter compares against
  (e.g. `"CreatedOn"`, `"Owner"`, `"State"`).
- `quickFilterType` — must match `filterType` at the root.
- `from` — page attribute the converter **reads** the current chip value from. Must be the
  `<ChipName>_Value` attribute that `QuickFilterPreprocessor` generates from the chip's
  `name` — the value attribute name is not taken from `from`, so any other value here
  breaks filtering.

The preprocessors:
- `FilterOptionsPreprocessor` declares the calculated `<ChipName>_Items` attribute (and its
  converter-args attributes), running the converter whenever `_Value` changes and writing
  the resulting filter expression to `_Items`.
- `QuickFilterPreprocessor` declares `<ChipName>_Value` and wires `valueChange` to update it.

You do **not** need to declare the `<ChipName>_Items` / `<ChipName>_Value` attributes in
`viewModelConfigDiff` yourself — the preprocessors create them.

What the preprocessors do **not** do is hook `<ChipName>_Items` into the collection. The
target collection's filtration API applies an attribute only if it is listed in the
collection's `modelConfig.filterAttributes` (step 2.2). Neither preprocessor touches
`filterAttributes`, so this one entry is on you.

---

## 6. Copy-paste minimal example — date range filter on a list page

```jsonc
// viewConfigDiff entry — inserts the chip into the page's filter bar
{
  "operation": "insert",
  "name": "QuickFilterByDate",
  "parentName": "LeftFilterContainerInner",
  "propertyName": "items",
  "index": 1,
  "values": {
    "type": "crt.QuickFilter",
    "filterType": "date-range",
    "config": {
      "caption": "#ResourceString(QuickFilterByDate_config_caption)#",
      "hint": "",
      "icon": "date",
      "iconPosition": "left-icon"
    },
    "_filterOptions": {
      "expose": [
        {
          "attribute": "QuickFilterByDate_Items",
          "converters": [
            {
              "converter": "crt.QuickFilterAttributeConverter",
              "args": [
                {
                  "target": { "viewAttributeName": "Items", "filterColumn": "CreatedOn" },
                  "quickFilterType": "date-range"
                }
              ]
            }
          ]
        }
      ],
      "from": "QuickFilterByDate_Value"
    },
    "visible": true
  }
}
```

```jsonc
// viewModelConfigDiff entry — registers the chip's filter on the Items collection (REQUIRED)
{
  "operation": "merge",
  "path": ["attributes", "Items", "modelConfig"],
  "values": {
    "filterAttributes": [
      { "name": "QuickFilterByDate_Items", "loadOnChange": true }
    ]
  }
}
```

Both entries are required. The `viewConfigDiff` insert renders the chip and produces the
filter expression; the `viewModelConfigDiff` merge registers that expression on the `Items`
collection so it is actually applied. Omit the second entry and the chip works visually but
the list never filters.

---

## 7. Common pitfalls

1. **Forgetting the `filterAttributes` registration (step 2.2)** — the #1 silent failure. The chip renders, accepts values, and the preprocessor computes the filter expression, but the list is never filtered because `<ChipName>_Items` is not in the collection's `modelConfig.filterAttributes`. No console error, no validation warning. Page Designer adds this automatically; an MCP-driven chip insert must add it explicitly via `viewModelConfigDiff`.
2. **Missing `filterType`** — the component renders nothing because the inner control is swapped on `filterType` change and there's no default.
3. **`config.defaultValue` shape mismatched with `filterType`** — e.g. `"defaultValue": []` on `"custom"` (which expects boolean) leaves the chip in an invalid state.
4. **`target.viewAttributeName` (inside `_filterOptions.expose[].converters[].args[]`) doesn't match the target collection's attribute** — the filter expression is generated but no collection consumes it. The most common target is `"Items"`; it must also be the `path[1]` of the step 2.2 `viewModelConfigDiff` merge.
5. **`filterAttributes` name doesn't match the exposed attribute** — the `name` in `modelConfig.filterAttributes` must be byte-for-byte the chip's `_filterOptions.expose[].attribute` (`<ChipName>_Items`). A typo registers a non-existent attribute and the filter silently does nothing.
6. **`merge` op drops existing `filterAttributes`** — a `merge` replaces the array. If the collection already has filter attributes, include them all in the step 2.2 `values.filterAttributes`, not just the new entry.
7. **Wiring `value`/`valueChange` manually instead of using `_filterOptions`** — works at the binding level but bypasses the preprocessor, so the collection's filtration API never sees the chip's output. Always go through `_filterOptions`.
8. **Inventing config fields** — `LookupQuickFilterConfig` does NOT have `displayColumn` or `multiSelect`; for an entity-lookup chip you only need `entitySchemaName` (plus optional `showRecordsInWindows` / `recordsFilter`). Multi-select behavior is built in for lookup variants.
9. **Setting `caption` at the root of `values`** — designer ignores it; the chip caption comes from `config.caption`. Always set it inside `config`. The platform convention is the localizable string `<elementName>_config_caption`.
10. **Forgetting `parentName`** — quick filters live inside the page's filter container (typically `LeftFilterContainerInner` on list pages); inserting into the wrong parent puts them outside the filter bar.
11. **Registering the filter on the DataTable element instead of the collection attribute** — putting `_filterOptions` / `filterAttributes` on the `crt.DataGrid` / `crt.DataTable` view element does nothing. The registration belongs in `viewModelConfigDiff` under the collection **attribute's** `modelConfig` (`["attributes", "Items", "modelConfig"]`), not on the view element. See §2.3.
12. **Registering at `["attributes", "Items"]` (one level too high)** — the runtime reads `filterAttributes` from `modelConfig`, so the `merge` path must end with `"modelConfig"`. A registration that omits the final `"modelConfig"` segment is silently ignored and the list never filters. See §2.3.

---

## 8. Quick checklist

- [ ] **`merge` op in `viewModelConfigDiff` registering `<name>_Items` in the target collection's `modelConfig.filterAttributes` (`{ "name": "<name>_Items", "loadOnChange": true }`) — without this the list does not filter.**
- [ ] `insert` op in `viewConfigDiff` with `type: "crt.QuickFilter"`, unique `name`, correct `parentName` (filter container).
- [ ] `filterType` set to one of `"custom" / "date-range" / "lookup"`.
- [ ] `config` matches the variant interface (`BaseQuickFilterConfig` + variant-specific fields).
- [ ] `_filterOptions.expose[0].attribute` = `<name>_Items` and `_filterOptions.from` = `<name>_Value` (preprocessor naming convention).
- [ ] `target.viewAttributeName` matches the target collection's exposed attribute (usually `"Items"`).
- [ ] `target.filterColumn` set to the entity column path being filtered.
- [ ] `quickFilterType` matches the root `filterType`.
- [ ] `config.caption` uses `<elementName>_config_caption` resource key convention.
- [ ] **Repairing a chip that renders but doesn't filter?** Confirm the §2.2 registration exists at the exact `["attributes", "<Collection>", "modelConfig"]` path with a byte-for-byte matching `name` before touching anything else — don't re-create the chip and don't wire `_filterOptions` onto the DataTable (§2.3).
