# set-page-modal-size

## Command Type

Development commands

## Name

set-page-modal-size - Set the modal window size of a Freedom UI modal page

## Description

Sets the window size of a Freedom UI modal page (a page inheriting `BaseMiniPageTemplate`) by writing
`modalSettings.size` to the page's `AppearanceSettings` add-on in the given package. The add-on is created when
the page has none in that package; every other add-on field is kept. The page body is not changed —
`layoutConfig.width` of `Main`, inner container widths and CSS do not set the window size.

`--size` takes the options the Page Designer offers:

| Size | Designer | Stored `size` |
|---|---|---|
| `small` | S | 1 |
| `medium` | M | 2 |
| `large` | L | 3 |
| `extra-large` | XL | 4 |

Any other value is refused with the accepted list (`availableSizes`) and nothing is written. A page that is not a
modal page is refused.

The response names `previousSize` (absent when none was set — the window opened `small`), `previousSizeValue`,
`previousSourcePackageName` and `size`. When a package above the given one
still overrides the size, `warning` says which size the window opens with.

## Synopsis

```
clio set-page-modal-size -e ENVIRONMENT --page-schema-name NAME --package-name NAME --size SIZE
```

## Options

| Option | Required | Default | Description |
|---|---|---|---|
| `-e, --environment` | No | | Registered Creatio environment to target. |
| `--page-schema-name` | Yes | | Modal page schema name, e.g. `UsrOrder_MiniPage`. |
| `--package-name` | Yes | | Editable package the add-on is written to, e.g. `Custom`. For a vendor page this is the package that customizes it. |
| `--size` | Yes | | `small` (S), `medium` (M), `large` (L) or `extra-large` (XL). |

## Examples

```
clio set-page-modal-size -e dev --page-schema-name UsrOrder_MiniPage --package-name Custom --size large
```

## Notes

- Aliases: `page-modal-size`.
- Read the current size with [`get-page-modal-size`](get-page-modal-size.md) first.
