# How to Add a Calendar (`crt.Calendar`) to a Freedom UI Page

> Audience: code agent inserting `crt.Calendar` into a Creatio Freedom UI page schema.
> A full-featured calendar view (week, day, month) backed by a datasource; supports drag-to-create,
> tile colorization, participant filtering, and mini-page pop-ups on tile click.

## Metadata

- **Category**: display
- **Container**: yes (tile content goes into the `tileContent` slot)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, root page container
- **Typical children**: view elements in the `tileContent` slot (e.g. `crt.FlexContainer` with tile labels)

---

## 1. Mental model — the 4 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | An `insert` op with `type: "crt.Calendar"` and binding wires. **Always present.** |
| 2 | `modelConfigDiff` | A datasource (e.g. `crt.EntityDataSource`) that provides the calendar items. |
| 3 | `viewModelConfigDiff` | Attributes for `showWeekends`, highlighted date range, and any filter or paging bindings. |
| 4 | `handlers` | Handlers for `openEditPage`, `silenceCreate`, `loadNextPage`, and `highlightArea` as needed. |

The `crt.AddCalendarCommand` also creates the `showWeekends` attribute and the two
`highlightedStartDate` / `highlightedEndDate` attributes automatically via the designer. Replicate
this when writing the schema by hand.

### 1.1 Naming convention

```
Calendar_<id>                         // view element name
Calendar_<id>DS                       // datasource key in modelConfigDiff
$Calendar_<id>                        // $-prefix collection attribute in viewModelConfigDiff
$Calendar_<id>_showWeekends           // attribute controlling weekend visibility
Calendar_<id>_highlightedStartDate    // attribute for area highlight start
Calendar_<id>_highlightedEndDate      // attribute for area highlight end
```

---

## 2. Step-by-step recipe

### 2.1 Add the datasource (`modelConfigDiff`)

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "dataSources": {
      "Calendar_mainDS": {
        "type": "crt.EntityDataSource",
        "scope": "viewElement",
        "config": {
          "entitySchemaName": "Activity",
          "attributes": {
            "Title":     { "path": "Title" },
            "StartDate": { "path": "StartDate" },
            "DueDate":   { "path": "DueDate" },
            "Notes":     { "path": "Notes" }
          }
        }
      }
    }
  }
}
```

### 2.2 Declare viewModel attributes (`viewModelConfigDiff`)

```jsonc
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "Calendar_main": {
      "isCollection": true,
      "viewModelConfig": { "attributes": { "Id": { "modelConfig": { "path": "Calendar_mainDS.Id" } } } },
      "modelConfig": { "path": "Calendar_mainDS" }
    },
    "Calendar_main_showWeekends": { "value": true },
    "Calendar_main_highlightedStartDate": { "value": "" },
    "Calendar_main_highlightedEndDate":   { "value": "" }
  }
}
```

### 2.3 Insert the calendar view element (`viewConfigDiff`)

```jsonc
{
  "operation": "insert",
  "name": "Calendar_main",
  "parentName": "SectionContentWrapper",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Calendar",
    "items": "$Calendar_main",
    "showWeekends": "$Calendar_main_showWeekends",
    "highlightedStartDate": "Calendar_main_highlightedStartDate",
    "hightlightedEndDate":  "Calendar_main_highlightedEndDate",
    "useAutoScrollToCurrentTime": true,
    "templateValuesMapping": {
      "startColumn": "Calendar_mainDS_StartDate",
      "endColumn":   "Calendar_mainDS_DueDate",
      "titleColumn": "Calendar_mainDS_Title",
      "notesColumn": "Calendar_mainDS_Notes"
    },
    "tileContent": [],
    "fitContent": true,
    "visible": true
  }
}
```

### 2.4 (Optional) Add handlers

```jsonc
{
  "request": "crt.OpenCalendarRecordRequest",
  "handler": async (request, next) => {
    // request.schemaName, request.recordId
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Calendar` are in `ComponentRegistry.json` under `componentType: "crt.Calendar"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// CalendarColumnsMapping — maps datasource attribute paths to calendar roles
interface CalendarColumnsMapping {
  startColumn?: string;        // DS attribute path for event start time
  endColumn?: string;          // DS attribute path for event end time
  titleColumn?: string;        // DS attribute path for event title
  notesColumn?: string;        // DS attribute path used for join-meeting URL extraction
  colorizationColumn?: string; // DS attribute path for colorization lookup (colorizationType: 'byField')
}

// QuickFilterDateRange — used in the `filters` input
interface QuickFilterDateRange {
  start: Date;    // required
  end: Date;      // required
  macros?: string; // e.g. "[#currentWeek#]", "[#currentMonth#]"
}

// LookupValue — used in participantFilters
interface LookupValue {
  value: string;
  displayValue: string;
}
```

---

## 5. Copy-paste minimal example

```jsonc
// modelConfigDiff — datasource
{
  "operation": "merge",
  "path": [],
  "values": {
    "dataSources": {
      "Calendar_tasksDS": {
        "type": "crt.EntityDataSource",
        "scope": "viewElement",
        "config": {
          "entitySchemaName": "Activity",
          "attributes": {
            "Title":     { "path": "Title" },
            "StartDate": { "path": "StartDate" },
            "DueDate":   { "path": "DueDate" }
          }
        }
      }
    }
  }
}
```

```jsonc
// viewModelConfigDiff — attribute
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "Calendar_tasks": {
      "isCollection": true,
      "modelConfig": { "path": "Calendar_tasksDS" }
    },
    "Calendar_tasks_showWeekends":         { "value": true },
    "Calendar_tasks_highlightedStartDate": { "value": "" },
    "Calendar_tasks_highlightedEndDate":   { "value": "" }
  }
}
```

```jsonc
// viewConfigDiff — calendar element
{
  "operation": "insert",
  "name": "Calendar_Tasks",
  "parentName": "SectionContentWrapper",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Calendar",
    "items": "$Calendar_tasks",
    "showWeekends": "$Calendar_tasks_showWeekends",
    "highlightedStartDate": "Calendar_tasks_highlightedStartDate",
    "hightlightedEndDate":  "Calendar_tasks_highlightedEndDate",
    "useAutoScrollToCurrentTime": true,
    "tileContent": [],
    "templateValuesMapping": {
      "startColumn": "Calendar_tasksDS_StartDate",
      "endColumn":   "Calendar_tasksDS_DueDate",
      "titleColumn": "Calendar_tasksDS_Title"
    },
    "fitContent": true,
    "visible": true
  }
}
```

---

## 6. Driving from page state

The `showWeekends` input accepts a `$Attribute` binding; toggle it from a checkbox or quick-filter to
control weekend column visibility. `filters` accepts a `QuickFilterDateRange[]` binding to drive the
visible date range from a quick-filter component.

---

## 7. Common pitfalls

1. **`templateValuesMapping` is required** — without it the calendar cannot map datasource rows to events and renders empty; every field used (`startColumn`, `endColumn`, `titleColumn`) must reference a real datasource attribute path.
2. **`hightlightedEndDate` (typo is intentional)** — the property name in the schema is spelled `hightlightedEndDate` (missing an `h`); match this exact spelling in your `viewConfigDiff` values.
3. **`highlightedStartDate` / `hightlightedEndDate` are plain attribute name strings, not `$`-bindings** — set them to the attribute name without the `$` prefix in `values`; the calendar reads them internally.
4. **`items` must be a `BaseViewModelCollection` attribute** — bind to a collection attribute from `viewModelConfigDiff`; a plain array literal does not work.
5. **`lightweightModeLimit: null`** — the create command sets this to `null` to disable lightweight mode by default; include it in your schema for consistency.
6. **`tileContent: []`** — always include `tileContent: []` in `values`; child inserts use `propertyName: "tileContent"` to inject tile content view elements.
7. **`pageSize` vs `loadNextPage`** — when the collection reaches a multiple of `pageSize` (default 150), the calendar prompts the user to load more and fires `loadNextPage`; wire this output if you need custom paging logic.

---

## 8. Quick checklist

- [ ] Datasource declared in `modelConfigDiff` with entity attributes for start, end, and title.
- [ ] Collection attribute declared in `viewModelConfigDiff` bound to the datasource path.
- [ ] `showWeekends` attribute declared with a default boolean value.
- [ ] `highlightedStartDate` and `hightlightedEndDate` (typo!) attributes declared with empty string defaults.
- [ ] `viewConfigDiff` insert with `items`, `showWeekends`, `highlightedStartDate`, `hightlightedEndDate`, `templateValuesMapping`, and `tileContent: []`.
- [ ] `templateValuesMapping` covers at least `startColumn`, `endColumn`, and `titleColumn`.
- [ ] Handlers wired for `openEditPage` and `silenceCreate` (drag-to-create) if those interactions are needed.
