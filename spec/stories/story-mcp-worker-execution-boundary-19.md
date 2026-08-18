# Story 19: a cohort member serializes every tenant on the shared fallback lock

Found 2026-08-18 by an adversarial audit of the tool classification, while establishing ground truth
independently of the declared metadata. Pre-existing — not introduced by this feature — but squarely
in the wedge family, and it affects a tool that stage 6 already ships in the worker cohort.

## As a
person using clio's MCP server against more than one Creatio environment

## I want
one environment's slow read to leave the other environments alone

## So that
the per-tenant isolation this whole feature exists to provide is not defeated by a single tool that
takes the wrong lock.

## The defect

`clio/Command/McpServer/Tools/GetRelatedPageAddonTool.cs:55` builds per-tenant options — environment,
URI, login, password — and then calls the **environment-less** `ResolveCommand` overload:

```csharp
resolvedCommand = ResolveCommand<GetRelatedPageAddonCommand>(options);
```

`BaseTool`'s own documentation on that overload says it uses the shared fallback lock and must NOT be
used to execute a per-tenant command, because doing so serializes tenants. Its siblings in the cohort —
`PageGetTool`, `PageListTool`, `GetSchemaTool` — all pass `options` to the per-tenant overload.

So `get-related-page-addon` holds `McpToolExecutionLock.SharedFallbackKey` across a Creatio round-trip.
Every other tool that falls back to that same key waits behind it, on every environment.

This is the same defect class as story 12 (`MobilePageConversionGuideTool` taking the shared key after
resolving a real tenant and never balancing it), which this branch already fixed. This is the second
instance, in a tool the cohort ships.

## Acceptance criteria

- AC-01 `get-related-page-addon` resolves through the per-tenant overload, like its siblings.
- AC-02 A test proves the shared fallback key is NOT taken when a real environment resolves — and it
  must fail against the current code. Asserting "a lock was taken" proves wiring; assert WHICH key.
- AC-03 The other 188 tools are swept for the same shape: per-tenant options built, environment-less
  overload called. Report the count found, even if it is zero — a zero that was actually measured is
  worth recording.
- AC-04 If the sweep finds more, each is fixed or explicitly justified in the story. "It looked
  deliberate" is not a justification; the reason must be stated.

## Tests

Unit, in the tool's own fixture. A live stand is not needed to observe which key is taken.

## RESOLVED 2026-08-18

### AC-01 — the named defect

`GetRelatedPageAddonTool.cs:56` now calls `ExecuteWithCleanLog(options, () => …)`, matching
`PageGetTool` / `PageListTool` / `GetSchemaTool`.

### AC-02 — red before green, observed

Reverting the one-line fix and rebuilding turns the new tests red with the key named:

```
Expected recorder.LockKeys to be equal to {"https://tenant-under-test.creatio.com|addon-reader"}
… but {"__mcp_shared_fallback__"} differs at index 0.
```

The test asserts WHICH key three ways — `LockKeys.Should().Equal([TenantKey])`,
`NotContain(SharedFallbackKey)`, and `MarkInUseKeys.Should().Equal([TenantKey])`. The third is
independent evidence rather than restated wiring, because `MarkInUse` is skipped entirely for a
fallback key (`McpToolExecutionLock.IsFallback`). A companion assertion —
`Received(1).Resolve<IEntityBusinessRuleService>(o => o.Environment == "dev")` beside the matching
`GetTenantKey` call — proves the lock SPANS the round trip rather than merely preceding it.

### AC-03 — the sweep, measured

Regex over every `.cs` under `clio/Command/McpServer/**` for `ExecuteWithCleanLog(`, chosen over a
`(()` grep because it also catches the wrapped and multi-line forms; doc-comment mentions excluded;
each site classified by its first argument.

| Classification | Before | After |
|---|---|---|
| options-aware (per-tenant) | 29 | 35 |
| `ExecuteWithCleanLogUnderToolLock` | 1 | 1 |
| **environment-less — the defect** | **8** | **0** |

**36 real call sites**, not 188 — this story's body said "the other 188 tools", which is the file
count under `Tools/**` (193), not the number of places that take a lock. Corrected here rather than
left to mislead the next sweep.

Cross-checked with a second detector: `GetLock(McpToolExecutionLock.SharedFallbackKey)` under
`Tools/**` → zero outside `BaseTool` and `McpToolExecutionLock` themselves. `CompileCreatioTool.cs:177`
names the constant but correctly uses `GetTenantKey`.

### The third detector — a shape invisible to both greps

**`BaseTool.ResolveTenantLockKey` returns `SharedFallbackKey` whenever the base has no
`IToolCommandResolver`.** All eight business-rule tools constructed their base as
`BaseTool<…>(null, logger)`, so **two of them already passed `options` to the options-aware overload
and were still serializing every tenant.** A call site that looks correct degrades silently.

So the complete detector for this defect class is:

> the environment-less overload **OR** per-tenant work in a `BaseTool` subclass that does not thread
> `commandResolver` to its base.

Recorded because a future sweep written from AC-03 alone would miss half of what this one found. A
third revert cycle — undoing only the base-constructor threading — turned both `BusinessRuleToolLockTests`
red, so that half of the fix is independently load-bearing.

### AC-04 — each additional instance, with its reason

| Site | Decision | Reason |
|---|---|---|
| `CreateRelatedPageAddonTool:76` | fixed | byte-for-byte the same defect in the sibling write tool; "fixed the read, left the write" is not defensible |
| `BusinessRuleTool` ×6 | fixed | `BusinessRuleToolExecutor.Execute` builds `EnvironmentOptions { Environment = … }` and performs a real per-tenant resolve plus Creatio round trip inside the shared-fallback scope. Same defect, different shape — and AC-04 rules out "different shape" as a justification |
| `BusinessRuleTool` ×2 (the create tools) | fixed | outside AC-03's literal shape, identical in effect via the third detector above |
| `BuildThemeTool:133` | **excluded, with reason** | `ExecuteWithCleanLogUnderToolLock` keys on `tool:<type>`, not the shared fallback, so a slow call blocks only concurrent calls to that same tool. `BuildThemeOptions` is not `EnvironmentOptions`-derived and its version probe runs before the lock |
| `CompileCreatioTool:177` | **excluded, with reason** | uses `GetTenantKey`, and deliberately takes no `GetLock` — the documented ENG-91315 constraint that detached past-deadline work must not hold the monitor |

### Verification

`Category=Unit&Module=McpServer` → 3645 passed, 0 failed, 1 skipped. `dotnet build clio/clio.csproj
-f net10.0` clean with zero `CLIO*` diagnostics. MCP reviewed, no update required — no tool name,
argument, description, destructive/read-only flag, result shape or error envelope changed. Docs
reviewed, no update required; the four business-rule tools have no `help/en` or `docs/commands` entry
because they are MCP-only with no CLI verb, which is correct rather than a gap. ClioRing compatibility
reviewed, no Ring-consumed contract changed — none of the four tool names appears in `ClioRing.Ipc`,
`ClioRing`, or `ClioRing.Desktop/actions.json`, and a lock key is not in the gate's trigger list.

### Accepted costs

- Eight new `CS9107` warnings in `BusinessRuleTool.cs` — unavoidable with primary constructors when the
  derived type both uses and forwards the parameter. In-repo precedent: `RestartTool.cs:17`,
  `PageUpdateTool.cs:28`. Exposing the resolver from `BaseTool` as `private protected` was rejected as
  warning cosmetics against shared infrastructure.
- Each business-rule call now pays one `GetTenantKey` (a `settings.Fill`) it did not before — the same
  trade `BaseTool.InternalExecute` already documents for the reserve-before-acquire guard.
