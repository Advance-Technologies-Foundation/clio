# ADR — MCP lazy-schema tool surface (reduce context impact, not just tool count)

- **Status:** Proposed
- **Date:** 2026-06-19
- **Jira:** ENG-90312 (same goal as PR #624; this ADR proposes an alternative design)
- **Related:** PR [#624](https://github.com/Advance-Technologies-Foundation/clio/pull/624) (draft), spike branch `spike/mcp-lazy-schema`

## Context

clio's MCP server registers **~124 tools unconditionally** (single seam
`McpFeatureToggleFilter.RegisterEnabledPrimitives`, `clio/BindingsModule.cs:632-655`).
The MCP protocol returns the full schema of every registered tool in `tools/list`,
and the client (claude / codex / copilot) sends that whole catalog to the model on
**every** request. There is no pagination, profile, or lazy loading.

**Measured cost** (direct `tools/list` over MCP stdio, spike build):

| Profile | tools | `tools/list` bytes | ~tokens |
|---|--:|--:|--:|
| FULL (current) | 124 | 226,640 | **~56,660** |
| CORE (2 tool types) | 9 | 7,137 | **~1,784 (−97%)** |

Heaviest single tools (bytes): `sync-schemas` 14.4k, `create-entity-business-rule`
12.5k, `update-page` 11.2k — three tools ≈ 38k bytes. Drivers: tool count, verbose
descriptions with inline instructions, `environment-name` description duplicated
**184×**, large JSON schemas (851 properties), 9.6k-char `ServerInstructions`.

When combined with the host's other MCP servers (ghe, atlassian, tmux, …) the
catalog reaches **~91–126k tokens** empirically and overflows small local models
(gpt-oss-20b in LM Studio failed before producing a token: *"number of tokens to
keep … is greater than the context length"*).

**PR #624 (ENG-90312)** consolidates 105→24 tools by hiding 52 non-read-only
commands behind one `clio-run` meta-tool whose args are a **52-branch `anyOf`**
JSON schema. This cuts the tool **count** (and lifts the 128-tool host limit) but
**keeps every schema body** inside the one `anyOf`, so the **token** saving is
modest. PR #624 explicitly chose to avoid a `describe-command` round-trip. For a
narrow local context window that trade-off is the wrong one: the token cost lives
in the schema bodies, not the tool-count wrapper.

## Decision

Adopt a **hybrid lazy-schema MCP surface**: keep a small, directly-callable core
flat in `tools/list`, and move the long tail behind an on-demand schema + a generic
executor, so **full schemas never sit in `tools/list`**.

1. **Core flat tools (~15–20).** The most-used, read-only tools stay registered
   flat with slimmed descriptions (e.g. `list-apps`, `get-app-info`, `find-app`,
   `list-pages`, `get-page`, `dataforge-find-tables`, …). Keeping them flat
   preserves per-tool `ReadOnly = true` host auto-approve and zero round-trip for
   the common path.
2. **`get-tool-contract(command)` = lazy schema.** Already exists
   (`ToolContractGetTool` + `ToolContractCatalog` + reflection fallback
   `McpToolSchemaCatalog`). Returns the full arg schema/contract of **one** command
   on demand. This is the schema store; it is not duplicated in `tools/list`.
3. **`clio-run(command, args)` = generic executor for the long tail.** `args` is a
   **free-form JSON object** (NOT a discriminated `anyOf`), validated at dispatch by
   deserializing into the target command's existing typed args via
   `System.Text.Json` and binding through the current command options. On invalid
   args it returns an error pointing the model at `get-tool-contract(command)`.
4. **Compact index.** A `list-clio-commands` (or reuse `get-guidance`) returns the
   command names + one-line summaries grouped by category — cheap, so the model can
   discover what exists without any full schema.
5. `tools/list` therefore carries only: core flat tools + `clio-run` +
   `get-tool-contract` + the index tool. The discover → describe → run pattern is
   documented in `ServerInstructions`.

Selection of core vs long-tail is driven through the existing registration seam
(`RegisterEnabledPrimitives` + `IFeatureToggleService`), not a parallel mechanism.

## Why this over PR #624

- The token cost is the **schema bodies in `tools/list`**. #624's `anyOf` keeps
  them; lazy schema removes them. The spike measured **−97%** on the core profile —
  a structural win the `anyOf` cannot reach.
- Reuses machinery that already exists: `get-tool-contract` is the lazy-schema store
  today; #624 already built the `clio-run` executor + DI dispatch targets, which we
  adapt to free-form `args` (drop the `anyOf`).
- Read-only core stays flat → host auto-approve and the common path keep working
  with no extra round-trip.

## Trade-offs (accepted)

- **+1 round-trip** for long-tail commands (`get-tool-contract` before `clio-run`) —
  exactly the trade-off #624 avoided; we accept it in exchange for the token win.
- **Single auto-approve surface** for `clio-run` (it covers non-read-only commands,
  which need confirmation anyway). Read-only core keeps per-tool flags.
- **Breaking MCP wire change** for long-tail invocation (now via `clio-run` +
  `command`); CLI verbs are unaffected.
- Weaker compile-time arg typing at the `clio-run` boundary (free-form object);
  mitigated by deserialize-and-bind validation reusing the command's own options.

## Alternatives considered

- **A. Description slimming only** (no protocol change): trim descriptions, dedup
  `environment-name`/creds, shrink `ServerInstructions` → ~14–16k saved, but
  clio-only still ~25–40k tokens. Insufficient alone; **kept as a complementary**
  low-risk first step (it also shrinks the core flat tools).
- **B. PR #624 `anyOf` consolidation:** modest token win (bodies retained).
- **C. Dynamic `tools/list_changed` profiles:** depends on client support for
  mid-session re-listing (varies across claude/codex/copilot) — compatibility risk.
- **Chosen: Hybrid (A's slimming on the core + lazy schema + generic executor).**

## Empirical basis (spike `spike/mcp-lazy-schema`)

- Profile gate added to `RegisterEnabledPrimitives` (env `CLIO_MCP_TOOL_TYPES`
  allowlists tool TYPES). Direct `tools/list` measurement: 124 tools/~56.7k tok →
  9 tools/~1.8k tok (**−97%**).
- **Orthogonal finding:** after the clio cut, an isolated gpt-oss-20b probe
  (`--strict-mcp-config`, clio-only) still overflowed — the residual envelope is
  **claude Code's own system prompt + 33 built-in tool schemas (~25–35k)** plus the
  model loaded with too small a context in LM Studio. That is independent of clio
  MCP and out of scope for this ADR (host/runtime config).

## Consequences

- Per-request context for clio drops from ~56.7k to a few k; far below the host's
  128-tool limit; viable for narrow-context local models when combined with host
  MCP isolation.
- Documentation/MCP maintenance: core vs long-tail split, `clio-run` contract,
  `get-tool-contract` as the canonical schema source must stay aligned (AGENTS.md
  MCP policy, `docs/McpCapabilityMap.md`).
- Tests: tool-budget ratchet (cf. #624's `McpToolBudgetTests`), `clio-run` dispatch
  + arg-binding unit tests, `get-tool-contract` coverage for every long-tail
  command, MCP e2e for the discover→describe→run flow.

## Open questions (for stories)

- Exact core-tool list and category taxonomy for the index.
- `clio-run` arg validation/error contract wording.
- Default profile: ship core-by-default vs opt-in (back-compat for existing MCP
  consumers that call flat tools today).
- Migration/aliasing so existing flat long-tail tool names degrade gracefully.
