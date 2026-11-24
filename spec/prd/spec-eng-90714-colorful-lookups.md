# SPEC: Colorful lookup values — set and read the schema color column (ENG-90714)

**Created**: 2026-11-23 (revised 2026-11-24)  
**Size estimate**: S (2 stories)  
**Recommended next**: /bmad-spec is sufficient

---

## Why

A no-code creator asks a code agent to make lookup values colorful. The procedure and the palette are owned by
the `lookup-colors` guidance article (clio-knowledge PR #356). Freedom UI colors a lookup value from the
object's **color column** (`PrimaryColorColumn`, the Object Designer "Color" setting), which must be of type
`Color`. clio can create a `Color` column and write hex values into it, but has no tool that reads or sets the
color column, so the article has to send the user to the Object Designer by hand after the agent adds the
column. This SPEC closes that gap.

## Capabilities

| ID | Intent (WHAT) | Success Signal (HOW WE KNOW) |
|----|--------------|------------------------------|
| CAP-01 | Set an entity schema's color column by name (own or inherited column) | `set-entity-schema-properties --primary-color-column UsrColor` saves and publishes; a readback mismatch is reported as an error, not a silent no-op (unit tests on the manager) |
| CAP-02 | Refuse a color column that is not of type `Color` or does not exist | Setting a text column (e.g. a text `Color`) fails before any save with a message naming the column and its type; no `SaveSchema` call is made (unit test) |
| CAP-03 | Report the current color column when reading schema properties | `get-entity-schema-properties` returns `primary-color-column-name` in both package-scoped and merged modes (null when none); CLI prints `Primary color column:` (unit tests) |
| CAP-04 | Expose CAP-01..03 over MCP | `set-entity-schema-properties` accepts `primary-color-column`; MCP unit test maps the argument; MCP e2e covers set + readback |

## Constraints

- **C1**: Additive only. Existing CLI options, MCP arguments and JSON fields keep their names and meaning; the new JSON field is appended. `primary-color-column` joins the "one settable property required" set.
- **C2**: Same save pipeline as `primary-display-column` (`SaveAndReloadSchema`, `ODataContractImpact.Unchanged` — the color column is a designer property absent from the OData contract) and the same readback verification.
- **C3**: Type check uses the designer column's data value type (`Color` = 18), resolved by `FindColumnForRead` (own, then inherited).
- **C4**: Kebab-case option names; docs (`help/en`, `docs/commands`, `Commands.md`) and MCP artifacts reviewed per AGENTS.md; ClioRing compatibility reviewed.

## Non-goals

- Will NOT: clear the color column (no "unset" value) — not requested; can be added additively later.
- Will NOT: add the color column to `sync-schemas` `update-entity` or `create-lookup`/`create-entity-schema` in this change.
- Will NOT: change how colors are written (`upsert-data-binding-row-db` already supports `Color`) or the palette (owned by guidance).

## Success Signal

`clio set-entity-schema-properties --package P --schema-name UsrLookup --primary-color-column UsrColor -e env` exits 0, and `clio get-entity-schema-properties --schema-name UsrLookup -e env` then prints `Primary color column: UsrColor`; the same pair over MCP returns `primary-color-column-name: "UsrColor"`.

---

## Companion Notes

- Designer DTO already carries `primaryColorColumn` (`EntitySchemaDesignerDtos.cs`); `RemoteEntitySchemaColumnManager` already clears it when its column is removed.
- Merged (runtime) read: the runtime payload is mapped tolerantly from `primaryColorColumnName` or `primaryColorColumnUId`, mirroring the primary-display resolution. The exact runtime field name is an open check on a live stand; when absent the field is `null`, never a guess.
- Follow-up in clio-knowledge: replace the manual Object Designer step in `lookup-colors` with `set-entity-schema-properties primary-color-column`, gated on the clio version that ships this. Re-pin `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` once PR #356 is published.
- Source: https://academy.creatio.com/guides/no-code-customization/customization-tools/system-settings-and-lookups/manage-lookup-values
