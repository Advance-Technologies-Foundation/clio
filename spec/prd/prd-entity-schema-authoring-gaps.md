# PRD: Close Entity-Schema Authoring Gaps — Primary-Display Column, Inherited-Column Caption Override, Color Type

**Status**: Draft
**Author**: PM Agent
**Created**: 2026-07-09
**Jira**: [ENG-93040](https://creatio.atlassian.net/browse/ENG-93040) (parent epic: ENG-85256 "AI no-code agents"; component: user customization; priority: Major)

---

## Problem Statement

A Creatio developer using the AI App Development Toolkit cannot fully model an object schema through clio's entity-schema tools (`create-entity-schema`, `modify-entity-schema-column`, `update-entity-schema`): they cannot set the primary-display column, cannot override the caption of an inherited column on a replacing/child schema, and cannot create a Color-type column. Each gap forces the developer (or an AI no-code agent) to abandon the toolkit and finish the work by hand in the Object Designer, which breaks the fully-automated authoring flow the epic (ENG-85256) depends on. All three gaps are verified live on Creatio 8 (core 10.1.185), and the underlying `EntitySchemaDesignerService.svc/SaveSchema` contract already carries the needed fields — the blockers are missing setter surfaces and one hard guard in clio.

## Background — Current State (verified, treated as ground truth)

All three tools save through `EntitySchemaDesignerService.svc/SaveSchema`, sending the full `EntityDesignSchemaDto`
(`clio/Command/EntitySchemaDesigner/RemoteEntitySchemaDesignerClient.cs`). The DTO already carries
`primaryDisplayColumn`, `inheritedColumns`, and `isInherited`.

1. **Primary-display column not settable.** No parameter in any of the three tools sets the schema's
   primary-display column, and no `set-entity-schema-properties` tool exists (only read-only `get-*`).
   The modern `SaveSchema` takes a nested `primaryDisplayColumn` **object** matched by `uId` (not the legacy
   flat `primaryDisplayColumnUId`). Server does not validate the target (need not be text; own or inherited
   allowed). Persistence already works in clio's DTO — only the setter surface is missing.
   `get-entity-schema-properties` already reports `primary-display-column-name`, so readback for this gap is
   partly in place.
2. **Inherited-column caption cannot be overridden.** `modify-entity-schema-column action=modify` on an
   inherited column is rejected with *"Column '<X>' is inherited and read-only in v1."* The server maps the
   caption unconditionally (column `Caption` is `[DesignModeProperty(AllowEditInherited=true)]`); sending the
   column in `inheritedColumns` with `isInherited:true`, the same `uId`/`name`/`type`, and a new localizable
   caption array produces a proper caption override on the child schema (persisted keyed
   `<Schema>.Columns.<Column>.Caption`) and leaves the parent untouched. clio already round-trips
   `InheritedColumns`; the **only** blocker is the hard guard in
   `RemoteEntitySchemaColumnManager.FindOwnColumnForMutation`.
3. **Color column type (dataValueType 18) unsupported.** `type:"Color"` and `type:"18"` are both rejected with
   *"Unsupported type."* `ColorDataValueType : TextDataValueType` (GUID `{DAFB71F9-EE9F-4E0B-A4D7-37AA15987155}`)
   stores a hex string (size 250), `IsLocalizableText=false`. Existing Color columns read back as opaque
   `"type":"18"` with no named token. clio must add the mapping and must **not** classify Color as text-like
   (which would wrongly enable multiline / accent / format-validated / masked options).

## Goals

- [ ] **G1 — Set primary-display column via the toolkit.** Success metric **SM-01**: a single toolkit call
  sets the schema's primary-display column and `get-entity-schema-properties` reports the same column as
  `primary-display-column-name` (measured by a passing E2E round-trip). **Counter-metric**: no regression to
  existing `create-entity-schema` / `update-entity-schema` behavior — schemas created without the new
  parameter keep their current default primary-display column (existing unit + E2E suites stay green).
- [ ] **G2 — Override inherited-column captions.** **SM-02**: `modify-entity-schema-column` /
  `update-entity-schema` successfully overrides the caption (title-localizations) of an inherited column on a
  replacing/child schema, and the parent schema's caption is unchanged (verified by reading both schemas after
  save). **Counter-metric**: name/type/flags of inherited columns remain rejected for mutation — no unit test
  that asserts inherited immutability of non-caption properties starts failing.
- [ ] **G3 — Create and read Color columns.** **SM-03**: `create-entity-schema` /
  `modify-entity-schema-column` accept a Color type token, create a dataValueType-18 column, and
  `get-entity-schema-properties` reports the named `Color` token (not raw `18`). **Counter-metric**: Color
  columns never expose text-only options (multiline / accent / format-validation / mask); a test asserts those
  options are rejected/absent for Color.

## Non-goals

- Will NOT add broader schema-level property editing beyond the primary-display column in this iteration
  (e.g. schema caption, description, parent reassignment). The new `set-entity-schema-properties` surface must
  be **designed to accommodate** future schema-level properties, but only primary-display is implemented now.
- Will NOT allow mutating **name, type, or flags** of an inherited column — only the caption override is
  unlocked; the read-only guard remains for all other inherited-column properties.
- Will NOT update the `creatio-ai-app-development-toolkit` repo docs (skill references to the
  "inherited and read-only in v1" limitation) — that is a downstream follow-up tracked separately (see
  Dependencies / OQ-02).
- Will NOT introduce color-value validation of the stored hex string beyond what the platform enforces
  (Color stores a plain hex text; format validation is out of scope).

## User Stories (high level)

| As a | I want | So that |
|------|--------|---------|
| Creatio developer (toolkit user) | to set the primary-display column when I create or update an object schema | I don't have to open the Object Designer to finish the model |
| Creatio developer (toolkit user) | to rename inherited column captions on a replacing/child schema (e.g. rebrand Case→Tickets: Symptoms→Description, Solution→Resolution Notes, ClosureDate→Closed Date/Time) | I can rebrand a base object without redefining columns or breaking the parent |
| Creatio developer (toolkit user) | to create Color columns via the toolkit and read them back as a named Color type | I can model color-bearing objects end-to-end in clio |
| AI no-code agent | the entity-schema tools to cover primary-display, inherited captions, and Color | I can author a complete object without a human finishing in the designer |
| QA engineer | unit + `clio.mcp.e2e` coverage for all three capabilities including negative cases | the new surfaces are verifiably correct and non-regressive |

## Feature Requirements

| ID | Requirement | Priority |
|----|------------|---------|
| FR-01 | Provide a new `set-entity-schema-properties` CLI command + MCP tool that sets schema-level properties, starting with the primary-display column, matched into the DTO's nested `primaryDisplayColumn` object by column `uId`. Mirror the read-only `get-entity-schema-properties` surface (same env/schema arguments). | Must |
| FR-02 | The primary-display target may be an own or inherited column; the command resolves the column by name to its `uId` before saving, and errors if the named column does not exist on the schema. | Must |
| FR-03 | Allow a **caption-only** modify of an inherited column via `modify-entity-schema-column` / `update-entity-schema`: send the column in `inheritedColumns` with `isInherited:true`, unchanged `uId`/`name`/`type`, and the new localizable caption. Persist the override on the child schema without touching the parent. | Must |
| FR-04 | Replace the hard guard in `RemoteEntitySchemaColumnManager.FindOwnColumnForMutation` so that a caption-only change to an inherited column is permitted, while name/type/flag changes to an inherited column remain rejected with a clear error. | Must |
| FR-05 | Accept a `Color` type token (and its numeric `dataValueType 18`) in `create-entity-schema` / `modify-entity-schema-column`, mapping to `dataValueType 18`; add `["color"]=18` to `EntitySchemaDesignerSupport.SupportedDataValueTypes`. | Must |
| FR-06 | `get-entity-schema-properties` reports a dataValueType-18 column's type as the named `Color` token (via `GetFriendlyTypeName` `18 => "Color"`), not raw `18`. | Must |
| FR-07 | Color must NOT be treated as text-like: multiline, accent, format-validation, and mask options must not be enabled/accepted for a Color column. | Must |
| FR-08 | Update the MCP surface for every touched/added command: tool, prompt, resources (including routing/guidance where the inherited-column rule or type list is described), and `clio.mcp.e2e` coverage. | Must |
| FR-09 | Update documentation for the new and changed commands: `help/en/<verb>.txt`, `docs/commands/<verb>.md`, `Commands.md`, and `Wiki/WikiAnchors.txt` where applicable. | Must |
| FR-10 | Emit a clear, user-friendly error when a caller attempts a non-caption mutation of an inherited column, or supplies an unsupported type, or names a missing primary-display column. | Must |
| FR-11 | Design `set-entity-schema-properties` so additional schema-level properties can be added later without a breaking change to the command/tool contract. | Should |

## CLI Impact

| Change | Details | Breaking? |
|--------|---------|-----------|
| New command | `set-entity-schema-properties` (new verb) | No |
| New flag | `--primary-display-column` on `set-entity-schema-properties` (column name to set as primary display) | No |
| New flag | `--schema-name` / `--environment` (`-e`) on `set-entity-schema-properties`, mirroring `get-entity-schema-properties` | No |
| Modified behavior | `modify-entity-schema-column` / `update-entity-schema`: caption-only change on an inherited column is now accepted (previously threw "inherited and read-only in v1"). No flag rename; existing caption arguments apply to inherited columns too. | No — widens acceptance, no contract removal |
| Extended value | `create-entity-schema` / `modify-entity-schema-column` type parameter now accepts `Color` (→ dataValueType 18) | No — additive |

All flags: **kebab-case only** (CLIO001 enforced). Confirmed kebab-case: `set-entity-schema-properties`,
`--primary-display-column`, `--schema-name`, `--environment`. New command follows `Command<TOptions>` +
constructor-injected services, registered in `BindingsModule.cs` and wired in `Program.cs` (no MediatR).
MCP surface per `docs/McpCapabilityMap.md` and `clio/Command/McpServer/**`.

## Acceptance Criteria

- [ ] **AC-01** (G1): Given a registered environment and an existing entity schema, when the caller runs
  `set-entity-schema-properties --schema-name <S> --primary-display-column <C>`, then `SaveSchema` persists the
  nested `primaryDisplayColumn` object matched by `<C>`'s `uId`, and a subsequent `get-entity-schema-properties`
  returns `<C>` as `primary-display-column-name`.
- [ ] **AC-02** (G1/FR-02): Given a schema whose primary-display target is an inherited column, when
  `set-entity-schema-properties --primary-display-column <inheritedCol>` runs, then the save succeeds and
  readback confirms the inherited column as primary display.
- [ ] **AC-03** (G2): Given a replacing/child schema with inherited column `<C>`, when
  `modify-entity-schema-column` (or `update-entity-schema`) overrides `<C>`'s caption with new
  title-localizations, then the caption override is persisted keyed `<Schema>.Columns.<C>.Caption` on the child
  schema, readback shows the new caption, and reading the parent schema shows the parent's caption unchanged.
- [ ] **AC-04** (G2/FR-04): Given an inherited column, when the caller attempts to change its **name, type, or
  flags**, then clio prints `Error: {message}` and exits non-zero (inherited immutability preserved for
  non-caption properties).
- [ ] **AC-05** (G3): Given a `create-entity-schema` / `modify-entity-schema-column` call with type token
  `Color` (or `18`), then a dataValueType-18 column is created and the save succeeds.
- [ ] **AC-06** (G3/FR-06): Given an existing dataValueType-18 column, when `get-entity-schema-properties` runs,
  then the column type is reported as the named `Color` token, not raw `18`.
- [ ] **AC-07** (G3/FR-07): Given a Color column, when text-only options (multiline / accent /
  format-validation / mask) are requested, then they are not applied/accepted (rejected or absent).
- [ ] **AC-ERR** (FR-10): Given invalid input (missing primary-display column name, unsupported type token, or a
  disallowed inherited-column mutation), clio prints `Error: {message}` and exits non-zero.
- [ ] **AC-MCP**: Given the MCP server, each new/changed capability is exposed as an MCP tool with aligned
  arguments/descriptions/destructive flags and has passing `clio.mcp.e2e` coverage.
- [ ] **AC-DOC**: Given the shipped change, `help/en`, `docs/commands`, and `Commands.md` reflect the new command
  and the widened behavior; guidance/routing MCP resources referencing the inherited-column rule and supported
  type list are updated.

## Assumptions Index

| # | Assumption | Risk if wrong |
|---|-----------|--------------|
| A-01 | Modern `EntitySchemaDesignerService.svc/SaveSchema` accepts the nested `primaryDisplayColumn` object matched by `uId` and persists it without server-side validation (verified on core 10.1.185). | If a target version expects legacy flat `primaryDisplayColumnUId`, primary-display setting silently no-ops on that version. |
| A-02 | Server maps inherited-column `Caption` unconditionally because `Caption` is `[DesignModeProperty(AllowEditInherited=true)]`, for both replacing and ordinary descendant schemas. | Caption override could fail or corrupt the parent on an untested schema shape. |
| A-03 | `ColorDataValueType` (dataValueType 18) behaves as a text-derived store (hex string, size 250) but is not localizable and must not receive text-like options. | Misclassifying Color as text-like enables invalid options and produces malformed columns. |
| A-04 | `get-entity-schema-properties` already reporting `primary-display-column-name` is sufficient readback for AC-01/AC-02 with no schema-read changes. | Extra readback plumbing needed, expanding scope. |
| A-05 | Core-rules invariants (compile/restart, destructive confirmation, profile culture) apply unchanged to the new setter command. | New command may skip required restart/confirm handling. |

## Open Questions

| # | Question | Owner | Due |
|---|---------|-------|-----|
| OQ-01 | Should `set-entity-schema-properties` require a compile/restart after setting primary display, or is the design-time save sufficient (per core-rules)? | Architect | TBD |
| OQ-02 | Who owns the downstream `creatio-ai-app-development-toolkit` doc touch-up (context/essentials.md, creatio-schema-naming, creatio-app-orchestrator) once clio ships, and does it need its own ticket? | PM / Toolkit maintainer | TBD |
| OQ-03 | Does caption override on an inherited column require a specific localizable-array shape per culture, or does clio's existing InheritedColumns round-trip already produce the correct `<Schema>.Columns.<C>.Caption` payload? | Architect | TBD |
| OQ-04 | ~~Should the numeric `18` token be accepted publicly, or only the named `Color` token (with `18` treated as internal)?~~ **RESOLVED 2026-07-09:** public surface accepts **only the named `Color` token**; raw `18` stays internal (matches every other name-keyed type in the registry). Readback reports `Color`. | PM / Architect | Done |

## Dependencies

- **Depends on**: existing Entity Schema Designer stack in clio
  (`RemoteEntitySchemaDesignerClient`, `RemoteEntitySchemaColumnManager`, `EntitySchemaDesignerSupport`,
  `EntitySchemaDesignerDtos`); `get-entity-schema-properties` already reporting `primary-display-column-name`.
- **Blocks**: full end-to-end object authoring via the AI App Development Toolkit (epic ENG-85256) —
  the Case→Tickets rebrand scenario and any model requiring Color columns or a custom primary-display column.
- **Downstream follow-up (not blocking)**: `creatio-ai-app-development-toolkit` skill-doc update removing the
  "inherited and read-only in v1" limitation note (see OQ-02).
