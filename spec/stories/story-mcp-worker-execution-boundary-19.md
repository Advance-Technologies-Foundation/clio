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
