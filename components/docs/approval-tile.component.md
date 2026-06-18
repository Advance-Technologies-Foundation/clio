# How to Add an Approval Tile (`crt.ApprovalTile`) to a Freedom UI Page

> Audience: code agent inserting `crt.ApprovalTile` into a Creatio Freedom UI page schema.
> Renders a tile card for a single approval record inside a Next Steps gallery; provides approve and
> reject action buttons with debounce protection and snackbar confirmation feedback.

## Metadata
- **Category**: interactive
- **Container**: no
- **Parent types**: `crt.Gallery` (items collection)
- **Typical children**: none

---

## 1. Mental model — the 2 places you must edit
| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | Configuration of the parent `crt.Gallery` with `itemType: "crt.ApprovalTile"`. **The tile itself is not inserted as a standalone op.** |
| 2 | `modelConfigDiff` | Datasource for the Next Steps / approval collection. |

`crt.ApprovalTile` has no `@CrtInput`/`@CrtOutput` decorators in its leaf class — it inherits all
inputs from `CrtGalleryBaseItemComponent` (`record`, `isSelected`, `tileSizeClasses`). The tile is not
a designer-palette item and cannot be dragged from the tray.

The tile renders per-row inside a `crt.Gallery`; you configure the gallery's `itemType` to
`"crt.ApprovalTile"` rather than inserting individual tile ops.

### 1.1 Naming convention
```
Gallery_<id>    // parent gallery view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the parent gallery (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "NextStepsGallery",
  "values": {
    "type": "crt.Gallery",
    "itemType": "crt.ApprovalTile",
    "items": "$NextStepsApprovals",
    "visible": true
  },
  "parentName": "NextStepsContainer",
  "propertyName": "items",
  "index": 0
}
```

---

## 3. Property reference
Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.ApprovalTile` are in `ComponentRegistry.json` under `componentType: "crt.ApprovalTile"`. This
guide covers only the assembly mechanics.

---

## 7. Common pitfalls

1. **Inserting `crt.ApprovalTile` directly** — the tile is rendered by the parent `crt.Gallery` for each collection item. Do not insert individual tile ops; set `itemType: "crt.ApprovalTile"` on the gallery instead.
2. **Providing the wrong `record` shape** — the tile reads `id`, `entityName`, `processElementId`, `additionalInfo`, and `tileConfig` from the record object; these fields must be present in the gallery collection datasource.
3. **`AcceptApprovalWithoutComment` system setting** — the approve action checks this setting. When it is `false`, a confirmation dialog is shown; ensure `crt.ApprovalConfirmationHandlerRequest` is handled in the page.
4. **Double-click debounce** — approve and reject actions are debounced at 500 ms. Rapid sequential clicks are safe but only the first action is processed.
5. **Snackbar duration** — approval/rejection feedback snackbars appear for 3000 ms. Do not suppress them by setting custom panel classes that hide the container.
6. **Custom event channels** — the tile fires `FilterNextStepsItemsEvent` and `ChangeNextStepsStateEvent` custom events on action completion. If your page uses the Next Steps component, ensure it listens on these channels.

---

## 8. Quick checklist

- [ ] Parent `crt.Gallery` configured with `itemType: "crt.ApprovalTile"`.
- [ ] Gallery `items` bound to a collection attribute that contains approval record objects.
- [ ] Datasource provides `id`, `entityName`, and status fields required by the tile.
- [ ] `crt.ApprovalActionHandlerRequest` and `crt.ApprovalConfirmationHandlerRequest` handled in the page.
- [ ] `FilterNextStepsItemsEvent` and `ChangeNextStepsStateEvent` custom-event listeners registered if Next Steps is also on the page.
