# ENG-91853 — Implementation prompts

Four sessions. Each block below is a complete prompt: paste one into a fresh session. **§0 is shared
context — paste it at the top of every session** together with that session's own block.

Order: **A ∥ B → C → D**. A and B touch disjoint files and cannot conflict; start A first because it is
the only part with algorithmic risk.

---

## §0 — Shared context (paste into every session)

```text
Task: ENG-91853 — "Exclusive and parallel gateways, conditional/default flows + basic Y auto-layout"
(https://creatio.atlassian.net/browse/ENG-91853). Component "bpms tools", assignee Dmitro Krestov.
Task 15 of the BP-generation task list; parent research ENG-90883.

READ FIRST, in this order, and treat them as the authority for every decision already made:
  C:/Projects/clio/spec/eng-91853-gateways-and-flows/README.md
  …/eng-91853-gateways-and-flows-serialization-capture.md
  …/eng-91853-gateways-and-flows-platform-reference.md
  …/eng-91853-gateways-and-flows-traps.md
  …/eng-91853-gateways-and-flows-layout.md
  …/eng-91853-gateways-and-flows-validator.md
  …/eng-91853-gateways-and-flows-plan.md
  …/eng-91853-gateways-and-flows-test-plan.md
Also read C:/Projects/clio/AGENTS.md and project-context.md, and grep docs/knowledge/ for the
symbols you are about to touch.

REPOSITORIES
  server   C:/Projects/workspace/ProcessBuilder   (package CrtProcessBuilder)
  client   C:/Projects/clio
  guidance clio-knowledge (GitHub, separate PR)
  read-only references:
    platform sources  C:/Projects/Creatio/TSBpm/Src/Lib
    shipped corpus    C:/Projects/PackageStore
    designer client   C:/Projects/creatio-ui,
                      C:/Projects/PackageStore/CrtProcessDesigner/branches/7.8.0
    C# test patterns  C:/Projects/UnitTests

BRANCHING — read plan D13 before you branch.
  ENG-95891 is MERGED in all three repositories, so branch from each one's own DEFAULT branch. There is
  no stacked branch and no rebase step.
      clio                 default branch: master   (contains 09898af82)
      crt-process-builder  default branch: main     <-- NOT master  (7e93995, PR #42)
      clio-knowledge       default branch: master   (84e2609, PR #122)
  Branch name in each: feature/ENG-91853-gateways-and-flows. One PR per repository, each targeting that
  repository's default branch — three PRs in total.

  Do NOT branch from wherever the working copy happens to sit. At the time of writing all three local
  checkouts were stale: clio and ProcessBuilder were still on feature/ENG-95891-formula-expressions
  (zero commits ahead of the default branch), and clio-knowledge was 216 commits behind origin/master
  with bundle-source.json reading libraryVersion 1.13.25 while clio's fixture pins 1.13.92.
  git fetch, then branch from the fetched default branch.

STATE AS OF 2026-09-05 (verify, do not assume)
  Bundled archive on clio master: CrtProcessBuilder 1.4.0.57.
  [RequiresPackage] floors on create/modify: 1.4.0.44.
  Package unit baseline: 63 fixtures, ~1190 [Test]/[TestCase]; the sprint note records 928 executed.
  ProcessLayoutEngine.cs untouched since 2026-08-10. ProcessGraphValidator.cs untouched since ENG-90883.
  Guidance article to edit (after pulling clio-knowledge):
  guidance/mcp/guides/processes/process-modeling.md

DO NOT re-litigate these — each is a measured decision, with the evidence in the documents:
  * Do NOT add a formula/condition validator. It was DELETED by
    spec/adr/adr-collapse-formula-validation-onto-platform-rule.md after measuring that the platform's
    own pre-save gate refuses a bad condition (the flow-schema generator builds the synthetic Boolean
    Source=Script parameter, and ParameterValuesValidationRule runs that generator first).
  * Do NOT model the default branch as ProcessSchemaExclusiveGateway.DefaultUId (BX1). It occurs 0 times
    in 1 099 shipped packages. A default flow is the PLAIN class + FlowType.Default + DefFlow manager item.
  * Do NOT set FlowType = Conditional on a plain ProcessSchemaSequenceFlow — the platform's design-time
    helper does an unguarded downcast and throws InvalidCastException at a human, not at the caller.
  * Do NOT implement R6 (gateway arity). It would reject 60+ shipped gateways.
  * Do NOT implement a kind change as remove + add: it regenerates the UId and appends the flow, which
    silently moves the branch to last in EVALUATION ORDER.
  * Do NOT add a guard for MatchBranchingDecisions (GV3). ENG-95891 wrote one, measured that nothing
    reads it, and removed it.
  * Do NOT re-sort flows[]. Array order IS runtime branch precedence and nothing else encodes it.
  * Do NOT support the activity-result (GV2) condition dialect on the WRITE side — read-back only; it is
    a separate follow-up ticket.

WORKING RULES
  * Follow AGENTS.md: kebab-case CLI options, DI over `new`, XML docs on public API, AAA tests with a
    `because:` on every assertion and a [Description] on every test method, CLIO* analyzer diagnostics
    clean in touched files.
  * Run the targeted test filter before every commit and record it in the commit/PR body.
  * Agentic code review: comprehensive fan-out before opening a PR and again before ready-to-merge;
    per-commit triage in between.
  * COMMIT THE SPEC FOLDER EARLY. The 2026-08-27 version of these documents was lost because it was left
    untracked through a merge; the Jira attachment was the only surviving copy.
```

---

## Session A — layout engine (server, ~0.5 d)

Do this first or in parallel with B. One production file, one test file; it cannot conflict with B.

```text
Implement work package S4 from the plan: branch-aware Y auto-layout.

Scope: exactly two files in C:/Projects/workspace/ProcessBuilder —
  packages/CrtProcessBuilder/Files/src/cs/Layout/ProcessLayoutEngine.cs
  tests/CrtProcessBuilder/ProcessLayoutEngineTests.cs
plus two constants in ProcessDesignConstants.Layout (GatewaySizePx = 55, BranchStep = 130).

Read eng-91853-gateways-and-flows-layout.md in full. It contains: the current algorithm, five hand-run
traces showing exactly which graphs break (cases B, C and D fail — B and C are the ticket's own basic
case, D is 15 % of real gateway processes), the three defects L1/L2/L3, the proposed five-phase
algorithm, the verification of that algorithm against all five traces, and the test list.

Implement §4 of that document as written. The two design decisions that are NOT free choices:
  * branches go DOWNWARD from the parent lane, not centred — because ProcessModifyHandler re-runs the
    layout on every modify, so a centred fan reshuffles the diagram on every unrelated edit;
  * lane order follows flow DECLARATION order, so top-to-bottom equals runtime evaluation order.

Acceptance: the nine tests in layout §7, all green, plus the existing ProcessLayoutEngineTests suite
unchanged and green. The engine must stay pure — no UserConnection, no I/O.

Handoff: leave the branch compiling and unit-green; add a line to spec/sprint-status.yaml.
```

---

## Session B — server core (server, ~1.2 d)

```text
Implement work packages S1, S2, S3 and S5 from the plan: the default-flow serialization triple, the two
gateway element handlers, the declarative flow-kind build path with its structural rules, setFlow, the
RemoveFlow detach fix, and the describe additions.

Read, in order: serialization-capture (§2, §3), platform-reference (§1, §2, §4), traps (T-1, T-3, T-4,
T-6, T-7, T-8, T-10, T-11, T-13, T-17), plan (D1–D5, D10–D12, S1/S2/S3/S5).

S1 — FlowManagerItems.Default (573ed909-e069-4161-b193-ae8dd9437c68). It is already recorded in
ProcessDesignConstants as prose with an explanation of why it was NOT given a constant ("this package
cannot build a default flow, so the constant would be dead"). Make it live and delete that reason in the
same edit. Add SchemaDefaults.ConditionalFlowNamePrefix / DefaultFlowNamePrefix.

S2 — two handlers, NOT one with two tokens: ProcessElementFactory.ResolveBuildType returns
SupportedTypes.FirstOrDefault(), so a shared handler would make describe report "exclusivegateway" for a
parallel gateway. Tokens "exclusivegateway" / "parallelgateway"; DefaultSize 55×55; CanBuild on the
concrete class; two AddScoped lines. Update the factory's hand-written rejection sentence.

S3 — the core.
  * IProcessGraphBuilder.AddFlow(schema, source, target, kind, condition) over ONE private
    CreateFlowElement switch that decides class + FlowType + ManagerItemUId + VisualType together.
    Those four fields are read by four different consumers and each wrong one fails differently.
  * Delete both NotSupportedExceptions in BuildGraph (the flow-kind refusal and the condition refusal).
  * setFlow: EXTEND setFlowCondition into {source, target, kind?, condition?}, keeping setFlowCondition
    as an alias. Reuse SetFlowCondition's existing clone body by EXTRACTING it into a helper
    parameterised by target class / FlowType / manager item — do not fork it. That body encodes hard-won
    facts (UId carried over, FlowElements index preserved, CreatedInSchemaUId restored,
    ModifiedInSchemaUId deliberately not, caption CLONED because LocalizableString.Merge returns by
    reference, CI11/CI12 carried although the platform's own copy constructor drops them).
  * Structural rules, server side, mirroring validator §4: self-loop refused; at most one default per
    source; a DIVERGING or-gateway's outgoings must be conditional or default; a parallel gateway's
    outgoings must be plain sequence; kind:conditional must carry a condition; a single unconditional
    continuation out of an or-gateway is NORMALISED to a default flow (that is what the designer does —
    it cannot draw a plain sequence flow from an or-gateway at all).
  * RemoveFlow: detach the endpoints (SourceRefUId = TargetRefUId = Guid.Empty) BEFORE removing, because
    FlowElements.Remove does not unregister the flow from the endpoints' keyed Outgoings/Incomings.
  * Extend ValidateStructure with the gateway/flow rules AND rewrite its now-false remark about gateways
    never appearing on the build path. Add a retry-loop fixture — reachability must keep passing.

S5 — describe: add the flow's `name`. `condition`, `branchesOnActivityResult` and CLR-type kind mapping
already exist from ENG-95891; do not touch them.

Acceptance: the test-plan cases in §3.1–§3.3 and §3.5, plus the whole existing package suite green
(baseline 63 fixtures / ~1190 test entries — do not regress).

Handoff: rebundle is Session C's; leave the branch compiling and unit-green.
```

---

## Session C — clio (client, ~0.8 d)

```text
Implement work packages S6, S7 and the rebundle half of S8.

Read: validator (all of it), traps T-14, plan S6/S7/S8 and D13.

S6 — clio/Command/ProcessModel/ProcessGraphValidator.cs (unchanged since ENG-90883):
  * FIX R14: scope it to sources with MORE THAN ONE outgoing flow. As written it raises an ERROR on 45
    shipped gateways (40 exclusive + 5 inclusive) whose only outgoing flow is a default flow — a shape
    the designer is FORCED to produce, because an or-gateway's allowed outgoing kinds are conditional and
    default only. Named counter-examples: BulkFileManagement/DeleteFilesInTable,
    CaseService/RunSendEmailToCaseGroup, CrtCaseCopilot/Copilot_GetCaseExternalMessages, BpmGDPR/BpmProcess6.
    Add a regression test that this shape yields NO finding.
  * ADD, as errors: R15 self-loop (source == target); at most one default flow per source; a diverging
    or-gateway must not carry a plain sequence flow (arity-scoped — 14 legacy single-outgoing gateways
    must NOT be flagged).
  * ADD, as a warning: a parallel join whose incoming branches trace back to a common exclusive split
    (promised in ai-bp-connection-rules.md, never implemented). Warning only.
  * REWRITE the R7/R9 message to name the real failure: FlowConditionalGateway.OnVisited throws
    MismatchItemsCountException when no condition matches and there is no default branch.
  * ADD an optional `condition` to ProcessGraphEdgeArg plus a condition-required error (the server-side
    guard is the load-bearing half and lands in Session B; this is the pre-flight half).
  * RECORD in spec/ai-business-process-generation/ai-bp-connection-rules.md: R14's arity scope, the R15
    self-loop rule, the three new rules, and the explicit decision NOT to implement R6.

S7 — the nine items in validator §5. Two deserve care:
  * DescribeProcessPrompt must introduce `condition` AND `branchesOnActivityResult` TOGETHER. ENG-95891
    reverted its own edit to this exact file, on the project owner's scope call, so that this ticket
    ships both at once (clio commit 09898af82: "half of the contract is worse here than none"). Shipping
    one repeats the mistake that revert undid.
  * ValidateProcessGraphTool's description currently says "a conditional branch IS buildable even though
    a gateway ELEMENT is not". That clause becomes obsolete — rewrite it, do not leave it standing.

S8 (rebundle half):
  * pwsh ./rebundle-process-builder.ps1 -PackageRepoPath <ProcessBuilder checkout> -Version X.Y.Z.W
    The version MUST go up; the current archive is 1.4.0.57.
  * Refresh the pins in clio.tests/Common/BundledProcessBuilderPackageTests.cs, and raise the
    [RequiresPackage] floors on create/modify (currently 1.4.0.44) to the version that first ships
    gateways — and credit the floor with the change that justifies it, which that file pins separately.
  * TRAP: an install command resolves the archive from the BUILD OUTPUT directory, so `clio compress -d
    <repo path>` has no effect until clio is rebuilt.
  * Extend clio.mcp.e2e: CreateBusinessProcessToolE2ETests, ModifyBusinessProcessToolE2ETests,
    DescribeProcessToolE2ETests, ValidateProcessGraphToolE2ETests, and add setFlow to
    ProcessDesignerContractRequiredArgsE2ETests.

Acceptance:
  dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)"
Plus: state "MCP reviewed" naming the artifacts touched, and
"ClioRing compatibility reviewed, no Ring-consumed contract changed" — Ring's tool surface is
clio-deploy-creatio, clio-env-info, clio-import-iis-environments, clio-list-packages, clio-manage-envs,
clio-restart, clio-uninstall-creatio, clio-version (checked ClioRing.Ipc, ClioRing,
ClioRing.Desktop/actions.json); no process-designer tool is consumed.
```

---

## Session D — verification, guidance, PRs (~0.5 d)

```text
Close the ticket: stand verification, the guidance PR, the review gates, and the three pull requests.

1. STAND VERIFICATION — plan §4, checks V1..V9. Run schema-write operations SEQUENTIALLY: a parallel
   burst trips IIS rapid-fail and downs a .NET Framework stand's application pool. The four that no unit
   test can substitute for:
     V3/V4  the right branch is taken, and FIRST-TRUE-WINS is real — swap flows[] order and confirm the
            outcome swaps (read SysProcessLog / SysProcessElementLog)
     V5     no default + nothing matches ⇒ MismatchItemsCountException in the process log
     V6     a parallel gateway waits for BOTH branches
     V1/V8  the diagram: correct dashed-default and diamond-conditional glyphs, and a readable retry loop
   The USER verifies UI results in the browser themselves — do not auto-open a browser after a write.

2. GUIDANCE — a pull request in the clio-knowledge repository (NOT in clio): the gateway/flow vocabulary,
   the evaluation-order rule, the run-time failure with no default branch, the two condition dialects,
   the R13 divergence from the platform, and the relayout-on-modify caveat. Bump libraryVersion +
   sequence, then re-pin clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json.

3. KNOWLEDGE RECORDS — two files under docs/knowledge/process-designer/ (plan §5):
   flow-kind-is-four-fields.md and branch-precedence-is-array-order.md.

4. REVIEW GATES — comprehensive agentic fan-out over each PR's full diff before opening it, and again
   before ready-to-merge.

5. PULL REQUESTS — three, one per repository, each targeting that repository's DEFAULT branch:
   clio -> master, crt-process-builder -> main, clio-knowledge -> master.
   Update spec/sprint-status.yaml with a row per repository and walk them to `done`.
   Re-attach the updated spec folder to the Jira issue as eng-91853-gateways-and-flows.zip, replacing the
   2026-08-27 snapshot.

6. DEFINITION OF DONE — plan §8. Do not close with an unchecked item.
```

---

## Why four sessions and three PRs

**Three PRs is forced, not chosen** — the work spans three repositories (server package, clio, guidance),
and the guidance library is published from its own repo by policy.

**One PR per repository, not more.** Splitting the layout into its own server PR would force a second
rebundle, a second version bump and a second round of clio pin refreshes for no reviewer benefit. Keep it
a self-contained *commit series* instead, so it can still be reviewed in isolation — or split later if a
reviewer asks.

**Four sessions, not one.** Each boundary is where the mental model changes, and each session ends
compiling and unit-green. ENG-95891 — a comparable ticket — ran to roughly sixty commits and thirty
archive restamps on one branch; one session cannot hold three repositories plus stand verification. A is
separated from B because it is the only algorithmically risky part and shares no files, so it can run in
parallel and de-risk the schedule.
