# How to Add a Message Editor Reply (`crt.MessageEditorReply`) to a Freedom UI Page

> Audience: code agent inserting a `crt.MessageEditorReply` into a Creatio Freedom UI page schema.
>
> `crt.MessageEditorReply` is a display-only view element that renders a quoted-reply preview inside a chat
> editor. It has no configurable inputs or outputs — all data is provided by the parent context.

## Metadata

- **Category**: display
- **Container**: no
- **Parent types**: `crt.MessageEditorBody` (slot `toolbarItems` or `items`)
- **Typical children**: none

---

## 1. Mental model — the 1 place you must edit

| # | Section | What you add |
|---|---|---|
| 1 | `viewConfigDiff` | A single `insert` op with `type: "crt.MessageEditorReply"`. No inputs, no handlers. |

`crt.MessageEditorReply` exposes no registry inputs or outputs. Its display content is controlled entirely
by the parent chat container.

### 1.1 Naming convention

```
MessageEditorReply_<id>     // view element name
```

---

## 2. Step-by-step recipe

### 2.1 Insert the view element (`viewConfigDiff` entry)

```jsonc
{
  "operation": "insert",
  "name": "MessageEditorReply",
  "values": {
    "type": "crt.MessageEditorReply"
  },
  "parentName": "MessageEditorBody",
  "propertyName": "toolbarItems",
  "index": 0
}
```

---

## 3. Property reference

Full `inputs`, `outputs`, `default`, `values`, `deprecated`, and `designerDefaults` for
`crt.MessageEditorReply` are in `ComponentRegistry.json` under `componentType: "crt.MessageEditorReply"`.
The registry shows no exposed inputs or outputs.

---

## 5. Copy-paste minimal example

```jsonc
{
  "operation": "insert",
  "name": "MessageEditorReply",
  "values": {
    "type": "crt.MessageEditorReply"
  },
  "parentName": "MessageEditorBody",
  "propertyName": "toolbarItems",
  "index": 0
}
```

---

## 7. Common pitfalls

1. **Expecting configurable inputs.** This component has no exposed inputs; do not attempt to bind data to it directly.
2. **Using outside a `crt.MessageEditorBody`.** The reply preview is meaningful only in the context of a chat editor body.

---

## 8. Quick checklist

- [ ] `insert` op with `type: "crt.MessageEditorReply"`, unique `name`, `parentName` pointing to a `crt.MessageEditorBody`.
- [ ] No inputs or outputs to wire.
