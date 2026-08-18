# Story 7: Sticky supervision + parent-owned configuration-build reservation

**Feature**: mcp-worker-execution-boundary
**Jira**: [ENG-95262](https://creatio.atlassian.net/browse/ENG-95262)
**ADR**: [adr-mcp-worker-execution-boundary.md](../adr/adr-mcp-worker-execution-boundary.md)
**Test plan**: [tp-mcp-worker-execution-boundary.md](../test-plans/tp-mcp-worker-execution-boundary.md)
**Stage**: 7
**Status**: ready-for-dev
**Size**: L

## As a
caller polling a long-running operation

## I want
my status polls to reach the worker that is running the operation

## So that
`compile-status` answers from the process that holds the compile, not from an empty registry in another one

## Design

### Prerequisite (inside this story): session-key normalisation lands BEFORE the registries move

**This is a first-class part of story 7, not a dependency on another story.** The original review finding
asked for `depends_on: story 5` — the story that owns normalisation in the credential threat model. That is
no longer available: **story 5 was deferred on 2026-08-18** (ADR §5, OQ-9), so a `depends_on` edge would
point at work nobody is doing and would silently block this story forever. The normalisation is therefore
re-homed here, in **stdio scope** — which is the whole scope the worker path has while the stdio-only gate
holds — and stated as an ordering constraint inside the story.

**Why the order is load-bearing.** Every registry this story moves is keyed by tenant. If the registries
move to the parent while a single target can still produce two different keys, the parent's registry is
split at birth: `compile-creatio` invoked with a registered environment name and `compile-status` polled
with an explicit URI for the same environment land in two buckets. The visible symptom is not an error —
it is `compile-status` answering "no such operation" for a compile that is running. Normalising afterwards
does not repair it either, because the split keys are already in flight. **Normalise first, then move the
registries.**

**The defect, cited from the current code** (`clio/Command/McpServer/Tools/ToolCommandResolver.cs:361-379`,
read 2026-08-18 — note the *pre*-ENG-94529 shape `options.Environment ?? settings.Uri` no longer exists):

```csharp
string identity = string.Concat(
    options.Environment ?? DefaultIdentifier, "|",   // DefaultIdentifier is the literal "default" (:72)
    settings.Uri ?? string.Empty);
```

So one target yields **two** keys — `myenv|http://x` through the name branch and `default|http://x` through
the URI branch. ENG-94529 put the URI into the identity (which fixed a re-pointed environment handing back
a stale client) but did **not** make the two branches converge. ADR rule 10 and §1.4 record the same fact.

**What this story must do, before AC-01…AC-04:**

- One resolved target produces **one** key, whether it was reached by registered name or by explicit URI.
- The folding follows the **conservative component-by-component algorithm in the credential threat model
  T-5** — scheme case, IDNA/Punycode and RFC 5952 host forms, default-port elision, one trailing slash,
  percent-encoding; `http`/`https` and hostname/IP stay **distinct**; userinfo, query and fragment are
  **rejected**, not normalised. Anything the algorithm does not name is a different target. Over-normalising
  merges two targets, and on a sticky worker that is a credential crossover, not a cache miss.
- Scope: **stdio only.** The principal and credential-fingerprint components of the sticky key (R-5) stay
  with the deferred story 5; on stdio the target is the whole key, because the child reads
  `appsettings.json` itself and no credential crosses the boundary. When `mcp-http` is revived (OQ-9), the
  same normalisation is reused with the other two components added — it is not rewritten.

### Sticky supervision

- **Private completion signal** between worker and parent (rule 5). "Reap on terminal status" cannot work: only two operation registries exist — `ICompileOperationRegistry` (`BindingsModule.cs:738`) and `IRestartOperationRegistry` (`:744`). `install-process-builder` and `create-app-section` have none, and `restart-by-credentials` is deliberately unreportable, so three of the four long-running modes have no terminal status to reap on.
- **Registry cardinality — two different keys, and conflating them fails either way** (cross-call state §3). The compile/restart *status* registries stay keyed like the sticky worker (`principal + normalised target + credential fingerprint`): they answer "whose operation is this". The `configuration-build` *exclusion* is keyed by `normalised target + resource` only: Creatio's configuration build is server-wide, so putting the principal in that key lets two principals on one environment compile concurrently and corrupt each other's package compilation state. Keying exclusion by target alone is correct but means one stuck build denies the whole environment — which is precisely why the **30-minute reclaim ceiling is the maximum lock-hold time**, not an incidental detail.
- Move the shared `configuration-build` reservation to the parent, keyed by **normalised tenant + resource**. Today it is `McpToolExecutionLock._configurationBuildInFlight` (`:215`), in-process, held by `CompileCreatioTool.cs:66` and `InstallProcessBuilderTool.cs:167`. Its 30-minute reclaim ceiling and monotonic ownership tokens carry over unchanged — they were designed for the "holder may never release" case.
- Prototype behaviour to preserve: `compile-creatio` returned in-progress at 8 s and three `compile-status` polls answered `running` from the same worker in 0.00–0.02 s.
- Sticky lifetime bounded by credential validity with an explicit maximum (T-8) — a threat that per-call workers do not have, and the reason stickiness stays confined to these four families.

## Acceptance Criteria
- [ ] AC-00 — **Session-key normalisation lands first** (TC-U-706): a registered environment *name* and an explicit *URI* for one target resolve to **one** key, and the equivalence table is asserted **both directions** — equivalent pairs share a key, near-miss pairs do not — generated from the threat model's T-5 component table rather than from cases the implementer happened to think of. This AC is ordered **before** AC-03: moving the registries to the parent on a split key produces a `compile-status` that answers "no such operation" for a running compile, and that is not repairable after the fact.
- [ ] AC-01 — `compile-creatio` returns in-progress; subsequent `compile-status` polls reach the **same** worker (TC-E-701).
- [ ] AC-02 — Private completion signal reaps workers for the three families with no registry (TC-U-701).
- [ ] AC-03 — Parent-owned reservation excludes compile ↔ install-process-builder **across processes and across principals**, keyed by normalised tenant + resource with the 30-minute ceiling as its maximum hold (TC-U-702).
- [ ] AC-04 — Sticky lifetime bounded by credential validity, explicit maximum (TC-U-703).
- [ ] AC-05 — **OQ-4 resolved**: whether `create-app-section` gains a real registry or only the private signal.

## Tests
E2E TC-E-701; unit TC-U-701…703 and **TC-U-706** (session-key normalisation, AC-00). **Full unit suite required.**
