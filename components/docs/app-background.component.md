# How to Add an App Background (`crt.AppBackground`) to a Freedom UI Page

> Audience: code agent inserting `crt.AppBackground` into a Creatio Freedom UI page schema.
> Renders the application-level background layer (color gradient, image with blur) beneath all shell
> content; typically placed once at the root shell level and driven by the application background service.

## Metadata
- **Category**: display
- **Container**: no
- **Parent types**: root shell container
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.AppBackground"` and optional class/visibility bindings. **Always present.** |

`crt.AppBackground` has no `@CrtInput` properties and no create command — it is not a designer-palette item.
Background configuration (image, color, blur) is managed entirely by the `AppBackgroundService` injected
at runtime; the component subscribes to that service and re-renders automatically.

### 1.1 Naming convention
```
AppBackground_<id>    // view element name; usually "ShellContainerWithBackground" in real schemas
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "ShellContainerWithBackground",
  "values": {
    "type": "crt.AppBackground",
    "classes": "$ApplicationBackgroundClassAttribute",
    "contentDisplayed": "$ApplicationBackgroundContentDisplayedAttribute"
  }
}
```

The `classes` and `contentDisplayed` values shown above are the canonical attribute names used in the
platform `BaseShell` schema. Wire them to the same attributes or supply your own.

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.AppBackground` are in `ComponentRegistry.json` under `componentType: "crt.AppBackground"`.
This guide covers only the assembly mechanics.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — matches real PackageStore usage (BaseShell)
{
  "operation": "insert",
  "name": "ShellContainerWithBackground",
  "values": {
    "type": "crt.AppBackground",
    "classes": "$ApplicationBackgroundClassAttribute",
    "contentDisplayed": "$ApplicationBackgroundContentDisplayedAttribute"
  }
}
```

---

## 7. Common pitfalls

1. **Using outside a shell context** — `crt.AppBackground` subscribes to `AppBackgroundService` which is provided at the shell level; using it on a regular form page will fail to inject the service.
2. **Adding more than one per shell** — only one background layer is expected; multiple instances produce overlapping visuals.
3. **Trying to set background properties via `viewConfigDiff` values** — the appearance is controlled by `AppBackgroundService.getConfig()`, not by view-level inputs. To change the background, configure the service, not the view element.
4. **`parentName` omitted** — `crt.AppBackground` is the topmost visual element in the shell; it typically has no explicit `parentName` (root-level insert) or is placed directly in the shell root container.
5. **`contentDisplayed` binding missing** — when `contentDisplayed` is false/unbound the component may hide the rest of the shell content while the background image loads.

---

## 8. Quick checklist

- [ ] `insert` op in `viewConfigDiff` with `type: "crt.AppBackground"` and unique `name`.
- [ ] `classes` bound to the application background class attribute (or omitted for default styles).
- [ ] `contentDisplayed` bound to the attribute that signals content is ready to display.
- [ ] Used only at the shell/root level — not on regular form pages.
- [ ] Only one instance per shell.
