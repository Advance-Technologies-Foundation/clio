# Claude consultation — two-environment slice

Review `rev_0a1eef5d066c4f66`, Claude via local Collab, completed 2026-09-22.
Reviewed commit: `1d61e0c08f98eb8f657a7f666ddf150c8eec0b54` against `0ab87e21c92bc00ee43ff41ee6b55a76cfd172a1`.

## Codex assessment

No blocking finding was reported. I checked the cited source paths and accept the three P3 observations as nonblocking qualifications, not reasons to expand this architectural slice. No implementation changes were made after this review.

- The run preceded creation of the reviewed commit. The evidence manifest pins the tested source bytes; Codex rechecked all ten hashes before committing. The reviewed commit contains that source snapshot. Git line-ending conversion can change raw checkout hashes across platforms. This note supplies the explicit reviewed-commit mapping; it does not claim Claude recomputed hashes.
- The 800 ms real-time drain budget in Q12 (and Q2) can expire before all intended submissions on a sufficiently slow host. Peers should inspect receipts and transcripts when a timing failure occurs and report any adjusted-budget reproduction separately. The work/cleanup overlap proof uses explicit handshakes; the deadline test is intentionally real-time.
- `Succeeded` together with `Reason: ExecutorLost` means the result is preserved while its executor was lost during cleanup. It does not establish successful cleanup or downgrade the known result. This inherited label can be made clearer in a production contract.

Claude was read-only and ran no tests. Codex's 13/13 run and three failed negative controls remain local measured evidence; this consultation is source/evidence review, not independent execution or policy approval. The frozen architecture document remains unchanged.

## Claude's returned review

Review of commit 1d61e0c08 (two-target extension) against parent 0ab87e21c, scope limited to experiments/GenerationQueue. I read the full Program.cs, QueueTests.cs, MultiEnvironmentTests.cs and verify.ps1 in context and cross-checked the transcripts in the evidence bundle.

**No blocking finding remains.** I could not falsify the small architectural conclusion from the source. The per-target ownership gate, the global drain condition, the crash reconciliation loop and the three Q11–Q13 oracles do what the documents say they do, and the Codex-measured transcripts are consistent with the source paths I traced.

What I checked and confirmed from source (not from measurement):

- **Dispatch gate** at `Probe/Program.cs:220-233` iterates a snapshot of the queue, re-evaluates `running` after each dispatch, so a second entry for the same target in one pass is skipped. Per-target FIFO holds because the list preserves insertion order and expiry only removes the head it inspects.
- **Retirement condition** at `Probe/Program.cs:235-236` requires `running` empty and no active-generation queue entries. `running` retains entries in Succeeded-with-held-cleanup state until `Released` arrives (`:254`), so B's cleanup does block replacement. The stale old-epoch `Exited` event after graceful stop is discarded by the epoch filter at `:110`, so it cannot corrupt the V2 start.
- **Crash path** at `Probe/Program.cs:282-287` iterates all owners, downgrades only Dispatched/Running to Unknown, preserves Succeeded, and clears ownership. Matches Q13's assertions and the Q13 transcript (sequence 14–17).
- **Q11 overlap oracle** at `Tests/MultiEnvironmentTests.cs:17` is deterministic, not timing-based: submit reply, Queued event and PumpAsync all run inside one event-loop turn before the subsequent query command is handled, so `Owns` on b-running is true in the normal build and false under `--serialize-targets`. The serialize-targets transcript (session 756ec7…) shows exactly that.
- **Q11 retirement oracle** at `Tests/MultiEnvironmentTests.cs:37` is checked while b-queued still owns cleanup; the ignore-b-drain transcript (session d131bb…) shows OldDrained at sequence 29 immediately after a-queued Released, matching the expected failure.
- **Evidence internals**: exit codes 0/1/1/1/0, 13 passed in normal.trx, 22 session directories (I counted them from the diff), effect files show one V1 PID and one V2 PID per Q11 run, and the three mutation logs fail at the intended assertion lines (QueueTests.cs:77, MultiEnvironmentTests.cs:17 and :37).

Findings, ordered by severity:

**[P3] Evidence bundle is not tied to the reviewed commit.** `evidence/20260922T123800858Z/base-commit.txt:1` records the parent `0ab87e21…`, and `worktree-status.txt:1-7` shows the run was made from a dirty worktree before this commit existed. The only link between the bundle and the committed sources is `source-hashes.json`. That is acceptable for a prototype, but a reader of the bundle alone cannot see which commit it validates. I could not recompute the SHA-256 values in this read-only review, so "manifest matches actual files" remains Codex's claim, not mine.

**[P3] Q12 has a real-time window that can flake on a slow host.** `Tests/MultiEnvironmentTests.cs:80-83` submits two V2 requests after arming an 800 ms drain budget. If the deferral fires before the second submit is handled, `b-v2` is rejected as incompatible-generation, `Find(state, "b-v2")` at `:96` throws, and the failure would look like a regression rather than a scheduling hiccup. Q2 has the same pattern with one submit; Q12 doubles the exposure. Not an architectural defect and the recorded run passed; noted only because a spurious red on a later peer reproduction could be misread.

**[P3] Reason label on a preserved success can mislead evidence readers.** In the Q13 transcript (`sessions/Shared_crash_preserves_each_targets_distinct_outcome/…/transcript.jsonl:20`) known-a is reported as `Status: Succeeded, Reason: ExecutorLost`. Source at `Probe/Program.cs:285` sets the reason unconditionally. The status is correct and asserted; the reason string just reads as if the success were in doubt. Pre-existing behavior from Q7, so not new to this commit.

Nothing here contradicts the stated scope limits. The documents correctly describe the worker's overlap as dictionary-held state with explicit release signals, the shared-delay tradeoff, and the absence of durability, MCP, Creatio, credential isolation, N-target scaling and supervisor recovery claims.

Verification gaps:

- I did not run any tests or the verify script. All pass/fail statements above about the recorded run are read from the committed TRX and transcript files, not reproduced.
- I did not recompute the SHA-256 manifest; with only Read/Grep/Glob available I could not hash files.
- I did not assess the frozen published architecture document, per scope.
