# Story 4: Documentation, compatibility, and no-regression closure

**Feature**: mcp-http-passthrough-runtime-routing
**Jira**: [ENG-93348](https://creatio.atlassian.net/browse/ENG-93348)
**FR coverage**: FR-07, FR-08, FR-09, FR-10, FR-11 · AC-08, AC-09, AC-10, AC-12
**PRD**: [prd-mcp-http-passthrough-runtime-routing.md](../prd/prd-mcp-http-passthrough-runtime-routing.md)
**ADR**: [adr-mcp-http-passthrough-runtime-routing.md](../adr/adr-mcp-http-passthrough-runtime-routing.md)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: Story 3

---

## As a

gateway integrator and clio maintainer

## I want

the public edge contract and compatibility record to match the implemented runtime requirement

## So that

producers send valid boolean payloads and existing stdio, HTTP, MCP, and Ring consumers are not surprised.

## Acceptance Criteria

- [ ] **AC-01 (PRD AC-12)** — `mcp-http` help and detailed docs show decoded JSON examples for `isNetCore: true` and `isNetCore: false`, define root versus `/0/` behavior, and state that missing, `null`, string, or numeric values return HTTP 400.
- [ ] **AC-02 (PRD AC-08, AC-12)** — docs retain the rule that credentials and runtime belong in the gated header, not MCP tool arguments, and do not suggest a default or auto-detection path.
- [ ] **AC-03** — `docs/McpCapabilityMap.md` describes the required edge contract and no-persistence/secret-hygiene boundaries consistently with the implementation.
- [ ] **AC-04 (PRD AC-10)** — stdio, default HTTP, and named/explicit environment no-regression evidence is recorded; their runtime continues to come from existing registered/options settings.
- [ ] **AC-05** — `clio/Commands.md` and `clio/Wiki/WikiAnchors.txt` are reviewed. If unchanged, the summary states `docs reviewed, no update required` for them.
- [ ] **AC-06** — tools, prompts, resources, guidance, arguments, descriptions, and destructive classifications are reviewed. If unchanged, the summary states `MCP reviewed, no update required`.
- [ ] **AC-07** — searches of `clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`, and `clio-ring/ClioRing.Desktop/actions.json` confirm no Ring consumer of this HTTP header contract, and the summary states `ClioRing compatibility reviewed, no Ring-consumed contract changed`.
- [ ] **AC-08** — final traceability shows every PRD FR and AC covered by an implemented story and test/doc evidence; the live Core operational prerequisite from Story 3 is not waived.
- [ ] **AC-09 (PRD AC-09)** — final no-write evidence confirms that neither runtime metadata nor ephemeral settings are persisted to clio settings, sessions, or other disk state.

## Implementation Notes

Use the `document-command` skill for:

- `clio/help/en/mcp-http.txt`;
- `clio/docs/commands/mcp-http.md`;
- `docs/McpCapabilityMap.md`.

Review `clio/Commands.md` and `clio/Wiki/WikiAnchors.txt`. Use `create-mcp-tool` and `test-mcp-tool` only if the required review finds an MCP contract/test mismatch; the ADR expects none. Do not add `isNetCore` to an MCP tool schema.

## Test and Review Requirements

- Validate documented base64-decoded examples are valid JSON booleans and match the actual parser.
- Run the affected help/doc checks and targeted `McpServer` tests when documentation assertions depend on executable behavior.
- Re-run or cite Story 3's stdio/default/named HTTP no-regression evidence and manual E2E result.
- Complete the repository's comprehensive pre-PR review gate before any PR is opened; resolve every Blocker/High finding.

## Definition of Done

- [ ] Documentation and capability map match the shipped contract.
- [ ] Required docs/MCP/ClioRing review statements are recorded exactly.
- [ ] All PRD FRs and ACs have traceable evidence; no unresolved live-E2E prerequisite remains.
- [ ] `git diff --check` passes and relevant targeted tests are green.
- [ ] Comprehensive code-quality, performance/correctness, and security review finds no unresolved Blocker/High issue.

## Dev Agent Record

- Implementation started:
- Implementation completed:
- Tests/checks passing:
- Review tier and findings:
- Notes:
