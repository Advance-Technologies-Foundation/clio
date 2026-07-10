# PRD — Durable MCP invocation (forgiving tool surface)

- **Status:** Draft (pending ADR)
- **Date:** 2026-07-10
- **Jira:** [ENG-93370](https://creatio.atlassian.net/browse/ENG-93370)
- **Author:** BMAD pm phase
- **Related:** [ADR — MCP lazy-schema tool surface](../adr/adr-mcp-lazy-schema.md) (PR #743, ENG-90312), [ADR — Durable MCP invocation](../adr/adr-mcp-durable-invocation.md)

## 1. Problem

AI coding agents work inside clio-generated workspaces guided by **static instruction
files that hardcode clio MCP tool names** — clio's own shipped template
`clio/tpl/workspace/AGENTS.md` (copied verbatim into every user/partner repo by
`clio createw`, frozen at tool-install time, no refresh path), plus external plugin
skills and partner repos. None of these copies can be updated retroactively.

PR #743 ("MCP lazy-schema tool surface") shrank the MCP `tools/list` to ~27
**resident** tools for a ~97% context saving. Every other ("long-tail") tool was
removed from `tools/list` and is now reachable only via the `clio-run` /
`clio-run-destructive` executors plus `get-tool-contract` discovery. As an
unintended consequence, the **resident-vs-long-tail split became a hard invocation
barrier**: an agent that follows `AGENTS.md` and calls a long-tail tool by name
(`get-fsm-mode`, `compile-creatio`, `push-workspace`, `restart-by-environment-name`,
`pkg-to-file-system`, `pkg-to-db` …) gets a soft, non-actionable "Unknown tool"
error — a dead end.

**Root cause (systemic):** clio guarantees backward compatibility at the **CLI-flag**
boundary (kebab-case + hidden aliases, analyzer CLIO001) but has **no equivalent
guarantee at the MCP-tool boundary**. Tool listing was legitimately reduced for
context economy, but *invocation* was silently reduced with it. Guidance authors are
left to forever chase the surface.

### Evidence (verified 2026-07-10)

- Fresh `clio createw` workspace: `tools/list` returns 27 resident tools; the six
  deploy/FSM tools named imperatively by `clio/tpl/workspace/AGENTS.md` are all
  absent from `tools/list`.
- A direct `tools/call get-fsm-mode` returns a soft `IsError` result
  ("MCP tool 'get-fsm-mode' failed: Unknown tool: 'get-fsm-mode'") with no
  did-you-mean, no discovery hint.
- `clio/tpl/**` is outside every mandatory documentation/MCP maintenance target and
  has zero test coverage, so this drift was uncaught by PR #743's (otherwise
  thorough) tool-surface tests.

## 2. Goal

Make the clio MCP server **forgiving** so that a call naming a real clio tool is not a
dead end: if the call reaches the server, the server transparently executes it via the
existing `clio-run` invocation path and returns the result **with an advisory note**
to prefer `clio-run` / resident tools going forward — while preserving the PR #743
context-economy win (small default `tools/list`) and the destructive-confirmation
invariant.

## 3. Non-goals (explicit)

- **Progressive disclosure / `tools/list_changed`.** No runtime mutation of the
  advertised tool set. The default `tools/list` stays exactly as PR #743 left it.
- **HTTP transport.** `clio mcp-http` is not yet released; only the stdio MCP server
  is in scope. Any stateless-HTTP notification concerns are therefore moot.
- **Jarvis / CLI-subprocess durability.** Jarvis `SKILL.md` files invoke shell
  `clio <verb>`, not MCP `tools/call`; an MCP-layer fix cannot repair them. A
  machine-readable CLI contract + CLI backward-compat policy is a **separate future
  task**, not this one.
- **Rewriting all downstream guidance.** We fix clio's own shipped template and add a
  drift guard; we do not attempt to update copies already distributed to partners.

## 4. Requirements

### Functional

- **FR-1 (resolve-by-name fallback).** When `tools/call` names a tool absent from the
  resident `tools/list`, the server MUST resolve the name against the full tool
  catalog (canonical + alias) using the same registry `clio-run` uses.
- **FR-2 (transparent execution, non-destructive).** A resolved **non-destructive**
  long-tail tool MUST be executed via the shared `clio-run` invocation path and its
  result returned, plus a lightweight `_meta` advisory note recommending
  `clio-run <canonical>` / resident tools next time.
- **FR-3 (destructive safeguard).** A resolved **destructive** tool MUST NOT be
  silently executed. The server MUST return a structured `confirmation-required`
  result carrying a ready-to-retry `clio-run-destructive` call shape. Destructiveness
  is determined by `IMcpToolInvokerRegistry.IsDestructive`, which fails **closed**
  (unknown ⇒ treated as destructive).
- **FR-4 (compatibility/alias catalog).** A single source of truth maps legacy /
  renamed / deprecated tool names to their canonical name (case-insensitive, emitted
  canonically). Collisions (duplicate canonical or alias) MUST fail startup and tests.
  An alias may execute only when its argument contract is identical or an explicit
  adapter is declared. Feature-disabled canonical targets remain unreachable.
- **FR-5 (actionable errors).** A name that does not resolve MUST yield a structured
  error with a stable machine-readable code (`unknown-tool`, `deprecated-tool-alias`,
  `cli-verb-not-mcp-tool`, `foreign-command`, `confirmation-required`,
  `feature-disabled`, `ambiguous-alias`), did-you-mean suggestions (Levenshtein), and
  a discovery hint (`get-guidance routing` / `get-tool-contract` / `clio-run`). Data
  mirrored in `StructuredContent`; concise text retained for older clients.
- **FR-6 (drift guard).** A unit test MUST scan clio's shipped
  `tpl/workspace/AGENTS.md` (+ `ui-project*/AGENTS.md`, `McpServerInstructions.Text`,
  enabled guidance articles) and assert every imperatively-named tool/verb/guide
  resolves (resident ∪ registry ∪ alias ∪ current `[Verb]` ∪ guide catalog ∪ explicit
  external allowlist).
- **FR-7 (template fix).** `clio/tpl/workspace/AGENTS.md` deploy/FSM section MUST
  delegate to the live channel (`get-guidance routing` / `get-tool-contract` /
  `clio-run`) instead of hardcoding long-tail tool names, and must pass FR-6.

### Non-functional

- **NFR-1.** Default `tools/list` unchanged (still 27; PR #743 budget tests stay
  green).
- **NFR-2.** No duplication of resolve/invoke logic: `clio-run` and the fallback
  handler share one executor and one registry (parity with the SDK `WithTools` scan
  preserved).
- **NFR-3.** All new/changed MCP behavior covered by unit + `clio.mcp.e2e` tests;
  docs + MCP surface reviewed per repo policy.

## 5. Acceptance criteria

- AC-1: Direct `tools/call get-fsm-mode` in a fresh workspace **executes** and returns
  the FSM mode plus the advisory note (FR-1/FR-2).
- AC-2: Direct `tools/call` of a destructive long-tail tool (e.g.
  `restart-by-environment-name`) returns `confirmation-required` with a retry shape
  and does **not** restart (FR-3).
- AC-3: A deprecated alias resolves to its canonical tool and executes (FR-4).
- AC-4: An unknown/foreign name returns a structured did-you-mean + discovery hint
  (FR-5).
- AC-5: `tools/list` still returns 27 resident tools; PR #743 budget/gating tests
  pass (NFR-1).
- AC-6: The drift test fails on the current template and passes on the fixed one
  (FR-6/FR-7); `clio/tpl/**` is added to maintenance targets.
- AC-7: Catalog collision test fails a duplicate canonical/alias at startup (FR-4).

## 6. Risks

- **Listed-only hosts.** FR-1/2 only trigger for calls that reach the server; hosts
  that emit `tools/call` solely for listed names still need the `clio-run` path — this
  is accepted (progressive disclosure deliberately out of scope), and reinforced by
  the FR-2 note + core-rules guidance.
- **Destructive bypass.** Mitigated by FR-3 (never silently execute destructive).
- **Parity drift.** Mitigated by NFR-2 (single registry/executor).

## 7. Out-of-scope follow-ups (tracked separately)

- Machine-readable CLI verb/alias contract + CLI backward-compat policy (for Jarvis
  and other CLI-subprocess consumers).
- A `clio createw --sync-instructions` refresh path for existing workspaces.
- Progressive disclosure spike (if ever revisited) with its own ADR.
