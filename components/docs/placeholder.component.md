# How to Add a Placeholder (`crt.Placeholder`) to a Freedom UI Page

> Audience: code agent inserting `crt.Placeholder` into a Creatio Freedom UI page schema.
>
> `crt.Placeholder` is an empty-state display element that shows a title, a subtitle, and an illustrative
> image (either a named Lottie animation or a platform icon). It is used to fill space when a list, panel,
> or section has no content to show. It supports an `items` slot for optional action buttons or custom content.

## Metadata

- **Category**: display
- **Container**: yes (`items` slot for optional action elements)
- **Parent types**: `crt.FlexContainer`, `crt.GridContainer`, `crt.TabContainer`, root page container
- **Typical children**: `crt.Button` (call-to-action inside the placeholder)

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.Placeholder"`, an `image` config, `title`, and `subhead`. **Always present.** |

`crt.Placeholder` is view-only — no datasource, no viewModel attribute, no handlers needed for basic usage.

### 1.1 Naming convention

```
Placeholder_<id>    // view element name; <id> = any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "Placeholder_empty",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Placeholder",
    "title": "#ResourceString(Placeholder_empty_title)#",
    "subhead": "#ResourceString(Placeholder_empty_subhead)#",
    "image": {
      "type": "animation",
      "name": "raccoon",
      "width": "100px",
      "height": "100px",
      "autoplay": true,
      "loop": false
    },
    "visible": true
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.Placeholder` are in `ComponentRegistry.json` under `componentType: "crt.Placeholder"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

The `image` input accepts one of two discriminated shapes depending on `type`:

```ts
// Animation variant — plays a Lottie animation by name
interface PlaceholderAnimation {
  type: 'animation';
  name: string;           // named animation key (e.g. 'raccoon', 'search', 'empty')
  width?: string;         // CSS length, e.g. '100px'
  height?: string;        // CSS length, e.g. '100px'
  autoplay?: boolean;     // default: true
  loop?: boolean;         // default: false
}

// Icon variant — renders a platform icon
interface PlaceholderIcon {
  type: 'icon';
  icon: string;           // icon name from the platform icon registry
  width?: string;         // CSS length, e.g. '80px'
  height?: string;        // CSS length, e.g. '80px'
  padding?: string;       // CSS padding, e.g. '25px' or '0px'
}
```

When `image` is absent or empty, the component falls back to the default animation (`'raccoon'`).

---

## 5. Copy-paste minimal examples

Animation variant (from `BaseGridSectionTemplate`):

```jsonc
{
  "operation": "insert",
  "name": "Placeholder_noSearch",
  "parentName": "GridContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Placeholder",
    "title": "#ResourceString(Placeholder_noSearch_title)#",
    "subhead": "#ResourceString(Placeholder_noSearch_subhead)#",
    "image": {
      "type": "animation",
      "name": "search"
    },
    "visible": true
  }
}
```

Icon variant (from `CopilotPanel`):

```jsonc
{
  "operation": "insert",
  "name": "Placeholder_copilot",
  "parentName": "FlexContainer_content",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.Placeholder",
    "image": {
      "type": "icon",
      "icon": "copilot-logo",
      "width": "80px",
      "height": "80px",
      "padding": "0px"
    },
    "visible": true
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff
{
  "operation": "merge",
  "path": [],
  "values": {
    "attributes": {
      "Placeholder_empty_visible": { "value": false }
    }
  }
}

// viewConfigDiff.values
"visible": "$Placeholder_empty_visible"
```

---

## 7. Common pitfalls

1. **`title` and `subhead` as raw strings** — use `#ResourceString(...)#` for all user-visible text; raw strings won't be localized.
2. **Unknown `name` for animation** — if the Lottie animation key is not registered in the platform, the animation area renders blank; verify the name against the platform's animation registry.
3. **`icon` name not in the icon registry** — an unknown icon name renders an empty icon box; cross-check against available platform icons.
4. **`width`/`height` as numbers** — the image config expects CSS length strings (`"100px"`, `"50%"`), not plain numbers; pass `"100px"` not `100`.
5. **Using `loading: true` without a follow-up toggle** — the `loading` input shows a skeleton animation; ensure it's set back to `false` (via `$Attribute` binding) once content loads.
6. **Nesting inside a `fitContent` flex container** — the placeholder may collapse to zero height; give the parent a minimum height or set `fitContent: false`.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.Placeholder"`, unique `name`, valid `parentName`, `propertyName: "items"`, and valid `index`.
- [ ] `image` provided with a valid `type` (`"animation"` or `"icon"`) and matching required fields.
- [ ] `title` and `subhead` use `#ResourceString(...)#` for localized text (or are intentionally empty).
- [ ] `visible` wired to a `$Attribute` binding for conditional empty-state display.
- [ ] If `items` contains action buttons, each child has its own `insert` op pointing to this placeholder's `name`.
