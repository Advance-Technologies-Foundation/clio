# ENG-91853 — final pre-merge gate

**Date:** 2026-09-06
**Gate:** AGENTS.md gate 3 — the comprehensive adversarial review over the *entire* PR diff, required
before a contribution is marked ready to merge.
**Reviewer:** the verification session. Did not write this code, and did not fix anything found here.

| Repository | Base | Tip | Commits | Diff |
|---|---|---|---|---|
| clio | `a9deb32bc` (`master`) | `e61c8720b` | 53 | 58 files, +8589 |
| package | `7e93995` (`main`) | `8d54f70` | 29 | 32 files, +5126 |
| guidance | `84e2609` (`master`) | `adb88d4` | 9 | 7 files, +184 |

Pull requests: [clio#1398](https://github.com/Advance-Technologies-Foundation/clio/pull/1398),
crt-process-builder#45, clio-knowledge#135 — all open, none draft.

---

## A process failure in this gate, stated first

The five adversarial lenses this gate is supposed to fan out to **did not run.** Four produced zero
bytes in three hours; the fifth stopped after twelve minutes, still reading files, with no findings.
No completion notification arrived from any of them — and for two turns I reported "waiting on the
lenses" on the strength of that silence.

That is the exact error this ticket has named four times: **a probe that can only come back one way is
not evidence.** An absent notification is indistinguishable from an agent that never started. The
check that settled it was one `stat` of the output files.

The review below was therefore done directly, by hand. Coverage is uneven as a result, and the
unevenness is stated in *Limits* at the end rather than hidden.

---

## Baselines, reproduced rather than accepted

| Suite | Command | Result | Claimed |
|---|---|---|---|
| clio | `-c Release --filter "Category=Unit&(Module=ProcessModel\|McpServer\|Command\|Common)"` | **10144 pass / 0 fail**, 18 skipped | — |
| package | `-c dev-nf` | **1257 pass / 0 fail** | 1257 ✓ |
| guidance | `Clio.Knowledge.Bundle.Tests -c Release` | **131 pass / 0 fail** | 131 ✓ |

`CLIO*` analyzer diagnostics: **0**, on a `--no-incremental` rebuild. The probe is valid — the same
build reports 40 other warnings (36 × CS9107, 4 × CS0168), none in a file this branch touches.

`clio.mcp.e2e` compiles: 0 errors.

---

## Mutations — 16 run, 12 RED

The question that has paid off repeatedly here is *what mutation would redden a test*. Each of these
was applied to the real file, measured, and reverted; both trees verified clean afterwards.

### Package — `FlowKindRules.cs`, `ProcessGraphBuilder.cs`

| # | Mutation | Result |
|---|---|---|
| M1 | drop the `IsDecisionalGateway` exclusion from `EnsureNoStrayBranchBesideACondition` | **RED** (1) |
| M2 | stop counting the incoming flow: `? 0 : 1` → `0` | **RED** (2) |
| M3 | `unconditional < 2` → `< 3` | **RED** (3) |
| M4 | ignore the incoming kind when detecting a condition | **RED** (1) |
| N1 | invert the no-default test in `NormaliseForADecidingGateway` | **RED** (14) |
| N2 | scan siblings for a *conditional* instead of a *default* | **RED** (5) |
| R1 | rename any flow, not only toolkit-generated names | **RED** (1) |
| R2 | drop the already-correct short-circuit in `RenameForItsKind` | GREEN — *equivalent mutant*: the removed branch would re-assign an identical string |
| R3 | drop the `source == null \|\| target == null` guard | GREEN — see finding 4 |

### clio — `ProcessGraphValidator.cs`

| # | Mutation | Result |
|---|---|---|
| V1 | R18 threshold `< 2` → `< 3` | **RED** (1) |
| V2 | R18 severity `Error` → `Warning` | **RED** (1) |
| V3 | R18 drop the conditional-present guard | **RED** (5) |
| W1 | R8: drop the synthesized-gateway arm — the rebuild itself | **RED** (1) |
| W2 | R8: `outgoing[n].Count > 1` → `> 0` | GREEN — see finding 5 |
| W3 | R8: join arity `ins.Count < 2` → `< 3` | **RED** (3) |
| W4 | R8: drop the `choosingElements.Count == 0` fast path | GREEN — *equivalent mutant*, and the file's own comment says so |

Severity being pinned (V2) is worth calling out: a demotion of R18 to a warning cannot pass silently.

---

## Claims checked against something other than the claim

**The two late behaviour changes**, both landed after the previous gate had accepted the old
behaviour, were probed directly rather than read:

- **Declaration order** — `[conditional, plain]` and `[plain, conditional]` now produce identical
  kinds (`conditional` + `default`). Only array order differs, which *is* branch precedence and is
  supposed to. The change is right for a reason my own earlier advice missed: the precedence guidance
  tells an author to declare the conditional arm first, so the documented order was the failing one.
- **At most one default** — second unconditional branch refused, exactly one default survives.
- **Re-kinding a gateway's own default** — silent no-op, consistent with `4fc1e63`'s rule that a
  notice about a write that did not happen sends the caller hunting.
- **`IsTheDefault` deleted** — the deletion is justified by reachability, not by corpus absence: a
  flow is not its own sibling, so `hasDefault` is false for it by construction and the branch it
  corrected cannot be entered.
- **The rename cannot collide** — `AddFlow` refuses a duplicate endpoint pair and `FindTheFlowBetween`
  throws when two flows share one, so `SetFlow` never reaches `ReKindFlow` on an ambiguous pair.
- **The rename cannot break a stored reference** — the platform *does* resolve a flow by name
  (`FlowSchemaGeneratorUtilities.ActualizeFormulaParameter`, which explicitly branches on
  `ProcessSchemaConditionalFlow`), but `ElementName` arrives in the designer's validation *request*;
  nothing persists it. Probe validity: a real by-name resolution path was found, it simply turns out
  to be request-scoped.

**R18's Error severity** rests on a corpus claim, so the corpus was re-measured independently:

```
process schemas with flows        : 1663
sources >=1 conditional AND >=1 unconditional : 704  (non-gateway: 278)
sources >=1 conditional AND >=2 unconditional : 0
```

Zero, over every branch present, classifying by CLR class per the platform's own field codes
(`CI1` = SourceRefUId, read from `ProcessSchemaSequenceFlow.cs:50`). The probe demonstrably returns
positives — it found 704 of the neighbouring shape. My totals run ~4% under the numbers in the code
comment (1711/736/310), a methodology difference that does not touch the conclusion: the zero is
measured over a subset of their set, and it is the zero that carries the severity.

**The `[RequiresPackage]` floor at 1.4.0.60 while the refusal arrives in .64** is not an exposure.
`master` ships **1.4.0.57**; versions .58–.63 existed only on this branch. Every environment is
therefore either below the floor — refused with a clear message and offered .66 by convergence — or
at .66. There is no released path into the window.

**Bundled archive (the defect class that has recurred most on this ticket — eighth check):** the
committed `.gz` hashes to `d37aafcc…f0cf`, identical to the pin; `ExpectedArchiveVersion` 1.4.0.66;
`ExpectedProducingCommit` `94e6f88`; the only commit after it is the restamp `8d54f70`, touching
`descriptor.json` and nothing else. No source change sits outside the shipped bytes.

**Guidance** teaches the shipped behaviour: "in ANY declaration order", with the version the change
landed in. No article still claims gateways or default flows are unbuildable (probe validity: six
"not buildable" statements exist elsewhere in the tree, none about this feature). No article quotes
an error string the code no longer emits. The enforced-rule list — R1–R3, R7–R15, R17–R18 — matches
the ids the validator actually emits, exactly.

---

## Findings

No Blocker. No High.

### 1 — Medium · the clio PR body's archive provenance is stale and self-inconsistent

`clio#1398` body, lines 21, 76 and 111.

The body was appended to per round and never reconciled, so it now names the bundled archive three
different ways:

- line 21 — "moves to **1.4.0.63**, cut from crt-process-builder `4aed165` (restamp `ed5f25d`)"
- line 76 — "Archive **1.4.0.65** (from crt-process-builder `5621c73`, restamp `f746b61`)"
- line 111 — "Archive **1.4.0.66**", with no producing commit

What merges is **1.4.0.66**, cut from `94e6f88`, restamped `8d54f70`.

**Failure:** a reviewer follows the summary at the top of the PR, resolves `4aed165` in the package
repository, cuts or inspects that archive, and compares it against the pin `D37AAFCC…` — which was
produced from `94e6f88`. The hashes disagree, and the reviewer concludes the pin or the archive is
wrong when both are correct. The line the ticket brief specifically asked for — the clio PR naming
the package commit its bytes came from — is present but names the wrong commit.

The correct provenance *is* recorded, in the package PR (crt-process-builder#45, line 66). Nothing
needs discovering; the clio body needs reconciling to it.

### 2 — Low · the browser leg measured 1.4.0.65; 1.4.0.66 merges

`spec/eng-91853-gateways-and-flows/…-manual-test-run-2026-09-06c.md`, manifest `packageVersion: 1.4.0.65`.

That run's headline is "run against the package that actually merges" — the standard its own author
set — and it no longer holds. The run's first finding was that `setFlow` leaves a flow named for its
old kind; that finding was then fixed in .66. So the fix that came *out of* the browser leg is the
one thing no browser has seen.

Rated Low rather than Medium because I went looking for what that leg would have caught and did not
find it: the rename cannot collide, and the platform's by-name flow resolution is request-scoped
(both above). The gap is in the evidence, not — as far as I can reach — in the behaviour.

> **Correction, after this report was committed.** This finding originally read that flow names are
> "precisely what the designer's element list and the process log display". A stand measurement by
> the implementation session falsified the first half. What was actually measured, stated no more
> strongly than that: **with a process open, no flow name appears anywhere in the page text, and the
> list beside the canvas holds ELEMENTS, not flows.** A selected connection's own properties were
> never inspected — the click on the connector did not select it — so whether some inspector surfaces
> the name remains unmeasured. On that evidence the browser leg would not have observed the rename
> through the element list, and the gap this finding describes is thinner than stated; the evidence
> that matters is `describe` plus the process log, verified at .66 and again after each rebundle.
>
> A first version of this correction wrote "the designer displays a flow's code nowhere" and said the
> connector click surfaced nothing. Both overstate the measurement, and the implementation session
> pushed back on exactly that: replacing one overstatement with a tidier one is the same defect. The
> thin claim stands.
>
> I did not measure the original claim either — I inherited it from the package's own comment at
> `ProcessGraphBuilder.cs:246` and repeated it into a review report. Which makes it an instance of the
> failure this report's process note names, one level down: a statement believed because it was
> written down.
>
> **The claim had nine sites, not one.** Five were production text and are corrected: four in the
> package (`DescribeContracts.cs` — XML doc on a public contract field — `ProcessGraphBuilder.cs`
> twice, and `ProcessDesignConstants.cs`) plus one a repository away, `DescribeProcessTool.cs:35` in
> clio, whose tool `[Description]` an agent reads on every `describe-business-process` call. That
> fifth one is the pattern worth keeping: **a claim quoted across a repository boundary is invisible
> to a grep of either repository alone.** Four more still carry it, and they are invisible to a grep
> scoped to production source: `ProcessFlowKindTests.cs:129` (a `[Description]`) and `:1025`,
> `ProcessConditionalFlowTests.cs:309` (both `because:` prose), and this feature's own
> `eng-91853-gateways-and-flows-traps.md:196`. Under AGENTS.md's test-style policy those strings are
> the repository's statement of intent, so they will be read as authority.
>
> The rename remains justified throughout by the log and the metadata diff — two of the three readers
> the comments claimed. It is the rationale that overstated, never the behaviour.

### 3 — Low · the sprint tracker still says nothing is pushed

`spec/sprint-status.yaml:3231` and `:3358` both read *"Nothing is pushed and no pull request exists."*
Three pull requests are open. The three stories are correctly at `status: review`; only the notes are
stale.

### 4 — Low · an unreachable guard kept, in the same PR that deleted one for being unreachable

`ProcessGraphBuilder.cs`, `RenameForItsKind` — `if (source == null || target == null) { return; }`.

Mutation R3 deletes it and the suite stays green, because the input cannot arrive: `RemoveElement`
removes every flow touching the element it detaches (`schema.FlowElements.Remove(flow)`), and the
platform's `TargetRefUId` setter refuses a dangling endpoint, so no flow in `schema.FlowElements` can
hold an endpoint that fails to resolve.

This is not wrong code — a defensive null on a `FirstOrDefault` is ordinary. It is worth one sentence
only because commit `5621c73`, in this same PR, deleted `IsTheDefault` with the reasoning *"Kept, it
would have been a branch no input reaches."* Two unreachable branches, opposite dispositions. Either
is defensible; the pair is not.

### 5 — Low · R8's new arm re-introduces the redundant fast path the comment above it condemns

`ProcessGraphValidator.cs:431` — `outgoing[n.Name].Count > 1 && …Any(o => o.FlowKind == Conditional)`.

Mutation W2 relaxes `> 1` to `> 0` and the suite stays green, and the mutation is genuinely
equivalent: `DivergesIntoTwoBranches` requires two branches leaving the element **by different
edges**, so an element with one outgoing edge can never satisfy it — every non-empty per-branch set
is the same singleton and they always overlap.

The comment ten lines above, at `:419`, explains that an arity filter was *deleted* from this very
rule because "a mutation showed it could not fail. The filter was a fast path no test could
distinguish from the check it guarded, which is the shape of code that rots." The new arm adds one
back.

### 6 — Low · stray comment marker

[`clio/Command/CreateBusinessProcessCommand.cs:61`](clio/Command/CreateBusinessProcessCommand.cs:61)
ends `…described above.//` — a leftover from an edit, in a file whose comments are load-bearing.

---

## Statements AGENTS.md requires

**MCP reviewed.** The four process-designer tools, their prompts, and `McpCapabilityMap.md` were
updated with the behaviour change; `clio.mcp.e2e` gained coverage for all three changed tools and
compiles. No tool was renamed or removed, so no `McpToolCompatibilityCatalog` entry is owed. The
tool-surface model itself is untouched: the diff contains no change to `McpCoreToolProfile`,
`clio-run`, `get-tool-contract`, the compatibility catalog, or `McpServerInstructions`.
`WorkspaceTemplateGuidanceDriftTests` passes inside the green `Module=McpServer` run, which
transitively confirms `curated-knowledge-names.json` is re-pinned to the new generation.

**ClioRing compatibility reviewed, no Ring-consumed contract changed.** Ring's live consumer surface
was enumerated by searching `clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing` and
`clio-ring/ClioRing.Desktop/actions.json` rather than taken from a list: it names `clio-run`,
`get-tool-contract`, `list-packages`, `list-environments`, `restart`, `deploy-creatio`, `deploy-app`
and `uninstall-creatio`. No process-designer tool appears, under any name, including as a nested
`clio-run` command — and the probe returns positives, since it found those eight. The diff touches
none of the dispatch, contract-output, progress or lifetime surfaces the gate enumerates.

**Docs reviewed.** The four CLI documentation targets (`clio/help/en/*.txt`,
`clio/docs/commands/*.md`, `clio/Commands.md`, `WikiAnchors.txt`) **do not apply**: neither
`CreateBusinessProcessCommand` nor `ModifyBusinessProcessCommand` carries `[Verb]` or is registered in
`Program.cs`, so no CLI surface exists to document. The applicable surfaces — tool descriptions,
prompts, `McpCapabilityMap.md` and the guidance library — were all updated, and the guidance was
checked against current behaviour above.

**Knowledge base.** `scripts/check-knowledge-applies-to.py` reports one record pointing at a path that
no longer exists — `docs/knowledge/McpServer/odata-write-transport-never-throws-on-non-2xx.md` →
`clio/Command/McpServer/Tools/ODataResponseError.cs`. **Pre-existing on `master`**; this branch does
not touch it. Spawned separately rather than attached here.

---

## Limits of this gate

Stated because the fan-out failed and the coverage is genuinely uneven:

- **Well covered** — flow-kind semantics, the two late behaviour changes, R8/R18, mutation coverage of
  everything added since the previous gate, the bundled-package machinery, the corpus argument, the
  three baselines, ClioRing, docs applicability, guidance-vs-behaviour consistency.
- **Thin** — general robustness and input handling. I did not systematically fuzz the MCP argument
  surface for null/empty/oversized/duplicate input, and three leads carried over from the previous
  gate remain unre-verified: the three flow/condition combinations reported to validate clean and
  then be refused at build, `[#Read1.ResultEntity.Amount#]` reaching the platform's unnamed error,
  and the 2048-character bound being rewritten after it is applied by `ResolveOnBuild`. Those are
  owner-flagged mediums from the previous round, not new.
- **Not attempted** — anything needing a running stand. Finding 2 is the standing consequence.

## Verdict

**No Blocker, no High.** One Medium and five Low, all advisory under the gate's own severity rule.

Finding 1 is worth fixing before merge, not because the code is wrong but because the PR body is the
only place a reviewer can learn which source produced a binary blob, and it currently points at two
superseded commits.

Findings 2–6 do not block. Findings 1, 2 and 3 are one defect wearing three hats: a document that
records "as of now", is appended to each round, and is never reconciled. If anything here is worth
carrying past this ticket, it is that.

---

## Closure

Added rather than edited into the text above, because a report that quietly rewrites its own findings
is the defect findings 1–3 are about.

| # | Severity | Disposition |
|---|---|---|
| 1 | Medium | **Closed.** Provenance reconciled to one line — 1.4.0.67 from `126d63b` (restamp `8fccbb2`), sha `BF9596E5…8DC1`. Both superseded provenance lines removed. |
| 2 | Low | **Narrowed by measurement, then closed.** See the correction under the finding: the designer shows no flow name, so the leg could not have observed the rename. Re-verified at .67. |
| 3 | Low | **Closed.** Both stale tracker notes removed. |
| 4 | Low | **Closed.** The guard is gone; both endpoints now resolve through `NodeByUId`, the file's existing `First()` convention. |
| 5 | Low | **Closed.** R8's arity arm removed. |
| 6 | Low | **Closed.** Stray marker removed. |

Two things came out of the closures that the gate itself had not reached:

- **R3 forced a rebundle to .67.** Removing a dead branch is behaviour-neutral, and it still changed
  the source — so the shipped bytes were no longer the reviewed ones. Stated as a rule, ninth
  occurrence of this class on the ticket and the first time it has been written down: *a source change
  absent from the archive means the shipped bytes are not the reviewed ones, even when the change is
  behaviour-neutral.*
- **`First()` now depends on finding 4's reachability argument**, so it was re-derived rather than
  reused. It holds, and more strongly than first stated: `SetFlow` resolves both endpoints at
  `ProcessGraphBuilder.cs:328` before anything else, and `FindTheFlowBetween` matches a flow on exactly
  those two UIds — so at the single call site (`:623`) both elements are already proved present. Not
  "no dangling flow can exist", but "this call resolved them three lines earlier".

Baselines re-verified across the closures: package **1257 / 0** at .67 and again at .68; clio
`Module=ProcessModel|McpServer` **4814 / 0**, 2 skipped. Archive pins verified clean at .66, .67 and
.68 — the eighth, ninth and tenth checks of this class on the ticket, each time SHA, version,
producing commit, and *only a descriptor restamp after it*.

A comment-only correction forced the rebundle to **.68**, which is the rule above applied to its
least intuitive case: in a source-only package the comments **are** the artifact, so changing one
changes the shipped bytes.

**Gate satisfied.** One item raised after closure and not blocking: four remaining sites still assert
that the designer's element list shows a flow name — three test strings in the package and this
feature's own traps document (see the correction under finding 2). Not blocking because nothing
behavioural depends on them; worth closing because two are `[Description]`/`because:` prose, which
AGENTS.md makes the repository's statement of intent.
