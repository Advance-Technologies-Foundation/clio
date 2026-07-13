# Virtual entity schema — PLAN (ADR)

> GitHub: [#864](https://github.com/Advance-Technologies-Foundation/clio/issues/864)

## Decision

Carry `IsVirtual` on `CreateEntitySchemaOptions`, map `is-virtual` from both MCP creation surfaces,
and assign the value to `EntityDesignSchemaDto.IsVirtual` inside `ApplySchemaMetadata` before
`SaveSchema`.

Keep `SaveSchemaDbStructure` in the existing lifecycle. Creatio's
`DbStructureInstaller.CanInstallEntitySchemaDbStructure` and DB meta-script logic explicitly
exclude `EntitySchema.IsVirtual`, so sending the saved virtual schema through the ordinary pipeline
preserves runtime activation while preventing a physical table. A sandbox E2E test will verify the
database outcome directly.

## Contract shape

- CLI: `create-entity-schema --is-virtual`, optional Boolean, default `false`.
- MCP `create-entity-schema`: top-level `is-virtual`, optional Boolean, default `false`.
- MCP `sync-schemas`: `operations[*].is-virtual`, meaningful only for `create-entity`, default
  `false`.
- `get-entity-schema-properties`: existing `virtual` output becomes part of its curated contract.
- `get-app-info`: each `entities[]` item gains required Boolean `virtual`.

## Compatibility

The change is additive. Existing callers omit the field and retain normal entities. The application
read model gains a non-null Boolean, which is backward compatible for tolerant JSON consumers.
Search of `clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`, and
`clio-ring/ClioRing.Desktop/actions.json` must confirm whether Ring consumes these surfaces.

## Rejected alternatives

- Create normally and update later: rejected because physical synchronization may already occur.
- Skip `SaveSchemaDbStructure` for virtual schemas: rejected because the platform already owns the
  exclusion rule and the ordinary lifecycle performs other required actualization work.
- Put `is-virtual` on the shared lookup/entity argument base: rejected because `create-lookup`
  should not advertise unsupported virtual lookup creation.
