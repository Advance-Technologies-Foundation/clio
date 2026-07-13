# ADR — MCP lazy-schema tool surface (reduce context impact, not just tool count)

- **Status:** Accepted (decisions resolved 2026-06-19; **pivoted to opt-in lazy mode** 2026-06-19 — see Resolved decisions #2). **2026-06-22: toggle removed — lazy is the only implementation (always on). The `mcp-lazy-tools` feature key and the full-catalog code path were deleted; `SelectToolTypes` / `RegisterEnabledPrimitives` always register the lazy surface. Rationale: an opt-in flag that defaulted off (and was silently reverted by a master merge) left CI on the full catalog and the surface in two states — one implementation removes that whole class of problem.**
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
12.5k, `update-page` 11.2k. Drivers: tool count, verbose descriptions with inline
instructions, `environment-name` description duplicated **184×**, large JSON schemas
(851 properties), 9.6k-char `ServerInstructions`. Combined with the host's other MCP
servers the catalog reaches **~91–126k tokens** empirically.

**PR #624 (ENG-90312)** consolidates 105→24 tools by hiding 52 non-read-only
commands behind one `clio-run` meta-tool whose args are a **52-branch `anyOf`** JSON
schema. This cuts the tool **count** (and lifts the 128-tool host limit) but **keeps
every schema body** inside the one `anyOf`, so the **token** saving is modest. The
token cost lives in the schema bodies, not the tool-count wrapper.

### What this revision corrects (adversarial review)

The first draft assumed machinery that does **not exist in this branch** and a
binding mechanism that does not hold up against the code. Corrected facts the design
now builds on:

- **`clio-run` is NOT present here.** It is part of PR #624's branch. This ADR
  therefore **builds the executor**, it does not "adapt an existing" one. See
  *Relationship to #624*.
- **`BaseTool<T>` dispatch is statically typed** with a hardcoded per-type `options
  switch` (`clio/Command/McpServer/Tools/BaseTool.cs:99-128`, throws
  `Unsupported options type`). A generic executor needs a new
  `command → optionsType → factory` resolution layer; it is not free.
- **CLI options use CommandLineParser attributes (`[Option]`, `[Value]`), not
  System.Text.Json.** Raw `JsonSerializer.Deserialize<TOptions>(args)` would ignore
  kebab `[Option]` names, silently drop typos, miss `Required=true`, and mishandle
  enums. Arg binding must be kebab/enum/required-aware (see *Dispatch & arg binding*).
- **The reflection fallback `McpToolSchemaCatalog` is lossy** (first param only,
  enum→"string", nested→"object", `required` only from `[Required]`):
  `McpToolSchemaCatalog.cs:91-178`. Only ~46 curated contracts
  (`CanonicalToolNames`) exist for 124 tools.
- **Feature flags have no env-var override** (`project-context.md`). The spike's
  `CLIO_MCP_TOOL_TYPES` env var is a throwaway measuring scaffold and must be
  replaced by `IFeatureToggleService` gating before any production use.
- **`McpToolBudgetTests` does not exist in this branch** (it is #624's). The budget
  ratchet must be built here.

## Decision

Adopt a **hybrid lazy-schema MCP surface**: keep a small, directly-callable core
flat in `tools/list`, and move the long tail behind an on-demand schema + a generic
executor, so **full schemas never sit in `tools/list`**.

1. **Core flat tools (~15–20).** Most-used, read-only tools stay registered flat with
   slimmed descriptions (`list-apps`, `get-app-info`, `find-app`, `list-pages`,
   `get-page`, `dataforge-find-tables`, …). Keeps per-tool `ReadOnly = true` host
   auto-approve and zero round-trip on the common path.
2. **`get-tool-contract(command)` = lazy schema.** Exists (`ToolContractGetTool` +
   `ToolContractCatalog` + fallback `McpToolSchemaCatalog`). Returns one command's
   full arg contract on demand. **Requirement:** every long-tail command MUST have a
   **curated** contract — the reflection fallback is not acceptable as the schema
   source for the long tail (it is lossy). Expanding curated contracts from ~46 to
   cover all long-tail commands is in scope.
3. **`clio-run(command, args)` = generic executor for the long tail.** `args` is a
   **free-form JSON object** (NOT a discriminated `anyOf`). A new dispatch layer maps
   `command → optionsType`, binds `args` with a **kebab/enum/required-aware**
   converter (reusing CommandLineParser semantics, e.g. re-pack to `argv` +
   `Parser.ParseArguments`, NOT raw STJ), resolves `Command<TOptions>` from the
   env-scoped container, and executes. Invalid/missing args return the **full
   contract inline** (not just an error), so a model that skipped `get-tool-contract`
   self-corrects in one round.
3. **Compact index** (`list-clio-commands` or reuse `get-guidance`): command names +
   one-line summaries by category, plus the critical anti-patterns/flow-hints that
   today live in heavy tool descriptions, so they are not lost from the core path.
4. `tools/list` carries only: core flat tools + `clio-run` + `get-tool-contract` +
   the index. The discover → describe → run pattern is documented in
   `ServerInstructions` **and** reinforced by the inline-contract-on-error behaviour
   (instructions alone are known to be ignored — see Risks).
5. **Gating uses `IFeatureToggleService`** through the existing
   `RegisterEnabledPrimitives` seam (flags in `appsettings.json features`) — NOT an
   env var. Core-vs-long-tail membership is config-driven.

## Dispatch & arg binding (the hard part, made explicit)

- **`command → optionsType` registry.** Built by reflecting `[Verb]` on options
  classes (the same source the CLI parser uses) into a lookup, or an explicit map.
  This replaces `BaseTool`'s hardcoded `options switch` with a general resolver.
- **Arg binding.** `args` (JSON object, kebab keys matching `[Option]` long names)
  is converted to the option model with CommandLineParser as the source of truth for
  names/aliases/`Required`/enum parsing — e.g. flatten to `--kebab value` argv and
  re-parse, surfacing parse errors verbatim. Unknown keys are an error (not silently
  dropped).
- **Execution path.** Reuse the env-scoped resolution `BaseTool` already performs;
  generalize `ResolveFromCallContainer` so it is not limited to the four hardcoded
  option types.
- **Output contract.** `clio-run` returns the same envelope shape flat tools return
  (`CommandExecutionResult` with execution-log messages), so 1 executor unifies 74
  command outputs predictably.

## Relationship to PR #624 (RESOLVED 2026-06-19)

#624 and this ADR share Jira ENG-90312 and both restructure the MCP surface in
**mutually incompatible** ways (anyOf consolidation vs lazy-schema + free-form
executor). **Decision (Alex):** #624 is **left as a fallback plan** — neither merged
into nor superseded right now. This ADR proceeds **independently**: the `clio-run`
executor is built from scratch here (it does NOT depend on #624's branch), and #624
is not coordinated against. If this design fails to pan out, #624 remains the backup.
Practical consequence: implementation must not assume any #624 artifact exists.

## Trade-offs (accepted)

- **+1 round-trip** for long-tail commands (mitigated by inline-contract-on-error).
- **Single executor surface** for non-read-only commands — see Security.
- **Breaking MCP wire change** for long-tail invocation; CLI verbs unaffected.
- Weaker compile-time typing at the `clio-run` boundary; mitigated by CommandLineParser-backed binding.

## Security consequence (not just a trade-off)

`clio-run` aggregates all non-read-only commands behind one tool. A host that
"always allows `clio-run`" thereby allows **every** destructive command
(`delete-entity-schema`, `application-delete`, …) without further prompts —
per-tool `Destructive=true` flags are no longer visible to the host. Mitigations to
evaluate in stories: (a) split `clio-run` (safe) vs `clio-run-destructive`; (b)
`clio-run` is never `ReadOnly`/auto-approve; (c) destructive commands still require
host confirmation via a distinct surface. Read-only operations stay in the flat core
precisely to keep their granular auto-approve.

## Risks

- **Model ignores the discover→describe→run pattern.** Passive instructions are a
  known failure mode in this project (ENG-91134: "пасивні інструкції ігноруються").
  Mitigation: inline-contract-on-error (first bad `clio-run` returns the full
  contract), few-shot in `clio-run` description, anti-patterns kept in the index.
- **Round-trip not yet validated on 3 hosts.** The spike measured only `tools/list`
  size, not whether claude/codex/copilot reliably read a schema from a tool *result*
  and compose the next `clio-run` call. Stories MUST include e2e on all three hosts
  (note: MCP e2e is not in CI — manual gate, per `project-context.md`).
- **Breaking consumers.** Existing integrations hardcode flat long-tail tool names
  (CAADT, creatio-adaclio-testing orchestrator, e2e). Inventory + deprecation aliases
  (flat name → internal proxy to `clio-run`) required before flipping any default.
- **Migration scale ×74.** Per AGENTS.md MCP+doc policy, every moved command touches
  tool/prompt/resource + `clio.tests` + `clio.mcp.e2e` + `help/en` + `docs/commands`
  + `Commands.md` + `McpCapabilityMap.md`, plus Prompts/Resources that reference tool
  names. A dedicated migration-inventory story is required; estimates without it are
  unrealistic.

## Success metric (reframed)

Primary: **clio stops being the dominant `tools/list` context consumer** and fits
well within the host 128-tool limit (clio `tools/list` ≤ ~5–8k tokens). It is NOT
"a small local model works end-to-end" — the spike showed that, after the clio cut,
gpt-oss-20b still overflowed on claude Code's own system prompt + 33 built-in tool
schemas (~25–35k) plus LM Studio's loaded context. That residual is **out of scope**
(host/runtime config), and the ADR must not be judged against it.

## Alternatives considered

- **A. Description slimming only** (no protocol change): ~14–16k saved, clio-only
  still ~25–40k. Insufficient alone; **adopted as the complementary first step** (it
  also shrinks the flat core).
- **B. PR #624 `anyOf`:** modest token win (bodies retained).
- **C. Dynamic `tools/list_changed` profiles:** client-support risk.
- **Chosen: Hybrid** (A's slimming on the core + lazy schema + generic executor).

## Empirical basis (spike `spike/mcp-lazy-schema`)

- Throwaway env-gate (`CLIO_MCP_TOOL_TYPES`) in `RegisterEnabledPrimitives`; direct
  `tools/list`: 124 tools/~56.7k tok → 9 tools/~1.8k tok (**−97%**). Validates the
  token-reduction mechanism; the production gate must use `IFeatureToggleService`.
- Isolated gpt-oss-20b probe (`--strict-mcp-config`, clio-only) still overflowed on
  the non-clio envelope (see Success metric).

## Resolved decisions (2026-06-19, Alex)

1. **#624** — left as a **fallback plan**; this ADR proceeds independently, `clio-run`
   built from scratch (see *Relationship to PR #624*).
2. **Default profile** — **OPT-IN lazy mode** (pivoted 2026-06-19). The default is
   the **full flat catalog, unchanged** — zero regression for every existing consumer
   (CAADT / adaclio / e2e / Claude Desktop). Context-constrained consumers (small
   local models, multi-server setups) **opt in** to lazy mode, which registers the
   core flat set + `clio-run`/`clio-run-destructive` + `get-tool-contract` and drops
   the long-tail flat schemas (the measured −97%). **Rationale:** the token-impact
   goal is delivered by the executor regardless of which mode is default; tying it to
   core-by-default forced a disproportionate breaking migration (×74 docs/tests + 76
   aliases + consumer breakage) that opt-in avoids entirely. **Consequence:** the
   deprecation aliases (Story 10) and the ×74 doc migration (Story 11) are **no longer
   required** — they become optional, only relevant if a future decision flips the
   global default to core. Story 9's inventory + alias map are kept for that option.
3. **`clio-run` split** — **YES**: `clio-run` (safe/non-destructive) and a separate
   `clio-run-destructive`. `clio-run` is never `ReadOnly`/auto-approve; destructive
   commands route only through the destructive surface (Story 8). (Already built,
   Story 4.)
4. **Feature key** — `mcp-lazy-tools`. OFF by default ⇒ **full flat catalog
   (unchanged)**; enabling it switches to lazy mode (core + executors + on-demand
   schema). This respects the project's opt-in / fail-closed feature-toggle contract:
   default-off = current behaviour, so nothing breaks until a consumer chooses lazy.

5. **Core/long-tail registration granularity** — **per-TYPE** (Option A). Lazy mode
   keeps whole core tool-type classes flat; a few long-tail tools living inside a
   core class staying flat is negligible against the −97% win, and it avoids custom
   per-method registration that would diverge from the SDK's `WithTools(types)` scan.

### Still open (during stories, non-blocking)

- Exact core tool-type list + index taxonomy (Story 7; provisional 20-tool core set
  in Story 9 inventory).
- Arg-binding converter approach: argv re-parse vs custom kebab-aware deserializer
  (Story 4 — resolved: argv re-parse via CommandLineParser).

## Consequences

- Per-request clio context drops from ~56.7k to a few k; below the 128-tool host
  limit; viable for narrow-context models when combined with host MCP isolation.
- Large doc/MCP/test migration (×74) — tracked as its own story.
- Tests: build a tool-budget ratchet from scratch; `clio-run` dispatch + arg-binding
  unit tests; curated-contract coverage for every long-tail command; manual MCP e2e
  for discover→describe→run on claude/codex/copilot.
