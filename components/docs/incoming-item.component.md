# How to Add an Incoming Item (`crt.IncomingItem`) to a Freedom UI Page

> Audience: code agent inserting a `crt.IncomingItem` into a Creatio Freedom UI page schema.
>
> `crt.IncomingItem` renders one contact-center chat/call tile. It is normally created by
> `crt.IncomingItems`; prefer that list component for page schemas unless you are composing a custom
> contact-center container.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.IncomingItems` internal tile host, `crt.FlexContainer`
- **Typical children**: none

---

## 1. Mental model — the 1-2 places you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.IncomingItem"` and an incoming item object. **Only for custom tile composition.** |
| 2 | `handlers` | A handler for the accept action when the tile needs to accept/open the item. |

`crt.IncomingItem` is a **leaf tile**. It does not load data; the parent must pass an `IncomingItem` object and
handle tile actions. For standard contact-center pages, insert `crt.IncomingItems` instead.

### 1.1 Naming convention

```
IncomingItem_<id>        // view element name; <id> = any short unique slug
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "IncomingItem_preview",
  "parentName": "IncomingItemsHost",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.IncomingItem",
    "data": "$IncomingItem_preview_data",
    "outdateDelay": 1,
    "acceptClick": {
      "request": "crt.AcceptChatRequest",
      "params": {
        "incomingItem": "@event"
      }
    }
  }
}
```

### 2.2 Add a handler (`handlers` entry)

```jsonc
{
  "request": "crt.AcceptChatRequest",
  "handler": async (request, next) => {
    // accept or open the incoming contact-center item
    return next?.handle(request);
  }
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.IncomingItem` are in `ComponentRegistry.json` under `componentType: "crt.IncomingItem"`. This guide
covers only the assembly mechanics.

---

## 4. Shape of types not in `references.typeDefinitions`

```ts
interface IncomingItem {
  schemaName: string;
  id: string;
  contact: LookupValue;
  itemTitle: string;
  createdOn: Date;
  accepted: boolean;
  additionalParams?: Record<string, unknown>;
}

interface LookupValue {
  value: string;
  displayValue?: string;
  primaryImageValue?: string;
}
```

The runtime tile derives its icon and record link from `schemaName`, `id`, and `contact`.

---

## 5. Copy-paste minimal example

```jsonc
// viewConfigDiff entry — use only for custom tile composition; crt.IncomingItems is the standard page API
{
  "operation": "insert",
  "name": "IncomingItem_preview",
  "parentName": "IncomingItemsHost",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.IncomingItem",
    "data": "$IncomingItem_preview_data",
    "outdateDelay": 1,
    "acceptClick": {
      "request": "crt.AcceptChatRequest",
      "params": {
        "incomingItem": "@event"
      }
    }
  }
}
```

---

## 6. Driving from page state

```jsonc
// viewModelConfigDiff
{
  "operation": "merge",
  "path": ["attributes"],
  "values": {
    "IncomingItem_preview_data": { "value": null }
  }
}

// viewConfigDiff.values
"data": "$IncomingItem_preview_data"
```

The bound value must be populated by a parent handler, converter, or contact-center service. A plain empty object
does not render useful caller or record details.

---

## 7. Common pitfalls

1. **Using the leaf tile when the list is needed.** `crt.IncomingItems` handles live item arrays and list-level
   click/accept outputs; use `crt.IncomingItem` only inside custom composition.
2. **Missing `data`.** The tile has no datasource and cannot render caller details without an `IncomingItem`.
3. **Missing `contact.value`.** Owner link generation depends on the contact lookup id.
4. **`outdateDelay` is minutes at the tile input boundary.** The component multiplies the value by 60 internally.
5. **Unwired `acceptClick`.** The accept button only emits the current item; page logic must handle the request.

---

## 8. Quick checklist

- [ ] Prefer `crt.IncomingItems` for normal page schemas.
- [ ] `insert` op uses `type: "crt.IncomingItem"` only for custom tile composition.
- [ ] `data` is bound to a valid `IncomingItem` object.
- [ ] `acceptClick.request` is wired when the accept action should do anything.
- [ ] Parent container has enough width for the tile content.
