# Story 14: MCP surface + docs — passthrough edge, header contract, api-key gate, allowlist, arg policy

**Feature**: mcp-http-credential-passthrough
**FR coverage**: FR-15
**PRD**: [prd-mcp-http-credential-passthrough.md](../prd/prd-mcp-http-credential-passthrough.md)
**ADR**: [adr-mcp-http-credential-passthrough.md](../adr/adr-mcp-http-credential-passthrough.md) (step 14; FR-15)
**Jira**: [ENG-93208](https://creatio.atlassian.net/browse/ENG-93208)
**Status**: ready-for-dev
**Size**: M (half day)
**Depends on**: Story 1 (spike), Story 2 (spike), Story 10 (arg policy), Story 11 (incubation gate), Story 12 (final flag shape)

---

## As a

AI Platform gateway author / developer

## I want

the `mcp-http` docs and MCP surface updated to describe the passthrough edge, the header contract, the api-key gate, the allowlist, and the mode-gated arg policy

## So that

integrators can wire the gateway correctly and the documented contract matches shipped behavior (FR-15 mandatory review)

---

## Acceptance Criteria

- [ ] **AC-01** — Given the feature lands, when docs are inspected, then `help/en/mcp-http.txt`, `docs/commands/mcp-http.md`, `Commands.md`, and `docs/McpCapabilityMap.md` document: the `X-Integration-Credentials` header (base64 JSON, three shapes, precedence), `Authorization: Bearer <platform-api-key>` gate, `--allowed-base-urls`, and the mode-gated plaintext-arg policy (maps FR-15; AC-17).
- [ ] **AC-02** — Given the new flags, when docs are inspected, then `--platform-api-key`, `--allowed-base-urls`, `--session-idle-ttl`, `--max-sessions`, `--credentials-header-name` are all documented with defaults and the `CLIO_MCP_HTTP_PLATFORM_API_KEY` env var (maps FR-15; AC-17).
- [ ] **AC-03** — Given a behavior/rule changed, when the MCP surface is reviewed, then affected MCP tool/prompt/resource `[Description]` and the matching `GuidanceCatalog` guide (+ its trigger line / routing-map row if a guide is added/renamed) are updated or explicitly confirmed unchanged (maps FR-15; MCP maintenance policy).
- [ ] **AC-04** — Given the canonical verb name, when docs are named, then they use `mcp-http` (resolve any alias) and the ReadmeChecker/doc gates pass (maps FR-15).
- [ ] **AC-05** — Given the incubation flag, when documented, then docs note passthrough is gated behind the `mcp-http-credential-passthrough` feature flag AND the api-key gate, and that the verb/stdio/`-e <env>` are always available (maps OQ-03/FR-10).

## Implementation Notes

From ADR step 14 (FR-15) + repo doc/MCP maintenance policy:

- Docs: `clio/help/en/mcp-http.txt`, `clio/docs/commands/mcp-http.md`, `clio/Commands.md`, `docs/McpCapabilityMap.md`.
- MCP surface: review affected tool/prompt/resource `[Description]`; matching `GuidanceCatalog` guide + trigger line; routing-map row if a guide is added/renamed.
- Use the `document-command` skill (`$document-command`) for the command docs and `create-mcp-tool` review conventions where MCP `[Description]`s change.
- Document the DNS-rebinding TOCTOU residual (Story 6) and the "nothing-persisted, memory-pooled" terminology.
- If a doc/MCP artifact is accurate after review, explicitly state "reviewed, no update required".

Key files: `clio/help/en/mcp-http.txt`, `clio/docs/commands/mcp-http.md`, `clio/Commands.md`, `docs/McpCapabilityMap.md`, affected `clio/Command/McpServer/**` `[Description]`/guidance.
Pattern to follow: existing `mcp-http` help/docs; `Wiki/WikiAnchors.txt` if anchors change.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | ReadmeChecker / doc-presence gate for `mcp-http` new flags (if the repo's doc-checker asserts flag coverage) | existing doc-checker fixture |

Docs are largely a review deliverable; the doc-checker gate must stay green.
Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"` (+ any ReadmeChecker fixture)

## Definition of Done

- [ ] `help/en/mcp-http.txt`, `docs/commands/mcp-http.md`, `Commands.md`, `docs/McpCapabilityMap.md` all updated (header contract, gate, allowlist, arg policy, flags, env var, incubation note)
- [ ] MCP tool/prompt/resource `[Description]` + `GuidanceCatalog` guide reviewed; changes made or "reviewed, no update required" stated (FR-15)
- [ ] Canonical verb name `mcp-http` used; doc-checker/ReadmeChecker gate green
- [ ] No secret examples in docs (use placeholders)
- [ ] Any code touched: no new `CLIO*` warnings; kebab-case (CLIO001)
- [ ] Targeted `dotnet test --filter "Category=Unit&Module=McpServer"` green before commit
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-07-09
- Implementation completed: 2026-07-09
- Tests passing: yes
- Notes:
  - **Docs updated:**
    - `clio/help/en/mcp-http.txt` — added `--platform-api-key`, `--allowed-base-urls`,
      `--session-idle-ttl`, `--max-sessions`, `--credentials-header-name` with defaults; a
      CREDENTIAL PASSTHROUGH section (double gate, header contract, precedence, cookie-rejected-in-v1,
      nothing-persisted, arg policy, DNS-rebinding residual); `CLIO_MCP_HTTP_PLATFORM_API_KEY` env var
      and a passthrough example.
    - `clio/docs/commands/mcp-http.md` — new option rows; full "Credential passthrough (multi-tenant
      edge)" section: double gate (incubation flag + api-key), header shapes + precedence, cookie leg
      parses-but-rejected in v1 / non-Bearer rejected, SSRF baseline + allowlist scheme requirement +
      fail-fast, nothing-persisted/memory-pooled, mode-gated arg policy, DNS-rebinding TOCTOU residual;
      example + env var row.
    - `clio/Commands.md` — overview entry now mentions the passthrough edge.
    - `docs/McpCapabilityMap.md` — added "HTTP credential-passthrough edge" targeting mode note.
    - `clio/Wiki/WikiAnchors.txt` — reviewed, no update required (verb name `mcp-http` unchanged; anchor
      already present at line 110).
  - **MCP surface review (AC-03):** `mcp-http` is the HTTP HOST verb (`Command<TOptions>`, not a
    `BaseTool`). Grepped `Tools/`, `Prompts/`, `Resources/` for `mcp-http` / `passthrough` /
    `X-Integration` / `credential`: only internal implementation classes reference it
    (`ToolCommandResolver`, `TenantExecutionLockProvider`, `SessionContainerCache`,
    `McpToolExecutionLock`, `CredentialContext`) — no MCP tool/prompt/resource `[Description]` and no
    `GuidanceCatalog`/routing guide is tied to it. **MCP reviewed, no update required.**
  - **StandaloneFeatureKeys (Story-11 follow-up):** `ExperimentalCommand.StandaloneFeatureKeys` now
    contains `McpHttpServerCommand.CredentialPassthroughFeatureName`
    (`"mcp-http-credential-passthrough"`) so `clio experimental` lists the attribute-less incubation key
    with its live on/off state and `--enable/--disable` no longer warns it is unknown. Stale
    "currently empty / mcp-lazy-tools" comment rewritten. Added two unit tests to
    `ExperimentalCommandTests`: listing surfaces the key (`IsFeatureEnabled` received) and toggling it
    emits no unknown-key warning.
  - **Gates:** `dotnet build clio/clio.csproj -f net10.0` clean (no new CLIO*; the 2 CLIO005 warnings
    are pre-existing in `BindingsModule`). `dotnet test --filter "Category=Unit&(Module=McpServer|Module=Command)"`
    → 3819 passed / 0 failed. Doc-checker gate (`HelpArtifactConsistencyTests`, `Module=Core`) +
    `CommandHelpRendererTests` green. `ExperimentalCommandTests` 11/11.
  - **Note:** `CommandHelpRendererTests.RenderMarkdownDoc_WhenManualHelpContainsCustomSections_PreservesThemInOrder`
    showed a pre-existing cross-test ordering flake (hermetic mock-FS test, unrelated to these files);
    passes in isolation and on re-run.
