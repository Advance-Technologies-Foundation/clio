# get-related-page-addon

## Command Type

Development commands

## Name

get-related-page-addon - Read an object's current RelatedPage configuration (the bound default/add pages per audience and type)

## Description

Reads the `RelatedPage` add-on of an object (entity schema) and returns its current page set: which Freedom UI
pages are bound as the default record page and the add-record page, per audience (role) and per record type.

For each entry it reports the page schema `UId` and the resolved page schema name, the role `UId` and the
resolved role name (for the standard `All employees` / `All external users` audiences), the `is-default` /
`is-add` / `is-ssp-default` flags, and any `type-column-value`, plus the top-level `type-column-uid`.

**Read-only** — it performs no save and changes nothing. Use it before
[`create-related-page-addon`](create-related-page-addon.md) for a safe read-modify-write: `create` **replaces**
the whole configuration, so read the current pages, modify the set, then send the full set back — otherwise the
entries you omit are lost.

## Synopsis

```
clio get-related-page-addon -e ENVIRONMENT --entity-schema-name NAME --package-name NAME
```

## Options

| Option | Required | Default | Description |
|---|---|---|---|
| `-e, --environment` | No | | Registered Creatio environment to target. |
| `--entity-schema-name` | Yes | | Object (entity schema) whose related pages to read, e.g. `UsrDeliveryItem`. |
| `--package-name` | Yes | | Package that owns the add-on configuration, e.g. `Custom`. |

## Examples

```
clio get-related-page-addon -e dev --entity-schema-name UsrDeliveryItem --package-name Custom
```

## Notes

- Aliases: `related-page-addon-get`, `get-related-pages`.
- Pairs with [`create-related-page-addon`](create-related-page-addon.md): read → modify → `create` (which replaces the full set). Raw page/role UIds are always returned so the modified set can be sent back exactly.
