# Virtual entity schema — SPEC

> GitHub: [#864](https://github.com/Advance-Technologies-Foundation/clio/issues/864)

## Problem

Clio's entity creation pipeline always saves a normal persisted entity because neither
`create-entity-schema` nor the canonical `sync-schemas` `create-entity` operation accepts the
Creatio entity designer's `isVirtual` flag. Creating a normal entity first is unsafe because the
database structure can be synchronized before the schema is changed.

## Requirements

- R1. Add canonical Boolean `is-virtual` input to the standalone CLI/MCP entity creation path and
  to `sync-schemas` `create-entity`; default it to `false`.
- R2. Set `EntityDesignSchemaDto.IsVirtual` before the first schema save.
- R3. Preserve the existing save, DB-structure, publish, OData rebuild, and runtime verification
  sequence. Creatio's DB structure installer must receive the virtual schema and exclude it from
  physical-table synchronization.
- R4. Expose virtual state as `virtual` in `get-entity-schema-properties` and every entity returned
  by `get-app-info`.
- R5. Document the input and output fields in CLI help/docs and curated `get-tool-contract` output.
- R6. Cover default-false and explicit-true mapping at command, MCP adapter, sync, read-back, and
  real sandbox levels.

## Acceptance criteria

- AC1. Omitting `is-virtual` produces the current normal entity behavior.
- AC2. `is-virtual: true` reaches the first `SaveSchema` request as `isVirtual: true`.
- AC3. A real virtual entity is readable with `virtual: true` and has no PostgreSQL table.
- AC4. Both creation contracts advertise `is-virtual` with default `false`.
- AC5. `get-entity-schema-properties` and `get-app-info` advertise and return virtual state.
- AC6. Required Command/MCP unit tests and MCP sandbox E2E tests pass.

## Out of scope

- Generating or registering an `IEntityQueryExecutor` implementation.
- Making lookup schemas virtual.
- Converting an existing persisted entity into a virtual entity.
