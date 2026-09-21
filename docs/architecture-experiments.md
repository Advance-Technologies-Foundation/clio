# Architecture experiments and decisions

Working decision record maintained by the main Codex agent. Updated 2026-09-21; discussion evidence read through 13:48:55 UTC. Kirill authorized autonomous experiment publication. This record distinguishes evidence and participant agreement from approval of new architectural policy.

Discussion: https://github.com/Advance-Technologies-Foundation/clio/discussions/1643

## Intent

Reusable Core/Composition libraries, optional adapters, explicit primitive I/O, partner workflows, agent/team-friendly ownership, dependency-aware testing, and compatible runtime updates. Update coverage must not dictate incorrect layer placement. Selective CI and production update readiness remain unproven.

## Convergence versus proof

- Participant agreement: responsibility-based layer boundaries; execution ownership and status are distinct from connection continuity; retaining a process alone does not establish detached-operation lifetime.
- Empirical support: controlled complete-runtime update fixtures; managed-only path/stream retirement probe reproduced on Windows by Codex and reportedly macOS by Alex.
- Open product decision: is automatic staging with activation on natural reconnect sufficient for host changes, or is transparent replacement during an existing connection a required future guarantee? Neither permanently excluding nor promising that guarantee is approved.
- Experiment publication authorized; no production shipping/merge approval.

## Experiment ledger

| ID | Idea / owner | Acceptance and evidence | Status / limits / next action |
|---|---|---|---|
| E1 | Complete-runtime replacement / existing Clio10 prototype | Baseline 7225ebcaa; docs/runtime-update-proof.md and docs/primitive-contract-boundary.md. Same MCP SDK connection, pinned old call, newer workflow, retained offline installation. Alex reported five reproduced cases in comment 18539190. | Supported in fixtures, not all client discovery, detached work or production trust. |
| E2 | Managed load mode versus retirement / Codex; Alex reproduces | Branch krylov/runtime-retirement-probe, SHA a72fae8ea62ee9dcddc757014b686811306a94d4. Eight combinations of loading mode, early deletion, lazy/preloaded dependency. Windows raw JSON in experiments/RuntimeRetirement/windows-results.json. Alex's macOS report: comment 18540361. | Windows measured locally; macOS peer-reported at same SHA/runtime. Both expose lazy dependency loss. Native/resources, concurrent owners and real Core unloading not tested. Ask for raw macOS JSON in the next exchange. |
| E3 | Detached-operation lifetime / Alex accepted ownership | Operation ID before V1 completes; activate V2; status remains truthful; V1 effect once; failure/cancellation; process loss yields uncertainty. Acceptance in comment 18540212. | Alex implementing; no branch/results received in this snapshot. Codex cross-review on Windows after publication. No durable replay assumed. |
| E4 | Host policy / Vladimir accepted with draft matrix | Scenarios: idle connection, active call, detached work, remote accepted work, natural reconnect. Draft comment 18540359; refinement 18540446. | Useful draft, not verified acceptance. Corrections below required before treating it as agreed. |
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

## Next coordination actions

Thank Alex for the exact-SHA macOS reproduction and request raw JSON on his branch. Confirm ownership-based retention rather than reliance on filesystem refusal.

Thank Vladimir for the matrix. Ask for three separate columns: outstanding execution/evidence, transport replacement, and activation policy. Correct rows (a)/(e): idle or quiescent is not transparent transport handoff, and natural reconnect is not automatically safe for detached work. A shared host cannot be replaced for one idle environment while preserving another environment's in-process work merely by adding a target key. Ask him to make that counterexample explicit rather than introduce isolation as an unstated assumption. Keep the zero-user-action host guarantee marked as Kirill's open product decision.

## Join point

After E3 has a branch and focused results, independently inspect/reproduce; combine its ownership conditions with E2 retention evidence and the corrected E4 policy matrix. Summarize agreed guarantees, counterexamples, remaining tests and requested human decisions. Do not silently turn agreement into permission to ship.

## Evidence links

- [Alex accepts ownership and responsibility table](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540212)
- [Windows probe](https://github.com/Advance-Technologies-Foundation/clio/tree/a72fae8ea62ee9dcddc757014b686811306a94d4/experiments/RuntimeRetirement)
- [Alex macOS reproduction](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540361)
- [Vladimir continuity matrix](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540359)
- [Per-target quiescence proposal](https://github.com/Advance-Technologies-Foundation/clio/discussions/1643#discussioncomment-18540446)
