# Local validation results — 22 September 2026

## Current: two environments sharing one backend

**Conclusion:** the small two-target fixture supports one shared supervisor/backend as a viable coordination model. A and B have overlapping in-flight work, while one coordinator serializes acceptance and update decisions. Backend replacement waits for both environments' accepted V1 backlog and cleanup. A busy B deliberately delays the shared update; after deferral A can continue on V1 without killing B.

This is a local extension of published baseline `0ab87e21c92bc00ee43ff41ee6b55a76cfd172a1`. It has not been published or independently reproduced by peers. See the [architectural slice](MULTI-ENVIRONMENT.md) for its scope and tradeoffs.

| Check | Result | Evidence |
|---|---|---|
| Normal Release build, Q1–Q13 | 13 passed, 0 failed | [normal log](evidence/20260922T123800858Z/normal.log), [TRX](evidence/20260922T123800858Z/normal.trx) |
| Wrong-generation fallback mutation | Expected Q2 failure | [mutation log](evidence/20260922T123800858Z/mutation.log) |
| Globally serialize A/B mutation | Expected Q11 failure: B cannot acquire ownership while A is held | [serialization log](evidence/20260922T123800858Z/serialize-targets.log) |
| Ignore B's cleanup mutation | Expected Q11 failure: retirement starts while B still owns cleanup | [retirement log](evidence/20260922T123800858Z/ignore-b-drain.log) |
| Restore normal rules | Q2 and Q11 both pass | [restored log](evidence/20260922T123800858Z/restored.log) |
| Native exit codes | 0 / 1 / 1 / 1 / 0 | [exit codes](evidence/20260922T123800858Z/exit-codes.json) |
| Owned-process cleanup | All 22 recorded supervisor sessions and their workers exited | Per-session cleanup records in [raw sessions](evidence/20260922T123800858Z/sessions/) |
| Source identity | Ten recorded file hashes match the tested local sources | [SHA-256 manifest](evidence/20260922T123800858Z/source-hashes.json) |

Q11 checks target-specific FIFO and exact effect IDs, target, generation, configuration label and output, one worker PID per generation, one surviving supervisor, and cleanup-before-retirement ordering. Q12 demonstrates the shared-delay tradeoff rather than hiding it. Q13 checks that a shared worker crash preserves A's known success and B's unknown outcome separately, without replaying either target's work.

The clearer receipt and stale-contract response are also exercised: acceptance explicitly lasts until supervisor exit, and stale callers receive the current generation/contract. This remains **two synthetic environments and overlapping operation lifetimes**, not real Creatio I/O, parallel CPU throughput, credential isolation, many-target scalability, MCP integration or supervisor crash recovery. Shared update delay remains a product decision. No per-target supervisors, message broker or dynamic policy system were added. The architecture document remains unchanged.

## Historical: original single-target slice

**Conclusion:** the generation-bound, supervisor-owned queue mechanism passed all ten fixture scenarios on Windows. This is evidence for the mechanism under the tested assumptions, not approval of production policy or proof of MCP/Creatio integration.

Run bundle: [20260922T114218336Z](evidence/20260922T114218336Z/).

| Check | Result | Evidence |
|---|---|---|
| Normal Release build and Q1–Q10 | 10 passed, 0 failed | [normal.log](evidence/20260922T114218336Z/normal.log), [TRX](evidence/20260922T114218336Z/normal.trx) |
| Deliberately unsafe V2-to-V1 fallback, Q2 | Expected failure: `NotStarted` became `Queued` | [mutation.log](evidence/20260922T114218336Z/mutation.log), [TRX](evidence/20260922T114218336Z/mutation.trx) |
| Safety rule restored, Q2 | 1 passed | [restored.log](evidence/20260922T114218336Z/restored.log) |
| Actual native test exit codes | Normal 0, mutation 1, restored 0 | [exit-codes.json](evidence/20260922T114218336Z/exit-codes.json) |
| Owned process cleanup | All 16 supervisor sessions and their spawned workers exited | Per-session `cleanup.json` under [sessions](evidence/20260922T114218336Z/sessions/) |
| Platform | Windows x64; SDK 10.0.401; host runtime 10.0.12 | [dotnet-info.txt](evidence/20260922T114218336Z/dotnet-info.txt) |
| Tested sources | Uncommitted local experiment on recorded base, pinned by SHA-256 | [source-hashes.json](evidence/20260922T114218336Z/source-hashes.json), [base](evidence/20260922T114218336Z/base-commit.txt) |

The mutation's independent effect witness also records request `v2` executing under `V1/cfg-1` with lowercase `mixed`. Normal and restored runs contain only `held` and `resumed`, confirming that the negative control causes the actual incompatible execution, not merely a cosmetic status change.

## Architectural findings

1. The old runtime must retain both accepted backlog and cleanup ownership. Merely waiting for a response or success event is insufficient; Q1 exercises both boundaries.
2. A finite cutoff is necessary for this drain-based update policy. The fixture rejects new V1 requests after cutoff and holds compatible V2 requests. Keeping old requests flowing indefinitely would not guarantee drain.
3. A queued receipt needs an explicit failure exit. If replacement is deferred or V2 cannot start, this prototype resolves waiting V2 work as `NotStarted`. It does not rewrite or replay the request on V1.
4. Stable supervisor ownership lets known results survive backend replacement. It does not make the records durable across loss of the supervisor itself.
5. Unknown execution stays unknown after a crash. Q9 makes an effect observable before killing the backend, yet the supervisor correctly retains `Unknown` instead of inferring success or automatically repeating the work.
6. The steady client connection and a service outage are separate concerns. The same pipe survives replacement, but backend startup and fallback can fail; Q4 makes unavailability visible.

These behaviors remain prototype choices for review. The in-memory receipt, exact generation requirement, bounded waiting and `NotStarted` exit policy need product agreement. The test uses a fixture JSON-lines client and local effects, so a real MCP client, Creatio operation and supervisor crash recovery would require separate evidence.

The proposal document was not edited and nothing was published. The prototype is ready for a later independent challenge using the [README and reproduction script](README.md).
