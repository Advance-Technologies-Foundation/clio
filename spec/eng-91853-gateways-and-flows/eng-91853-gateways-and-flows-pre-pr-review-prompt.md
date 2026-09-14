# ENG-91853 — prompt for the pre-PR review session

Paste everything below the line into a fresh session.

---

Task: ENG-91853 — "Exclusive and parallel gateways, conditional/default flows + basic Y auto-layout"
(https://creatio.atlassian.net/browse/ENG-91853). Story, component "bpms tools", assignee Dmitro
Krestov. Task 15 of the BP-generation list; parent research ENG-90883.

THE CODE IS COMPLETE AND FROZEN. Nothing is pushed except the guidance branch, and no pull request
exists. You are the **final comprehensive review gate** that AGENTS.md requires before a PR is opened,
and after you the only remaining work is the browser leg and the three PRs themselves.

You did not write this code, and that is the point. A fix authored inside the round that found the
problem carries no independent review — that principle has been held throughout this ticket and it
applies to you too: **do not implement what you find.** Report it.

## Read first, in this order

- `spec/eng-91853-gateways-and-flows/README.md` — the index and the seven findings that shaped the work
- `spec/reviews/review-eng-91853-gateways-and-flows-2026-09-05.md` — the first comprehensive gate: two
  blockers, six High, eleven executed mutations, and the corpus measurements behind them
- `spec/eng-91853-gateways-and-flows/eng-91853-gateways-and-flows-manual-test-run-2026-09-06.md` — run 1
  (six of eleven cases, and the D1 finding)
- `spec/eng-91853-gateways-and-flows/eng-91853-gateways-and-flows-manual-test-run-2026-09-06b.md` — run 2
  (fifteen cases, D1 confirmed on a stand)
- `AGENTS.md` and `project-context.md`
- `docs/knowledge/` — grep it for every symbol you touch; there are records in `ProcessModel/`,
  `platform/`, `Tests/` and `process/` written by this ticket

## Scope — three repositories, one branch each

| Repository | Path | Base | Branch tip | Commits |
|---|---|---|---|---|
| clio | `C:/Projects/clio` | `a9deb32bc` (`master`) | `e5ce93f11` | 42 |
| package | `C:/Projects/workspace/ProcessBuilder` | `7e93995` (**`main`**, not master) | `a82a779` | 17 |
| guidance | `C:/Projects/clio-knowledge`, worktree `.worktrees/eng-91853` | `84e2609` (`master`) | `c1a9e69` | 5 |

Read-only ground truth — read these instead of reasoning about what the platform does:

- platform sources `C:/Projects/Creatio/.devenv/repos/core/TSBpm/Src/Lib` — **note the path; it moved.
  The old `C:/Projects/Creatio/TSBpm/Src/Lib` does not exist, and an empty grep there looks exactly
  like absence.**
- shipped corpus `C:/Projects/PackageStore` (Creatio 7.8.0)
- designer client `C:/Projects/creatio-ui`

## What is SETTLED — do not spend a round re-deriving these

Every item below was measured or proved, and the evidence is in the documents above.

**From the ticket's own DO-NOT list (still holds in full):** no formula validator; the default branch is
not `ExclusiveGateway.DefaultUId`; never `FlowType=Conditional` on a plain `ProcessSchemaSequenceFlow`;
R6 is not implemented; no remove+add re-kind; no `MatchBranchingDecisions` guard; never re-sort
`flows[]`; `GV2` is read-only.

**Proved against source or by running the code:**

- a duplicate element UId cannot reach the engine — `MetaItemCollection.InsertItem` throws first
- a dangling endpoint cannot be set — the `TargetRefUId` setter throws; the reachable shape is an
  element removed AFTER its flows
- layout performance is ~0.2 ms at the largest shipped process; nothing was optimised, deliberately
- `VisualType` does not reach the metadata — `WriteMetaData` passes the literal
- validator rule-id reuse breaks no consumer
- **layout case B is decided and implemented** (owner chose option 2): a merge with a column-skipping
  inbound branch takes that branch's lane instead of the mean. Rows A and E untouched
- **D1 is decided and implemented** (owner chose "in this ticket"): `flows[].condition` on the build
  path accepts a parameter NAME and the server expands it. Confirmed on a stand — `[#AmountParameter#]`
  in, UId meta-path stored
- the floor/bundled gap (`[RequiresPackage]` 1.4.0.60, bundled 1.4.0.61) is **correct**, not sloppy:
  convergence and the attribute are deliberately different rules and may disagree
- convergence gates only **triggered** `[RequiresPackage]` requirements — a command with none never
  fetches the package list

**Twenty-plus guards have already been mutation-tested.** Every one that survived its mutation was
either deleted or given a test. Do not re-run the whole set; spot-check the ones you doubt.

## What is OPEN and is NOT yours to settle

- **the browser leg has not run.** Design time and runtime are `not verified` for every case. Storage
  proves the text is right, not that the platform agrees with it. Specifically unproven: that the
  default branch renders as the fallback, that a three-way join waits for all three, that an expanded
  condition evaluates
- four spawned tasks belong to owners, not to this PR: `odata-read` argument shapes; `list-user-tasks`
  being long-tail while four shipped surfaces name it imperatively; extending
  `WorkspaceTemplateGuidanceDriftTests` from templates to tool descriptions and prompts; and two
  AGENTS.md trigger fixes (**deliberately not made — that file governs the gates, and the pair that
  found the gap must not amend its own review criteria**)
- R12 warns about an implicit parallel split only on the plan check, never at build time. Predates this
  ticket; spawned separately

## Build and test — four traps, each of which reads as a broken checkout

1. **Package:** `dotnet test tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf`
   → **1237 pass, 0 fail.** `dev-n8` fails with ~900 missing references because `.application/net-core`
   is absent. This repository has **no CI**; a local run is the only gate the package ever gets.
2. **clio:** `-c Release`, not Debug — a running MCP server holds the Debug output open and the build
   fails with `MSB3027` naming a file rather than a culprit.
   ```
   dotnet test clio.tests/clio.tests.csproj -c Release \
     --filter "Category=Unit&(Module=ProcessModel|Module=McpServer|Module=Command|Module=Common)"
   ```
   `Module=ProcessModel` is in that filter deliberately; leaving it out is the mistake that once shipped
   a red test.
3. **clio targets `net10.0` here.** `clio/bin/Release/net8.0` ships an older bundled archive and would
   install the wrong package while every later observation still looked valid.
4. **If the package build fails with `CS1705` naming `Terrasoft.Core`,** the `.application/net-framework/core-bin`
   junction is dangling because the platform tree moved. Do not conclude the checkout is broken. Either
   repoint it, or pass both properties:
   ```
   -p:TestCoreLibPath=<a real core-bin> -p:CoreLibPath=<the same>
   ```
   Both are needed — the test project reads the first, the package project the second.

## The question that paid off repeatedly on this ticket

For every guard, ask: **what mutation would redden a test?** Then run it. Three separate times, code no
test could falsify survived a first review here, and a further nine were caught by mutation afterwards.
A green suite is not evidence of coverage.

Two companion rules, both earned the hard way on this ticket:

- **A probe that can only come back one way is not evidence.** A truncated `head`, a grep at a path that
  moved, a string transformed by an unescaped literal, and a belief never checked against the one-line
  file that carries it — four instances, one failure. Before believing a negative, show the probe
  capable of a positive.
- **Reachability, not corpus absence, decides whether a guard stays.** "Zero in the shipped corpus" is
  evidence about today's data; "the input cannot arrive" is a property of the call graph. Only the
  second justifies deleting.

## What your review must produce

A ranked list, most severe first. For each finding: severity, `file:line`, the claim in one sentence, a
concrete failure (inputs/state → wrong output, crash, or silently wrong metadata), the evidence, and for
a coverage finding the exact mutation that leaves the suite green. If you cannot write the failure
paragraph, it is an observation, not a finding.

Then state explicitly, because AGENTS.md requires the sentences:

- `MCP reviewed` — or what needs updating
- `ClioRing compatibility reviewed, no Ring-consumed contract changed` — or the gate work owed
- docs reviewed for both commands, or why none is possible

**Resolve every Blocker and High before the PRs are opened.** Medium and Low are advisory.

## After you

Three pull requests, one per repository, into that repository's default branch — `main` for the package,
`master` for the other two. The clio PR body must name the package commit its bundled bytes came from,
which the rebundle commit message records. Reference `spec/reviews/` and both run reports so the reviewer
does not re-derive what has already been measured.
