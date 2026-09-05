# ENG-91853 — State of the branch, and what is left

Updated 2026-09-06, after the review gate closed. Everything is implemented, unit-green and
mutation-checked in all three repositories. **Nothing is pushed and no pull request exists.**

Two things remain, and neither is "build":

1. the **stand verification** V1–V9 from [plan §4](eng-91853-gateways-and-flows-plan.md), of which only
   V1 is outstanding — it is a browser check, which the owner does personally;
2. one **design decision** that is the owner's and not a reviewer's — layout §4 case B, below.

Then three pull requests, one per repository, into that repository's default branch.

---

## 1. The branches

| Repository | Branch | Base | Commits | Review with |
|---|---|---|---|---|
| `crt-process-builder` (`C:/Projects/workspace/ProcessBuilder`) | `feature/ENG-91853-gateways-and-flows` | `7e93995` (**`main`**, not master) | 12 (head `92bab27`) | `git diff 7e93995 HEAD` |
| `clio` (`C:/Projects/clio`) | `feature/ENG-91853-gateways-and-flows` | `a9deb32bc` (`master`) | 18 first-parent | `git diff a9deb32bc HEAD` |
| `clio-knowledge` | `feature/ENG-91853-gateways-and-flows` | `84e2609` (`master`) | 3 (head `6ea736c`) | `git diff 84e2609 HEAD` |

`clio-knowledge` must be worked in its linked worktree `.worktrees/eng-91853` — that repository's
`AGENTS.md` makes the root checkout coordination-only.

**One thing to decide before the clio PR opens.** The branch carries a merge of
`docs/bp-manual-test-skills` (`709a12a21`), which is on `origin` but **not on master**. Its own
pull request is #1288. If that merges first the commit is a no-op in this diff; if not, this PR
carries the two skill files as well. Either is fine — just say which in the PR body.

---

## 2. Running the tests

**Package** — `dev-nf`, not `dev-n8`: `.application/net-core` is absent on this host, so `dev-n8` fails
with ~900 missing-reference errors that read like a broken checkout.

```
dotnet test tests/CrtProcessBuilder/CrtProcessBuilder.Tests.csproj -c dev-nf
```
→ **1224 pass, 0 fail** (baseline at the start of the ticket: 1149). This repository has **no CI at
all**, so a local run is the only gate the package ever gets.

**clio** — `-c Release`, not Debug: a running MCP server holds the Debug output open and the build fails
with `MSB3027` naming a file rather than a culprit.

```
dotnet test clio.tests/clio.tests.csproj -c Release \
  --filter "Category=Unit&(Module=ProcessModel|Module=McpServer|Module=Command|Module=Common)"
```
→ **10 136 pass, 0 fail**. `Module=ProcessModel` is in that list deliberately; leaving it out is the
mistake that shipped a red test earlier in this ticket.

**clio-knowledge**

```
dotnet test automation/Clio.Knowledge.Bundle.Tests/Clio.Knowledge.Bundle.Tests.csproj -c Release
```
→ **131 pass, 0 fail**.

**clio.mcp.e2e** — `ValidateProcessGraph_Should_BindEdgeCondition_FromTheWire` is written and compiles
but has **never been executed**: that suite is manual-only and its sandbox `EnvironmentName` is unset in
`clio.mcp.e2e/appsettings.json`. Run it with the stand verification. It is the only thing that exercises
the JSON binder for `flows[].condition`; the unit tests build the record positionally in C#.

---

## 3. What the review gate found, and what was done

The comprehensive gate ran on 2026-09-05 —
[spec/reviews/review-eng-91853-gateways-and-flows-2026-09-05.md](../reviews/review-eng-91853-gateways-and-flows-2026-09-05.md).
Everything in it is now actioned. Do not re-derive these:

**Blockers.** The 1.4.0.58 restamp was uncommitted in the package tree (`1fc5944`). `setFlow` with an
omitted `kind` read as `sequence`, DESTROYED a conditional branch and reported success (`78b87fe`).

**Highs.** R7/R9's new "diverging gateway carries a plain sequence flow" finding shipped as an ERROR and
rejects 7 shipped or-gateways — demoted to a warning. R14's arity scope took the shipped
counter-examples from 45 to 1, not 0, and the survivor needed an exemption for a plain sibling leading
into a gateway. `create-business-process` never named the two gateway type tokens and its description
contradicted its own `flows[]` paragraph. `branch-conditions.md` said there is no clear-condition
operation, which `setFlow kind=sequence` now is.

**Nine unfalsifiable guards.** Eight are now pinned by a test that was confirmed RED under the mutation
that removes the guard (P1–P4, P6, P8, plus clio's M1 and the R8 gap M2). The ninth, `ReKindFlow`'s
`if (createdInSchemaUId != Guid.Empty)`, is **unreachable** rather than untested — an empty capture
implies an empty `schema.UId`, which implies the backfill writes empty too, so both arms agree. The
measurement is written next to it; do not chase it and do not write a fixture that assigns `schema.UId`
between the add and the re-kind to force it.

**Mediums.** All ten actioned: the build path now bounds a condition's length at `AddFlow` (the funnel
both add paths share), two factory tests stopped using a buildable element as their unbuildable one, the
`1 522` corpus denominator is gone, `docs/McpCapabilityMap.md` and `activity-connections.md` describe
the current product, `branch-conditions.md` no longer routes callers into a hard refusal, sprint story 3
is `in-progress`, and the e2e binder test exists.

**The one recurring lesson**, worth carrying into any future round on this code: four separate times in
this ticket, code that no test could falsify survived a review. Ask of every guard, *what mutation would
redden a test?* — and run it.

---

## 4. Settled by measurement — do not spend a round re-deriving these

The ticket's own DO-NOT list is in
[implementation-prompt](eng-91853-gateways-and-flows-implementation-prompt.md); it still holds. On top
of it:

- **A duplicate element `UId` crashing the layout / the builder.** Unreachable:
  `MetaItemCollection.InsertItem` throws `ItemAlreadyExistException`.
- **A flow with a dangling endpoint being constructible.** `set_TargetRefUId` calls
  `GetBaseElementByUId`, which throws. The reachable shape is the reverse — the element removed AFTER
  its flows — and that is what the tests build.
- **Layout performance.** ~0.2 ms at the largest shipped process (~300 elements), against a modify call
  measured in tens to hundreds of milliseconds.
- **`VisualType` being what makes the metadata say AutoPolyline.** It is not — the writer passes the
  LITERAL. `docs/knowledge/platform/sequence-flow-visualtype-is-written-as-a-literal.md`.
- **Rule-id reuse breaking a consumer.** Nothing in `clio` or `clio-ring` filters, counts or keys by
  rule id.
- **The corpus.** 1 711 schemas, 1 405 conditional flows (337 of them result-driven), 757 default flows,
  7 599 plain. The branch's prose says 1 406 / 756 / 7 600 — within one, from a scan-boundary
  difference, and deliberately left consistent rather than made a third number.

### The open decision

Layout §4's **case B** is marked ✔ in its verification table and is **not fixed**; it cannot be fixed by
placement. Three of the ticket's commitments conflict for that one shape and connector routing is out of
scope. The measurement, the three options and a recommendation are in
[layout-addendum §2](eng-91853-gateways-and-flows-layout-addendum.md). It needs a decision, not a fix.

---

## 5. Stand verification

The full table is [plan §4](eng-91853-gateways-and-flows-plan.md#4-stand-verification-the-part-no-unit-test-can-cover).
V2–V9 passed in the verification session. **V1 (designer glyphs, in the browser) is outstanding** and is
the owner's own check.

Three constraints that are not in the table:

- **Run schema-write operations SEQUENTIALLY.** A parallel burst trips IIS rapid-fail and downs a
  .NET Framework stand's application pool.
- **Only `clio/bin/Release/net10.0` carries the 1.4.0.59 archive.** The rebundle script rebuilds one
  output; `Debug/net10.0`, `Debug/net8.0` and `Release/net8.0` still ship an older one, and an install
  run from any of them will verify the wrong package. Debug could not be the one rebuilt, because of the
  MCP-server lock above.
- **The owner verifies UI results in the browser personally** — do not auto-open a browser after a
  successful write.

For a fuller manual pass, the branch now carries the `bp-test-cases` and `bp-test-run` skills; AGENTS.md
describes how they split the agent run from the browser run.

### One environment repair this host needed

The crt-process-builder checkout could not build at all until it was fixed:
`.application/net-framework/core-bin` was a junction into a Creatio core that had been reinstalled away,
and every build failed with ~900 `The name 'Terrasoft' does not exist` errors that read like broken
package sources. Recorded in
[docs/knowledge/process/rebundle-needs-a-working-core-bin-in-the-package-checkout.md](../../docs/knowledge/process/rebundle-needs-a-working-core-bin-in-the-package-checkout.md).

---

## 6. Then the pull requests

One per repository, into that repository's default branch — `main` for `crt-process-builder`, `master`
for the other two. The clio PR body should name the package commit its bundled bytes came from
(`92bab27`, version 1.4.0.59), which the rebundle commit message already records, and should say what
happened with #1288.
