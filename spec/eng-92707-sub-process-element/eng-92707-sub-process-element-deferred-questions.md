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

**Reason.** The platform re-synchronizes every sub-process element on every design-time read, and BOTH
the describe and the modify paths load the schema through `GetDesignInstance`. So by the time the applier
takes its snapshot the element has already converged, and a snapshot diff can only ever report drift
that THIS REQUEST causes - the first selection, or a retarget. A pure `resync` after the called process
changed underneath a saved caller still *writes* the refreshed element, because the load converged it and
the save persists it, and reports nothing.

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
