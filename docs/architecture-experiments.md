# Architecture experiments and decisions

Working decision record maintained by the main Codex agent. Updated 2026-09-21; discussion evidence read through 14:31:17 UTC. Kirill authorized autonomous experiment publication. This record distinguishes evidence and participant agreement from approval of new architectural policy.

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
| E3 | Detached-operation lifetime / Alex accepted ownership | Operation ID before V1 completes; activate V2; status remains truthful; V1 effect once; failure/cancellation; process loss yields uncertainty. Acceptance in comment 18540212. | Published Alexandr-Kravchuk/detached-operation-probe: ac85e894b1d7 initial proof, e3138962cec2aad53918e791c1fc7d219d38a43d adds swap windows, 9401b5a937cb adds Windows evidence only. Initial 17/17 reproduction exposed barrier defects. Alex repaired at f7baab2ed, published 551f25c925383f27e58d6961e5cf07f3ebc91288 with Windows evidence. Codex independently reran 21/21 on Windows/.NET10.0.12 and inspected the fixes; narrow Claude recheck also marks all four closed. No durable replay assumed. |
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

## Next coordination actions

Alex's four repairs have source and independent execution support; retain regression-sensitivity qualifications instead of treating stress sampling as exhaustive proof. Do not request unrelated implementation expansion.

Vladimir owns the next bounded extension: explicitly integrate the repaired E3 revision, hold global admission closure through backend termination and replacement readiness, assert a different backend identity and a successful post-replacement operation, and attempt admission during handover. Fail explicitly if expected acknowledgements do not arrive; clean up owned child processes on failure. No need for a competing Codex supervisor. His reconciliation qualification in comment 18540920 is accepted: remote status alone does not preserve unfinished local work. Stable supervisor continuity remains distinct from whole-product updates.
## Join point

After the E3 barrier repairs and E4 composition probe are published, independently inspect/reproduce; combine its ownership conditions with E2 retention evidence and the corrected E4 policy matrix. Summarize agreed guarantees, counterexamples, remaining tests and requested human decisions. Do not silently turn agreement into permission to ship.

## Evidence links

- [Alex accepts ownership and responsibility table](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540212)
- [Windows probe](https://github.com/Advance-Technologies-Foundation/clio/tree/a72fae8ea62ee9dcddc757014b686811306a94d4/experiments/RuntimeRetirement)
- [Alex macOS reproduction](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540361)
- [Vladimir continuity matrix](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540359)
- [Per-target quiescence proposal](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540446)
- [E3 independent Windows reproduction, counterexamples and repair handoff](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540898)
