# ADR: Close Entity-Schema Authoring Gaps — Primary-Display Column, Inherited-Column Caption Override, Color Type

**Status**: Proposed
**Author**: Architect Agent
**PRD**: [prd-entity-schema-authoring-gaps.md](../prd/prd-entity-schema-authoring-gaps.md)
**Jira**: ENG-93040 (epic ENG-85256)
**Created**: 2026-07-09
**stepsCompleted**: [1, 2, 3, 4]

---

## Context

The AI App Development Toolkit cannot fully model an object schema through clio: there is no way to set the
primary-display column, inherited-column captions cannot be overridden on a replacing/child schema, and the
Color type (`dataValueType 18`) is rejected. All three gaps are verified live on Creatio 8 core 10.1.185, and
the confirmed server contracts are recorded in the `.codex/workspace-diary.md` entry dated 2026-07-09 (treated
as ground truth here). The `EntitySchemaDesignerService.svc/SaveSchema` payload clio already sends carries
`primaryDisplayColumn`, `inheritedColumns`, and `isInherited`; the blockers are missing setter surfaces, one
hard guard, and one missing type-registry entry — not persistence plumbing.

## Decision

Deliver three narrowly-scoped changes on the existing Entity Schema Designer stack, with **no new
`SaveSchema` transport code**:

1. **New `set-entity-schema-properties` CLI command + MCP tool** that sets schema-level properties (v1:
   `--primary-display-column`), implemented as a new method on the existing
   `IRemoteEntitySchemaColumnManager` so the proven
   load → mutate → `SaveSchema` → `SaveSchemaDbStructure` → `PublishAndRebuildOData` → `GetRuntimeEntitySchema`
   pipeline is reused verbatim. Ships **enabled** (no `[FeatureToggle]`).
2. **Relax the inherited-column guard** in `RemoteEntitySchemaColumnManager` so a **caption-and/or-description-only**
   `modify` of an inherited column is permitted (applied in place on the `InheritedColumns` entry, `uId`/`name`/`type`
   unchanged), while name/type/flag changes to an inherited column stay rejected.
3. **Register the Color type** by adding `["color"] = 18` to `SupportedDataValueTypes` and `18 => "Color"` to
   `GetFriendlyTypeName`, deliberately NOT adding `18` to any text-like set. Only the named `Color` token is public
   (OQ-04); raw `18` stays internal.

## Alternatives Considered

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| A: Dedicated `IRemoteEntitySchemaPropertiesWriter` service for the setter | Single-responsibility class | Duplicates `LoadSchema`, `ResolvePackage`, and the whole save/publish/verify pipeline (all private to the manager) or forces extracting them prematurely | Rejected: the manager already owns the pipeline; one owner of the designer save path |
| **B: New `SetSchemaProperties` method on `IRemoteEntitySchemaColumnManager` + new thin command/tool** | Reuses `LoadSchema` + save/publish/verify; no transport duplication; consistent with the read setters already on the manager | Grows the manager interface | **Chosen** |
| C: Lighter save for primary-display (call only `SaveSchema`, skip DDL/publish) | Faster; primary-display is metadata-only (no DDL) | `get-entity-schema-properties` merged readback (AC-01) reads the **runtime** schema, which only reflects a published change; a divergent path risks a stale readback and inconsistent behavior vs every other designer write | Rejected: run the same pipeline (OQ-01) |
| D: Add `--primary-display-column` to `create-entity-schema` / `update-entity-schema` instead of a new verb | No new command | Cannot set primary-display on an existing schema without a column mutation; PRD FR-01 mandates a dedicated `set-entity-schema-properties` surface designed for future schema-level props (FR-11) | Rejected (a create-time convenience flag may be added later, out of scope) |
| E: Override inherited caption by moving the column into `columns` | Reuses the own-column mutate path | Creates an own column shadowing the inherited one, changes semantics, and contradicts the verified server contract (caption belongs on the `inheritedColumns` entry, persisted keyed `<Schema>.Columns.<C>.Caption`) | Rejected |
| F: Accept raw numeric `18` publicly | Symmetric with internal representation | Contradicts OQ-04 (resolved) and the name-keyed registry convention | Rejected |
| G: Add `18` to `TextDataValueTypes` to reuse text handling | Less code | Wrongly enables multiline / accent / format-validated / masked on Color (violates FR-07 / AC-07) | Rejected |

## Implementation Plan

### Files to create

| File | Purpose |
|------|---------|
| `clio/Command/SetEntitySchemaPropertiesCommand.cs` | New `SetEntitySchemaPropertiesOptions` (`[Verb("set-entity-schema-properties")]`) + `SetEntitySchemaPropertiesCommand : Command<SetEntitySchemaPropertiesOptions>` (ctor-injects `IRemoteEntitySchemaColumnManager`, `ILogger`) |
| `clio/help/en/set-entity-schema-properties.txt` | CLI `-H` help |
| `clio/docs/commands/set-entity-schema-properties.md` | GitHub command docs |
| `clio.tests/Command/SetEntitySchemaPropertiesCommandTests.cs` | Command tests (`BaseCommandTests<SetEntitySchemaPropertiesOptions>`) |

### Files to modify

| File | Change description |
|------|-------------------|
| `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs` | Add `void SetSchemaProperties(SetEntitySchemaPropertiesOptions options)` to the interface + impl. Extract the existing save/publish/verify tail of `ModifyColumns` into a private helper and reuse it. Relax `FindOwnColumnForMutation` into an inherited-aware mutation resolver; make `ModifyColumn` apply caption/description only for an inherited target. Fix `VerifyColumnMutations`/`VerifyColumnMutation` to check `InheritedColumns` for an inherited caption modify |
| `clio/Command/EntitySchemaDesigner/EntitySchemaDesignerSupport.cs` | Add `["color"] = 18` to `SupportedDataValueTypes`; add `18 => "Color"` to `GetFriendlyTypeName`. Do NOT touch `TextDataValueTypes` / `BinaryLikeDataValueTypes` / `RuntimeDataValueTypeUIdMap` |
| `clio/Program.cs` | Add `typeof(SetEntitySchemaPropertiesOptions)` to `CommandOption` list and a dispatch arm `SetEntitySchemaPropertiesOptions opts => Resolve<SetEntitySchemaPropertiesCommand>(opts).Execute(opts)` |
| `clio/BindingsModule.cs` | Add `services.AddTransient<SetEntitySchemaPropertiesCommand>();` next to the other entity-schema commands (~L657). The manager interface is auto-registered by `RegisterAssemblyInterfaceTypes`, so no manual service registration is needed |
| `clio/Command/McpServer/Tools/EntitySchemaTool.cs` | Add `SetEntitySchemaPropertiesTool : BaseTool<SetEntitySchemaPropertiesOptions>` + `SetEntitySchemaPropertiesArgs`. Add `Color` to the `type` `[Description]` lists on create/modify column args. Note inherited caption-override support in the modify/update tool `[Description]` |
| `clio/Command/ModifyEntitySchemaColumnCommand.cs` | Add `Color` to the `--type` `HelpText`. (No validation change: inherited handling lives in the manager) |
| `clio/Command/CreateEntitySchemaCommand.cs` (options) | Add `Color` to the `--type`/columns help text |
| `clio/Command/McpServer/Prompts/EntitySchemaPrompt.cs` | Mention `set-entity-schema-properties`, inherited caption override, and Color |
| `clio/Command/McpServer/Resources/AppModelingGuidanceResource.cs` | Add guidance: set primary-display via the new tool; inherited caption/description override allowed (name/type/flags still read-only); Color is a supported type |
| `clio/Command/McpServer/Resources/RoutingGuidanceResource.cs` | Update the `app-modeling` routing row wording only if the trigger phrasing changes (no new guide) |
| `clio/Commands.md`, `clio/Wiki/WikiAnchors.txt` | Add the new command; note the Color type + inherited-caption widening on the three touched commands |
| `clio/docs/commands/{create-entity-schema,modify-entity-schema-column,update-entity-schema}.md` and `clio/help/en/{...}.txt` | Document Color type and inherited-caption override |
| `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs` | Add coverage for `SetSchemaProperties`, inherited caption override (allow/reject), inherited verify, Color add/modify + not-text-like |
| `clio.tests/Command/EntitySchemaDesignerSupportTests.cs` | Color resolves to 18, friendly name `Color`, `IsTextLikeDataValueType(18)` false |
| `clio.tests/Command/McpServer/EntitySchemaToolTests.cs` | New tool mapping + updated descriptions |
| `clio.mcp.e2e/EntitySchemaToolE2ETests.cs` (+ `Support/Results/EntitySchemaEnvelope.cs` if needed) | E2E for all three capabilities incl. negatives |

### Key interfaces / contracts

```csharp
// New method on the EXISTING interface (no MediatR; ctor-injected service):
public interface IRemoteEntitySchemaColumnManager {
    // ... existing members ...
    void SetSchemaProperties(SetEntitySchemaPropertiesOptions options);
}

// New options — an extensible "bag": each settable schema-level property is a separate
// OPTIONAL option; only supplied ones are applied. Mirrors get-entity-schema-properties' env/schema args.
[Verb("set-entity-schema-properties", HelpText = "Set schema-level properties on a remote Creatio entity schema")]
public class SetEntitySchemaPropertiesOptions : RemoteCommandOptions {
    [Option("package", Required = true, HelpText = "Target package name")]           public string Package { get; set; }
    [Option("schema-name", Required = true, HelpText = "Entity schema name")]        public string SchemaName { get; set; }
    [Option("primary-display-column", Required = false,
        HelpText = "Column name (own or inherited) to set as the primary-display column")]
    public string? PrimaryDisplayColumn { get; set; }
    // FR-11: future schema-level properties add new optional [Option]s here — no contract break.
}
```

`SetSchemaProperties` algorithm:
1. `ResolvePackage(options.Package)` → `LoadSchema(...)` (design item, package-scoped; a write must target a package layer).
2. Require at least one settable property; else `EntitySchemaDesignerException("No schema property to set.")`.
3. For `--primary-display-column <C>`: find `<C>` in `schema.Columns` then `schema.InheritedColumns`
   (case-insensitive); if absent throw `"Column '<C>' was not found in schema '<S>'."`; set
   `schema.PrimaryDisplayColumn = matchedColumn` (matched by its `uId` object — modern contract, NOT the legacy
   flat `primaryDisplayColumnUId`).
4. Run the **shared save/publish/verify pipeline** (extracted from `ModifyColumns`). Verify by reading back
   `reloadedSchema.PrimaryDisplayColumn?.Name` == `<C>` — this converts the A-01 silent-no-op risk into a clear error.

Inherited caption-override guard (replaces the current `FindOwnColumnForMutation` throw at ~L751):
- Locate the target: own column wins; else inherited column; else "not found".
- If the target is **inherited** and the requested `modify` is **caption/description-only**
  (`Title`/`TitleLocalizations`/`Description`/`DescriptionLocalizations` set, and none of
  `NewName`, `Type`, `ReferenceSchemaName`, `Required`, `Indexed`, `Cloneable`, `TrackChanges`, default-value*,
  `MultilineText`, `LocalizableText`, `AccentInsensitive`, `Masked`, `FormatValidated`, `UseSeconds`,
  `SimpleLookup`, `Cascade`, `DoNotControlIntegrity` present) → apply **only** `ApplyColumnCaptionAndDescription`
  in place on the `InheritedColumns` entry (keep `uId`/`name`/`type`; do NOT move it to `Columns`).
- If the target is inherited and the mutation is NOT caption-only → throw
  `"Column '<C>' is inherited; only its caption and description can be overridden. Its name, type, and flags are read-only."`
- **OQ-03 resolved:** the existing `NormalizeTitleLocalizations` → `ApplyColumnCaptionAndDescription` path already
  produces the correct `caption` localizable array; the server maps it unconditionally
  (`Caption` is `[DesignModeProperty(AllowEditInherited=true)]`) keyed `<Schema>.Columns.<C>.Caption` on the child,
  parent untouched. No new localizable shape is required — reuse `ApplyColumnCaptionAndDescription` verbatim.
- **Verify fix:** `VerifyColumnMutation` for a `Modify` must accept the column in `Columns` **OR**
  `InheritedColumns` (it currently checks `Columns` only, which would falsely fail an inherited override); for an
  inherited caption override additionally assert the reloaded inherited column's caption equals the requested value
  in the effective culture (fall back to en-US).

### CLI flag specification

| Flag | Type | Required | Description |
|------|------|----------|-------------|
| `--package` | string | Yes | Target package name (writes are package-scoped) |
| `--schema-name` | string | Yes | Entity schema name |
| `--primary-display-column` | string | No | Column name (own or inherited) to set as primary display |
| `-e` / env, uri, login, password, oauth (inherited from `RemoteCommandOptions`) | — | per env | Environment selection, mirrors the other entity-schema commands |

Extended value: `create-entity-schema` / `modify-entity-schema-column` `--type` now accepts `Color` (→ 18).
All flags kebab-case — CLIO001 enforced.

### Test strategy

| Layer | Framework | What to cover | File |
|-------|----------|--------------|------|
| Unit | NSubstitute mocks | `SetSchemaProperties` own/inherited resolution + not-found; inherited caption allow/reject; inherited verify against `InheritedColumns`; Color add/modify; Color rejects masked/multiline/accent/format-validated; primary-display readback mismatch → error | `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs` |
| Unit | NUnit | Color resolves to 18, friendly name `Color`, not text-like | `clio.tests/Command/EntitySchemaDesignerSupportTests.cs` |
| Unit | `BaseCommandTests<SetEntitySchemaPropertiesOptions>` | Validation (required package/schema, at least one property) + delegation to manager | `clio.tests/Command/SetEntitySchemaPropertiesCommandTests.cs` |
| Unit | NSubstitute | New MCP tool arg mapping + updated descriptions | `clio.tests/Command/McpServer/EntitySchemaToolTests.cs` |
| E2E | clio.mcp.e2e | set-primary-display round-trip; inherited caption override + parent unchanged; Color create + `get-entity-schema-properties` reports `Color`; negatives (inherited non-caption mutation, missing column, unsupported type) | `clio.mcp.e2e/EntitySchemaToolE2ETests.cs` |

MCP E2E is NOT in CI yet — run manually. Unit mapping tests alone do NOT complete the MCP work (repo policy).

## Consequences

- **Positive**: Unblocks fully-automated object authoring for epic ENG-85256 (the Case→Tickets rebrand and Color
  models). No new `SaveSchema` transport; the change is surface + one guard relaxation + one type-registry entry.
- **Trade-offs**: `set-entity-schema-properties` inherits the publish (compile-class, 60-min timeout) cost of the
  shared pipeline even though primary-display is metadata-only; accepted for readback consistency (OQ-01).
  The manager interface grows by one method.
- **MCP FeatureToggle**: **Ship enabled, no `[FeatureToggle]`.** The server contracts are verified live (A-01), the
  setter mirrors an existing read tool and reuses a proven pipeline, and the caption/Color changes are widenings of
  already-shipping commands (which cannot be behavior-gated anyway). Gating would delay epic value with no safety
  benefit. Add a toggle later only if a target-version regression surfaces.
- **Breaking change**: No. New verb is additive; inherited-caption acceptance is a widening (no contract removed);
  Color is additive. No `RELEASE.md` breaking-change entry required (add a feature note).
- **Risks**: (A-01) a target version expecting the legacy flat `primaryDisplayColumnUId` would silently no-op —
  mitigated by the readback verification that turns it into a clear error. Inherited-verify culture mismatch —
  mitigated by effective-culture match with en-US fallback. CLIO005: no dead registrations (new method on an
  already-injected interface; new command wired through `Program` dispatch; MCP tool resolved dynamically).

## Ordered implementation plan (maps to stories)

1. **Story 1 — Color type.** Registry + friendly name + validation coverage + tool/help/docs type lists. Smallest, self-contained, unblocks G3.
2. **Story 2 — set-entity-schema-properties.** Options + command + `SetSchemaProperties` (extract shared pipeline) + DI/Program wiring + MCP tool + command tests + docs. Delivers G1.
3. **Story 3 — Inherited caption override.** Guard relaxation + `ModifyColumn` caption-only path + `VerifyColumnMutations` fix + manager/tool tests + docs. Delivers G2.
4. **Story 4 — MCP guidance/prompt/routing + E2E.** `AppModelingGuidanceResource`, `EntitySchemaPrompt`, routing row, and `clio.mcp.e2e` coverage for all three capabilities incl. negatives.

## Pre-implementation Checklist

- [ ] All new CLI options are kebab-case (`set-entity-schema-properties`, `--primary-display-column`, `--schema-name`, `--package`)
- [ ] `SetEntitySchemaPropertiesCommand` registered in `BindingsModule.cs` and dispatched in `Program.cs`; option type added to `CommandOption`
- [ ] Error messages user-friendly (`Error: ...`, non-zero exit) for missing column, unsupported type, disallowed inherited mutation
- [ ] Existing tests that assert inherited immutability of non-caption props still pass (counter-metric G2)
- [ ] Color never exposes text-only options (masked/multiline/accent/format-validated) — asserted (AC-07)
- [ ] MCP tool + prompt + guidance + `clio.mcp.e2e` updated; if any surface unchanged, state "MCP reviewed, no update required"
- [ ] Docs updated (`help/en`, `docs/commands`, `Commands.md`, `Wiki/WikiAnchors.txt`)
- [ ] No MediatR; behavior via `Command<TOptions>` + ctor-injected services; DTOs may be records/new
