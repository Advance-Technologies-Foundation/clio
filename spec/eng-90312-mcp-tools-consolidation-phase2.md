# ENG-90312 Phase 2 — Pivot to single meta-tool dispatcher

Jira: [ENG-90312](https://creatio.atlassian.net/browse/ENG-90312)
Branch: `xenodochial-moser-229844` (Phase 1 PR [#624](https://github.com/Advance-Technologies-Foundation/clio/pull/624), pivot in-place)
Status: planning — pending approval
Phase 1 spec: [eng-90312-mcp-tools-consolidation.md](eng-90312-mcp-tools-consolidation.md)

## 1. Goal

Complete the consolidation of clio's MCP tool registry by collapsing every non-read-only tool behind a single dispatcher `clio-run`. Final surface: **24 tools** (23 read-only flat + 1 `clio-run` discriminated-union meta-tool), down from 75 at end of Phase 1 and 105 at baseline. Safety flags (`ReadOnly` / `Destructive`) remain semantically correct for every call. JSON Schema discovery in `tools/list` stays one round trip via `[JsonPolymorphic]` / `[JsonDerivedType]` — no `describe-command` indirection.

## 2. Why pivot (not follow-up)

Phase 1 PR #624 is still open. It introduces new top-level MCP tool names (`restart-creatio`, `clear-redis-db`, `restore-db`, `create-schema`, `app-section`, etc.) that disappear again under `clio-run` in Phase 2. Shipping Phase 1 as-is would force AI clients through **two breaking MCP wire changes inside one release cycle**. Pivoting Phase 1 in place — adding the Block Z series on top of the existing Blocks A–H — collapses both phases into a single user-visible breaking change while preserving every piece of infrastructure already built in Phase 1.

Reused from Phase 1 verbatim:
- [`CommandExecutionResult.ValidateExactlyOneMode`](../clio/Command/McpServer/Tools/CommandExecutionResult.cs:41) + `ValidateRequiredForMode` + `ValidateEnvOrCredentialsMode` helpers.
- Every per-command args record (`RestartCreatioArgs`, `RestoreDbArgs`, `SchemaCreateArgs`, `AppSectionArgs`, `LinkFromRepositoryArgs`, etc.) with its kebab-case `JsonPropertyName` and `[Description]` annotations.
- Every consolidated tool class (`RestartTool`, `ClearRedisTool`, `RestoreDbTool`, `SchemaCreateTool`, `AppSectionTool`, …) keeps its public dispatch method. Only the `[McpServerTool]` attribute is removed; the class becomes an internal dispatch target for `clio-run`.
- The reflection-based budget ratchet at [`McpToolBudgetTests`](../clio.tests/Command/McpServer/McpToolBudgetTests.cs).
- The breaking-changes section in [RELEASE.md](../RELEASE.md) — table is rewritten to reflect the single 105 → 24 hop.

## 3. Architectural decisions (locked)

### 3.1 Discriminated union over `args.command`
`clio-run` takes a `[JsonPolymorphic(TypeDiscriminatorPropertyName = "command")]` base record with one `[JsonDerivedType]` entry per inner command. The MCP SDK 1.3.0 publishes this as JSON Schema `anyOf` in `tools/list` (verified against existing usage in [`BusinessRuleAction`](../clio/Command/BusinessRules/BusinessRuleModels.cs:130) — see [`EntityBusinessRuleToolE2ETests.cs:50`](../clio.mcp.e2e/EntityBusinessRuleToolE2ETests.cs:50) for the `anyOf.GetArrayLength().Should().Be(6)` precedent). LLM consumers get the full per-command schema on the first `tools/list` call — no extra `describe-command` round trip is required.

### 3.2 Single meta-tool, no domain split
One `clio-run` tool absorbs all 52 non-read-only commands. Reason: a domain split (`clio-env`, `clio-schema`, `clio-app`, …) would yield ~30 tools instead of 24, and every group still has to be marked `Destructive=true` because each contains at least one destructive command — domain split costs slot budget without recovering per-command safety flags. The slot saving from single meta is what makes this phase worth doing.

### 3.3 Read-only stays flat
The 23 tools with `ReadOnly = true` keep their top-level `[McpServerTool]` registration. Host UX (Claude Desktop / Cursor / Claude Code) auto-approves or applies a lighter confirmation flow for read-only calls. Hiding them under `clio-run` (which must be `Destructive = true`) would force confirmation prompts on every `list-environments`, `get-schema`, `dataforge-find`, etc. — a hard UX regression. The safety-flag heterogeneity inside the destructive bucket (Destructive=true vs mutation-non-destructive) is acceptable because the host already gates the entire `clio-run` call as destructive.

### 3.4 Dispatcher pattern: explicit switch over reflection
Inside `clio-run.Apply`, dispatch by `args.GetType()` (the polymorphic deserializer already produces the right derived type) via an explicit `switch` expression. Reasoning: 52 inner commands is enough that a hand-written switch is still readable and IDE-navigable (`Go to references` resolves each adapter call), while reflection-based dispatch via `[InnerCommand("name")]` adds an attribute, a registry, and lookup failure paths for no real win at this scale. The dispatch method stays under 200 lines. Compile-time enforcement of exhaustiveness is best-effort (C# only emits CS8509 for switches over sealed hierarchies, and only as a warning); the load-bearing safety net is the reflection fixture test described in §8 that asserts every `[JsonDerivedType]` has a matching switch arm.

### 3.5 No backward-compatibility aliases
Phase 1's "deprecation = remove, no aliases" rule extends to Phase 2. The 52 tool names introduced or kept in Phase 1 disappear from `tools/list` in the same PR. AI clients migrate to `clio-run` in one cut. CLI verbs (`[Verb]` on Options classes) are unaffected, which preserves the bash-side migration path.

### 3.6 Pre-implementation gates (both block Z1)

Two preconditions must be satisfied before any Z1 work starts. If either fails, the design needs to change *before* code lands, not after.

**Gate A — anyOf schema sanity at 52 branches.** The only empirical precedent for `[JsonDerivedType]` in clio is the 6-branch `BusinessRuleAction`. Scaling to 52 is an 8× jump in published schema size and parser depth, and the consumers (Claude Desktop / Cursor / Claude Code) are out of our control. Block Z0 (§6) prototypes the worst-case schema and smoke-tests it against the three hosts before we commit. If any host truncates, refuses, or mis-renders the schema, the fallback is a domain split (the variant the user rejected for slot economy reasons in §3.2) — but that decision must be made on data, not after the full implementation lands.

**Gate B — DataForge state lifetime.** §3.3 keeps the 5 DataForge read-only tools flat while routing `dataforge-initialize` / `dataforge-update` through `clio-run`. The split is only safe if DataForge state is a per-MCP-server-process singleton — i.e., the `dataforge-context` read and the `dataforge-initialize` write share the same instance. If state is per-tool-instance or per-call (e.g., transient DI lifetime), `dataforge-context` flat will read a fresh empty state after `dataforge-initialize` runs under `clio-run`. This is a design blocker, not a verification step: confirm in [`BindingsModule.cs`](../clio/BindingsModule.cs) at Z0, and if the lifetime is wrong, choose one before Z1 — either (a) lift DataForge state to singleton, (b) move all DataForge tools (including reads) under `clio-run`, or (c) keep the entire DataForge family flat.

## 4. Tool inventory (final surface = 24)

### 4.1 Tools staying flat (23 read-only)
All publish `ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false`. The host can auto-approve these.

| Tool | File |
|---|---|
| `apps` | [`AppsTool.cs`](../clio/Command/McpServer/Tools/AppsTool.cs) |
| `assert-infrastructure` | [`AssertInfrastructureTool.cs`](../clio/Command/McpServer/Tools/AssertInfrastructureTool.cs) |
| `check-settings-health` | [`SettingsHealthTool.cs`](../clio/Command/McpServer/Tools/SettingsHealthTool.cs) |
| `dataforge-context` | [`DataForgeTool.cs`](../clio/Command/McpServer/Tools/DataForgeTool.cs) |
| `dataforge-find` | `DataForgeTool.cs` |
| `dataforge-get-relations` | `DataForgeTool.cs` |
| `dataforge-get-table-columns` | `DataForgeTool.cs` |
| `dataforge-status` | `DataForgeTool.cs` |
| `find-empty-iis-port` | [`FindEmptyIisPortTool.cs`](../clio/Command/McpServer/Tools/FindEmptyIisPortTool.cs) |
| `get-component-info` | [`ComponentInfoTool.cs`](../clio/Command/McpServer/Tools/ComponentInfoTool.cs) |
| `get-fsm-mode` | [`FsmModeTool.cs`](../clio/Command/McpServer/Tools/FsmModeTool.cs) |
| `get-guidance` | [`GuidanceGetTool.cs`](../clio/Command/McpServer/Tools/GuidanceGetTool.cs) |
| `get-schema` | [`GetSchemaTool.cs`](../clio/Command/McpServer/Tools/GetSchemaTool.cs) |
| `get-schema-name-prefix` | [`SchemaNamePrefixTool.cs`](../clio/Command/McpServer/Tools/SchemaNamePrefixTool.cs) |
| `get-tool-contract` | [`ToolContractGetTool.cs`](../clio/Command/McpServer/Tools/ToolContractGetTool.cs) |
| `list-environments` | [`ShowWebAppListTool.cs`](../clio/Command/McpServer/Tools/ShowWebAppListTool.cs) |
| `list-packages` | [`GetPkgListTool.cs`](../clio/Command/McpServer/Tools/GetPkgListTool.cs) |
| `list-page-templates` | [`PageTemplatesListTool.cs`](../clio/Command/McpServer/Tools/PageTemplatesListTool.cs) |
| `list-pages` | [`PageListTool.cs`](../clio/Command/McpServer/Tools/PageListTool.cs) |
| `list-schemas` | [`SchemaListTool.cs`](../clio/Command/McpServer/Tools/SchemaListTool.cs) |
| `show-passing-infrastructure` | [`ShowPassingInfrastructureTool.cs`](../clio/Command/McpServer/Tools/ShowPassingInfrastructureTool.cs) |
| `sys-setting` | [`SysSettingsTool.cs`](../clio/Command/McpServer/Tools/SysSettingsTool.cs) — consolidated read of one setting (`code`) or list (`code` omitted) |
| `validate-page` | [`PageValidateTool.cs`](../clio/Command/McpServer/Tools/PageValidateTool.cs) |

### 4.2 Tools folded into `clio-run` (52 = 40 destructive + 12 mutation-non-destructive)

All 52 are exposed as `[JsonDerivedType]` variants of a `ClioRunArgs` base record. Each variant carries its existing per-command args fields verbatim. The 75 → 23 + 52 split was produced by running [`McpToolBudgetTests.DiscoverMcpToolNames`](../clio.tests/Command/McpServer/McpToolBudgetTests.cs:73) with a temporary flag-dump (the dump-instrumented test was reverted after the audit).

#### 4.2.1 Destructive (`Destructive = true`) — 40

Environment lifecycle (10): `restart-creatio`, `stop-creatio`, `stop-all-creatio`, `uninstall-creatio`, `clear-redis-db`, `restore-db`, `deploy-creatio`, `compile-creatio`, `install-application`, `delete-app`.

Schema lifecycle (6): `create-schema`, `update-schema`, `delete-schema`, `install-sql-schema`, `sync-schemas`, `modify-entity-schema-column`.

Page lifecycle (3): `create-page`, `update-page`, `sync-pages`.

Package & workspace (4): `push-workspace`, `restore-workspace`, `link-from-repository`, `pkg-mode`.

Application surface (8): `app-section`, `create-app`, `create-entity-business-rule`, `create-page-business-rule`, `create-data-binding`, `create-data-binding-db`, `data-binding-row`, `data-binding-row-db`.

DataForge mutating (2): `dataforge-initialize`, `dataforge-update`.

Skills (2): `delete-skill`, `update-skill`.

System-settings mutating (1): `upsert-sys-setting`.

Misc destructive (4): `add-item-model`, `generate-process-model`, `modify-user-task-parameters`, `set-fsm-mode`.

#### 4.2.2 Mutation, non-destructive (`ReadOnly = false, Destructive = false`) — 12

`add-package`, `create-user-task`, `create-workspace`, `download-configuration`, `finish-hotfix`, `generate-source-code`, `get-page`, `install-skills`, `new-test-project`, `reg-web-app`, `start-creatio`, `unlock-for-hotfix`.

Reconciliation: 40 + 12 = 52 derived types, plus the 23 read-only flat tools (§4.1) = **75 total commands** before consolidation, exactly matching the Phase-1 ratchet.

### 4.3 `clio-run` safety flags
```
[McpServerTool(Name = "clio-run", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
```
Destructive=true because the union includes destructive variants. The host will treat every `clio-run` invocation as destructive, which is the correct conservative default.

## 5. `clio-run` design

### 5.1 Wire shape
```jsonc
// MCP request
{
  "tool": "clio-run",
  "args": {
    "command": "restart-creatio",        // discriminator
    "mode": "environment",                // per-command field
    "environment-name": "dev"             // per-command field
  }
}
```
The discriminator lives on the *outer* args object — same level as the per-command fields. This is the shape `[JsonPolymorphic(TypeDiscriminatorPropertyName = "command")]` produces. Each derived record names its own fields; the host LLM sees the full `anyOf` schema in `tools/list`.

### 5.2 Base record + derived records
```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "command")]
[JsonDerivedType(typeof(RestartCreatioRunArgs), "restart-creatio")]
[JsonDerivedType(typeof(ClearRedisDbRunArgs),  "clear-redis-db")]
[JsonDerivedType(typeof(RestoreDbRunArgs),     "restore-db")]
// … one line per inner command, 52 entries total
public abstract record ClioRunArgs;

public sealed record RestartCreatioRunArgs : ClioRunArgs {
    // Identical to the existing RestartCreatioArgs from Phase 1.
    // Either inherit-then-extend the existing record, or rename it.
    // Decision: rename Phase 1 records by appending "RunArgs" suffix so they live as polymorphic variants directly.
}
```
Naming policy: every Phase-1 `XxxArgs` record that backs a non-read-only tool is renamed to `XxxRunArgs` and made `sealed record : ClioRunArgs`. This avoids two parallel record hierarchies. Records backing read-only tools (e.g., `SchemaListArgs`, `AppsArgs`) keep their current name.

### 5.3 Dispatcher method
```csharp
[McpServerToolType]
public sealed class ClioRunTool {
    // adapters resolved via DI (the existing Phase-1 tool classes)
    public ClioRunTool(RestartTool restart, ClearRedisTool clearRedis, /* … */) { /* … */ }

    [McpServerTool(Name = "clio-run", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Executes a clio MCP command. Use 'command' to select the operation; remaining args are command-specific (see anyOf branches). For read-only operations, prefer the dedicated tools (list-environments, get-schema, …) which the host can auto-approve.")]
    public async Task<object> Apply(
        [Required] ClioRunArgs args,
        global::ModelContextProtocol.Server.McpServer server,
        CancellationToken cancellationToken = default
    ) => args switch {
        RestartCreatioRunArgs a   => restart.Restart(a),
        ClearRedisDbRunArgs a     => clearRedis.Apply(a),
        RestoreDbRunArgs a        => restoreDb.Restore(a),
        // …
        SchemaCreateRunArgs a     => await schemaCreate.Create(a),
        AppSectionRunArgs a       => await appSection.Apply(a, server, cancellationToken),
        // …
        _ => CommandExecutionResult.FromError(
            $"clio-run: unknown command. JsonPolymorphic should have rejected this before dispatch.")
    };
}
```
- The compiler emits CS8509 (warning) if a sealed-hierarchy switch is non-exhaustive. To make this load-bearing rather than informational, Block Z1 promotes CS8509 to an error in [`clio/clio.csproj`](../clio/clio.csproj) via `<WarningsAsErrors>CS8509</WarningsAsErrors>` (scoped to the single diagnostic rather than enabling `<TreatWarningsAsErrors>` globally, which would force a larger warning-cleanup pass). The reflection fixture test in §8 remains the authoritative guard, but the elevated CS8509 catches the common mistake (adding a `[JsonDerivedType]` and forgetting the switch arm) at compile time instead of at test time.
- `IMcpServer` and `CancellationToken` are injected by the SDK on the outer call and threaded into adapters that need them (e.g., `AppSectionTool.Apply` which already takes both).
- The `_ =>` arm is dead code in practice (`JsonPolymorphic` deserializer rejects unknown discriminators with a wire error before reaching the switch); it stays for compile completeness.

### 5.4 Error model
- Unknown `command`: `System.Text.Json` polymorphic deserializer raises a deserialization error, surfaced by the SDK as an MCP error response — no clio-side validation needed.
- Missing required per-command field: existing Phase-1 validators (`ValidateExactlyOneMode`, `ValidateRequiredForMode`, `ValidateCredentials`, `ValidateEnvOrCredentialsMode`) run inside the adapter as before. Behaviour is unchanged from Phase 1.
- Adapter exceptions: surface via `CommandExecutionResult.FromException` (existing helper).

### 5.5 Schema-discovery test
A new test in [`McpToolBudgetTests`](../clio.tests/Command/McpServer/McpToolBudgetTests.cs) or a sibling fixture loads the `clio-run` tool's published schema (via the SDK's `McpServerTool` reflection helpers, mirroring `EntityBusinessRuleToolE2ETests.cs:50`) and asserts:
- `args` has property `anyOf` with the expected branch count (52).
- Every branch has a `const` for its `command` discriminator value.
- A canary set of well-known fields (`environment-name`, `mode`, `schema-type`, `action`) appears in at least one branch with the expected description.

## 6. Implementation order (Block Z series, on top of existing Phase 1 commits)

| Block | Title | Effect on count |
|---|---|---|
| **Z0** | **Pre-impl gates (§3.6).** Build a throwaway prototype tool that publishes a 52-branch `anyOf` schema with representative field shapes per branch. Capture the schema payload (JSON Schema and serialized byte size). Smoke-test against the three target hosts (Claude Desktop, Cursor, Claude Code) — load `clio mcp-server`, invoke `tools/list`, invoke one branch end-to-end. Confirm DataForge DI lifetime in [`BindingsModule.cs`](../clio/BindingsModule.cs) per Gate B in §3.6. **Block Z1 only starts if both gates pass.** Deliverable: a short Z0 report committed to the PR description (host behaviour matrix + DataForge lifetime determination). | — |
| Z1 | Introduce `ClioRunArgs` base + `[JsonDerivedType]` registrations + `ClioRunTool` skeleton with empty switch; promote CS8509 to error in [`clio/clio.csproj`](../clio/clio.csproj) via `<WarningsAsErrors>CS8509</WarningsAsErrors>`; budget test stays at 75 | 75 → 75 |
| Z2 | Rename 52 Phase-1 `*Args` records to `*RunArgs : ClioRunArgs`, fill switch arms, wire adapters through DI; legacy `[McpServerTool]` attributes stay temporarily | 75 → 76 (transient: `clio-run` added, none removed yet) |
| Z3 | Strip `[McpServerTool]` from the 52 inner tool methods; lower ratchet to 24; update `McpToolBudgetTests.ToolBudget = 24` | 76 → 24 |
| Z4 | Rewrite e2e `clio.mcp.e2e/*ToolE2ETests.cs` for the 52 destructive flows to call `clio-run` with the new envelope; verify the 23 read-only e2e flows are unaffected | — |
| Z5 | Update [AGENTS.md](../clio/Command/McpServer/AGENTS.md) — new ratchet (24), updated "extend before add" rule ("destructive/mutation tools extend `clio-run` via a new `[JsonDerivedType]`; read-only tools may stay flat if `ReadOnly = true` is correctly set"). Rewrite [RELEASE.md](../RELEASE.md) MCP migration table from 105 → 24 (single hop) | — |
| Z6 | Drop the now-redundant Phase-1 RELEASE migration table content that referred to 75 as the final state | — |
| Z7 | Schema-discovery test (§5.5) | — |

Block Z2 is the only block where the ratchet temporarily moves up by 1 — this is acceptable because Z2 + Z3 land as a unit (atomic squashed commit during review if needed).

### 6.1 Z0 exit criteria (gate pass / fail)

| Check | Pass criterion | If it fails |
|---|---|---|
| Schema payload size | `tools/list` response stays under each host's documented or empirically-observed cap (Claude Desktop ~1 MB combined, Cursor ~256 KB per tool — verify at Z0 time) | Switch to domain split (`clio-env`, `clio-schema`, …); §3.2's slot-economy argument no longer holds — re-open architecture review before re-attempting Phase 2. |
| `anyOf` rendering | Each of the 3 hosts shows all 52 branches in its tool inspector and lets an LLM call any of them with the correct field schema | Same fallback: domain split, or stay at Phase 1 (75 tools). |
| DataForge state lifetime | `[`BindingsModule.cs`](../clio/BindingsModule.cs) registers the DataForge state holder as a singleton scoped to the MCP server process | Apply one of the three remediations in §3.6 Gate B before Z1. |
| `BusinessRuleAction` parity check | The 6-branch precedent still works correctly against all 3 hosts (sanity baseline — if it regressed, our measurement is contaminated by an unrelated host bug) | Pause Z0; investigate the host regression before proceeding. |

## 7. Files touched (delta over Phase 1 PR #624)

### New files
- `clio/Command/McpServer/Tools/ClioRunTool.cs` — the dispatcher.
- `clio/Command/McpServer/Tools/ClioRunArgs.cs` — abstract base + `[JsonDerivedType]` registry.

### Modified production files
- Every per-command args record in Phase 1: rename to `*RunArgs` and reparent to `ClioRunArgs`. Files affected: roughly every file under [`clio/Command/McpServer/Tools/`](../clio/Command/McpServer/Tools/) that defines a non-read-only args record (~25 files).
- Every Phase-1 tool class with `[McpServerTool]` on a non-read-only method: remove the `[McpServerTool]` attribute, keep the method as an internal dispatcher target. Class-level `[McpServerToolType]` stays only if the class still has at least one read-only `[McpServerTool]` method (e.g., `DataForgeTool` keeps it for `dataforge-context` / `dataforge-find` / `dataforge-get-*` / `dataforge-status`).
- [`clio/BindingsModule.cs`](../clio/BindingsModule.cs) — DI registration of `ClioRunTool`.

### Modified documentation
- [`clio/Command/McpServer/AGENTS.md`](../clio/Command/McpServer/AGENTS.md) — ratchet 75 → 24, updated extend-before-add policy.
- [`RELEASE.md`](../RELEASE.md) — replace the 105 → 75 migration table with a 105 → 24 table.

### Modified tests
- [`clio.tests/Command/McpServer/McpToolBudgetTests.cs`](../clio.tests/Command/McpServer/McpToolBudgetTests.cs) — `ToolBudget = 24`.
- New schema-discovery test (§5.5) in the same fixture.
- Existing unit tests for the 52 inner tool classes keep their assertion shape but the e2e wire envelopes update (see Z4).
- [`clio.mcp.e2e/*ToolE2ETests.cs`](../clio.mcp.e2e/) — every test that today calls a destructive tool by name updates to `clio-run` with the new envelope.

### Out of scope (carries over from Phase 1)
- DataForge initialize/update flows (mentioned, but mutating variants still get folded — this is consistent with Phase 1 §6).
- Hotfix and skills feature areas (their tools are folded into `clio-run` like everything else, no behaviour change beyond wire envelope).
- CLI verbs themselves.
- MCP resources and prompts.

## 8. Risks and mitigations

| Risk | Mitigation |
|---|---|
| MCP SDK 1.3.0 publishes the polymorphic schema in a form the major hosts (Claude Desktop, Cursor, Claude Code) don't parse correctly, breaking tool discovery | Verified in Phase 2 pre-spec investigation: existing `BusinessRuleAction` polymorphism (6 branches) is consumed correctly by Claude Code today. Add an explicit assertion in Z7 schema-discovery test so a future SDK bump that regresses this surfaces immediately. |
| 52-branch `anyOf` produces a huge schema payload that some hosts truncate or skip | Worst-case mitigation: domain split (the variant the user rejected) becomes the escape hatch. Tracked under Phase 3, not implemented in Phase 2. |
| AI clients integrated against Phase-1 tool names (`restart-creatio`, `clear-redis-db`, `restore-db`, etc.) break a second time after this phase ships | Phase 1 PR is not merged. We are pivoting before any production release. Migration table in RELEASE.md goes directly from baseline (105 tools) to Phase 2 (24 tools); intermediate Phase 1 surface never reaches end users. |
| Renaming 52 records breaks downstream consumers inside clio | The records are public types in `Clio.Command.McpServer.Tools`. Other clio code rarely instantiates them directly (they exist to model the MCP wire). A pre-rename grep for `XxxArgs(` constructor calls and `new XxxArgs` enumerates the call sites; expected to be unit tests and a small number of internal helpers. Run before Z2. |
| Exhaustiveness of the `switch` regresses silently if a new `[JsonDerivedType]` is added without a switch arm | C# compiler emits CS8509 for non-exhaustive switch over a sealed hierarchy when `<TreatWarningsAsErrors>` is on. Verify the project flag; if not on, add a small fixture test that walks `ClioRunArgs.GetCustomAttribute<JsonPolymorphic>` and asserts every derived type has a matching switch arm via reflection on `ClioRunTool.Apply`. |
| `IMcpServer` / `CancellationToken` injection plumbing leaks across adapters | The SDK already supports these as method-level injected parameters. The outer `clio-run.Apply` takes them and the switch passes them only to adapters that already accept them in Phase 1 (e.g., `AppSectionTool.Apply`). No new injection mechanism is introduced. |
| 12 mutation-non-destructive commands get treated as destructive by the host | Today `add-package`, `download-configuration`, `start-creatio`, `create-user-task`, `create-workspace`, `finish-hotfix`, `generate-source-code`, `get-page`, `install-skills`, `new-test-project`, `reg-web-app`, `unlock-for-hotfix` are `Destructive=false`. Bundled under `clio-run` (`Destructive=true`), they get the same confirmation flow as `restart-creatio` / `restore-db`. This is the structural cost of single-meta accepted in §3.3 and is documented here for review-time visibility. The 23 read-only tools stay flat precisely to keep this regression bounded to the mutation surface. |

## 9. Open questions (resolve during Z1)

1. **Read-only audit on stateful tools.** `dataforge-context` and the other DataForge read-only tools share in-process state with `dataforge-initialize` / `dataforge-update` (which move under `clio-run`). Confirm the shared state lives in a per-MCP-server-process DI singleton — if it's per-tool-instance or per-call, the split is unsafe. Verify in [`BindingsModule.cs`](../clio/BindingsModule.cs) at Z1 start.
2. **Migration ergonomics for human callers.** Anyone today driving `clio mcp-server` from a script (not via an LLM host) writes JSON for the consolidated tool name + args. Phase 2 forces them to wrap that JSON in a `clio-run` envelope. The RELEASE.md table needs a "before / after" example for at least 3 commands so the wrap-in-envelope mechanic is obvious. Decide which 3 commands at Z5 time.

## 10. Definition of done

- `McpToolBudgetTests` passes with `ToolBudget = 24`.
- New schema-discovery test in §5.5 passes against the registered `clio-run` tool.
- All `clio.tests` and `clio.mcp.e2e` build and pass.
- [`clio/Command/McpServer/AGENTS.md`](../clio/Command/McpServer/AGENTS.md) has the updated budget policy (24 ratchet, single-meta-tool policy).
- [`RELEASE.md`](../RELEASE.md) has a single 105 → 24 migration table with example payloads for every consolidated command, including at least 3 "old wire / new wire" before-and-after pairs.
- PR description summarizes Phase 1 + Phase 2 as one consolidation, reports final count of 24 plus the 23-flat / 1-meta split.
- Manual smoke: `clio mcp-server` started locally; `tools/list` returns 24 tool entries; `clio-run`'s schema contains a 52-branch `anyOf`; one read-only flat tool and one `clio-run` command invoked successfully end to end.
