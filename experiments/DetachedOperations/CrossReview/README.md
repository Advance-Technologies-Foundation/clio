# Independent Windows cross-review of E3

Reviewed source: `e3138962cec2aad53918e791c1fc7d219d38a43d` on Alex's detached-operation branch. Later `9401b5a937cb` adds Windows evidence only; the reviewed source is unchanged. Codex assisting Kirill performed this reproduction on Windows 26200 / .NET 10.0.12.

## Original suite

All 17 cases passed using the four commands in the parent README. Raw output: `../codex-windows-results.json`. This independently supports the measured detached ownership, terminal classification and process-loss recovery scenarios; it does not establish safe concurrent admission or production readiness.

## Counterexamples (unmodified ledger)

Run from repository root:

```shell
dotnet run --project experiments/DetachedOperations/CrossReview/CrossReview.csproj -c Release
```

Raw output: `../codex-counterexamples.json`. **Exit 0 means both defects reproduced**, not that the barrier passed a safety test.

1. A global window and an envA window are both granted simultaneously. Global/target exclusion is asymmetric.
2. With the existing evidence lock deliberately held through reflection, completion publishes `Succeeded`; a global swap window is granted although the completion writer is blocked and disk contains only the begin record. A caller shutting down at that point can lose the terminal evidence. After releasing the writer, disk contains two records. No kill or simulated restart is needed to prove this ordering.

The runner writes only to its unique temp directory. It performs no network operations or process kills. Reflection is test instrumentation, not a proposed API. Original source remains unchanged; Alex owns the repair.

## Source findings not stress-measured here

- `Begin` releases `_swapLock` after `IsScopeHeld`, before inserting into `_live`. Another thread can acquire a window between those steps. The existing sequential A5 cases cannot exclude this interleaving. The admission decision and registration need one synchronization boundary.
- A5d reacquires a window; it does not actually start work after release, despite its title, and leaves the acquired window undisposed.
- Terminal status is not the same as completed task teardown or finished persistence. A host shutdown predicate must cover its stated ownership scope; per-target idleness never authorizes replacement of a shared host while another target has work.

These findings narrow the barrier claim; they do not negate the original 17 observations. No production Core, adapter or primitive behavior changed. This is a bounded review artifact, not a second implementation of Alex's ledger.

Local Collab Claude review rev_80a7592e42a540fc independently confirmed the admission race (P1), overlapping windows, terminal-before-persistence ordering and A5d mismatch (P2) from the exact reviewed source. Claude did not execute tests; Codex reproduced the original suite and the two counterexamples above. All four source findings are accepted. Its note about missing committed Windows A5 evidence applies to e3138962c; Alex's successor 9401b5a93 supplies that evidence without source changes.

