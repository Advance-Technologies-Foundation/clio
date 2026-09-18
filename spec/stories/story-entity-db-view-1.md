# Entity database-view support

Status: done
Issue: #1618
Design: [Native flag propagation](../adr/adr-entity-db-view.md)

Expose `is-db-view` through CLI/MCP creation, sync create-entity, and schema-property updates.
Reuse the native designer pipeline and verify the saved flag. Preserve omitted values.
Reject sync storage-kind conflicts and DB-view inline seeding before mutation.

Acceptance:
- Unit tests cover argument propagation, true/false updates, persistence failures, and sync conflicts.
- Real MCP tests on a disposable PostgreSQL Creatio instance verify flags, table generation, and replay.
- Documentation and discoverable contracts describe separately provisioned SQL views.
- Disposable runtime is removed after validation; evidence is preserved.

Validation: 10,380 affected-module unit tests passed; all three disposable Creatio MCP
cases passed. The runtime was removed and evidence retained. Parallel review and the
focused Claude consultation found no material issues; documentation feedback was applied.
