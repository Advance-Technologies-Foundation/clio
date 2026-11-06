# Page modal size — spec

Jira: [ENG-105681](https://creatio.atlassian.net/browse/ENG-105681)

## Problem

The size of a Freedom UI modal window is not part of the page body. It is stored in the page's
`AppearanceSettings` add-on (`<Page>AppearanceSettings`, attached to the page through
`ClientUnitSchemaManager`), whose metadata is `{"modalSettings": {"size": N}}`. clio has no way to read or
write that add-on, and the guidance does not mention it, so an agent asked to resize a modal window edits
`layoutConfig.width` of `Main` (inherited from `BaseMiniPageTemplate`), inner container widths, or CSS. It
reports success, and the window size in runtime does not change.

## Platform facts

| Fact | Source |
|---|---|
| Designer options: S, M, L, XL → stored `size` 1, 2, 3, 4 | creatio-ui `appearance-settings-properties-panel.component.ts`, `SchemaSize` |
| `SchemaSize` also has `None = 0`, `ExtraSmall = 6`, `Custom = 5`; the designer never offers them | creatio-ui `size-settings.ts` |
| No size (`None` or no add-on) → runtime default: mini page → Small, any other page → ExtraLarge | creatio-ui `appearance-settings-default-loader.service.ts` |
| The size panel is shown only by the modal page designer, which opens pages of schema group `MiniPage` or pages inheriting `BaseMiniPageTemplate` | creatio-ui `modal-page-interface-designer-features.service.ts`, `designer-by-schema-group.ts`, `NavigationUtils.isMiniPage` |
| The designer saves the add-on through `AddonSchemaDesignerService` with the rest of the page | creatio-ui `design-time-appearance-settings-store.service.ts` |

## Decisions

- Two MCP tools and two CLI verbs, mirroring `get-related-page-addon` / `create-related-page-addon`:
  `get-page-modal-size` (read-only) and `set-page-modal-size` (write).
- Accepted sizes: `small`, `medium`, `large`, `extra-large` and the designer captions `S`, `M`, `L`, `XL`
  (case-insensitive). Anything else is refused with the list of accepted values and nothing is written.
- A page is modal when its hierarchy contains `BaseMiniPageTemplate`. Any other page is refused and nothing is
  written.
- The write goes to the package the caller names (`package-name`), the same rule as the other add-on writers.
  Every other top-level field of the add-on metadata, and every field of `modalSettings` other than `size`, is
  kept.
- The read returns the size stored in the add-on as seen from the package, the package of the layer it comes
  from, and the runtime default when no size is set.
- The write result names the old and the new size and the package of the add-on.

## Out of scope

- Finding the modal page from the button that opens it (TC-05) and asking the user on a vague request (TC-03)
  are agent behaviour. They are documented in the guidance (clio-knowledge), not enforced by the tool.

## Test cases

Jira QA cases TC-01..TC-07 map as follows:

| Case | Coverage |
|---|---|
| TC-01 set by name, no add-on yet | unit (service creates metadata), manual runtime check on the stand |
| TC-02 change an existing size | unit (old size reported, other fields kept), runtime check |
| TC-03 vague request | guidance only |
| TC-04 unsupported size | unit (parser + tool) |
| TC-05 window named by its opener | guidance only |
| TC-06 size in a vendor layer | runtime check on the stand (`EmailMiniPage`) |
| TC-07 not a modal page | unit (service refuses) |
