# SPEC: Colorful lookup values (ENG-90714)

**Created**: 2026-11-23  
**Size estimate**: M (3-4 stories)  
**Recommended next**: /bmad-spec is sufficient

---

## Why

A no-code creator asks a code agent to "make the values of lookup X colorful" so options are easier to tell
apart on Freedom UI pages. clio already ships every mechanical step, but nothing tells the agent the
procedure, the recommended palette, or which write path accepts a Color column — so today the agent
guesses, reaches for `odata-update` (which cannot write Color) and either fails or picks arbitrary colors.

## Capabilities

| ID | Intent (WHAT) | Success Signal (HOW WE KNOW) |
|----|--------------|------------------------------|
| CAP-01 | Agent determines whether a lookup object already has a Color-typed column | Guidance names the read tool and field; MCP e2e reads a lookup created with a `Color` column and the column reports type `Color` |
| CAP-02 | When the column is missing, agent asks the user for a target package and adds a `Color` column through a replacing object there | Live-stand run: replacing object of an OOTB lookup with an added `Color` column is saved and published; guidance requires the package question before any schema write |
| CAP-03 | Agent writes a hex color to selected lookup records through a write path that accepts Color | MCP e2e: `upsert-data-binding-row-db` stores `#EDF9CC` on a lookup row and `execute-esq` reads the same literal back |
| CAP-04 | When the user names no colors, agent assigns them deterministically from the Academy palette: 16 recommended colors in order, then 16 auxiliary colors | Guidance carries both ordered lists verbatim; a unit test pins the same 32 values in the same order wherever clio embeds them |
| CAP-05 | Agent is routed to the procedure from the tools it will naturally call | `create-lookup`, `modify-entity-schema-column` and `upsert-data-binding-row-db` descriptions carry a one-line trigger to `get-guidance name=lookup-colors`; drift test passes |

## Constraints

- **C1**: No new write tool. Color values go through the existing `upsert-data-binding-row-db` (dataValueType 18 already supported); `odata-update`/`odata-create` stay unable to write Color (absent from `$metadata`) and guidance must say so.
- **C2**: Guidance content lives in `clio-knowledge` (new article `lookup-colors` + routing pointer, `libraryVersion` + `sequence` bump); clio carries only trigger lines and re-pins `curated-knowledge-names.json`. No guide text duplicated in `McpServerInstructions.cs`.
- **C3**: Palette = Academy "Manage lookup values" lists verbatim, uppercase `#RRGGBB`. Recommended: #EDF9CC #CCF4DC #D2EED0 #FFEECD #FFE7CC #FED6E5 #FFE0D6 #FFD9D0 #F1E5F5 #E4DAFC #CCE9FB #CCDEFC #CDEEFD #DCD9F3 #CCEAE7 #CCF2ED. Auxiliary: #A6DE00 #00C853 #22AC14 #FFAC07 #FF8800 #F9307F #FF6534 #FF4013 #B87CCF #7848EE #0091EA #0058EF #03A9F4 #4F43C2 #009688 #00BFA5. It is distinct from `SectionIconPalette` and must not be merged with it.
- **C4**: A schema change (replacing object) never happens without the user naming/confirming the package; colors explicitly given by the user override the palette but must be valid `#RRGGBB`.
- **C5**: Tool-description edits are part of the MCP contract → ClioRing compatibility review required (expected: no Ring-consumed contract changed).

## Non-goals

- Will NOT: add Color (dataValueType 18) support to `execute-dataservice-batch` or make OData write Color.
- Will NOT: change Freedom UI page bodies — coloring renders from the column value alone.
- Will NOT: color Classic UI pages or introduce a palette-advisor tool.

## Success Signal

On a live stand, the prompt "make the values of lookup <X> colorful" drives an agent through `get-guidance name=lookup-colors` → column check → (package question + replacing object if needed) → `upsert-data-binding-row-db` per value, and `execute-esq` returns the recommended colors in palette order for the lookup's values.

---

## Companion Notes

- Existing building blocks: `create-lookup` / `create-entity-schema` (`extend-parent` for replacing objects), `modify-entity-schema-column` (type `Color`), `upsert-data-binding-row-db` (`DataBindingDbCommand.ConvertColorValue`, dataValueType 18), `get-entity-schema-properties`, `execute-esq`.
- Side effect to document in guidance: `upsert-data-binding-row-db` also binds the record into the package, so the colors ship with it — the intended behaviour for a package-delivered customization.
- Open (verify on stand, story 1): whether OOTB lookups that already have `Color` (e.g. `ActivityStatus`-style lookups) need no replacing object; whether binding an OOTB record into a user package has caveats.
- Value ordering for auto-assignment: the lookup's natural order (`Name` ascending unless the user gives an order) so reruns are idempotent.
- Source: https://academy.creatio.com/guides/no-code-customization/customization-tools/system-settings-and-lookups/manage-lookup-values
