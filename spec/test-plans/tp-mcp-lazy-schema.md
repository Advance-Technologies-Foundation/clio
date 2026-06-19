# Test Plan: Hybrid lazy-schema MCP surface

**Feature**: mcp-lazy-schema
**Stories**: [story-0](../stories/story-mcp-lazy-schema-0.md) … [story-11](../stories/story-mcp-lazy-schema-11.md)
**ADR**: [adr-mcp-lazy-schema.md](../adr/adr-mcp-lazy-schema.md) (Proposed — gated on Story 0)
**Sprint tracker**: [sprint-status.yaml](../sprint-status.yaml)
**Author**: QA Planner Agent
**Status**: Draft
**Created**: 2026-06-19

---

## Scope

### In scope
- `clio-run` dispatch: `command → optionsType` resolution; kebab/enum/required-aware arg binding; env-scoped vs env-less execution; generalized `ResolveFromCallContainer`; uniform output envelope.
- `get-tool-contract`: curated contract on **every** long-tail command (enums, nested, required, kebab names); reflection fallback never presented as authoritative.
- `IFeatureToggleService`-gated core/long-tail profile: config-driven membership; fail-closed default (FULL catalog); the four enforcement surfaces; **no env-var override** (`CLIO_MCP_TOOL_TYPES` removed).
- `tools/list` budget ratchet (built from scratch): core profile ≤ ~5–8k tokens; FULL baseline recorded.
- Security: `clio-run` never `ReadOnly`/auto-approve; safe vs `clio-run-destructive` split; destructive commands cannot execute via the safe surface; unknown destructiveness fails closed.
- inline-contract-on-error self-correction (one-round).
- Migration / deprecation aliases: flat name → `clio-run` proxy; existing consumers (CAADT / adaclio / e2e) do not break.
- Regression: existing flat-tool behavior unchanged when flag OFF; CLI verbs untouched.

### Out of scope (with reason)
- **Small-local-model end-to-end success** (e.g. gpt-oss-20b) — ADR "Success metric": host/runtime residual (host system prompt + built-in tool schemas) is out of scope; the ADR must not be judged against it.
- **PR #624 `anyOf` design** — superseded per Story 0 decision; not tested here.
- **CLI verb behavior / CLI arg parsing** — unchanged by design; only smoke-asserted as a regression guard (no new CLI flags in this feature).
- **Story 0 / Story 9** — documents-only decision/inventory stories (empty code diff, `spec/**` only); no automated TCs, validated by review checklists in their DoD.

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| Flag-ON profile silently changes flat-tool behavior (regression in 124-tool catalog) | Med | High | TC-U-01 golden: flag OFF == byte-for-byte FULL catalog; full `Module=McpServer` suite green |
| `clio-run` silently drops unknown/typo args (the STJ failure mode the ADR forbids) | High | High | TC-U-12 unknown-key → error; TC-U-13 asserts binding goes through CommandLineParser, NOT raw STJ |
| Missing `Required` option silently defaulted | Med | High | TC-U-10 missing-required → verbatim parser error, non-success |
| Generalizing `BaseTool` switch breaks the 4 special-cased env-less option types | Med | High | TC-U-06 parametrized over the 4 existing types; `BaseToolTests.cs` regression-pinned |
| `clio-run` auto-approves destructive commands behind one "always allow" | Med | **Critical** | TC-U-20/21/22 safe-rejects-destructive + never-ReadOnly + fail-closed-on-unknown; security sign-off (Story 8 DoD) |
| Lossy reflection fallback presented as authoritative long-tail schema | High | Med | TC-U-15 curated corrects fallback (enum/nested/required); TC-U-16 coverage-guard fails on uncovered command |
| Model ignores discover→describe→run (passive instructions — ENG-91134) | High | Med | TC-I-02 fail→inline-contract→retry-succeeds; TC-E-04 3-host self-correction (NOT in CI) |
| Breaking flat-tool-name consumers (CAADT / adaclio / e2e) on default flip | High | High | TC-U-24 alias resolves; TC-I-04 alias output == pre-migration golden; TC-E-06 3-host consumer calls (NOT in CI) |
| `tools/list` silently regrows after migration | Med | Med | TC-U-26 budget ratchet fails past recorded ceiling, names offender |
| Round-trip behavior unvalidated on 3 hosts | High | Med | E2E gate (TC-E-*) — **manual, NOT in CI**; explicit 3-host checklist in PR |
| Env-var scaffold left readable in production | Low | Med | TC-U-05 grep-guard: no production path reads `CLIO_MCP_TOOL_TYPES` |

---

## Unit Tests (`clio.tests/Command/McpServer/`)

### Story 1 — IFeatureToggleService profile gating (`McpProfileGatingTests.cs`)

#### TC-U-01: Flag OFF reproduces the FULL catalog (golden)
- **Story**: 1 (AC-01, AC-ERR) — regression-critical
- **Pre**: feature flag absent/false; default `appsettings` features.
- **Steps**: build the enabled-tool-type set through the profile selector with the flag off.
- **Expected**: registered tool-type set == current FULL set (~124); byte/count golden matches the pre-change baseline. Malformed/absent flag → FULL (fail-closed), never a partial profile.
```csharp
[Test]
[Category("Unit")]
[Description("Profile selection returns the full tool-type catalog when the lazy-schema flag is off, so existing consumers see no change.")]
public void SelectToolTypes_ShouldReturnFullCatalog_WhenFeatureFlagOff()
{
    // Arrange
    var toggle = Substitute.For<IFeatureToggleService>();
    toggle.IsEnabled(Arg.Any<Type>()).Returns(false);
    var sut = new McpToolProfileSelector(toggle); // resolved from DI in real fixture

    // Act
    var result = sut.SelectToolTypes(AllToolTypes);

    // Assert
    result.Should().BeEquivalentTo(AllToolTypes,
        because: "an off flag must fail closed to the full catalog with no behavior change");
}
```

#### TC-U-02: Flag ON narrows to the core profile
- **Story**: 1 (AC-02, AC-04)
- **Pre**: flag ON; core membership config present.
- **Steps**: select tool types with flag on.
- **Expected**: only configured core tool types remain; selection routed through `RegisterEnabledPrimitives` semantics (`IEnumerable<Type>` — never `Type[]`).

#### TC-U-03: Feature-key consulted case-insensitively
- **Story**: 1 (AC-03)
- **Pre**: feature-key string fixed by Story 0.
- **Expected**: `IFeatureToggleService.IsEnabled` is queried with the exact Story-0 key; upper/lower variants resolve identically (keys compared case-insensitively per project-context).

#### TC-U-04: Core membership is config-driven and unit-assertable
- **Story**: 1 (AC-04)
- **Expected**: core set comes from one config source (not scattered constants); asserting the configured set yields the expected core tool types.

#### TC-U-05: No production path reads the env-var scaffold
- **Story**: 1 (AC-05) — regression guard
- **Steps**: source-scan (test-time reflection / file scan helper) for `CLIO_MCP_TOOL_TYPES` in production assemblies.
- **Expected**: zero references outside `spec/**`/tests; `ApplyToolProfile` env reader removed.

---

### Story 3 — command→optionsType registry + generalized resolver (`CommandOptionsRegistryTests.cs`)

#### TC-U-06: Generalized resolver subsumes the 4 hardcoded option types (regression)
- **Story**: 3 (AC-03, AC-04) — protects `BaseTool.cs:110-127`
- **Pre**: parametrized over `CreateTestProjectOptions`, `AddPackageOptions`, `CreateWorkspaceCommandOptions`, `CreateUiProjectOptions`.
- **Steps**: resolve `Command<TOptions>` for each via the generalized `ResolveFromCallContainer`.
- **Expected**: env-less branch chosen exactly as the old switch did (when Environment+Uri blank / Empty); env branch otherwise. No "Unsupported options type" throw for registered types.

#### TC-U-07: Registry maps every verb (canonical + aliases) to options Type
- **Story**: 3 (AC-01)
- **Expected**: every `[Verb]` (incl. `Aliases`) reflected from options classes maps to its options `Type`, using the same source the CLI parser reflects over.

#### TC-U-08: Unknown command name returns a miss (no throw for control flow)
- **Story**: 3 (AC-02, AC-ERR)
- **Expected**: `ResolveOptionsType("not-a-command")` returns a miss; a structured "unknown command 'X'" result is produced for the boundary, not an unhandled exception.

#### TC-U-09: Duplicate verb→optionsType collision detected at build/startup
- **Story**: 3 (AC-05)
- **Expected**: two verbs mapping to ambiguous/duplicate option types → detected at registry build (not silent last-wins).

---

### Story 4 — clio-run arg binding + executor (`ClioRunArgBinderTests.cs`, `ClioRunToolTests.cs`)

#### TC-U-10: Missing Required option → verbatim parser error (not defaulted)
- **Story**: 4 (AC-03, AC-ERR)
- **Pre**: command with a `[Option(Required=true)]`; `args` omits it.
- **Expected**: binding fails; result carries the verbatim CommandLineParser error; non-success; option NOT silently defaulted.

#### TC-U-11: Enum option parses from string; invalid value is a parse error
- **Story**: 4 (AC-04)
- **Expected**: valid enum string parses via CommandLineParser enum handling; invalid value → parse error (not coerced).

#### TC-U-12: Unknown arg key → error (NOT silently dropped)
- **Story**: 4 (AC-05) — the core STJ failure mode the ADR forbids
- **Expected**: an `args` key not matching any `[Option]` long name → error result, non-success.

#### TC-U-13: Binding uses CommandLineParser, not raw STJ (mechanism assertion)
- **Story**: 4 (AC-02, DoD)
- **Steps**: pass a camelCase / PascalCase key for an option whose kebab `[Option]` name differs; pass a typo.
- **Expected**: non-kebab key is rejected (proves names come from `[Option]`, not STJ property names); typo rejected. Demonstrates the argv-reparse path, not `JsonSerializer.Deserialize<TOptions>`.

#### TC-U-14: Bool flag (presence) and positional `[Value]` bind correctly
- **Story**: 4 (AC-01, impl note)
- **Expected**: bool flag binds by presence; `[Value]` positional binds from the free-form object; round-trips to argv correctly.

#### TC-U-14b: Unknown command → structured miss (not exception); envelope shape uniform
- **Story**: 4 (AC-06, AC-07)
- **Expected**: unknown `command` → structured "unknown command" result (from Story 3 resolver); successful runs return the same `CommandExecutionResult` envelope (execution-log messages) that flat tools return — identical shape across commands (golden).

---

### Story 6 — curated contract coverage (`ToolContractCoverageTests.cs`)

#### TC-U-15: Curated contract corrects each lossy aspect of the fallback
- **Story**: 6 (AC-02) — sampled
- **Pre**: sample long-tail commands; compare curated entry vs `McpToolSchemaCatalog` (`:91-178`).
- **Expected**: curated entry has ALL params (not first-only), real enum values (not "string"), nested structure (not "object"), and `Required=true` from `[Option(Required=true)]`. kebab option long-names present.

#### TC-U-16: Coverage guard — every long-tail command has a curated contract
- **Story**: 6 (AC-01, AC-04) — regression guard
- **Steps**: enumerate long-tail commands (not in core profile); assert each has a curated `CanonicalToolNames` entry.
- **Expected**: zero uncovered; a newly-added long-tail command with no curated entry FAILS this test (prevents silent fallback regression).

#### TC-U-17: get-tool-contract returns curated, not fallback, for long tail
- **Story**: 6 (AC-03)
- **Expected**: for sampled long-tail commands, `get-tool-contract(command)` returns the curated contract; fallback used only as last-resort net.

#### TC-U-18: Unknown command → error + index pointer
- **Story**: 6 (AC-ERR)
- **Expected**: `get-tool-contract("not-a-command")` → `Error: unknown command 'X'` + index pointer; non-success.

---

### Story 5 — inline-contract-on-error (`ClioRunInlineContractTests.cs`)

#### TC-U-19: Failed bind returns the full curated contract inline
- **Story**: 5 (AC-01, AC-05)
- **Expected**: on bind failure, the result contains the SAME payload `get-tool-contract` returns (one source of truth, reuses Story 6) plus the parse error.

#### TC-U-19b: Unknown command → index pointer (not a non-existent contract); lossy flagged best-effort; lookup failure degrades
- **Story**: 5 (AC-03, AC-04, AC-ERR)
- **Expected**: unknown command → pointer to the compact index (Story 7), not a fabricated contract; a command with only a lossy fallback is flagged best-effort (not authoritative); a contract-lookup failure degrades to parse-error + index pointer, never an unhandled exception.

---

### Story 8 — security split (`ClioRunSecuritySplitTests.cs`)

#### TC-U-20: Safe clio-run rejects/redirects destructive commands
- **Story**: 8 (AC-01, AC-03) — security-critical
- **Pre**: a destructive command (e.g. `delete-entity-schema`, `application-delete`).
- **Expected**: invoking it via `clio-run` (safe) is rejected/redirected to `clio-run-destructive`; it cannot execute via the safe surface.

#### TC-U-21: clio-run is never ReadOnly; destructive surface is Destructive=true
- **Story**: 8 (AC-02) — security-critical
- **Expected**: `clio-run` tool metadata: `ReadOnly == false` (never auto-approve); `clio-run-destructive`: `Destructive == true`.

#### TC-U-22: Destructive set derives from existing metadata; unknown → destructive (fail closed)
- **Story**: 8 (AC-04, AC-ERR) — security-critical
- **Expected**: the destructive command set is computed from each command's existing `Destructive=true` flag (single source, no hand-divergent list); a command with missing destructiveness metadata classifies as destructive (fail closed); a test guards the gap.

#### TC-U-23: Read-only ops stay in flat core, not routed through either clio-run surface
- **Story**: 8 (AC-05)
- **Expected**: read-only commands are excluded from both clio-run surfaces (granular auto-approve preserved).

---

### Story 7 — compact command index (`CommandIndexTests.cs`)

#### TC-U-23b: Index lists all commands by category, stays small, points long-tail to get-tool-contract/clio-run
- **Story**: 7 (AC-01, AC-04, AC-05)
- **Expected**: index returns names + one-line summaries grouped by category (enumerated via `ICommandOptionsRegistry`, not a hand list); long-tail entries point to `get-tool-contract`+`clio-run`; a new long-tail command appears automatically (or a test guards omission); index payload stays small (does not reintroduce schema bulk).

#### TC-U-23c: Every Story-2-migrated anti-pattern/flow-hint is present (no guidance lost)
- **Story**: 7 (AC-02, AC-03)
- **Expected**: cross-checked against Story 2's migration list — each migrated hint is present in the index/guidance output; no duplication across `get-guidance` and the index.

---

### Story 2 — core-tool description slimming (`CoreToolDescriptionTests.cs`)

#### TC-U-23d: environment-name description resolves to one shared constant; arg contracts unchanged
- **Story**: 2 (AC-01, AC-02) — regression-sensitive (pure-text refactor)
- **Expected**: on a sample of tools, the `environment-name` description resolves to the single shared constant; arg names / `required` / enum values are byte-identical to pre-slim (only description text changed).

---

### Story 10 — deprecation aliases (`ClioRunAliasTests.cs`)

#### TC-U-24: Each flat alias resolves to the correct clio-run target + args
- **Story**: 10 (AC-01)
- **Expected**: each Story-9 alias maps to the correct `clio-run`/`clio-run-destructive` invocation with the same args/output envelope; a deprecation notice is surfaced (log/response field, not a hard error).

#### TC-U-25: Destructive alias → destructive surface; removed-not-aliased → moved error; unknown target fails closed
- **Story**: 10 (AC-04, AC-05, AC-ERR) — security + back-compat
- **Expected**: a destructive command's flat alias proxies to the destructive surface (never-auto-approve preserved); a genuinely-removed name not in the alias list → `Error: tool 'X' has moved to clio-run` + pointer; an alias to an unknown/renamed command fails closed with a clear error (no silent no-op).

---

### Story 11 — budget ratchet (`McpToolBudgetTests.cs`)

#### TC-U-26: Core profile tools/list ≤ recorded ceiling; FULL baseline recorded; offender named
- **Story**: 11 (AC-03, AC-04, AC-05, AC-ERR) — primary success-metric guard, built from scratch
- **Steps**: measure the same direct-stdio `tools/list` payload the spike measured, core profile.
- **Expected**: core-profile token/byte size ≤ ADR target (~5–8k tokens); FULL profile (flag off) recorded as documented baseline (NOT asserted as a ceiling — unchanged-by-design); a future command that pushes core over the ceiling fails the test with a message naming the regression. Test comment links the ADR target and notes the out-of-scope host residual.

---

## Integration Tests (`clio.tests/Command/McpServer/`)

### TC-I-01: env-scoped resolve of a real Command<TOptions> from the container
- **Story**: 3 (Integration row) — `GeneralizedResolverTests.cs`
- **Setup**: DI container with a registered `Command<TOptions>` and `IToolCommandResolver` stub keyed by env.
- **Steps**: resolve via the generalized resolver for an arbitrary registered options type.
- **Expected**: returns the correct env-scoped `Command<TOptions>` instance; env-key caching matches `BaseTool`'s existing behavior.
- **Category**: `[Category("Integration")]`
- **Teardown**: clear received calls.

### TC-I-02: clio-run end-to-end bind → resolve → execute (non-destructive command)
- **Story**: 4 (Integration row) — `ClioRunExecutionTests.cs`
- **Setup**: a representative non-destructive command (e.g. `get-pkg-list`) wired in the env-scoped container with a substituted `IApplicationClient`.
- **Steps**: `clio-run(command, args)` with valid kebab args.
- **Expected**: command executes via the env-scoped path (NOT the startup-injected instance); returns the uniform `CommandExecutionResult` envelope.

### TC-I-03: fail → inline-contract → retry-succeeds (one-round self-correction)
- **Story**: 5 (Integration row) — `ClioRunSelfCorrectTests.cs`
- **Setup**: command with a Required option; first call omits it.
- **Steps**: (1) `clio-run` with missing arg → inline contract returned; (2) re-invoke with corrected args from that contract.
- **Expected**: second call succeeds — demonstrates one-round self-correction without a separate `get-tool-contract` round-trip.

### TC-I-04: destructive command executes only via the destructive surface
- **Story**: 8 (Integration row) — `ClioRunDestructiveExecutionTests.cs`
- **Setup**: a destructive command with substituted dependencies.
- **Expected**: executes via `clio-run-destructive`; the safe `clio-run` path produces a rejection/redirect, not execution.

### TC-I-05: alias invocation output == pre-migration flat output (sampled golden)
- **Story**: 10 (Integration row) — `AliasParityTests.cs`
- **Setup**: representative sample of flat long-tail tools + their aliases.
- **Expected**: alias-proxied output envelope is golden-equal to the pre-migration flat tool output.

### TC-I-06: no dangling flat-name references in prompts/resources (grep-style guard)
- **Story**: 11 (Integration row) — `ToolNameReferenceTests.cs`
- **Setup**: scan `Prompts/*`, `Resources/*` for moved flat tool names.
- **Expected**: every reference points to the new surface (`clio-run` + `get-tool-contract`); no dangling flat-name references remain.

---

## E2E Tests (`clio.mcp.e2e/`) — ⚠️ NOT in CI

> **All E2E below are a MANUAL gate.** `clio.mcp.e2e` is NOT wired into CI (project-context.md, AGENTS.md). The ADR's #1 unvalidated risk — whether a host reliably reads a schema from a tool *result* and composes the next `clio-run` call — lives entirely here. **TC-E-02, TC-E-04, TC-E-05, TC-E-06 require a 3-host run** (claude / codex / copilot) and must be recorded in the PR checklist before merge. TC-E-01/03 are single-host protocol checks.

### TC-E-01: tools/list size flag-off vs flag-on over stdio (single host)
- **Story**: 1, 11
- **Tool**: protocol-level `tools/list`
- **Expected**: flag-off ≈ FULL baseline (~56.7k tok); flag-on ≈ core ≤ ~5–8k tok (−≈90%+). Confirms the token-reduction mechanism end-to-end over real stdio.
- **CI**: NOT in CI — manual.

### TC-E-02: discover → describe → run round-trip — **3-host gate**
- **Story**: 4, 6, 7
- **Steps (per host)**: model uses index → `get-tool-contract(command)` → composes `clio-run(command, args)` → reads the envelope.
- **Expected**: each of claude/codex/copilot completes the pattern and gets a successful envelope.
- **CI**: NOT in CI — **manual on 3 hosts**.

### TC-E-03: get-tool-contract returns curated (not fallback) for sampled long-tail (single host)
- **Story**: 6
- **Expected**: sampled long-tail commands return curated contracts over stdio (correct enums/nested/required).
- **CI**: NOT in CI — manual.

### TC-E-04: inline-contract self-correction after a bad call — **3-host gate** (primary risk mitigation)
- **Story**: 5
- **Steps (per host)**: issue a deliberately invalid `clio-run` (missing required) → receive inline contract → model retries and succeeds **in one round**.
- **Expected**: all 3 hosts self-correct in one round (validates the ENG-91134 "passive instructions ignored" mitigation).
- **CI**: NOT in CI — **manual on 3 hosts**.

### TC-E-05: host sees clio-run-destructive as Destructive / not auto-approved — **3-host gate**
- **Story**: 8 (security)
- **Expected**: on each host, `clio-run` is not auto-approvable and `clio-run-destructive` surfaces as `Destructive=true` (host prompts).
- **CI**: NOT in CI — **manual on 3 hosts**.

### TC-E-06: existing consumers (CAADT / adaclio / e2e) still work via alias — **3-host gate**
- **Story**: 10
- **Expected**: representative CAADT / creatio-adaclio-testing / clio-e2e calls hitting flat long-tail names still resolve via alias on each host.
- **CI**: NOT in CI — **manual on 3 hosts**.

### TC-E-07: full discover→describe→run post-migration — **3-host gate** (final acceptance)
- **Story**: 11
- **Expected**: complete pattern works post-migration on all 3 hosts; recorded in PR.
- **CI**: NOT in CI — **manual on 3 hosts**.

---

## Regression Guard

Tests / surfaces that MUST stay green after this feature ships:

| Test file | Test / surface | Why at risk |
|-----------|----------------|-------------|
| `clio.tests/Command/McpServer/McpFeatureToggleFilterTests.cs` | existing seam behavior | Story 1 replaces the `CLIO_MCP_TOOL_TYPES` scaffold on the same `RegisterEnabledPrimitives` seam (`McpFeatureToggleFilter.cs:120-156`) |
| `clio.tests/Command/McpServer/BaseToolTests.cs` | env-scoped resolution + 4 special-cased option types | Story 3 generalizes the hardcoded `options switch` (`BaseTool.cs:110-127`) |
| `clio.tests/Command/McpServer/ToolContractGetToolTests.cs` | existing ~46 curated entries | Story 6 extends `CanonicalToolNames`; existing entries must not break |
| ~50 `clio.tests/Command/McpServer/*ToolTests.cs` | each flat tool's behavior | Flag OFF must reproduce FULL catalog byte-for-byte (TC-U-01) |
| `clio/BindingsModule.cs:632-655` (full `Module=McpServer` suite) | DI composition root | Story 1 edits the registration seam — full-suite trigger per AGENTS.md rule 4 |
| CLI verb parsing / `Program.cs` | no CLI flag change | ADR: "CLI verbs unaffected"; no new CLI flags — smoke-assert unchanged |
| ~50 `clio.mcp.e2e/*ToolE2ETests.cs` | flat-tool e2e | Aliases (Story 10) must keep these resolving (manual gate) |

**Suggested regression filter (per AGENTS.md smart-regression):**
`dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`
Story 1 (touches `BindingsModule.cs`) → additionally run the full unit suite: `--filter "Category=Unit"`.

---

## Coverage Estimate

| Layer | New tests | Modified/regression-pinned | Notes |
|-------|-----------|----------------------------|-------|
| Unit | ~30 (TC-U-01 … TC-U-26 + sub-IDs) | 4 existing fixtures pinned | All `[Category("Unit")]` |
| Integration | 6 (TC-I-01 … TC-I-06) | — | `[Category("Integration")]`, PR-merge gate |
| E2E | 7 (TC-E-01 … TC-E-07) | ~50 flat e2e fixtures must stay green | `[Category("E2E")]` — **NOT in CI**; 5 of 7 are 3-host |

**Per-story TC map:**
- Story 0 — none (documents-only decision).
- Story 1 — TC-U-01…05, TC-E-01.
- Story 2 — TC-U-23d, TC-E-01 (delta).
- Story 3 — TC-U-06…09, TC-I-01.
- Story 4 — TC-U-10…14b, TC-I-02, TC-E-02.
- Story 5 — TC-U-19/19b, TC-I-03, TC-E-04.
- Story 6 — TC-U-15…18, TC-E-03.
- Story 7 — TC-U-23b/23c, TC-E-02.
- Story 8 — TC-U-20…23, TC-I-04, TC-E-05.
- Story 9 — none (documents-only inventory).
- Story 10 — TC-U-24/25, TC-I-05, TC-E-06.
- Story 11 — TC-U-26, TC-I-06, TC-E-07.

---

## Coverage gaps (explicit)

- **E2E entirely out of CI.** The round-trip (TC-E-02), self-correction (TC-E-04), destructive-visibility (TC-E-05) and consumer-alias (TC-E-06) checks — the ADR's highest-risk, least-validated behaviors — are manual-only on 3 hosts. CI cannot catch a regression here; gate them in the PR checklist before any default flip (Story 10).
- **3-host divergence risk.** A behavior may pass on claude but fail on codex/copilot; only a disciplined 3-host run surfaces it. No automation.
- **Stories 0 & 9 unverifiable by tests** — documents-only; rely on DoD review checklists, not TCs.
- **Real-Creatio long-tail execution** is sampled (representative commands), not exhaustive across all ~74; the coverage guard (TC-U-16) protects *contract* presence, not *runtime* correctness of every command via `clio-run`.

---

## Definition of Done for QA

- [ ] All TC-U-* implemented with `[Category("Unit")]` — NOT `[Category("UnitTests")]`
- [ ] All TC-I-* implemented with `[Category("Integration")]`
- [ ] Regression guard tests green (esp. `BaseToolTests`, `McpFeatureToggleFilterTests`, `ToolContractGetToolTests`)
- [ ] Flag-OFF FULL-catalog golden (TC-U-01) green
- [ ] Security TCs (TC-U-20/21/22, TC-I-04, TC-E-05) green + security sign-off recorded (Story 8 DoD)
- [ ] Budget ratchet (TC-U-26) built from scratch, green, ceiling + FULL baseline documented
- [ ] MCP E2E documented; **5 of 7 (TC-E-02/04/05/06/07) run + recorded on 3 hosts** before default flip
- [ ] PR checklist includes the manual 3-host MCP E2E gate
- [ ] Test naming follows `MethodName_ShouldExpectedBehavior_WhenCondition`; AAA + `because` on every assert + `[Description]` on every test
- [ ] Command tests use `BaseCommandTests<TOptions>` where applicable; substitutes cleared in teardown
- [ ] Validated filter recorded in PR (`Category=Unit&Module=McpServer`; full suite for Story 1)
- [ ] PR includes the test files in the changed-files list
