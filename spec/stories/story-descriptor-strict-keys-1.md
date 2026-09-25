# Generated write-key schema, verified against the bundled archive

**Jira**: [ENG-95244](https://creatio.atlassian.net/browse/ENG-95244) (work package C — the rescope is not yet agreed with the reporter)
**Spec**: [spec-descriptor-strict-keys.md](../prd/spec-descriptor-strict-keys.md) ·
**ADR**: [adr-descriptor-strict-keys.md](../adr/adr-descriptor-strict-keys.md) ·
**Test plan**: [tp-descriptor-strict-keys.md](../test-plans/tp-descriptor-strict-keys.md)
Status: in-progress

As a clio maintainer I want the set of keys CrtProcessBuilder accepts to be a checked-in JSON Schema that a
unit test regenerates from the contract sources inside the bundled archive, so the enforced set can never
drift from what clio ships.

Acceptance:
- [ ] `clio/Command/ProcessModel/Schemas/process-builder-write-keys.schema.json` (draft 2020-12, embedded
      resource): one `$defs` entry per write contract reachable from `BuildProcessRequest` and
      `ProcessOperationDescriptor`, `additionalProperties: false`, `$ref` / `items.$ref` for contract members,
      inheritance flattened, the source CrtProcessBuilder version recorded.
- [ ] Unit test regenerates it with Roslyn from `Files/src/cs/Contracts/*.cs` read through
      `ICompressionUtilities` and fails on any difference, naming the regeneration command
      (`CLIO_REGENERATE_PROCESS_BUILDER_KEY_SCHEMA=1`). TC-U-01, TC-U-02, TC-U-15.
- [ ] `docs/agent-instructions/bundled-packages.md` says a contract-changing rebundle regenerates it.
