# Virtual entity schema — STORY

Status: review

As an MCP client, I want to create a Creatio virtual entity atomically so that its data is supplied
by an `IEntityQueryExecutor` without ever creating a physical database table.

## Definition of done

- [x] Standalone and batched creation accept `is-virtual` with default `false`.
- [x] The flag is present on the designer DTO before schema save.
- [x] Read-back exposes virtual state through both requested surfaces.
- [x] Curated tool contracts, command help, detailed docs, and command index are aligned.
- [x] Command, MCP unit, MCP E2E, and real database side-effect coverage pass.
- [x] ClioRing consumer compatibility is reviewed.
- [x] Comprehensive agentic review has no unresolved Blocker/High findings.
- [x] Draft PR references and closes GitHub issue #864.
