# Test plan: Strict keys for process descriptors and modify operations

**Jira**: [ENG-95244](https://creatio.atlassian.net/browse/ENG-95244) (work package C)
**Spec**: [spec-descriptor-strict-keys.md](../prd/spec-descriptor-strict-keys.md)
**ADR**: [adr-descriptor-strict-keys.md](../adr/adr-descriptor-strict-keys.md)
**Created**: 2026-09-25

## Risk assessment

| Risk | Likelihood | Impact | Covered by |
|---|---|---|---|
| The checked-in key set drifts from the bundled contracts (a rebundle adds a member) → a valid key is refused | Medium | High — false refusal of a working call | TC-U-01, TC-U-02 |
| A key the server accepts is refused (walker descends into the wrong type, inheritance not flattened, list vs object confusion) | Medium | High | TC-U-03, TC-U-04, TC-U-12 |
| A typo still passes (walker skips a level: arrays of objects, nested blocks inside a modify operation) | Medium | High — the silent drop this fixes | TC-U-05, TC-U-06, TC-U-07 |
| A newer environment is refused | Low | High — blocks developer builds | TC-U-09, TC-U-10 |
| The check posts anyway on a refusal, or touches Creatio on a clean call | Low | Medium | TC-U-11, TC-U-13 |
| An existing descriptor in tests/E2E/guidance carries a key the server never accepted | Medium | — a real finding | TC-U-14 |

## Regression scope

- `clio.tests` Module `ProcessModel`, `Command`, `McpServer` — create/modify/as-new-version service tests,
  block read-back tests, tool tests.
- `clio.mcp.e2e` process-designer fixtures that send descriptors or operations (create, modify,
  as-new-version).

## Test cases

### Unit (clio.tests)

- **TC-U-01** The checked-in schema equals the schema regenerated from `Contracts/*.cs` inside the bundled
  archive (Roslyn, syntax only). Fails with the regeneration command on any difference.
- **TC-U-02** The generator reads what the drift test relies on: the two roots exist, `FilterDescriptor`
  carries its base type's keys, and a list-of-contract member resolves to `items.$ref`.
- **TC-U-03** The measured controls are accepted: `flows[].label`, `elements[].readData.sort`,
  `parameters`, `operations[].label` on `setFlow`.
- **TC-U-04** Every key of a representative full descriptor used by existing tests is accepted (no false
  refusal on the documented contract).
- **TC-U-05** The measured typos are reported with their JSON path: `flows[0].lable`,
  `elements[1].readData.sortt`, `parametres`, `operations[0].lable`.
- **TC-U-06** The measured wrong-case keys are reported: `Label`, `Sort` — with a "keys are case-sensitive"
  hint naming the exact key.
- **TC-U-07** A key inside a nested block of a modify operation (`operations[0].elementUpdate.email.subjct`,
  `operations[0].filter.conditions[0].colum`) is reported with the full path.
- **TC-U-08** Hint selection: case-only → that key; edit distance within the bound → the closest key; no
  near key → the valid keys at that level (bounded list).
- **TC-U-09** Guard: an unknown key on an environment at the bundled version → refusal naming every path;
  nothing posted.
- **TC-U-10** Guard: an unknown key on a NEWER environment → one warning per key, and the call proceeds;
  unreadable installed or bundled version → warning.
- **TC-U-11** Guard: a clean payload never reads the installed version.
- **TC-U-12** A JSON `null` value for a contract-typed key, a non-object value where an object is declared,
  and an array of strings are not descended into and produce no finding.
- **TC-U-13** Create, modify and as-new-version services refuse before the page-facts check and before the
  POST.
- **TC-U-14** Every descriptor and operations array in `clio.mcp.e2e` and the unit fixtures passes the
  key check (sweep over the source files' JSON literals is not practical; covered by running both suites
  green — a failure there is a finding, not a test to relax).
- **TC-U-15** The schema is a valid JSON Schema: JsonSchema.Net (test-only) evaluates a clean descriptor
  as valid and a descriptor with `flows[0].lable` as invalid.

### Integration / E2E (clio.mcp.e2e, live stand)

- **TC-I-01** `create-business-process` with `flows[0].lable` over the real MCP server is refused naming
  `flows[0].lable` and `label`; the same descriptor with `label` builds (the stand's CrtProcessBuilder is
  NEWER than the bundle → expect the warning path instead; the test asserts whichever the stand's version
  dictates, and reports which one ran).
- **TC-I-02** `modify-business-process` `setFlow` with `lable` is refused (or warned on a newer stand) naming
  `operations[0].lable`.
- **TC-I-03** `modify-business-process-as-new-version` with an unknown key in an operation is refused before
  any version is created.

## Exit criteria

All TC-U-* implemented and green under the targeted filter; TC-I-* run against the `Creatio` stand with
results recorded in the PR description.
