# Process executor stream-drain timeout - ADR

Status: Accepted for implementation

## Context

`ProcessExecutor.ExecuteInternalAsync` starts stdout and stderr readers, waits for the immediate process with a linked timeout token, then awaits both readers without that token. On Windows and POSIX systems, descendants may retain inherited pipe handles after the immediate process exits, so the readers do not observe EOF and the operation-wide deadline is lost.

## Decision

Use the same linked operation token for standard-input writes, process waiting, resource monitoring, and both redirected stream readers. When any bounded phase observes cancellation, classify it with the existing timeout/caller-cancellation/resource-limit rules, close Clio's redirected stream ends, attempt best-effort process-tree termination from the retained root process, and return the partial output already captured.

Read output incrementally so cancellation preserves unterminated fragments. Maintain a small stateful real-time parser that treats CR, LF, and CRLF as line boundaries without changing the captured payload.

Expose `DescendantTerminationUncertain` on the execution result and include the limitation in the Git timeout diagnostic. This makes the portable guarantee explicit: Clio disconnects its readers and returns within the operation budget, while already reparented descendants are outside the termination guarantee.

The implementation will not add a second drain timeout. One linked token preserves a single wall-clock budget and prevents the process-wait and drain phases from each consuming a full timeout.

## Consequences

- A descendant-held pipe can no longer block captured execution beyond the configured deadline.
- Partial output remains available because the readers append data as it arrives and cancellation is handled only after both reader tasks have settled.
- Output and directory resource-limit detection cancels the same operation token, so neither redirected reader can remain blocked on a descendant-held handle.
- Real-time progress callbacks retain the former CR, LF, and CRLF behavior.
- Closing the redirected readers releases Clio from the inherited-pipe lifetime. Clio does not assume ownership of an arbitrary descendant after the immediate process has already exited.
- Process-tree termination after the root exits is platform-dependent: Windows can still traverse the retained root in the tested case, while Unix may already have reparented descendants. Correctness therefore depends on disconnecting Clio's pipe ends, not on OS-specific descendant discovery.
- Callers that omit `Timeout` preserve the existing wait-until-EOF behavior.

## Alternatives rejected

- Await stream drain without cancellation: this is the defect.
- Add a fresh timeout only after process exit: this can double the caller's operation budget.
- Fix only `KnowledgeGitTransport`: other `ProcessExecutor` callers would retain the same unbounded post-exit behavior.
- Add Windows Job Objects plus Unix process-group launch: rejected for this fix because it adds separate native lifecycle implementations and changes descendant ownership semantics. The public result instead exposes the portable bounded-capture guarantee and its limit.
