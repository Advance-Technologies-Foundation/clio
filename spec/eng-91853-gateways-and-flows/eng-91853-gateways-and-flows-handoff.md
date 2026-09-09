# ENG-91853 — State of the branch, and what is left

> **PULL REQUESTS ARE OPEN (2026-09-06).** Everything below describes how the branches got here; the
> branches are pushed and the three pull requests exist.
>
> | Repository | PR | Base |
> |---|---|---|
> | `crt-process-builder` | [#45](https://creatio.ghe.com/engineering/crt-process-builder/pull/45) | `main` |
> | `clio` | [#1398](https://github.com/Advance-Technologies-Foundation/clio/pull/1398) | `master` |
> | `clio-knowledge` | [#135](https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/135) | `master` |
>
> The stand is clean: the six process instances this ticket's runs left parked on user tasks are
> cancelled. The eleven test PROCESSES stay — they are the manual legs' evidence. Six instances still
> read `Running` on that stand and none are this ticket's: four date from 2019, one from 2020, and
> `Parallel check confirmation` from 00:32 belongs to an earlier session.
>
> One gap is stated in the clio PR body rather than hidden: the new `clio.mcp.e2e` cases **compile and
> have never been executed** — that suite is manual-only and its sandbox `EnvironmentName` is unset
> here. The runtime evidence comes from the manual legs, not from that suite.

Updated 2026-09-06, after the review gate closed. Everything is implemented, unit-green and
mutation-checked in all three repositories. **Nothing is pushed and no pull request exists.**

Three things remain, and none of them is "build":

1. the **browser leg** — `/bp-test-run ENG-91853 --mode browser --env Creatio`. V2–V9 passed in the
   review session, and the second agent-mode run (2026-09-06) attempted **all 15 cases** and found no
   new defect at the stored level. What storage cannot prove is that the platform AGREES with the text:
   that a default branch renders as the fallback, that a three-way join waits for all three, and that an
   expanded condition evaluates. **An agent run is never a pass for the feature.** Eleven processes are
   left in `Custom` on `Creatio` as that run's input;
2. three pull requests, one per repository, into that repository's default branch.

Both design decisions the owner had open are now settled and implemented — see §4. The stored-level
evidence is in §5.

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
→ **1235 pass, 0 fail** (baseline at the start of the ticket: 1149). This repository has **no CI at
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

## 3b. The second review gate (2026-09-06) and what it changed

[spec/reviews/review-eng-91853-gateways-and-flows-2026-09-06.md](../reviews/review-eng-91853-gateways-and-flows-2026-09-06.md).
One High, no Blocker, eleven Mediums, ~20 Lows — all actioned. The five that mattered:

- **The 88% claim was wrong by 242 flows.** Re-measured here: 242 of the 487 element-output conditions
  address a COLUMN through a third meta-path segment the name form cannot express, so by-name coverage
  is **65%**, not 88%. The sentence it came from — 932 of 1 061 carry a UId that cannot exist yet — is
  still true, so the error was the inference, not the measurement. Both numbers now appear separately in
  five places.
- **R7/R9's no-default warning fired where its own message is false.** A diverging or-gateway carrying a
  plain flow does not stop at run time; the runtime takes that flow as the default. Seven shipped
  gateways are in that shape and all seven reach the rule through describe-then-validate.
- **Seven guards nothing could redden** (M2, M7–M11 plus the or-gateway type filter) now have tests
  confirmed RED under their own mutation.
- **Two write-path messages were wrong**: a `setFlow` that changed nothing raised a notice about a write
  that did not happen, and the refusal for clearing a default branch advised making it the default it
  already was.
- **The e2e gap AGENTS.md calls mandatory is closed on paper**: cases now send `setFlow`, `flows[].kind`,
  `flows[].condition` and a gateway type token through the real MCP path. They **compile and have never
  run** — the suite is manual-only and its sandbox `EnvironmentName` is unset here. The PR body must say
  the e2e evidence is compile-only.

**Archive is 1.4.0.62; the `[RequiresPackage]` floor stays at 1.4.0.60** — the floor states what the code
NEEDS, and .61/.62 change only comments and two refusal wordings. Be exact about which archive buys what:
`flows[].kind` and the gateway type tokens arrive in **.58**; the by-name condition expansion in **.60**.

**One rule this gate produced, now in `docs/agent-instructions/bundled-packages.md`:** rebundle when the
archive's BEHAVIOUR changes, and once more at the end for everything else. A raised bundled version
hard-blocks every `[RequiresPackage]`-triggered command on any environment below it, and for a
source-shipped package that is a configuration build plus a restart.

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

### The two decisions, both SETTLED by the owner on 2026-09-06

**Layout §4 case B — option 2, implemented.** A merge with a column-skipping inbound branch takes THAT
branch's lane instead of the mean of its arriving lanes. It was never the conflict the addendum called
it: the corridor IS where the merge belongs, because it is the row that connector already occupies. Rows
A and E of the verification table are untouched (neither has a skipping branch), all 27 pre-existing
layout tests pass unchanged, and row B's ✔ is now honest. Package commit `180025a`.

**D1 — implement here, done.** `flows[].condition` takes a parameter NAME on the build path
(`[#Amount#]`, `[#Element.Parameter#]`) and `ConditionParameterNames` expands it to the UId meta-path
after `ApplyDeclarativeContent`, which is the only order that works — a `typeFromElement` parameter is
added there. Package commit `feae3ff`, shipped in **1.4.0.60**, floor raised to match.

The rule is narrow toward doing nothing: only a bare identifier, or a dotted one whose head is an
element of this schema. `[#SysSettings.Code<Type>#]`, `[#Lookup.Schema.Record#]` and both meta-path
spellings pass through. A bare name that resolves to nothing is REFUSED naming the flow and listing what
exists — safe because no macro family is a single identifier. The modify path is deliberately excluded:
its UIds exist, and a whole-schema pass there could rewrite a designer-authored condition.

Two guards in that pass were caught unfalsifiable BEFORE shipping, which is the first time in this
ticket that happened in the right order. The body-length cap could not be reddened by any mutation —
every condition arrives through `AddFlow`'s 2 048-character bound — and was deleted. The bracket guard
also survived its first mutation and turned out to be load-bearing for an unreached reason: the platform
emits a parameter reference with AND without the `[IsOwnerSchema:false].[IsSchema:false].` prefix, and
the short form carries no dot, so without it a valid hand-written reference is read as a name and
refused. It has a test now.

The texts turned over a second time, exactly as this document predicted they would: the create tool's
description, `docs/McpCapabilityMap.md`, `branch-conditions.md`, `formulas.md` and the knowledge record
(renamed, because its subject reversed) all now say the build path takes a name.

---

## 5. Stand verification

The full table is [plan §4](eng-91853-gateways-and-flows-plan.md#4-stand-verification-the-part-no-unit-test-can-cover).
V2–V9 passed in the verification session. **V1 (designer glyphs, in the browser) is outstanding** and is
the owner's own check.

### The agent-mode run of 2026-09-06 — stored level, all 15 cases

Report and manifest: `eng-91853-gateways-and-flows-manual-test-run-2026-09-06b.md`. Read back from the
stand rather than from the executor's transcript, which is the only thing that settles it.

**The one result this ticket most needed**, because 1.4.0.60/.61 had no stand evidence at all. A blind
agent chose to write a build-path condition as a NAME, unprompted, and the expansion ran:

```
sent   : [#AmountParameter#] > 100
stored : [#[IsOwnerSchema:false].[IsSchema:false].[Parameter:{874f9328-…}]#] > 100
```

The stored form ALONE cannot tell an expansion from a hand-written meta-path — they are byte-identical
in `CI3`. What settles it is what was SENT. And the same agent used the UId meta-path on a later
`setFlowCondition`, which is the first evidence that `branch-conditions.md`'s split-by-call is followed
rather than merely written.

Also proven at the stored level: the self-loop refusal has no hole (build exits 1, no schema is created,
and R15 says the same thing in the same words); a re-kind preserves POSITION rather than appending (the
default came back in the MIDDLE of three flows); a conditional + default straight off an activity with no
gateway element builds, which is half the real corpus and had never been tested; and a three-branch AND
split and join builds.

The prompt fix is measured rather than asserted — run 1 delegated the suite to sub-agents and reached 6
of 11 cases in 9 of its own turns; run 2 delegated nothing, took 126 turns, attempted 15 of 15, and
errored on 2 tool calls instead of 9.

**One finding, not this ticket's.** Two plain flows off one element get R12 from the plan check and
NOTHING from `create-business-process`, which returns 0 in silence — so the net hangs on a step an agent
may skip. R12 and the silent build path both predate this change; spawned separately.

Three constraints that are not in the table:

- **Run schema-write operations SEQUENTIALLY.** A parallel burst trips IIS rapid-fail and downs a
  .NET Framework stand's application pool.
- **Only `clio/bin/Release/net10.0` carries the 1.4.0.60 archive.** The rebundle script rebuilds one
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
