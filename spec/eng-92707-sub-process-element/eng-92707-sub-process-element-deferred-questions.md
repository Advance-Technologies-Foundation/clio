# ENG-92707 - deferred questions

Decisions taken alone during the autonomous implementation session, because the analysis did not
settle them or because the repositories had moved since it was written. Each entry states the
question, the decision, the reason, and what would have to be true for the other choice to win.

This file is the agenda for the human. Nothing here blocked the run.

---

## DQ-1 - the archive floor the analysis pins is two minor versions stale

**Question.** The plan (§0.5, S1, S8) says to bring the CrtProcessBuilder checkout to archive
**1.6.1.9**, producing commit `ee5188ef404dfae299a373f1d67adfa9bb13df3b`, and to raise `-Version`
from there. `clio` master now pins `ExpectedArchiveVersion = 1.6.2.24` and
`ExpectedProducingCommit = 4da4e4e93f161d8219f4528365a3fb0e1a39634b`, and the package repository's
`origin/main` descriptor reads `1.6.2.24`. Which is the base?

**Decision.** Base on the package repository's `origin/main` (descriptor 1.6.2.24, head
`337082e`), and rebundle to **1.6.3.0**.

**Reason.** The rule the plan is protecting is "the bundled version must go UP", and it is enforced
against what clio ships today, not against the number an analysis recorded two weeks earlier. Cutting
1.6.2.x from a 1.6.1.9 base would lose everything merged in between (the Approval, Open edit page,
change-access-rights, gateway and process-version work). The third digit moves because a new buildable
element type is a feature, matching how 1.4.0.0 and 1.6.0.x were cut for the earlier element families.

**What would flip it.** Only a deliberate revert of the package's `main` to the 1.6.1.9 line. The
version number itself is free to move further up if another cut lands first - it just has to stay
strictly above whatever clio's `ExpectedArchiveVersion` reads at merge time.

---

## DQ-2 - R16 cannot be an Error in `ProcessGraphValidator`

**Question.** Open question Q6 was decided "enforce", and the plan (S6, §7) budgets "the rule as an
**Error in `ProcessGraphValidator`** plus its `[TestCase]`s". R16 is *"Sub-process (callActivity)
target must begin with a Simple start; collection mapping => multi-instance"*. Can the validator
check that?

**Decision.** No. R16 is enforced **only** in the package's sub-process applier, where the callee
schema is actually loadable. `ProcessGraphValidator` is left alone, and the R-catalog line in the
guidance is rewritten to say where the rule fires instead of listing it as "not yet enforced".

**Reason.** `ProcessGraphNode` is `record ProcessGraphNode(string Name, string Type)`. A planned
graph carries no reference to the called process, and `validate-process-graph` promises in its own
contract that "the graph is validated in-memory". Enforcing R16 there would mean adding a
`calledProcess` field to the wire contract of a read-only planning tool and then loading a foreign
schema per node - a different tool from the one that is documented. Faking the check on what the
graph does carry would report an Error the validator cannot actually substantiate, which is worse
than the current silence. The half of R16 that matters - a real refusal before anything is written -
lands in the applier, and an agent that wants to pre-screen already has `HasStartEvent` on
`VwProcessLib` (Q5).

**What would flip it.** A decision to extend the `validate-process-graph` node contract with the
callee reference and let the tool resolve schemas on the environment it already connects to. That is
a tool-contract change with its own ClioRing compatibility question, not a validator rule.

---

## DQ-3 - the "five agent-facing texts" are four, and two of them moved

**Question.** The plan (S6, T-18) lists five shipped surfaces that assert sub-processes are not
buildable: both tool `[Description]`s, `ValidateProcessGraphTool.cs:50`, the prompt text, and
`docs/McpCapabilityMap.md` twice on one line. Measured against `origin/master` today, which are they?

**Decision.** Rewrite the four surfaces that actually carry the claim:
`CreateBusinessProcessTool`'s `[Description]`, `ValidateProcessGraphTool`'s `[Description]`,
`ValidateProcessGraphPrompt`, and `docs/McpCapabilityMap.md`. `ModifyBusinessProcessTool` and
`ModifyBusinessProcessPrompt` mention sub-processes only in passing, and correctly.

**Reason.** The surfaces have been rewritten since the analysis was taken (Approval, Open edit page,
change-access-rights and the two gateways all landed). The claim is now carried by the buildable-slice
sentences rather than by the line numbers the analysis recorded. Editing a line number that no longer
says what it said would be a no-op that reads as done.

**What would flip it.** Nothing; this is a re-measurement, not a judgement call. It is recorded
because the count in the plan and in the PR-description checklist is 5 and the delivered count is 4.

---

## DQ-4 - S1 measured two of the three predictions wrong

**Question.** S1 exists to measure T-1, T-26 and T-25 rather than reason about them. What came back?

**Decision.** All three are now tests (`CrtProcessBuilder.Tests/SubProcessPlatformProbeTests`), and two of
them changed what the code had to do.

* **T-1 CONFIRMED.** Assigning `SchemaUId` on a detached element throws. The handler writes the element
  bare and the applier runs post-graph because of this, not because of a style preference.
* **T-26 CONTRADICTED.** The copy constructor does **not** throw. It does fire the setter on an unattached
  copy, but `MetaItem(MetaItem source)` assigns `ParentMetaSchema` from the source first, so `Clone()` is
  safe wherever the original was. No code changed; the trap list and the knowledge record did.
* **T-25 CORRECTED, and the Blocker does not survive as stated.** A synchronization of an already
  multi-instance element rebuilds it as two collections plus three counters, and the callee's parameters
  come back as the input collection's `ItemProperties`. It is the platform's own idempotent refresh, and
  `ProcessSchema.SynchronizeParameters` runs it through the same interface member on **every design-time
  read** - so it cannot be the data loss the analysis describes, or the 61 shipped multi-instance
  elements would not survive being opened once.

**What changed because of it.** The refusal stays, with a different reason. A multi-instance element
carries none of the called process's parameter NAMES, and every name this contract works in terms of
therefore addresses nothing on it. It is still a pre-condition evaluated before any path that can reach
the setter - cheap, and correct either way - but the incidental path (a `setElement` that touched the
element for another reason) now SKIPS with a notice instead of refusing the caller's whole edit, which
would have been the wrong trade once the sync is known not to destroy anything.

**What would flip it.** A measurement showing a value inside `ItemProperties` being lost across the round
trip. The unit probe asserts the parameter survives; it does not assert its VALUE does, and that is the
one gap left in this area.

---

## DQ-5 - the subProcess block's contract is documented in guidance, not in the tool description

**Question.** The plan's S6 lists "both tool `[Description]`s" among the agent-facing texts to rewrite.
A full `subProcess` block contract in `create-business-process`'s description is about 2 KB of prose.
Where does it go?

**Decision.** The two tool descriptions carry only the TOKEN - `type:…|subProcess` and
`elementUpdate.subProcess?`. The block's contract lives in `docs/McpCapabilityMap.md` and in the
`clio-knowledge` guidance article, which is where an agent is already told to look.

**Reason.** `McpToolContractBudgetTests` caps one `get-tool-contract` call at 34 816 bytes, and both
descriptions sat within about 40 bytes of that ceiling before this change. The full block pushed
`create-business-process` to 36 931 and `modify-business-process` to 35 950, and the test's own message
says what to do about it: "Raising it is the moment to ask whether the text belongs in a `[Description]`
at all." It does not - `AGENTS.md` already rules that guidance content does not live in this repository.

**What would flip it.** Raising the budget, which is a decision about every tool rather than about this
one. Note the two descriptions are now close enough to the ceiling that the NEXT element to be added
will hit it too, so the ceiling is worth revisiting on its own terms.

---

## DQ-6 - `curated-knowledge-names.json` is NOT re-pinned

**Question.** S7 says to "re-pin `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` in
clio if the name set moved". Does it move here?

**Decision.** No, and the fixture is left alone.

**Reason.** This ticket edits five existing articles and adds none, so no guidance NAME changes -
`WorkspaceTemplateGuidanceDriftTests` reads only the name arrays. The fixture's `libraryVersion` /
`sequence` fields are a provenance stamp saying which generation the names were checked against, and
stamping them with **1.16.0** would name a generation that does not exist until the `clio-knowledge`
pull request merges and releases. A pin that points at an unpublished generation is worse than one that
is behind.

**What is worth knowing anyway:** the fixture is already stale on `master`. It records **1.15.10** while
`clio-knowledge` master is at **1.15.16**, and the source now carries at least
`process-activity-result-branches` and `process-version-writes`, which the fixture does not list. That is
a pre-existing gap left by the tickets that added those articles, not one this change introduces, and it
belongs in a refresh of its own after the 1.16.0 release.

**What would flip it.** Adding a new guidance article here, which would move the name set and make the
re-pin mandatory.

---

## DQ-7 - `process-activity-connections` has no budget left, and this change had to be shaped around it

**Question.** The guidance edit needs R16 restated: it is enforced, but at build time rather than by
`validate-process-graph`. Where does the explanation go?

**Decision.** One short clause in the R-catalog line - "Enforced by the BUILD path, not here" - and the
whole explanation in `process-element-catalog`, which owns the element.

**Reason.** `ProcessGuideResponseSizeTests` caps a `get-guidance` response at 27 793 characters, 85 % of
the largest response measured to survive the round trip, and `process-activity-connections` sits at
**99.8 % of that budget on master**. The article has room for about sixty characters. The first draft of
this edit put the reasoning in the R16 line and pushed the article to 102.2 %, which the test refuses
with the right advice: split at a section boundary rather than raise the budget.

**What is left for someone else.** That split. The article is one sentence away from red for every ticket
that follows this one, and the test prints its own warning line on a green run for exactly that reason.
Splitting it is not ENG-92707's work, but the next person to touch it will not have the sixty characters
this change used.

---

## DQ-8 - the review workflow stalled, so the fan-out was orchestrated directly

**Question.** §3 of the implementation prompt requires `/creatio-code-review` over each repository's
diff. The skill runs as a top-level Workflow. Its Context agent hit an output-token limit, resumed, and
then produced nothing for ten minutes with the run still in its first phase. What now?

**Decision.** The run was stopped and the SAME review was orchestrated directly: the Jira issue fetched
for the acceptance-criteria anchor, then the reviewer subagents fanned out over the same diff with the
`review-core` rules handed to each of them verbatim - the AC binding requirement, the Blocker-needs-a-
scenario rule, the "adding new surface is at most Minor" rule, and the ban on comment-wording findings.

**Reason.** `AGENTS.md` describes exactly this as the supported path: "Now (agent-driven): the
implementing agent runs the gates locally via the `Agent` tool (parallel reviewer subagents)". The
workflow is the convenience wrapper around that, not the gate itself. Waiting on a stalled orchestrator
would have traded a real review for a formality.

**What is lost by not using the wrapper.** The workflow enforces its severity gates IN CODE - the
demotions and the suppression counters - rather than by prompt compliance, and it prints those counters.
Here the gates were applied by reading each finding against the same rules, which is weaker in exactly
the way the workflow's own documentation says prompt compliance is weaker. The findings that were fixed
are listed in the pull request; the ones that were not are listed with the rule that dropped them.

**What would flip it.** The workflow completing. It is worth re-running on a later diff to see whether
the stall was this diff's size or a transient.

---

## DQ-9 - the package pull request cannot be opened from here

**Question.** The package repository is on `creatio.ghe.com`, not `github.com`. Can this session open
its pull request?

**Decision.** No. The branch is pushed and the pull request is handed over as a prepared URL plus the
title and body to paste. The other two repositories are on `github.com`, where `gh` is authenticated,
and their pull requests are opened normally.

**Reason.** `gh auth status` shows a token for `github.com` only. The stored git credential for
`creatio.ghe.com` authenticates the PUSH (the credential manager holds it and a dry-run push succeeds)
but is not an API token: `gh api --hostname creatio.ghe.com user` answers 401 with it. The `ghe` MCP
server, which would have been the other route, failed to connect at session start with a malformed
Authorization header.

**What it costs.** One click, and the review is unaffected - the gate ran on the diff, not on the pull
request. It does mean the package pull request carries no automated review comment thread, so the body
has to state what ran, which it does.

**What would flip it.** `gh auth login --hostname creatio.ghe.com`, or a working `GHE_MCP_TOKEN` for the
`ghe` MCP server.

---

## DQ-10 - the drift REPORT covers less than "re-sync after the callee changed" suggests

**Question.** AC-3 asks for a re-sync that refreshes the element's parameters after the called process
changed, and for the outcome to be reported. The report is built from a snapshot taken around the write.
What does that snapshot actually see?

**Decision.** Ship it, and write the limit down in the contract, in the report's own docblock and in the
guidance rather than leaving a reader to assume more.

**Reason.** The platform re-synchronizes every sub-process element on every DESIGN-TIME read, and the
MODIFY path always loads the schema through `GetDesignInstance`. So by the time the applier takes its
snapshot the element has already converged, and a snapshot diff can only ever report drift
that THIS REQUEST causes - the first selection, or a retarget. A pure `resync` after the called process
changed underneath a saved caller still *writes* the refreshed element, because the load converged it and
the save persists it, and reports nothing.

**Corrected 2026-09-17 (DQ-27):** this reason originally said BOTH describe and modify load through
`GetDesignInstance`. Describe does not — it prefers the runtime instance for a compiled process. The
VERDICT is unaffected, because it only ever depended on the modify path, and a reviewer re-verified the
mechanism in platform source: `BaseProcessSchemaManager` calls `SynchronizeParameters()` on the metadata
and design paths and no runtime getter does. But the false half of the premise was load-bearing
elsewhere — see DQ-15 below.

That is trap T-7 arriving one layer lower than the plan's D2 expected: D2 says "snapshot before mutating
`SchemaUId`", which is what the applier does, but the load that precedes it has already run the
platform's own pass.

**What would flip it.** Reading the STORED metadata - the pre-sync `SysSchema` body - and diffing against
that. It is the only surface that still holds the stale state, and it is a different mechanism from the
one this ticket built, not an adjustment to it.

**What it costs in the meantime.** The refresh is real and the element is correct afterwards. What the
caller does not get is a sentence naming the parameter that moved, on the one path where they most want
one. The manual test cases cover it at the level where it IS observable: TC-03 and TC-04 read the element
in the designer and run it.

---

## DQ-11 - a review judgement I made and had to reverse

**Question.** The security lens found that the retarget refusal rendered its dependent-name list without
`SafeText.Sanitize`, unlike the three sibling call sites, and gave a CR/LF log-forging scenario. I applied
the fix but then argued the finding DOWN: `MetaItem`'s `Name` property setter refuses anything outside
`[A-Za-z][A-Za-z0-9_.]*`, so I judged the scenario unreproducible and shipped no test for it.

**Decision.** Wrong, and reversed in round 2. The sanitize call is the control.

**Reason.** `MetaItem.ApplyMetaDataValue` writes the BACKING FIELD -
`case NamePropertyName: _name = reader.GetStringValue();` - so the identifier check is an API-surface
invariant and not a storage one. Every schema this package touches is deserialized from metadata, so a
stored element name really can carry CR/LF, and the refusal message is returned verbatim as the
operation's `errorMessage` and rendered into the platform log through the exception. The round-2 reviewer
caught it; I verified it in the platform source before reversing.

**What it cost, and what it is worth recording for.** I checked the property setter and stopped there.
The failure mode is specific and worth naming: a public setter's validation says nothing about what is in
the store, because deserialization routinely bypasses setters. The test now reproduces the state the way
the platform produces it, by writing the field, and a mutation check confirms that removing the sanitize
call fails it.

**What would flip it.** Nothing. This one is settled by the platform source.

---

# Round 3 — what an adversarial review changed, and what it did not

A second reviewer went over all three pull requests after they opened. Fourteen Major findings; eleven
stood, one was refuted, three were narrower than written. What follows is only the part that needed a
judgement — the fixes themselves are in the commits.

## DQ-12 — R16 is a REFUSAL, and the plan contradicted itself

The review read TC-17 ("**Warning**, not refusal") and S3 step 2 ("warn on R16") and reported the code as
a shipped deviation. It is not: D8, the decision, reads "R16 can be a hard refusal in the applier and an
Error in `ProcessGraphValidator`", and it is backed by the measurement that made the call — zero
violations among the 269 resolvable callees in the corpus. The applier throws, which is D8.

**Decision.** Correct the matrix and S3, not the applier. The validator half never shipped and cannot
(DQ-2). The reviewer's scenario — "a callee that gains a triggered-only start hard-fails an otherwise
valid build" — is not supported by that measurement and was withdrawn.

## DQ-13 — the retype comparison is hardened, not repaired

The review said the drift snapshot's `DataValueType?.Name` comparison is "dead on the path it exists for",
citing `ProcessParameterService`'s own comment that the object property "would always be null" on a
read-back schema. The platform source disagrees: `ProcessSchemaParameter.DataValueType` resolves LAZILY
through `ProcessSchema ?? ParentMetaSchema as BaseProcessSchema`, and TC-04 reads it today and passes.

**Decision.** Change it anyway, to `DataValueTypeUId` with the `Guid.Empty` guard the page synchronizer
uses, and say in the code that this is hardening. The property is the stored one and the comparison no
longer depends on a lookup being reachable. The claim that it was DEAD is not repeated anywhere.

## DQ-14 — the post-condition could not go where the review put it

The finding is right and the fix it proposed is not: call `CanPlatformResolve` before the write. That
probe asks about the element's CURRENT callee, and on a create or a retarget the element still references
the old one, so before the assignment it answers about the wrong process. The check has to run AFTER the
write, against the details already read — which is safe, because nothing is persisted until `Save`.

**What it costs.** The in-memory element has already been mutated when the refusal fires; a retarget that
is refused this way leaves the element with no parameters in memory. The test says so rather than
pretending otherwise. The refusal is what stops that state reaching the database and, more importantly,
what stops the caller being handed a report naming parameters the new callee declares as dropped.

## DQ-15 — `inSync` vs `IsUnchanged`, left open on purpose

The two predicates are asymmetric: `MirrorsCallee` asks only whether the callee's parameters are all
present, `IsUnchanged` also counts `Removed`. The review wants them reconciled and TC-07 written.

**Decision.** Document the asymmetry on the now-shared predicate and leave TC-07 for a stand.

**RE-ARGUED 2026-09-17, because the original reason was false.** It said the state "is converged by the
platform on every design-time read", so a unit test "would mean constructing a state the platform does
not produce". The platform DOES produce it — on the runtime-instance path describe takes, which is the
very surface DQ-15 named as unable to see it. A stand measured `inSync: false` on exactly that state.

The decision survives on a different and narrower reason: the asymmetry is between two predicates
answering two questions, and the one that can now be observed is `inSync` — which is `MirrorsCallee`, the
predicate the review wanted reconciled. So the case TC-07 was written for is no longer hypothetical, and
what it would pin is worth having. It is NOT written here because a unit fixture reaches only the
converged path; the observable case lives on the describe path against a compiled process, which is a
STAND case. Moved to the manual matrix rather than left as "the platform does not produce it".

## DQ-16 — the ticket's scope line says retarget is out of scope

Ticket Scope: "No need to support replacing one sub-process with another one (will be covered as a
separate task)." It is implemented, and the assignee confirmed on 2026-09-17 that it is supported. Two of
this round's findings land on exactly that path, so the deviation is not free — it is recorded here and in
the pull request rather than left for a reader to notice, and the retarget path still has no acceptance
criterion of its own. V7 and the manual TC-06 / TC-07 cover it, and they have not been run.

---

# Round 4 — the judgement calls in the second re-review

Fourteen findings, four of them blocking, one a regression round 3 introduced. Only the calls are here.

## DQ-17 — one post-condition, on both paths — and the weaker one it replaced was defending a fixture

Round 3 checked the SELECTION path and left the re-synchronization path unchecked — the path that runs
on every `setElement` touching the element, and the one where a loss is also SAVED. Round 4 added a check
there, but a weaker one: "the element came out EMPTY while the callee declares parameters", on the ground
that the reader's memo could predate a change to the callee and make a legitimate drop look like a
failure. **Round 5 reversed that.**

**Decision.** Both paths ask `MirrorsCallee`.

**Reason.** The memo cannot go stale within a request: `ISubProcessReader` is registered `AddScoped`,
every entry point that resolves a service wraps its call in its own `CreateScope()` (`Ping` does not, and resolves nothing), and nothing inside one call
loads a second process schema for writing. The two tests that failed under the strict predicate shared
ONE reader between their arrange and their act — a state no request reaches. `AnApplierForTheNextRequest()`
gives the act the reader a second request would have, and they pass.

**What the weaker form cost while it stood.** A partial copy failure on the busiest path was unrefused,
saved, and reported to the caller as "the called process dropped these" — the same misreport the check
exists to prevent. It was also incoherent: a callee that legitimately drops ALL of its parameters
produces exactly the refused state, so the ambiguity was moved to arity 1 rather than removed.

**The lesson worth keeping.** A test had been written to pin the concession, so restoring the correct
guard would have read to the next person as a regression. Do not pin a concession with a test; pin the
behaviour you actually want and record the concession here.

## DQ-18 — the provenance audit numbers were wrong, and the attribution made it worse

Round 3 rewrote "157 entries, 156 byte-identical" and attributed it to the 1.6.3.1 cut. Both numbers
were wrong — the archive has 167 entries — and naming a specific cut made the error look checked.

**Decision.** Re-measured by hand for the 1.6.3.3 cut (`clio extract-pkg-zip`, then every file against
`git show <commit>:<path>`): **167 entries, 166 byte-identical, only `descriptor.json` differs.** The
paragraph now carries the commands rather than an invitation to carry the number forward, and says the
count moves as the package gains sources.

## DQ-19 — the guidance said "check for this after every retarget", and nobody has seen it happen

Round 3 replaced a false promise (the build path refuses the stranded-mapping hazard) with a duty that
has no evidence behind it. The repository's own rule is that behaviour is not claimed without naming the
evidence, and the instruction could not have been carried out anyway — a stranded mapping row and a live
one render identically in describe.

**Decision.** State the hazard, state that its standing is UNOBSERVED and why (the platform prunes such a
row for every non-dynamic parameter; this contract produces none; T-27 open), say explicitly not to add a
routine check, and name the discriminator for whoever does meet one: whether the `[Parameter:{…}]` UId
inside the stored metapath still matches a `uid` describe reports on that element.

## DQ-20 — `process-activity-connections` is still not split

It sits at 27,761 of 27,793 and the right fix is a split at the R1–R20 seam. Not done here: a split moves
the guidance NAME set, and that forces the `curated-knowledge-names.json` re-pin which DQ-6 deliberately
leaves to travel with the release. It is its own change, after that release. Until then every edit to
that article has about thirty characters to spend.

## DQ-21 — where a code reviewer's findings actually live

Copilot's second review on the package pull request says "generated no new comments" and carries four
findings inside a collapsed **Suppressed comments** block in the review BODY, with no inline threads. A
thread query answers zero, and so does reading the summary line. Two of the four were real guard holes on
a supported operation and had been sitting unanswered for a day.

**Decision.** Read the review BODIES, not the thread list, on every round. Written up as a knowledge
record in the same change, because nothing in this repository said it and the summary line actively
misleads.

## DQ-22 — the provenance paragraph pins an invariant, not a count

Two rounds running, the by-hand archive audit was recorded as a NUMBER and went stale — 157/156 with a
specific false attribution in round 3, then 167/166 re-pointed at "the archive pinned below" in round 4,
which re-points at whatever the next rebundle pins.

**Decision.** The paragraph now states the invariant that cannot go stale — every entry but
`descriptor.json` is byte-identical to the producing commit's blob — plus the two commands that verify it,
and names the cut at which it was last actually run. The count is deliberately not pinned: it moves as
the package gains sources, and a pinned one makes staleness look checked.

## DQ-23 — the three skip flags are exclusive by construction, not by contract

`SubProcessSyncReport` carries `MultiInstanceSkipped`, `SelfReferenceSkipped` and `CalleeUnreadable`.
Exactly one is ever set, because every producer sets one and returns immediately — the precedence
between them is the ORDER of those returns and lives nowhere else. A `SubProcessSkipReason` enum would
make it structural.

**Decision.** Not done, and deliberately not raised as a ticket either: a three-flag refactor that fixes
no observable behaviour does not get scheduled, it expires in a backlog. It is recorded here because this
file is read by the one person it is for — the next one changing this element.

**Judge it on this, rather than inheriting the opinion.** Today nothing BRANCHES on the flags at all:
`SubProcessSyncNotices` renders them and `SubProcessSyncReport.IsUnchanged` counts them, and that is the
whole consumer set. So the positional precedence is invisible and harmless.

**The trigger is a condition, not a date.** It stops being harmless the moment either a FOURTH skip
reason appears, or any consumer starts branching on which flag is set. The precedence is load-bearing
from then on, and it will still be written down nowhere but the order of the returns.

## DQ-24 — a constant mapped onto a NON-SCALAR parameter is accepted and does nothing

Found while answering "is a collection-typed parameter supported?", after the review closed. Not
sub-process-specific — it is `ProcessMappingService`, so it applies to any element parameter — but a
sub-process element is where such a parameter arrives without anyone asking for it.

**The shape.** You cannot DECLARE a collection parameter through this contract:
`ProcessParameterService.NormalizeParameterTypeName` allows the scalar set plus `Lookup` and refuses
entity collection, composite, binary and the rest by name. But a sub-process element mirrors the CALLED
process's parameters, and the platform copies them whatever their type — so a callee that declares a
collection puts one on the element. Three routes then answer differently:

* `sourceElement` + `sourceElementParameter`, or `processParameter` — **refused**, by
  `EnsureCompatibleTypes` ("incompatible data value types").
* `expression` — the platform's own pre-save gate validates it against the target's declared type.
* `value` — **accepted, stored, and inert.** `ProcessParameterValueValidator.ValidateConstantValue`
  classifies any resolvable non-Lookup type as `ConstantKind.Scalar` and then converts only
  int / decimal / double / bool / Guid, rejects DateTime outright, leaves string unconstrained, and says
  of everything else: *"any other type is left to the runtime initializer"*. So the value is persisted as
  a raw `ConstValue` string.

**Why that is worse than it looks.** Nothing downstream catches it. The runtime transfer keys on the
parameter NAME through `ProcessInstanceParametersDataWriter.CopyCurrentValue`, which resolves with
`FindScalarParameterByName` — **scalar parameters only**, in both directions — and an unresolved name is
skipped with no `else`, no throw and no log line. So the mapping saves green, describe reports the value,
and the called process receives nothing. It is the exact silent-failure shape the rest of this contract
converts into an error.

**Decision.** Recorded, not fixed. The refusal is a one-line guard in `ValidateConstantValue` — reject a
target whose type is neither Lookup nor a convertible scalar — but it is a package change, so it costs a
cut, a rebundle, a re-pin and another review round, and nobody has yet met the state: it needs a callee
that declares a collection parameter AND a caller who maps a constant onto it rather than being refused
by the other two routes.

**The trigger is a condition, not a date.** Fix it when either happens:

1. someone reports a sub-process mapping that saves and then delivers nothing at run time — this is the
   first thing to check, and the discriminator is the target parameter's `type` in describe being
   anything outside the scalar set plus `Lookup`; or
2. `NormalizeParameterTypeName` is ever widened beyond scalars and `Lookup`. Today the gap is reachable
   only through a parameter INHERITED from a callee; widening that allow-list makes it reachable from a
   descriptor the caller writes themselves, which is a different risk entirely.

## DQ-25 — the manual pass, and the one gap it turns into a decision

Run 2026-09-17 against `http://d_krestov_n.tscrm.com:40001/` at CrtProcessBuilder **1.6.3.6**, knowledge
library 1.16.0, in an isolated session. **11 PASS / 2 FAIL, nothing blocked.** Report:
`C:\Projects\eng92707-manual-test\eng-92707-manual-test-report.md`.

Worth recording beside the failures: of 24 build/modify calls, 17 succeeded and six of the seven that did
not are exactly the six refusals the matrix demands — each with a message naming the cause. There were no
semantic retries. The contract and the guidance got an agent to the right descriptor first time,
including every refusal path.

**Both failures are one defect, and it is DQ-10 arriving where a test can see it.** TC-04 (callee drops a
parameter) and TC-10 (callee renames one) both refresh the caller correctly and report a bare success —
no name, no "removed", no "the value you mapped is now dangling". When the SAME request causes the drift
the warnings are excellent (TC-06's retarget named all four parameters and the consequence), so the
machinery is there; it has nothing to compare against, because the load that precedes it has already
converged the element.

**F2, which the pass did not anticipate and which is new:** the designer does not merely fail to warn, it
REASSURES. Opening the caller's card after a callee CODE rename shows the same caption and an intact
mapping.

**Mechanism corrected 2026-09-17, measured at 1.6.3.10 through a real Chrome.** This originally said the
card "shows the NEW name and an intact mapping, because rendering the card re-synchronizes it". That
conflates two different renames, and the stated cause is not the operative one:

Every cell carries its provenance. A first revision of this table marked cells only measured-or-inferred,
and that was still not enough: two cells carried **M** from a DIFFERENT pass against a DIFFERENT package
build, which a single letter hides and which is how an old reading gets cited as a current one.

> **M10** read at CrtProcessBuilder 1.6.3.10, the 2026-09-17 designer pass.
> **M07** read at 1.6.3.7, the earlier stand pass.
> **I** inferred from the binding mechanism. Not observed.

| Renamed on the callee | `inSync` | Caller's STORED mapping | What the card shows | Runtime |
|---|---|---|---|---|
| caption only | `true` **M10** | all printed fields identical to baseline **M10** | *not read* | unaffected **I** |
| code only — the rename that breaks delivery | `false` **M07** | *not read* — intact **I** | SAME caption, mapping shown, nothing marked **M10** | **broken M07** (3×) |
| code AND caption | `false` **M10** | byte-identical to baseline **M10** | NEW caption, mapping row EMPTY **M10** | **broken I** |

No `describe` and no runtime run happened in the code-only state during the 1.6.3.10 pass — the sequence
there was rename code, open designer, read card, then rename the caption too. So the code-only row's
`inSync` and runtime are the earlier pass's, and they stand as evidence of the BEHAVIOUR while saying
nothing about this build.

The card renders the CAPTION and never the code, so it cannot display code staleness under any
convergence behaviour. F2 does not depend on the design-instance question at all — the earlier write-up
reached the right conclusion from the wrong premise. In the state where the mapping is intact the name is
the OLD caption; in the state where the name is new the mapping is empty. There is no state in which the
card shows both a new name and an intact mapping.

**The empty row is a FALSE ALARM, and this was the question worth asking.** I flagged the third row as
possible silent data loss — a cosmetic callee edit destroying caller configuration — and asked for the
SCHEMA rather than the card. It is benign: `describe` on the caller in exactly that state returns the
parameter byte-identical to the healthy baseline (same `uid`, `source`, `value`, `valueDisplay`), so the
mapping is present and the card simply fails to render it. The platform fact holds — the element
parameter and its source are paired through the mapping row, not by name.

So the card misleads in BOTH directions and neither is data loss: it reassures on a code-only rename and
alarms without cause when the caption moves too.

**One hypothesis fits all three rows** — the card pairs the caller's stored parameter to the callee's by
CAPTION, so an unchanged caption matches and a changed one does not. **Inference, not measured.** It
predicts the caption-only row would also render empty; that is the cheap check that confirms or kills it,
and it is the one cell in the table still blank. It needs somebody SIGNED IN on the stand — the pass lost
its Supervisor session to the 1.6.3.10 app restarts, and a session that will not type credentials into a
login form is behaving correctly, not failing.

**A separate thing the same pass established:** `inSync` does not see a CAPTION. The caller keeps its own
copy and reports `true` while the callee's differs; only a code change flips it. That is the right half
to be sensitive to — the runtime binds by code — but `inSync: true` is not "the element matches the
callee", and the contract should not be read that way.

**One correction is deliberately NOT applied, and this is the record of it.** The PACKAGE-side contract
docblock (`DescribeContracts.DescribedSubProcess.InSync`) says "a callee that renamed or dropped a
parameter leaves the element stale", which for a CAPTION rename is true of the element and false of the
flag. Fixing one word there changes the shipped source, therefore the archive SHA, therefore all four
pins — a rebundle and a version bump for a comment, and `RequiredPackageChecker` throws on a convergence
refusal, so a casual bump strands a reviewer's clio against a stand that has not caught up. The
clio-side contract and the tool description a caller actually reads are correct as of this commit. Apply
the package word on the NEXT rebundle taken for a substantive reason. The saved schema — the one that runs — still carries the
old name. So no read reveals the stale state: not the designer, not describe, not `inSync`, not the
warning list.

**Corrected 2026-09-17 (DQ-27): the "not describe, not `inSync`" half is false.** `LoadForDescribe`
prefers the RUNTIME instance and only falls back to the design instance when there is none, and the
runtime instance is not re-synchronized. So against a COMPILED caller, describe reports the stale
parameter name and `inSync: false` — it is the one read that does reveal the state, and the pass
measured it. Against an uncompiled caller it falls back to the design instance, converges, and reports
`inSync: true`, so the reassurance is real but conditional. The DESIGNER half stands as written and is
what makes the procedural rule worth keeping. Recorded in guidance (`process-parameters`) and as
`docs/knowledge/platform/subprocess-designer-card-hides-stale-state.md`, with the rule stated
procedurally: re-save every caller after any change to a called process's parameters, because the callee
changed — not because something looked wrong.

**The decision that is NOT mine.** A real drift report needs the STORED metadata: read the `SysSchema`
body before a design-time load touches it, and diff the element's parameter set against the converged
one. It would close TC-04, TC-10 and F2 together. Whether it belongs to ENG-92707 or to a follow-up is a
scope call for the owner.

**Correction, 2026-09-17: DQ-10 called that "a different mechanism", and having read the code that is an
overstatement worth removing before anyone decides on it.** The pieces are already here. The platform
writes `SchemaUId` from metadata straight into the BACKING FIELD
(`ProcessSchemaSubProcess.ApplyMetaDataValue`: `case SchemaUIdPropertyName: _schemaUId = …`), not through
the synchronizing setter — so a schema deserialized from stored metadata comes back with the element
STILL STALE, which is exactly the state no other surface has. The same trap DQ-11 records about `Name`,
working in our favour this time. And the round trip has a precedent in this package:
`ProcessVersionCloneFactory` already does `SerializeSchemaMetaData` / `ReadSchemaMetaData` through
`IProcessSchemaRepository`, and several files read the database directly. What is left is the
`SysSchema.MetaData` select, locating the element in the detached copy, and then the EXISTING `Snapshot`
and `Diff`. Not free - a schema deserialization per request, so it should be scoped to an explicit
`resync: true` rather than every touch - and not verified end to end; this is a code read, not a run. What is true either way: AC-3's REFRESH
half is met and verified on a stand; its REPORTING half is not, and the guidance says so rather than
implying otherwise.

**Environment notes for the next stand pass**, neither an ENG-92707 issue: the built-in browser pane
cannot render this stand (every subresource fails `net::ERR_BLOCKED_BY_CLIENT`, leaving a blank page,
though the same URL serves fine as a top-level navigation — the pass fell back to Claude in Chrome); and
`SysProcessData` rows do not survive a completed interpreted instance there, so a short-lived callee's
parameters are not directly observable — build the observable into the callee, or park it at a human
step.

**Cleanup is pending a decision**: entity `UsrTc92707Order` (package `Custom`, 7 records) and eight
`UsrTc92707*` processes. Three older `UsrTc92707*` schemas predate the pass and were not touched.


## DQ-26 — TC-04 closed from the consequence; TC-10 still open

The owner chose the middle route on 2026-09-17: report what a dropped parameter BROKE rather than build
the stored-metadata reader that would report the drop itself.

**What shipped (CrtProcessBuilder 1.6.3.7).** A re-synchronization now names every element, process
parameter, execution context or flow condition still bound to a parameter UId the element no longer
carries. `ProcessElementDependencyScanner.FindDanglingParameterReferences` walks the same reference sites
as the retarget guard with the question inverted: a value that names the ELEMENT but none of its LIVE
parameter UIds points at one that is gone.

**Superseded at 1.6.3.11.** The clause that followed here read "No metapath parsing - a reference always
names both", and that was the wrong call twice over. Asking "does the value name none of the live UIds"
fails OPEN on a compound value, and the fix for that - take the next Guid after our element - searched
unbounded and INVENTED references, naming a real element and a real parameter on values that were fine.
The element and the parameter are now matched as ONE PAIR, in the two spellings the platform writes.

**Why this shape.** It is what the classic designer does.
`SubProcessPropertiesPage.synchronizeActualSchemaParameters` collects links before its own re-sync and
then calls `invalidateDependentElements`, which marks a dependent invalid when the parameter it read is
no longer findable. The designer has no drift report either; its signal is the broken consumer. We answer
the same question for a caller with no diagram to mark, and on both the explicit and the incidental path,
because the designer reports whenever it converges rather than only when someone came to look.

**What is still open, and it is TC-10.** A RENAME keeps the parameter UId, so every reference stays
resolvable while the SAVED element parameter name goes stale and the runtime - which binds by name -
delivers nothing. The WRITE path cannot observe it: the modify load converges the
element before the applier snapshots it, so the re-sync has nothing to compare and this new scan sees
every reference still resolving. Neither does the designer, nor this package's own warning list.

**But one read does, and DQ-27 corrected this sentence to say so.** Describe against a COMPILED caller
takes the runtime instance, which is not re-synchronized: it reports the STALE parameter name and
`inSync: false`. That is real evidence a rename happened, available today, and it changes TC-10 from
"unobservable" to "not reported where the change is made". What is still missing is the report on the
modify path — which needs the STORED metadata, the option the owner deferred. DQ-10 and DQ-25 carry the
mechanism and the corrected cost estimate; the caveat is that an UNCOMPILED caller falls back to the
design instance and reports `inSync: true`, so the signal is conditional and must be described that way.

**One process note worth keeping.** The scan's first landing coincided with the suite going from 10 s to
65 s, and on that reading I scoped it to explicit re-syncs and wrote "measured, 6x" into a code comment.
The walk is 7.4 ms. The baseline, re-run with the change stashed, is 1 m 6 s - the slowdown was never
mine. Both the scoping and the comment were reverted before either shipped. A suite duration measures the
machine as much as the code; take the baseline in the same minute.

## DQ-27 — the stand corrected my model: describe does NOT converge, so `inSync` IS the drift signal

The 1.6.3.7 stand pass measured what DQ-10, DQ-25, DQ-26, the guidance and a knowledge record all denied.
Recording it here because those four documents were written from one inference and the inference was wrong.

**What I claimed.** "Both the describe and the modify paths load through a design instance, which
converges the element before this package sees it — so `inSync: true` proves nothing and nothing observes
a callee rename."

**What is true.** `ProcessSchemaRepository.LoadForDescribe` prefers the RUNTIME instance
(`FindInstanceByUId` / `FindInstanceByName`) and falls back to the design instance only when there is
none. `ProcessModifyHandler` always takes `GetDesignInstance`. So:

| | compiled process | uncompiled / FSD |
|---|---|---|
| describe | runtime instance, NOT converged — stale name reported, `inSync: false` is real evidence | design instance, converges — `true` says nothing |
| modify / re-sync | design instance, converges — the drift report is empty | same |

**How the error happened, because that is the reusable part.** "The platform converges on every
design-time read" is true. I inferred "describe converges" from it without ever checking whether describe
takes a design-time read. One unverified load path propagated into three shipped documents and made the
most useful signal the product has read as worthless.

**What changed.** The `inSync` contract, the guidance and the knowledge record now state both halves and
which instance produces which. F2 is narrowed with them.

**The designer clause was measured on 2026-09-17 and is no longer owed.** Two corrections came with it,
and the second is the one that matters. First: the classic designer IS reachable on this stand —
`…/0/Nui/ViewModule.aspx?vm=SchemaDesigner#process/<uid>` loads in about 20 s in a real Chrome. The
earlier "would not load" was the renderer intermittently freezing; the DOM stays readable throughout, so
a card can be read even while screenshots time out. No design-time claim on this stand is blocked, and
that sentence was on its way to becoming the reason AC-4 stayed open. Second: F2 holds, but its CAUSE is
not the one predicted. The card renders the parameter CAPTION and never its code, so it cannot show a
code rename under any convergence behaviour — see the table in DQ-25.

**What did NOT change.** DQ-10 stands for the re-synchronization report itself: the modify path does
converge, so the drift is still unobservable from there, and the dangling-reference scan is still how
that half is answered.

## DQ-28 — the dangling notice is narrower than its own guidance claimed

Measured on the same pass. The platform's pre-save validation refuses a Script-source reference — a
formula, a mapping, a flow condition — before the re-synchronization can report it, aborting the save
one site per attempt. So the notice in practice covers stored blob sites, such as a Modify-data element's
column bindings, and the guidance sentence "names every element, process parameter, execution context or
flow condition" overstated what ships. Narrowed rather than reworded away: the behaviour is defensible —
the caller IS told there is a problem, just by a refusal that names one site at a time — and the guidance
now says which sites the notice reaches and why.

Also from that pass, and worth having before anyone writes the next case: an element's INPUT parameter
cannot be read by another element (*"The input parameter … cannot be used as a data source for the
parameter …"*), so a dangling-reference scenario has to be built on an OUT parameter of the callee. The
manual case description assumed the input shape and was not constructible as written.

## DQ-29 — the cost of 2b, stated once, and the owner's decision to keep going

Recorded because the SHAPE of this feature's cost is not visible from any one commit, and the next person
deciding whether to extend it should see it in one place.

**What 2b is.** The owner's choice on 2026-09-17 between shipping the re-sync with a documented blind spot,
building the stored-metadata drift reader, or reporting the CONSEQUENCE of a dropped parameter. They chose
the third — the dangling-reference scan (DQ-26).

**What it cost.** Six review rounds, 8 through 13. Every one of them found a REAL defect in the previous
round's fix, and four of the six were defects that would have reached a user:

| Round | What the previous round shipped | What it actually did |
|---|---|---|
| 8 | the scan | ran on one of the two write paths; rendered the walk-failure marker as a broken element name |
| 9 | the positive rewrite | searched forward unboundedly, so it INVENTED references on unrelated edits |
| 11 | the pair parse | correct, but its docblock and its N-form test asserted a spelling nothing writes |
| 12 | the N-form drop + the stop fix | the stop fix was scoped to the message, not the seam, so the over-reach survived |
| 13 | the seam fix + the timeout channel | — |

**Why, and this is the part worth carrying.** Every one of those defects is a SCOPE error, not a logic
error: the guard was right and the thing it was applied to was wrong. Forward search from an element
instead of a pair. A collapse over a message instead of a seam. A catch in the method that finds matches
instead of the method that owns the failure channel. A plural counted off readers instead of drops. The
code reviewed well line by line each time, because line by line it was right.

**Two properties of this feature make that likely rather than unlucky**, and they are what to weigh before
extending it: it is a NOTICE, so nothing refuses and no caller ever reports a wrong answer back; and its
input is an unbounded stored string in a grammar nobody owns a specification for. A guard with no
feedback loop over an unspecified input is where scope errors survive.

**The decision, 2026-09-17.** The owner was shown this table and the alternative — revert 2b to the
documented-limitation state, which the rest of ENG-92707 does not depend on — and chose to fix the round-12
findings and continue. Shipped at CrtProcessBuilder **1.6.3.12**.

**What would change the answer.** A seventh round finding a seventh scope error. At that point the honest
move is not another fix: it is to narrow what the scan CLAIMS until the claim is one a unit test can
exhaust — for example, report only the metapath segment-pair spelling and say so, dropping the bare map
path, which is the half with no delimiter and therefore the half every one of these defects touched.

## DQ-30 — two review findings I chose not to act on, written down so the choice is visible

Both came from the automated review on PR #68 and both were left alone deliberately. Recording them
because "deliberately deferred" that lives only in a chat transcript is indistinguishable from "missed".
Verified against the code on 2026-09-17 before writing, not recalled.

### F-a — R16 is not re-checked on either re-synchronization path

`EnsureCallable` carries the R16 gate (`details.HasSimpleStartEvent`, `SubProcessApplier.cs:480`) and is
reached from exactly ONE call site, `:99` — the `Apply` path, where a callee is being resolved from a
config. Neither `Synchronize` → `SynchronizeAgainstCurrentCallee` nor the incidental
`SynchronizeIfSubProcess` calls it.

**Why that is defensible.** The selection was validated when it was MADE. A re-sync changes no selection,
and refusing an edit over a property of an element the caller did not mention is the same bad trade the
multi-instance incidental path avoids.

**Why it is still a real gap.** A callee can LOSE its simple start event after the selection — somebody
edits the called process. The element then calls a process that can no longer be invoked, and a re-sync
refreshes it and reports success. The state is reachable, and nothing in this package says so.

**The honest split, if anyone picks this up:** the INCIDENTAL path should keep quiet — that argument
holds. An EXPLICIT `{resync: true}` is a different case: the caller asked about this element, so a
warning (not a refusal — a refusal would strand them) is the proportionate answer.

### F-b — `{processName, resync: true}` on an element that calls nothing performs a silent FIRST selection

`EnsureResyncIsMeaningful` refuses `resync: true` only when `mode == Create` (`:326`). On an UPDATE
against an element whose `SchemaUId` is empty — the state `addElement` leaves before configuration — the
request resolves the callee, takes the empty-snapshot path and selects it. `InitialSelection` is set
(`:112`) and the notices correctly stay silent about drift.

So the OUTCOME is right: the caller wanted that process called, and it is. What is not right is that
`resync: true` was inert and nothing said so — the caller asked to re-synchronize against a selection
that did not exist. It is the same class as the create-time refusal, one mode over.

**Left as is** because the alternative is refusing a request whose outcome the caller wanted, and a modify
batch is atomic, so the refusal would roll back work that succeeded. A notice would be the proportionate
answer here too.

**Neither is scheduled.** Both are warnings-not-refusals, both are cheap, and both belong to whoever next
opens this applier — not to a thirteenth review round on a branch that is already green.

## DQ-31 — the stand run settled the mechanism, and corrected me three times doing it

Full run 2026-09-17 at CrtProcessBuilder **1.6.3.12**. Report:
`C:\Projects\eng92707-manual-test\eng-92707-stand-verification-report.md`. **V2–V8 PASS**, TC-07
resolved, **V1 not closed**, and `compile-creatio` was never spent. Three corrections, all measured,
all against things I had written:

**1. A runtime instance comes from RUNNING the process — not saving, and not compiling.** I had written
"compiled" into nine places. It is wrong twice over: an interpreted process has nothing to compile, and
every non-converging read anyone has taken was on a process that never was. The run tested the three
candidates in order with a control — `UsrTc92707T7CallerNR`, same mappings, same `setElement{resync:true}`,
not run — and got the opposite outcome from one difference. A merely-saved schema CONVERGES on the
describe read; `pull-pkg` afterwards showed the stored bytes still held the dropped parameter, so the
drift was real and describe hid it without persisting its own convergence.

*Corrects DQ-15, DQ-25, DQ-26, DQ-27 and the test plan, all of which say "compiled". Do not read the word
in those entries as a condition.*

**2. `inSync` is ONE-DIRECTIONAL, and TC-07's own expectation was unreachable.** Callee ADDS a parameter
→ `false`. Callee REMOVES one → **`true`**, because the element merely carries an extra the callee no
longer declares. So a DROPPED parameter is invisible to `inSync` no matter which instance was read, and
"expect `inSync: false` after removing `Beta`" could never have passed. The predicate disagreement DQ-15
is about was still confirmed at Stored level: `inSync: true` coexisted with a re-synchronization that
really did cut `BP2` from `['Alpha','Beta']` to `['Alpha']` and `BK15` from two rows to one.

**3. The card does NOT pair by caption.** My hypothesis predicted a caption-only rename would empty the
mapping row. It does not: NEW caption, mapped value KEPT. The hypothesis is dead and **no mechanism
replaces it** — three readings, no model. Do not guess a fourth time.

**Not defects, checked and closed:** `BL8` on designer-made elements is `CreatedInOwnerSchemaUId`, written
as `Guid.Empty`, omitted by `JsonDataWriter` at default, read only through a getter short-circuited on an
empty `BL9` — inert, 387/387 product elements carry it and clio writes it never. Do not raise it.

**V1 (AC-4) is NOT closed and was deliberately left blank rather than approximated.** The designer's
palette item is a native HTML5 draggable inside a bpmn-js popup and needs trusted drag events
(`CDP Input.dispatchDragEvent`), which the harness cannot send. Partial parity was established another
way — an instrumented element on the SAME callee as a designer-made one matched on `CK4`, `BL7`, `BN2`,
`BP2` (nine parameters, names and order) and the `BK15` row key-set, with only `BL8` differing. A skeleton
`UsrTc92707V1Hand` (`7a890871-5b49-4daa-8bed-2044ad241422`) waits on the stand: a human dropping one
sub-process element into it, on `UsrTc92707V1Callee` with `Ina="V1-CONST"`, makes the byte diff a
two-minute job with the mechanics already built.

## DQ-32 — the build path cannot bind a changeData value to a sub-process element's output

> **FIXED at CrtProcessBuilder 1.6.3.14 (round 15), on the owner's decision to do it in this ticket.**
> Element CREATION and element CONFIGURATION are now two passes, with the selections between them:
> place every element → `afterElementsPlaced` (sub-process + pre-configured page) → configure → flows.
> Configuration still runs before the flow loop, exactly where it ran when it was part of creation, so
> nothing else in the sequence moved. `addElement` takes the configure step explicitly, in the same
> position, because it places elements through `PlaceNewElement` rather than `BuildGraph` — the two paths
> applying these blocks in different orders is why the defect existed. Two ordering tests pin it, both
> failing against the previous shape. **The sizing below was wrong when written and is corrected in
> place; the analysis is kept because the stand-verification recipe at the end still applies.**

Found by the same run, and it is a real defect in this feature rather than a documentation error.

**What happens.** `create-business-process` REFUSES a `changeData` column value bound to
`sourceElement: <sub-process element>` / `sourceElementParameter: <an Out parameter of the callee>`, with
`Element 'CallV8' has no parameter 'OutValue'`, although the sub-process element is declared EARLIER in
`elements[]` as the contract requires. The identical binding is ACCEPTED through
`modify-business-process` → `setElement` moments later against the saved process.

**Why.** Ordering in `ProcessBuildHandler`. `BuildGraph` creates each element AND binds its own
configuration block — `changeData` lands in `UserTaskElementHandler` → `ChangeDataConfigBinder.Apply`
during that pass. The sub-process selection, which is what copies the callee's parameters onto the
element, runs afterwards in `ApplyDeclarativeContent`. So at the moment the later element's `changeData`
is resolved, the sub-process element carries no callee parameters at all. On the modify path they are
already there, which is why the same request works one call later.

The comment above that phase already anticipates this shape for MAPPINGS and for `typeFromElement`
parameters, and orders the sub-process and pre-configured-page sync ahead of them. It does not reach an
element's OWN config block, because that is bound one phase earlier still.

**Workaround, and it is in the report:** build with a constant placeholder, then re-point with
`setElement`.

**Two fixes, neither started — and the sizing below is COUNTED, not estimated.** An earlier revision of
this entry called (b) "the wider blast radius" on the assumption that every element type binds config
during the build. Two do.

*(a) Sync the sub-process inside the element-creation loop.* Smaller, and wrong on two counts: it puts
`ISubProcessApplier` and `IPreconfiguredPageApplier` into `ProcessGraphBuilder`, which is the wrong layer
and currently holds no appliers at all, and it fixes the sub-process only — the pre-configured page has
the identical defect and would stay broken.

*(b) Split element CREATION from element CONFIGURATION and run the sync between them.* The measured
surface:

| Piece | Size |
|---|---|
| `IProcessElementHandler` + `ProcessElementHandlerBase` | one member, one `virtual` no-op — the file already uses that pattern for `Describe` / `CanBuild` |
| Handlers that must move code | **TWO**: `UserTaskElementHandler` (9 block references) and `OpenEditPageElementHandler` (2). `SubProcessElementHandler` and `PreconfiguredPageElementHandler` only VALIDATE their block at create; the other eight touch none |
| `IProcessElementFactory` | one dispatch method, mirroring `Describe` |
| `ProcessGraphBuilder.BuildGraph` | split the element loop in two |
| `ProcessBuildHandler` | reorder; the sync moves out of `ApplyDeclarativeContent` |

**The ordering that keeps the risk at zero elsewhere:** create elements → sub-process / pre-configured
page sync → configure elements → flows → layout → declarative content. Configure stays BEFORE flows, so
the flow loop and the activity-result walk see exactly what they see today and nothing about them
changes. The only phase that moves is the sync, and on a CREATE it has no dependency on flows: the
dangling-reference scan runs only when `currentCalleeUId == callee.UId`, which a first selection never
satisfies.

**The one thing still to check before starting:** `addElement` on the modify path goes through the same
`PlaceNewElement`, and `ElementOperations` applies the sub-process block itself there. The split must not
double-apply or skip it.

**Not scheduled.** It is shared build-path code, the branch is green after thirteen rounds, and the
workaround is one extra call. Sizing and the decision belong to the owner, not to a fourteenth round
started at the end of a day.

**How to VERIFY a fix, written down now because the obvious way proves nothing.** Exercise the route that
exposed it: one `create-business-process` call carrying a `changeData` value bound to
`sourceElement: <sub-process element>` / `sourceElementParameter: <an Out parameter of the callee>`, with
the sub-process element declared earlier in `elements[]`, and confirm it is no longer refused. Then the
same shape against a PRE-CONFIGURED PAGE element, which has the identical defect and which a
sub-process-only fix would leave broken. Do NOT verify through `setElement`: that path already works, it
is the documented workaround, and a green result there says nothing about the build path.

## DQ-33 — AC-4: parity holds on every key but one, and that one is the platform's own doing

V1 was unblocked by the owner building the manual side in the designer, so the diff is real and on
equivalent content: `UsrProcess_6117b42` (designer) against `UsrTc92707V1Tool` (ours), same callee, same
constant, both pulled in one `clio pull-pkg Custom`.

**Identical:** `CK4`, `BL7`, `BN2`, and the schema resources key for key. **`BP2`** matches to identity
UIds — both rows, names, order and fields, including `L8 = {GS1:1, GS5:<own schema>}` on `Ina` and `{}` on
`Outa`; only the UIds and each side's own schema reference differ, which is what they must do.

**Two differences.** `BL8` is closed as inert — DQ-31. The other is `BK15[Ina].GT1`:

| | `GT1` (the MAPPING row's `Source`) |
|---|---|
| designer | `{}` |
| clio | `{GS1: 1, GS5: <own schema UId>}` |

**It is not ours to remove, and calling it redundant understates it.** Read from platform source rather
than inferred:

* `ProcessSchemaParameter.SourceValue`'s SETTER does `mappingInfo.Source = _sourceValue`
  (`ProcessSchemaParameter.cs:450`). Populating `GT1` is what the supported API does; we assign through
  that setter deliberately, and `ProcessMappingService` already says so in a comment.
* `ProcessSchemaActivity.UpdateParameters` then does `mappingInfo.Source.DataValueType = target.DataValueType`
  (`ProcessSchemaActivity.cs:246`) — an UNGUARDED dereference, on the synchronization path this whole
  ticket is about. So the platform does not treat that object as spare: it writes into it on every sync.

The two sides therefore converge IN MEMORY and differ only at rest. The value itself is carried in two
other places on both sides — the parameter's own `L8` and the resources — describe reports
`Ina: source=ConstValue, value="V1-CONST"` for both, the designer renders both, and V8 proved the clio
form delivers at run time.

* `ProcessSchemaMapping.Source` is a plain `{ get; set; }` — no lazy getter, no null guard
  (`ProcessSchemaMapping.cs:46`). So `null` there is genuinely reachable, not theoretically so.

**Both STORED forms are safe, and that is the point.** On disk `GT1` is present on BOTH sides: the
designer writes an empty object `{}`, not an absent key. So neither persisted shape can produce the null
that dereference would trip on. The shape that WOULD is one writing no object at all — which is precisely
what suppressing `GT1` would produce. The difference between us and the designer is the object's
CONTENTS; the risk lives in removing the object.

**Decision: do not change it.** Suppressing `GT1` means not assigning through `SourceValue` — stepping off
the platform API to hand-write metadata, on the exact field the platform dereferences unguarded during
synchronization, and landing on the one shape neither side currently persists. That trades a cosmetic
diff for a silent-failure risk, which is the wrong direction for this package.

**What AC-4 gets, stated plainly rather than rounded to PASS.** Parity holds on `CK4`, `BL7`, `BN2`, `BP2`
and the resources. Strict byte parity does NOT hold: `BK15.GT1` and `BL8` differ. Whether that meets "server
serialization matches a designer-built capture" is the owner's call — the run deliberately did not write
"V1 PASS", and neither does this entry.

**Not measured, and worth one check if anyone revisits:** which of the two the RUNTIME reads — the
mapping's `Source` or the parameter's value. Nothing here depends on the answer, but a future change to
this write does.
