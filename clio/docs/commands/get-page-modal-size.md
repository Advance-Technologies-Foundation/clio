# get-page-modal-size

## Command Type

Development commands

## Name

get-page-modal-size - Read the modal window size of a Freedom UI modal page

## Description

Reads the window size of a Freedom UI modal page (a page inheriting `BaseMiniPageTemplate`). The size is not
part of the page body: it is stored in the page's `AppearanceSettings` add-on (`modalSettings.size`).
`layoutConfig.width` of the `Main` container is **not** the window size.

The response reports:

| Field | Meaning |
|---|---|
| `size` | The size the package sees: `small`, `medium`, `large` or `extra-large`. Absent when no size is set. |
| `sizeValue` | The raw stored `modalSettings.size` (1–4 for the designer sizes). |
| `sourcePackageName` | The package of the add-on layer the size the package sees (`size`) comes from. It is not the source of `effectiveSize` when another package overrides it. Absent, with a `warning`, when it cannot be determined. |
| `effectiveSize` | The size the window opens with; the runtime default `small` when no size is set. |
| `effectiveSourcePackageName` | Set on an override: the package whose layer sets `effectiveSize`. Absent when there is no override or that package could not be told apart. |
| `availableSizes` | The sizes [`set-page-modal-size`](set-page-modal-size.md) accepts. |
| `warning` | Set when a package the requested one does not depend on overrides the size: a write to the requested package would not change the window. It names that package and says that a stale runtime cache looks the same (clear it with `clio clear-redis-db` or restart the application, then read again). |

**Read-only.** A page that is not a modal page is refused.

## Synopsis

```
clio get-page-modal-size -e ENVIRONMENT --page-schema-name NAME --package-name NAME
```

## Options

| Option | Required | Default | Description |
|---|---|---|---|
| `-e, --environment` | No | | Registered Creatio environment to target. |
| `--page-schema-name` | Yes | | Modal page schema name, e.g. `UsrOrder_MiniPage`. |
| `--package-name` | Yes | | Package the page is read from, e.g. `Custom`. |

## Examples

```
clio get-page-modal-size -e dev --page-schema-name UsrOrder_MiniPage --package-name Custom
```

## Notes

- Aliases: `page-modal-size-get`.
- Pairs with [`set-page-modal-size`](set-page-modal-size.md).
