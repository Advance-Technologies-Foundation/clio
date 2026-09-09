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

Baselines re-verified across every closure: package **1257 / 0** at .67, .68 and .69; clio
`Module=ProcessModel|McpServer` **4814 / 0** (2 skipped), `Module=Common` **1292 / 0** (3 skipped),
pins + `WorkspaceTemplateGuidanceDrift` **31 / 0**. Archive pins verified clean at .66 through .69 —
the eighth to eleventh checks of this class on the ticket, each time SHA, version and producing
commit, with the last two read by the corrected rule below rather than the wrong one.

Two comment-only corrections forced rebundles, to **.68** and **.69**, which is that rule applied to
its least intuitive case: in a source-only package the comments **are** the artifact, so changing one
changes the shipped bytes.

### The B1 rule, corrected by decoding the archive

This class was checked ten times on this ticket against the wrong rule. I had been asserting *"only a
descriptor restamp may sit after the producing commit"* — which would have raised a **false alarm** the
first time a legitimate non-archived change landed after a rebundle, as one did (a tests-only commit
after `b7f5e14`).

The right rule comes from what the artifact actually contains. Decoding the committed `.gz` with the
format `rebundle-process-builder.ps1` documents — `[int32 nameLength][UTF-16LE path][int32
contentLength][bytes]` — gives **129 entries: `descriptor.json`, `Files/` ×123, `Resources/` ×1,
`Schemas/` ×4, and zero whose path mentions "test"**. So:

> No commit after the producing commit may touch `descriptor.json`, `Files/`, `Resources/` or
> `Schemas/` under the package directory — the restamp's own `descriptor.json` edit excepted.
> Everything else in the repository, `tests/` included, is outside the bytes.

Run that way against `b7f5e14..HEAD`: the restamp's `descriptor.json` (the exception) and two files
under `tests/` (outside every archived path). Clean.

The converse also has a least-intuitive case, met on this ticket: a **comment-only** change to package
source *does* change the artifact, because in a source-only package the comments are compiled by the
target and shipped as the source itself. `.68` was cut for exactly that.

### A correction that drifted back, inside the correction

Of the five production sites fixed, two state only what was measured — `DescribeContracts.cs:888`
carries the exemplary form, an explicit *"whether a selected connector's own properties expose a code
was not established"* — and two re-assert the universal negative the measurement does not reach:
`ProcessDesignConstants.cs:218` (*"shows a flow's code nowhere on an open process"*) and the
`describe-business-process` tool `[Description]` (*"the DESIGNER shows a flow's name nowhere"*), the
latter read by an agent on every call.

In the tool description only the justification was affected: *"do not send a caller there to find it"*
is sound whether or not an inspector exists, because the element list does not show it. Right
instruction, overstated reason — the original defect's exact shape, recurring inside its own fix.

**Both closed.** The bound is now explicit in each: `ProcessDesignConstants.cs:219` scopes its
"nowhere" to *an open process's page text* and adds *"(measured; a selected connector's own properties
were never inspected, so that much and no more)"*; the tool `[Description]` now reads *"the designer's
element list does NOT show it and no flow name appears in an open process's page text, so do not go to
the designer to find it"* — instruction intact, justification bounded. `ProcessDesignConstants` is
inside `Files/`, so a one-clause comment owed the rebundle to **.69**; the corrected B1 rule above says
so in both directions, and this was the direction it fired in.

**Gate satisfied. Nothing outstanding.** Nine sites bounded to the measurement, all findings closed,
no Blocker or High at any point.

### What this exchange is actually about

One comment reached nine places by being quoted, and the count rose 4 → 5 → 9 as the search widened:
the **repository** boundary hid one site, and the **source/prose** boundary hid four — test
`[Description]`/`because:` strings and a spec document, which AGENTS.md makes the repository's
statement of intent and which no grep of `src/` sees.

The sharpest instance is not the count. One of those sites was written *in the same session that was
reasoning carefully about the claim*, because it arrived as an established sentence rather than as
something to check. That is the mechanism: a claim propagates in the form of prose, and prose is not
read as a claim.

And the correction is not exempt from it — twice above, the tidy version came back. **A correction
that quietly tightens is the defect it was correcting.** Its companion, which is the half that tells
you where to look: **the correction is the most likely place for the defect to reappear, because that
is where the writer is most confident.** Neither of us caught our own; each caught the other's. If
that pair ever becomes a check, that asymmetry is why it cannot be a self-check.

### The search that was cheap, and the four rounds that were not

Four rounds hunted the claim that was **wrong** — "the designer's element list shows a flow name" —
and found nine sites. None of those rounds found a second stale family sitting in the same files: four
docs asserting *"a re-kind deliberately keeps the existing name"*, false since `.66`, two of them
twelve and five lines from text being edited at the time. A reviewer reading only the PR comments
found them; PR review caught what four targeted sweeps did not.

One grep in the other direction then found **five more** — the "lone unconditional continuation"
family, stale since `5621c73`, including the `create-business-process` tool `[Description]`. So the
count went 4 → 5 → 9 for one claim and 0 → 4 → 9 for the other, and the difference was never effort:

> The question is not *"where else did we say the wrong thing"* — that needs the wrong thing named
> first. It is **"what did this change, and where is that described"**, which is answerable from the
> diff, and therefore cheap and complete rather than lucky.

A closing instance, recorded because it is the same failure one level up: verifying that five-site
sweep, this review's own greps under-reported **twice** — missing four package sites on the case of a
word, then concluding the clio tool stated no rule at all when it states it in different words. Both
misses have one cause: searching for the other session's *sentence* instead of for the *concept*. The
verification carried the defect it was verifying.

And its counterpart, from the Sonar round — recorded here in its **corrected** form, because the first
version of this paragraph was wrong and the way it was wrong is the better lesson.

A MINOR smell was reported at `ProcessGraphValidator.cs:439` and neither session could say which loop
it meant: the file had changed eleven times on the branch and nothing at that line matched the rule.
Reading line 439 against the revision the report was written for (`d014b6275`) produced
`foreach (string seed in queue) { visited.Add(seed); }` — canonical S3267, exact line match, present
twice. That looked like identification and this report wrote it up as a transferable method:
*"a stale line number is still evidence, if you read it against the revision it was written for."*

**It was the wrong loop.** A current analysis carried the rule's own message —
*"Loop should be simplified by calling Select(node => node.Name)"* — and pointed at `:471`, a loop
that reads nothing but `node.Name`, twice. So the honest statement is narrower:

> A stale line number read against its own revision produces a **candidate**. An exact line match on
> a canonical instance of the right rule *family* is not identification. What identifies an issue is
> an analysis carrying its message.

The overconfidence is the part worth keeping. A method that produced a plausible answer was written up
as a method that produces the right one, without anyone asking **what would distinguish the two** —
which is the same failure as every other instance on this ticket, one layer up: applied to the tool
rather than to the claim. The `UnionWith` rewrite that came out of it stays, because the consumer
analysis below shows it equivalent and simpler, but it fixed nothing Sonar had asked for.

Same shape as the two grep misses either way: the search was not bad, it was aimed at the wrong
artifact. The line number was never ambiguous; the revision was — and then the rule family was.

### The rule that supersedes this report's own discipline

Four instances across two sessions, and they are the same failure:

| Probe | Returned | Could not return |
|---|---|---|
| grep for the other session's sentence | 0 hits | the same rule in different words |
| grep for "in any declaration order" in the clio tool | 0 hits | *"in any order you declare them"* |
| `project_analyses/search` for the PR's analysis | a master merge commit | the pull request's own analysis |
| a stale line read against its own revision | an exact line match | the rule id that would confirm or deny it |

Every one of those four passes the discipline this report opened with — *"a probe that can only come
back one way is not evidence"* — because each demonstrably returned positives elsewhere: the corpus
grep found 704 neighbouring cases, the "not buildable" sweep found six, `project_analyses` returned
real analyses, the revision match found a real loop. Validating that a probe *can* answer is not
enough, and that is why this discipline let four through:

> **Before trusting a probe, say what it would have to return to be wrong.** Not whether it can return
> anything — whether it can return the specific answer that would falsify the conclusion being drawn.

None of the four could. That is the sharper statement of the rule, it is the one to put first, and it
was arrived at by the other session — after this review had already recorded the weaker version twice
and then failed it twice.

### Three guards, three verdicts, one method

The ticket produced three of one shape, and it is worth recording together because each time the
answer came from the **consumer** and never from the code under the cursor:

| Guard | Mutation | Verdict |
|---|---|---|
| `RenameForItsKind`'s null-endpoint check | delete → suite GREEN | unreachable input — `SetFlow` resolves both endpoints three lines earlier; **deleted** |
| R8's `outgoing[n].Count > 1` arity arm | `> 1` → `> 0` → GREEN | equivalent — the divergence test needs two distinct edges; **deleted** |
| `TraverseForward`/`Backward`'s `visited.UnionWith(queue)` | delete → GREEN | equivalent — `CheckReachability` queries the sets only for `role != Role.Start` / `!= Role.End`, and the seeds are exactly those roles, so a seed's membership is never asked; **kept**, simpler than the loop it replaced |
| `NameBlankEndpoints`' early return for a fully-connected edge | delete → GREEN | equivalent — the fallthrough would `with`-copy identical values; an allocation choice, not a guard, and not claimed as pinned |

A green mutation is not a coverage hole and not dead code until you have read who consumes the value.
**Four for four**, and none of the four was settled by looking at the line under the cursor.

### The Sonar round, and what it cost to check the criterion

The owner required the five Sonar issues fixed. The code changes were verifiable by inspection; the
*criterion* was not, and checking it turned up two failures that inspection had passed:

| Analysis | Commit | New issues |
|---|---|---|
| 13:14 | `1d1bcb515` — one commit **before** the fix | 5 |
| 13:30 | `6c46b478a` — the fix | **3** (`:67` and `:62` cleared) |
| 13:38 | `4d6864a48` — branch tip | **0**, Quality Gate OK, all five conditions OK |

Verified here against the tip through the issues API rather than the bot summary, which carries counts
and not identities. Two lessons with a life beyond this ticket:

- **Sonar accepts a nullability annotation as the reason a guard is needed.** `:67` asked to remove a
  null check that measurement showed load-bearing (`NullReferenceException` on both paths). Fixing the
  *signature* — `IReadOnlyList<ProcessGraphNode?>` — cleared the rule with the guard intact. A rule that
  says "unnecessary" can be answered by making the necessity visible to it rather than by obeying it.
- **This repository has no PR-time Sonar workflow.** The only scanner invocation is in
  `reliase-to-nuget.yml`, gated on `release: published`, and commented out. The PR analysis comes from
  SonarCloud's own integration, roughly five minutes after a push — so "trigger the analysis" has no CI
  answer here, and a bot comment is always about whichever commit was analysed, not the tip. The right
  endpoint for a pull request is `project_pull_requests/list`; `project_analyses/search` mixes in
  master-branch analyses and this review misread it once, reporting an unrelated PR's merge commit as
  the analysed revision.

---

## Round 2 — pre-push review of the ten human-review fixes (2026-09-07)

Four parallel lenses over the uncommitted diff: correctness, testing coverage, quality/consistency,
performance. No Blocker. The findings clustered into two groups, and the second is the one worth
recording because both members of it were written **in this batch**.

### The rule that did not settle the split it was cited for

The blank/omitted R13 severity split was justified with the ticket's own severity rule — *a finding is
an error iff the builder refuses that shape*. Measured against `FlowKindRules.cs:126`:

```csharp
bool hasCondition = !string.IsNullOrWhiteSpace(condition);
```

`IsNullOrWhiteSpace`, not `== null` — so the builder refuses **blank and omitted alike**, and the rule
makes both errors. It does not separate them. What separates them is whether the shape has a legitimate
reading **before any predicate exists**: omission does (the field is optional so a caller can check a
graph's shape first), whitespace does not. That is now the reason at the decision site, in the interface
doc, in the rules document and in the two test descriptions — replacing a rule that looked like it had
done the work.

The blank finding's message was corrected in the same place: it predicted that the platform "stores it
as the literal `true`", which the build path never reaches, because it refuses first. It now names the
refusal and keeps the literal-`true` outcome scoped to the designer/direct-save route a read-back graph
in that state actually comes from.

### Two comments that outlived their own mechanism

Both cite a fact this same diff removed, which is the failure mode
`docs/knowledge/Tests/reachability-not-corpus-absence-decides-whether-a-guard-stays.md` exists to prevent:

- `CheckSelfLoops`'s missing null-source guard was justified by "`CheckMissingNodeFlows` runs first and
  its `ContainsKey(null)` throws". That throw is precisely what `NameBlankEndpoints` was added to
  eliminate. The guard is still dead, for a different reason, and the resurrection tripwire was aimed at
  the wrong edit — it is `NameTheNameless` running first, not the order of the two checks.
- R18's comment said "R12 does fire on this shape, but as a warning whose text describes an all-plain
  split". R12 counts only `FlowKind == Sequence` and needs more than one, so on
  `[conditional, default, sequence]` — the exact shape raised in review as a false positive — it does not
  fire at all. R18 is the only finding there. The sentence was weaker than the truth.

A third claim in the same class: `NameBlankEndpoints` said its placeholder is "a name no element can
have". A node named `(missing source)` makes the placeholder resolve and the caller is told about that
node's flow arity instead. Pre-existing (the single `(missing)` literal had it identically), bounded
rather than fixed — the alternative is a reserved-name check on every node.

### Test oracles that would have passed for the wrong reason

- `ProcessGraphValidatorTests:167` — the `Cond` helper omits the condition, so R13 now fires **twice**
  and either warning satisfied `RuleId == "R13" && Severity == Warning`. The source-role clause could
  have been deleted whole with the test green. Now keyed on the message.
- The both-blank-endpoint pin asserted only the **absence** of `"to itself"`, a substring of the rule it
  suppresses, living in the same file as the fix. Now a count plus both placeholders **by name** — which
  is also the only thing in the suite that pins the two-placeholder decision at all.
- The plain-flow negative keyed on `"has no default flow"` while its positive twin keyed on
  `"None of the conditions were met"` — two fragments of one message, so re-wording the prefix made the
  negative vacuously green. Both now key on the same fragment.
- `Validate_ShouldWarnR7_NamingTheRuntimeException_...` asserted the inverse of its own name, and carried
  a **second Act inside its Assert block**. Renamed; the second scenario is its own test.

### Performance

The `joins x candidates x branches x edges` term is genuinely gone — `E` is now paid once per join rather
than per candidate. But the reviewer priced the replacement honestly: `GroupBy().ToDictionary(g =>
g.ToHashSet())` allocates three structures per branch and passes twice, turning a per-**candidate** scan
into a per-**join** allocation — a net loss where a graph has many joins and one or two candidates each,
which is the ordinary shape. Hand-rolled into one loop; behaviour identical (same ordinal comparer, same
keys, same sets, no empty groups possible) and pinned by the existing R8 coverage.

Not in this diff and not taken: `ValidateProcessGraphArgs` has no size cap, and
`McpReadResponseDeadline` **abandons** rather than cancels at 120 s (`Validate` takes no
`CancellationToken`), so a large graph leaves a thread-pool thread burning behind a benign-looking
timeout. Worth its own change.

**Result:** `dotnet test --filter "Category=Unit&(Module=ProcessModel|Module=McpServer)"` → **4819
passed, 0 failed**; 0 `CLIO*` warnings in changed files. The package repository is untouched, so the
bundled archive stays at **1.4.0.70** and no rebundle is owed.

### Round 3 — the corpus number, measured a third time (2026-09-07)

A peer session published **344** conditional flows with no condition — a quarter of the corpus — and
argued it inverts R13-omitted from a warning back to silence, since this repository's own demotion rule
says a shape the corpus contains in bulk is not a finding. Their arithmetic reproduces exactly. Their
reading does not survive one more join.

Full census, 1711 schemas / 1367 shipped `ProcessSchemaConditionalFlow`:

| `CI3` | `GV2` | count | what it is |
|---|---|---|---|
| a real expression | empty | 1023 | formula branch |
| absent or `"null"` | **has entries** | **337** | **activity-result branch** |
| absent or `"null"` | empty | **7** | genuinely nothing decides it |
| empty string | — | 0 | — |

`GV2` is `ProcessSchemaConditionalFlow.ProcessActivitiesSelectedResultsPropertyName`.
`SpecifyConditionalSequenceFlow` turns it into `ConditionalSequenceFlow.ActivityResults`, and
`CheckCondition` dispatches on `ResultParameterName` — it **never evaluates an expression**. So an empty
`CI3` on those 337 is correct, not defective: it is the designer's *Activity results* preset, which R13
in `ai-bp-connection-rules.md` already names.

Three probes, three answers, and the failure is the same one each time — the probe could not return the
result that falsifies it:

- key-absent only → **3** ("nobody does this")
- key-absent + the `"null"` spelling → **344** ("a quarter of the corpus does this")
- either of those, joined against `GV2` → **7**

**The warning stands**, on 7 rather than on 344. Recorded as
`docs/knowledge/ProcessModel/conditional-flow-condition-lives-in-two-places.md`, because the next person
to grep `CI3` will get 3 or 344 and neither is the number the decision turns on.

The measurement did surface one real defect in the tool. `ProcessGraphEdge` carries `Condition` and
nothing else, so an activity-result flow arriving by describe-then-validate is **indistinguishable** from
a bare one and raises the warning — 337 shipped flows' worth. The finding is defensible (clio cannot
build an activity-result condition either) but the remediation "give it a condition, or make the flow
`sequence`" would destroy the branch, so the message now names the case and points at
`branchesOnActivityResult`, which `describe-business-process` does report.

Two peer items checked and **not** actionable: the BOM trap (my scripts wrote `utf-8`, not `utf-8-sig`;
all five markdown files still start with ASCII) and the claim that a comment at `:404` calls R13 "a
warning" while annotating an `Error` arm — that comment annotates the source-role arm, which does emit
`Warning`. The peer's `CheckSelfLoops` item was already fixed in round 2, independently and to the same
conclusion.

### Round 4 — mutation X4, and the gap it found (2026-09-07)

A peer flagged four mutations against this batch's new decisions and named **X4** as the one that
mattered: narrow R18's counter from `FlowKind != Conditional` to `FlowKind == Sequence`, dropping an
explicitly declared `default` out of "unconditional". Run here rather than waited for, because the
rebuttal to the human reviewer's finding 2 rests on exactly that predicate.

**X4 came back GREEN — 4819 passed, 0 failed, with R18 narrowed.**

The rule's disposition was defended three ways — a comment, a corpus count, and a stand measurement
showing `CrtProcessBuilder` refusing the shape with exit code 1 — and **nothing executable held the
predicate in place.** The reason is specific and worth keeping: every existing R18 case used
`[sequence, sequence, conditional]`, where *both* readings of "unconditional" count two. Only a shape
that MIXES the kinds can tell them apart, and none existed.

Closed by `Validate_ShouldReturnR18Error_WhenAConditionalHasADefaultAndAPlainSibling` —
`[conditional, default, sequence]` off one element, which is also the exact shape the reviewer raised.
Verified both directions:

| predicate | result |
|---|---|
| `FlowKind != Conditional` (real) | 130/130 ProcessModel, 4820/0 on the full filter |
| `FlowKind == Sequence` (X4) | **1 failed** — the new test, and only it |

So the rebuttal is now executable rather than argued: narrowing R18 the way the finding asks turns a
test red, naming the shape and the mechanism (`GetIsDefSequenceFlow` is
`BpmnElementName != ConditionalSequenceFlowName`, so the `default` marker is never read, and
`RemoveDefSequenceFlow` drops exactly one non-conditional flow by list order). The test also pins the
R12-silence fact, so "delete R18 and R12 covers it" fails a test too.

**The lesson, since it is the fourth probe-cannot-falsify-itself finding on this ticket:** a rule
defended by measurement is not thereby pinned. Corpus counts, stand runs and platform source say the
rule is *right*; only a mutation says the code still *implements* it. Three defences and no oracle is
the shape to look for.

### Round 5 — the blocker that was a shared-tree artifact (2026-09-07)

The reviewing session raised a **blocker**: `ProcessGraphValidator.cs:271` read
`o.FlowKind == ProcessFlowKind.Sequence` — the reviewer's own proposal — and `[conditional, default,
sequence]` produced no finding of any kind. Measured in this tree, not inferred, and correct for the
bytes in front of them.

It was the X4 mutation, mid-run. Applied → built → 7-minute test pass → restored. Their read landed
inside that window. Confirmed after: `:271` is `!= Conditional`, the file hashes identical to the
pre-mutation backup, and all four R18 tests pass — including the one that *cannot* pass under X4.

**The finding is real and it is a process one, owned here.** A destructive mutation was run in the
shared working tree while another session was reviewing it, without telling them. The dangerous
direction is the opposite of what happened: a sample taken during a mutation that *removes* a finding
would have produced a "reviewed clean" on a state that never existed, and nothing in either workflow
would have caught it. Mutations now go to a worktree; if that is not possible, the reviewing session is
told before and after.

Their `anchor NOT FOUND` is what surfaced it — a mutation harness failing to find its anchor was a
better outcome than silently matching.

**Mutation verdicts, against a re-taken diff and a valid 62/0 baseline:**

| | |
|---|---|
| X1 collapse the two placeholders | RED (1) |
| X2 omitted arm `Warning` → `Error` | RED (5) |
| X3 drop the omitted arm entirely | RED (1) |
| X4 R18 counts only `Sequence` | RED (1) — after the new test; **green before it** |

Their first run was discarded for a 5-red baseline caused by a stale diff, and the earlier
"NO RESULT ×4" was reported as a non-answer rather than a pass. Three separate refusals to report a
number that could not be stood behind.

**Two items settled against the reviewing session's own retraction.** They withdrew the BOM finding on
the grounds that my scripts wrote `utf-8`; `git cat-file -p HEAD:<file> | head -c3` returns `757369` on
all three files and `utf-8-sig` was this batch's patch default, so the finding was correct and the
retraction was not. Worth naming because every other overreach on this ticket was *claiming too much*
and this one was *conceding too much* — the same defect with the opposite sign, and harder to spot
because it reads as rigour.

**Closing edits:** R18's message now names the `default` case explicitly — answering the reviewer at the
message rather than at the predicate, which is where the answer belongs since the predicate is what the
platform mechanism forces. Recorded
`docs/knowledge/Tests/three-defences-and-no-oracle-is-an-unpinned-rule.md`.

**Final: 4820 passed / 0 failed / 2 skipped, 0 `CLIO*` in changed files, archive 1.4.0.70, nothing
pushed.**
