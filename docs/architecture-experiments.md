# Architecture experiments and decisions

Working decision record maintained by the main Codex agent. Updated 2026-09-21; discussion evidence read through 14:49:25 UTC. Kirill authorized autonomous experiment publication. This record distinguishes evidence and participant agreement from approval of new architectural policy.

Discussion: https://github.com/Advance-Technologies-Foundation/clio/discussions/1643

## Intent

Reusable Core/Composition libraries, optional adapters, explicit primitive I/O, partner workflows, agent/team-friendly ownership, dependency-aware testing, and compatible runtime updates. Update coverage must not dictate incorrect layer placement. Selective CI and production update readiness remain unproven.

## Convergence versus proof

- Participant agreement: responsibility-based layer boundaries; execution ownership and status are distinct from connection continuity; retaining a process alone does not establish detached-operation lifetime.
- Empirical support: controlled complete-runtime update fixtures; managed-only path/stream retirement probe reproduced on Windows by Codex and macOS by Alex (raw JSON inspected); detached-operation probe independently reproduced on Windows (17/17), with initial admission-barrier counterexamples and subsequent repair recheck below.
- Open product decision: is automatic staging with activation on natural reconnect sufficient for host changes, or is transparent replacement during an existing connection a required future guarantee? Neither permanently excluding nor promising that guarantee is approved.
- Experiment publication authorized; no production shipping/merge approval.

## Experiment ledger

| ID | Idea / owner | Acceptance and evidence | Status / limits / next action |
|---|---|---|---|
| E1 | Complete-runtime replacement / existing Clio10 prototype | Baseline 7225ebcaa; docs/runtime-update-proof.md and docs/primitive-contract-boundary.md. Same MCP SDK connection, pinned old call, newer workflow, retained offline installation. Alex reported five reproduced cases in comment 18539190. | Supported in fixtures, not all client discovery, detached work or production trust. |
| E2 | Managed load mode versus retirement / Codex; Alex reproduces | Branch krylov/runtime-retirement-probe, SHA a72fae8ea62ee9dcddc757014b686811306a94d4. Eight combinations of loading mode, early deletion, lazy/preloaded dependency. Windows raw JSON in experiments/RuntimeRetirement/windows-results.json. Alex's macOS report: comment 18540361. | Windows measured locally; Alex committed macOS raw JSON and exact-SHA metadata in experiments/E2-RuntimeRetirement on his E3 branch (b2f9c1f94f67). Codex inspected all eight rows. Both expose lazy dependency loss; path deletion succeeds on macOS but may partially fail on Windows. Native/resources, concurrent owners and real Core unloading not tested. |
| E3 | Detached-operation lifetime / Alex accepted ownership | Operation ID before V1 completes; activate V2; status remains truthful; V1 effect once; failure/cancellation; process loss yields uncertainty. Acceptance in comment 18540212. | Published Alexandr-Kravchuk/detached-operation-probe: ac85e894b1d7 initial proof, e3138962cec2aad53918e791c1fc7d219d38a43d adds swap windows, 9401b5a937cb adds Windows evidence only. Initial 17/17 reproduction exposed barrier defects. Alex repaired at f7baab2ed, published 551f25c925383f27e58d6961e5cf07f3ebc91288 with Windows evidence. Codex independently reran 21/21 on Windows/.NET10.0.12 and inspected the fixes; narrow Claude recheck also marks all four closed. Mutation-control follow-up at 1f17a7763d4a76c823cb036b939a712a7fad9989 independently passes 22/22; repaired 0 sampled violations versus broken 16. No durable replay assumed. |
| E4 | Host policy / Vladimir accepted with draft matrix | Scenarios: idle connection, active call, detached work, remote accepted work, natural reconnect. Draft comment 18540359; refinement 18540446. | Published nikonov/host-update-policy, docs/host-update-continuity.md. Vladimir accepted separation of execution, transport and policy (18540642); withdrew redundant counterexample and published supervisor-plus-ledger probe at 07ac87df051708f7d8130f2206ebe37e4704fcf0 (nikonov/supervisor-quiescence-probe). Codex independently reproduced 4/4 Windows cases; source kills but does not respawn, so evidence is drain-before-termination only. Require global admission/drain for shared-host replacement; external reconciliation alone does not preserve unfinished local orchestration. |
| E5 | Production executable trust / no implementation owner yet | Trusted publisher/source and verification before activation. | Open release gate; no production signing verification proof. |
| E6 | Layer-directed testing and embedding | Architectural boundaries permit focused composition tests and reusable libraries without CLI/MCP. | Selective CI speedup unmeasured. Do not infer automated test-selection correctness from project separation. |

## Rejections and qualifications

- Rejected: path loading makes all in-session reclamation impossible. Alex withdrew after measuring unload/collection (18540212). Path loading pins mapped files while alive on Windows; controlled collection allowed deletion.
- Rejected: stream loading by itself makes early deletion safe. Lazy dependency failed on both platforms. Failed recursive deletion on Windows also removed the lazy dependency. Preserve required files through execution ownership.
- Rejected: zero open requests establishes safe replacement. Detached work may continue after response.
- Not established: quiescence establishes transparent stdio replacement. Even zero outstanding work does not transfer the old process's open client pipe/handshake to its replacement.
- Not established: per-target quiescence permits replacing a shared host process. If the process owns work for A and B, replacing it still affects B while A is idle. Per-target routing helps only with an explicitly different isolation/handoff mechanism, which is not demonstrated.
- Qualified: a natural reconnect supplies a transport restart opportunity, not guaranteed operation continuity. Active/detached work and remote outcome reconciliation still matter.
- Qualified: read-only versus effect-bearing classification is not necessarily nonexistent in Clio8; avoid adopting the matrix's universal absence claim without source audit.
- Qualified: the controlled held-call runtime fixture is not proof of every dead-socket/drain scenario. Mark its actual tested condition.

## Local Claude review

rev_90dcd502f921444e completed read-only against a72fae8ea: no blocking finding in the three narrow loading evidence claims. Advisories: documented invocation working directory, reliance on inherited project defaults, final deletion when directory already absent, and weak-reference timing. No independent execution by Claude. The documented repo-relative invocation was already executed successfully by Codex; Alex also reports using both documented commands. Thus the suggested command failure is not confirmed. Other advisories remain to assess narrowly; no full rereview of unchanged target needed.

## E3 independent cross-review

Evidence branch: [krylov/detached-operation-cross-review](https://github.com/Advance-Technologies-Foundation/clio/tree/krylov/detached-operation-cross-review), exact SHA **29d29c1b8f883e98829b6a6ae64624fe7b4e2483**. Original ledger unchanged. Original 17 cases passed on Windows 26200 / .NET10.0.12. Raw observations, reproduction commands and reviewer-only counterexamples live under experiments/DetachedOperations/CrossReview and adjacent codex-*.json files.

- Source-confirmed admission race: Begin releases the scope lock before registering Running. A window can be granted in that gap; sequential A5 cannot falsify it. Not stress-measured in this review.
- Deterministically reproduced overlapping global/target windows: both handles granted.
- Deterministically reproduced terminal-before-persistence gap: completion writer held at its existing file lock, terminal visible and global window granted while only the begin record exists. Release writer and the second record appears. No process killed in this counterexample.
- A5d checks window reacquisition, not the promised successful Begin after release, and leaks its handle.

Local Claude read-only review **rev_80a7592e42a540fc** confirmed those four source findings (admission P1, others P2). Codex accepts them after source inspection and the two reproductions. Claude ran no tests; its observation that committed Windows A5 output was missing applies to e3138962c, not the evidence-only successor 9401b5a93. Passing fixture observations stand; safe concurrent swap admission is **not confirmed**. This is a gap in the probe, not evidence that the layered architecture must be replaced.

## Repair recheck and E4 reproduction (14:36 run)

E3 at 551f25c925383f27e58d6961e5cf07f3ebc91288: independent Windows 26200/.NET10.0.12 run passed 21/21, with 114 admissions, 10 refusals, 7 windows and zero sampled violations. Source closes the four reported defects within the probe contract. A5h samples at acquisition, not throughout the held interval; A5g polls completion rather than deterministically pausing persistence. Regression sensitivity remains narrower than the test titles. Arbitrary post-Complete task teardown, I/O failures and production readiness are not proved. Local Claude narrow recheck rev_d75a28ade73446db marks all four prior defects closed; its P2 regression-sensitivity qualification is accepted. Disk-write failures can strand Running/retained owners and failure paths may exit before JSON: recorded limitations, not production guarantees. Claude could not access external Git metadata; Codex independently resolved the full SHA and inspected the actual diff. Raw Codex .NET10.0.12 evidence is on krylov/detached-operation-recheck.

E4 at 07ac87df051708f7d8130f2206ebe37e4704fcf0: independent Windows 26200/.NET10.0.12 run passed S1-S4. Evidence/review published on krylov/supervisor-probe-cross-review at 837d0c18d. S1 loses envA, S2 retains envA, S3 loses envB despite draining envA, S4 retains both. The runner starts each backend once and kills it; there is no replacement startup or post-replacement call. README wording saying Kill + respawn exceeds implementation. A source link inside this checkout does not automatically pick up another branch's repair; E4 still contains E3 at e3138962c. These are useful controlled termination results, not end-to-end handover.

## Latest evidence and policy challenge (14:56 run)

E3 mutation control at **1f17a7763d4a76c823cb036b939a712a7fad9989**: independently reproduced 22/22 Windows/.NET10.0.12. Repaired arm: 0 sampled violations, 66 admissions, 55 refusals, 9 windows. Deliberately split arm: 16 violations, 20 admissions, 84 refusals, 16 windows. This closes the earlier sensitivity qualification for the injected defect, not arbitrary concurrency. Evidence: krylov/detached-operation-mutation-review at f4a715dad. No repeat Claude review of unchanged normal admission logic.

E4 S5 at **d01e236277654a996bc189be816b1eb655fadfbe**: independently reproduced 5/5 Windows/.NET10.0.12. E3 repair is explicitly integrated. S5 still disposes the global window before killing the original backend; its V2 lease retains the same original Process object and is completed locally without sending work. No replacement starts. Evidence: krylov/supervisor-handover-review at b69a92784. The requested real handover remains open; these are direct ledger-admission observations.

Alex's reconcilability.md / comment 18541234 introduces a proposed three-tier policy. Its live observations are peer-reported, not independently reproduced here. The distinction between observable state and attributable outcome is useful, but the conclusion that artifact reads or a last-result endpoint permit skipping drain is not established:

- A pre-existing artifact or one produced by another caller can satisfy a name/version read without proving this request completed or finished all steps. Absence may be partial/in-progress, not a definitive failed outcome.
- Even assuming compilation is serialized, A completes, then B completes, then a recovering client reads the last result: it receives B's result. A's durable terminal marker does not correlate that endpoint to A. Persisting A's actual result would be different evidence.
- Detecting interrupted work afterward does not satisfy preservation of its execution. Skipping drain intentionally permits interruption and needs an explicit product-policy decision plus operation-specific evidence.
- A get-info payload with no restart identity establishes only that this inspected surface cannot answer; it does not prove every possible server/platform observation permanently lacks identity.

These are challenges to inference and policy, not a new implementation assignment or permission to alter Core. Alex should refine the matrix and test pre-existing artifacts/intervening compilation before asserting attribution. No live environment activity by Codex in this run.

## Next coordination actions

Alex's four repairs and mutation control have independent support. His next contribution is tightening the attribution and drain-policy inference, not expanding the ledger framework.

Vladimir owns the next bounded extension: with the repaired E3 now integrated, hold global admission closure through backend termination and replacement readiness, assert a different backend identity and a successful post-replacement operation, and attempt admission during handover. Fail explicitly if expected acknowledgements do not arrive; clean up owned child processes on failure. No need for a competing Codex supervisor. His reconciliation qualification in comment 18540920 is accepted: remote status alone does not preserve unfinished local work. Stable supervisor continuity remains distinct from whole-product updates.
## Join point

After the E3 barrier repairs and E4 composition probe are published, independently inspect/reproduce; combine its ownership conditions with E2 retention evidence and the corrected E4 policy matrix. Summarize agreed guarantees, counterexamples, remaining tests and requested human decisions. Do not silently turn agreement into permission to ship.

## Evidence links

- [Alex accepts ownership and responsibility table](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540212)
- [Windows probe](https://github.com/Advance-Technologies-Foundation/clio/tree/a72fae8ea62ee9dcddc757014b686811306a94d4/experiments/RuntimeRetirement)
- [Alex macOS reproduction](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540361)
- [Vladimir continuity matrix](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540359)
- [Per-target quiescence proposal](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540446)
- [E3 independent Windows reproduction, counterexamples and repair handoff](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540898)

## S6 arrived during the 14:56 review

Vladimir published 86c1f64232ad96c00db6ee73a2c87be3f9165a8a (18541428): real respawn, global window through readiness, pressure attempts and failure cleanup. Codex independently reproduced 6/6 Windows/.NET10.0.12; the earlier missing-respawn finding is superseded for S6.

However a one-line mutation changing the actual post-handover operation from succeed to fail still passes 6/6. The backend correctly omits that operation's effect, but the supervisor marks every done as Succeeded and accepts the separate readiness effect from the same V2 PID. Evidence and exact reproduction: krylov/supervisor-s6-review, experiments/SupervisorQuiescenceComposition/codex-s6-review.md and raw JSON. Source restored byte-for-byte; no implementation fix by Codex. Vladimir owns tightening the oracle to exact target/operation ID/PID plus terminal result and retaining this negative control.

Vladimir also adopted Alex's reconciliation table (18541433). That is participant agreement, not confirmation of attribution or approval to skip drain. The pre-existing-artifact and intervening-compilation counterexamples above remain unresolved; no weaker activation policy is approved.

## Mid-term boundary decisions: Codex contribution (15:25 checkpoint)

The [approved mid-term coordination post](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541706) assigns the following review to Codex. These are source-grounded proposals, not approved product policy or implemented guarantees. Source inspected on `krylov/clio-10-experiment` at 58a50c157; relevant implementation originates in the published blueprint.

| Expensive decision | Current evidence | Proposed boundary and remaining decision |
|---|---|---|
| Update and compatibility unit | `src/Runtime/RuntimeBundle.cs`, `src/Core/UpdatingComposition.cs`, and `docs/primitive-contract-boundary.md` load complete Composition + Primitives releases with private feature contracts. Static embedding explicitly shares matching capability assemblies. | Keep a complete runtime as the independently activated unit. A workflow uses one selected release throughout. Static consumers pin their feature SDK; they do not acquire new compile-time interfaces by downloading a runtime. Manifest ranges and assembly identity are admission checks, not proof of behavioral compatibility. |
| Values crossing the unload boundary | `src/Contracts/Contracts.cs` permits `object` arguments and `OperationResult.Payload`; `examples/Partner.Composition/FlushThenRestart.cs` returns partner-defined DTOs. `UpdatingComposition` returns results unchanged and deliberately retains compositions until shutdown. | Do not promise reclamation merely because execution finished. A caller-held result, callback, exception or task can retain runtime types. Decide whether the dynamic boundary copies outcomes into host-owned data, or exposes explicit retained ownership. Recommend host-owned data for dynamic invocation; keep typed DTOs available in the separately documented static embedding mode. This narrows a real existing API and requires review before implementation. |
| Settings ownership across versions | `CoreOptions.SettingsPath` and `ClioCore` load environment configuration; there is no versioned migration/writer protocol. | Core owns environment identity, credential access and configuration location; feature settings belong to the runtime. Bind a configuration snapshot to a workflow. For incompatible feature settings, prepare a separate version-scoped copy and validate before activation; do not rewrite the older session's settings. Accepted drift and cleanup timing still need an explicit policy. Avoid one setting file becoming a shared mutable ABI across all runtime versions. |
| Durable evidence ownership | E3 demonstrates a small host-owned operation ledger, not persistence of arbitrary feature state. | Keep operation identity, selected runtime identity and lifecycle status host-readable. Feature results carry an explicit schema identity and portable data. Recovery must not require loading an obsolete DLL just to read status. Persistence failure, retention and sensitive-result handling belong in Alex's lifetime contract; do not infer that storing a terminal marker preserves orchestration or proves remote attribution. |
| Partner extension and trust | The partner example registers workflows through DI; complete runtimes keep feature interfaces private. This is not a demonstrated independent plugin loader or sandbox. `BundleUpdateService` accepts HTTPS / loopback HTTP sources and validates package layout/compatibility; it does not establish publisher authenticity. | Support trusted partner composition packaged with a compatible runtime, or explicitly composed by a static host. Treat dynamically loaded code as having the host's privileges. Independent plugin release selection and untrusted execution are additional requirements, not free consequences of interfaces. Before production automatic acquisition, decide trusted publishers/feed controls and verification; compatibility checks are insufficient. |

These choices preserve reusable libraries without CLI/MCP dependencies. They also keep the simple path simple: app invokes Composition, Core supplies a pinned context, primitives perform I/O, and structured results return to the app. They do not require another architectural layer.

### Peer deltas since the checkpoint

- Vladimir reports repaired S6 and a failing-operation S7, including a negative control against the old oracle ([18541654](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541654)). Remote head observed as 3184c6f4a41a714ad4355d58d22d37f2742215d1. Independent reproduction of this repair is pending; earlier 6/6 is not sufficient evidence.
- Alex has withdrawn the attribution/drain inference and reports three controlled attribution counterexamples (25/25, [18541529](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541529)). These new cases and his Clio8 background-drain audit remain peer-reported, separate from Codex's independently reproduced 22-case baseline.
- Vladimir accepts the host-activation comparison assignment ([18541741](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541741)). Natural-restart activation removes a live transport handover requirement; it does not eliminate staging, selection, startup failure or settings compatibility. Waiting for a natural restart can be indefinite. His preference for this sequencing is a proposal, not Kirill's approval of a permanent product limitation.

Next agreement sought: resolve dynamic outcome ownership and settings ownership alongside Alex's lifetime contract and Vladimir's activation comparison. Keep empirical observations, participant agreement and human-approved policy distinct.

### Independent supervisor repair closure (15:35 checkpoint)

Vladimir's 3184c6f4a41a714ad4355d58d22d37f2742215d1 independently passes 7/7 on Windows 26200 / .NET10.0.12. S6 checks exact target/operation/PID and terminal success; S7 checks Failed plus absence of that exact effect. Six handover pressure attempts were refused, zero admitted. The two reported oracle defects are closed. Evidence: [krylov/supervisor-s7-review at f78bcf3a1](https://github.com/Advance-Technologies-Foundation/clio/tree/f78bcf3a1/experiments/SupervisorQuiescenceComposition). This reviewer did not repeat the peer's mutation run. Failed startup/rollback, an external MCP client connection and whole-product replacement remain outside this proof.

Alex delivered the lifetime contract and P1 persistence-failure case at 79e5e8151206, then O1 escaped-result retention at 92b89866f5a8 ([18541880](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541880)). His 27/27 and Windows reproduction are peer-reported, not independently reproduced by Codex yet. He accepts host-owned data across replaceable-runtime boundaries and corrected ledger-release versus collectibility. Persistence failure remains a decision: successful external work must not be relabeled failed or replayed merely because outcome storage failed.

Vladimir retracts the no-failure-modes claim for natural-restart activation and proposes confirm-V2-before-retire-V1 ([18541860](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18541860)). Overlapping startup requires a concrete check for exclusive ports/files/background effects; readiness must not admit customer work or mutate shared state before commit. Neither ordering nor fallback policy is approved as a universal product guarantee.

### Lifetime evidence recheck (15:43 checkpoint)

Independently reproduced Alex's 61ae2696f18797f79403ef3ad813fb5c5043433e: 28/28 Windows 26200 / .NET10.0.12. N1-N3 attribution controls, P1/P2 failed-write outcome/degradation and O1 retained runtime result all passed. Repaired admission sampled zero violations; mutant 19. [Raw evidence and limitations at 7a86d3f1c](https://github.com/Advance-Technologies-Foundation/clio/tree/7a86d3f1c/experiments/DetachedOperations). This tests the successor policy experiment; the older stuck-Running version was source-inspected rather than rerun. Pre-Append failure injection does not prove partial-write/fsync failure recovery.

Important correction pending peer agreement: the host-owned `_live` records survive runtime DLL unload; replacing their owning process/Core loses them. `Complete` already releases `_owners` even after persistence degradation. Thus evidence health can block replacement of its owner without necessarily blocking retirement of an unrelated runtime DLL. Do not conflate these boundaries or call the experimental persistence policy human-approved. Local Claude review rev_fb0ad4af33204158 pending against exact source/evidence; collect before another consultation.
