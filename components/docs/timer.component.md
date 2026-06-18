# How to Add a Timer (`crt.Timer`) to a Freedom UI Page

> Audience: code agent inserting `crt.Timer` into a Creatio Freedom UI page schema.
> `crt.Timer` is a **display** component that renders a countdown or stopwatch value as a colored label,
> changing its color based on whether the remaining time is positive or negative.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.HeaderContainer`
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.Timer"` and timer configuration. **Always present.** |
| 2 | `viewModelConfigDiff` | *(only if binding `control` to a page attribute)* |

`crt.Timer` is **view-only** — it owns no datasource. The `control` input receives the date/time value to
count against. The component automatically applies `positiveTextColor` or `negativeTextColor` based on
whether the countdown is above or below zero.

### 1.1 Naming convention

```
Timer_<id>            // view element name; <id> is any short unique slug
$Timer_<id>_control   // $-prefix attribute for the bound date value
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ResolutionTimer",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Timer",
    "caption": "#ResourceString(ResolutionTimer_caption)#",
    "labelType": "headline-3",
    "labelThickness": "default",
    "labelEllipsis": false,
    "labelColor": "#098401",
    "labelBackgroundColor": "transparent",
    "labelTextAlign": "center",
    "timerType": "countdown-to-specific-date",
    "showNegativeCountDownValue": true,
    "negativeTextColor": "#FD3F11",
    "positiveTextColor": "#098401",
    "positiveTextValue": "#ResourceString(ResolutionTimer_positiveTextValue)#",
    "negativeTextValue": "#ResourceString(ResolutionTimer_negativeTextValue)#",
    "control": "$ResolutionDeadline",
    "visible": true,
    "layoutConfig": { "column": 1, "row": 1, "colSpan": 1, "rowSpan": 1 }
  }
}
```

### 2.2 (Optional) Declare the attribute in `viewModelConfigDiff`

```jsonc
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "ResolutionDeadline": { "value": null }
    }
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Timer` are in `ComponentRegistry.json` under `componentType: "crt.Timer"`. This guide covers
only the assembly mechanics.

The `label*` inputs (`labelType`, `labelThickness`, `labelColor`, etc.) are inherited from
`CrtLabelComponent` and control the text style; the timer-specific inputs (`timerType`, `positiveTextColor`,
`negativeTextColor`, `showNegativeCountDownValue`, `positiveTextValue`, `negativeTextValue`, `control`,
`adjustToUserTimezone`) extend it with countdown logic.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
// timerType values (TimerType enum)
type TimerType =
  | 'countdown-to-specific-date'   // counts down to control date; turns negative after
  | 'time-since-date'              // counts up from control date
  | 'stopwatch';                   // real-time stopwatch (ignores control; ticks every 1s)
```

When `timerType` is `'stopwatch'`, the component subscribes to `timer(0, 1000)` and ignores the
`control` date. `adjustToUserTimezone` shifts the `control` date by the logged-in user's time-zone
offset before computing the diff.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry (from Cases_FormPage real usage)
{
  "operation": "insert",
  "name": "ResolutionMainTimer",
  "parentName": "TimerContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Timer",
    "caption": "#ResourceString(ResolutionMainTimer_caption)#",
    "labelType": "headline-1",
    "labelThickness": "default",
    "labelEllipsis": false,
    "labelColor": "#098401",
    "labelBackgroundColor": "transparent",
    "labelTextAlign": "start",
    "timerType": "countdown-to-specific-date",
    "showNegativeCountDownValue": true,
    "negativeTextColor": "#FD3F11",
    "positiveTextColor": "#098401",
    "positiveTextValue": "#ResourceString(ResolutionMainTimer_positiveTextValue)#",
    "negativeTextValue": "#ResourceString(ResolutionMainTimer_negativeTextValue)#",
    "label": "$Resources.Strings.DateTimeAttribute_26hjo04",
    "visible": true
  }
}
```

---

## 6. Driving the timer from page state

```jsonc
// viewModelConfigDiff
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "ResolutionDeadline": { "value": null }
    }
  }
}

// viewConfigDiff.values
"control": "$ResolutionDeadline"
```

`control` is `propertyBindable`; use a `$Attribute` binding to feed the deadline date from
a datasource attribute. Other timer inputs (`timerType`, `positiveTextColor`, etc.) are static values
set at schema time.

---

## 7. Common pitfalls

1. **`timerType` must be set.** Without it the component's factory creator has no strategy; the display value will be empty.
2. **`control` expects a `Date` object, not a string.** Binding a raw ISO string from a datasource won't work unless the datasource already deserializes it.
3. **`adjustToUserTimezone: true` with `timerType: 'stopwatch'`** — timezone adjustment is ignored for stopwatch mode (no `control` date to shift).
4. **`showNegativeCountDownValue: false`** — when the countdown passes zero the timer freezes at `0`; set `negativeTextValue` to show a custom "overdue" label instead.
5. **`positiveTextValue` / `negativeTextValue`** — these replace the numeric countdown with a static label when the time is positive or negative respectively. Use `#ResourceString(<key>)#` to localize.
6. **Forgetting `layoutConfig`** when placing inside a `crt.GridContainer` — the timer uses the label type sizing; without proper grid coordinates it may overlap other elements.
7. **`labelColor` is overridden at runtime.** Setting `labelColor` as a static value has no effect because `_setColor()` always overwrites it via `positiveTextColor`/`negativeTextColor`.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Timer"`, unique `name`, valid `parentName`.
- [ ] `timerType` set to one of the three supported values.
- [ ] `control` bound to a date attribute (required for countdown/time-since modes).
- [ ] `positiveTextColor` and `negativeTextColor` set as hex color strings.
- [ ] `showNegativeCountDownValue` set explicitly (`true` or `false`).
- [ ] `caption` / `positiveTextValue` / `negativeTextValue` use `#ResourceString(<key>)#` for localization.
- [ ] `layoutConfig` provided when parent is `crt.GridContainer`.
