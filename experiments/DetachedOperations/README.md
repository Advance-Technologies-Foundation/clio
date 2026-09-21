# Detached operations: explicit ownership across a runtime update

Discussion #1643; baseline `7225ebcaa`. This isolated probe changes no product/Core contracts. It does
not implement durable replay, a lease manager, or retirement policy.

## Why

Measured on clio 8, in one session with no reconnect: `create-app-section` returned
`section-created: in-progress` at its response deadline, the backend process was replaced 1029 ms later,
and 4.5 minutes on neither the section nor its entity schema existed. A parallel measurement had
`compile-status` answer `success: true, status: not-found` — *"No compile-creatio operation has been
recorded for this environment in the current MCP server session"* — while the compile was still running
server-side. Both are the same defect: work that outlives its response has no owner, and the host cannot
tell "nothing started" from "I no longer know".

## Hypothesis

An operation that outlives its initial response can be owned explicitly, so that activating a new runtime
mid-flight changes nothing about its completion, its external effect, or the truthfulness of its status —
and so that losing the process reports uncertainty rather than absence.

## Shape

Four tiny projects, no added packages. `Contract` is the only assembly crossing the load boundary and
carries data only — `OperationRecord` has no delegates and no runtime objects, so it can be written to
evidence and survive its runtime. `RuntimeV1`/`RuntimeV2` are one workflow source compiled as two
releases into `artifacts/detached/<version>/`. `Host` holds the generic ledger and the measurement runner.

The ledger is deliberately generic: it stores an opaque `Code` supplied by the runtime and never reads it.
Interpreting what an operation *means* stays with the runtime that owns it, so Core does not become a
catalog of business operations.

`OperationRecord` carries a `Target` (environment/tenant key) so quiescence can be asked per target rather
than per process — raised by @vladimir-nikonov, on the grounds that a session driving several environments
would almost never be globally idle and a global-only predicate would be correct but unusable.

## Cases

| id | case |
|---|---|
| A1a | the original operation still answers after a newer runtime is activated |
| A1b | quiescence is scoped per target while the operation runs |
| A2a | the terminal state is truthful and the external effect happened exactly once |
| A2b | the target is quiescent once the operation terminates |
| A3a | a failing operation reports `Failed`, not silence |
| A3b | a cancelled operation reports `Cancelled`, not silence |
| A3c | neither the failure nor the cancellation added an external effect |
| A4a | an operation lost with its process reports `Unknown`, with its target preserved |
| A4b | an identifier that was never issued is still reported as `NotFound` |
| A4c | the recovered host surfaces the lost operation rather than reporting a clean slate |
| A5a | **control** — reading quiescence does not hold it: work starts in the check-then-act gap |
| A5b | a swap window is refused while the target has work in flight |
| A5c | holding a swap window closes the gap for its scope and leaves other targets alone |
| A5d | the scope accepts work again once the window is released |
| R1 | the V1 release becomes collectible once no lease retains it |
| C1 | **control** — a host with no evidence answers `NotFound` for the very same lost operation |
| C2 | **control** — the release is retained while its operation runs, and only then collectible |

A5 was added after @vladimir-nikonov pointed out, while scoping the composition probe, that
`IsQuiescent(target)` is only an observation: reading it and then swapping is check-then-act, and an
operation can begin in the gap. A5a measures that the gap is real; `TryEnterSwapWindow(target)` takes
quiescence and holds it in one step, and A5c measures that it closes the gap for its scope without
freezing unrelated targets. While a window is held, `Begin` for that scope is **refused rather than
queued** — a refusal is observable, a stall is not.

C1, C2 and A5a exist because a suite that passes on its first run is worth nothing until it has been shown to
fail when the property is absent. C1 reproduces today's wrong answer inside the same harness: the same
identifier, queried by a ledger with no evidence, comes back `NotFound`. C2 shows retention is real rather
than incidental — the release is *not* collectible while its work is in flight.

A4 kills a real child process with `Process.Kill(entireProcessTree: true)` while work is in flight, so
nothing gets a chance to write a terminal state. Evidence is flushed with `FileStream.Flush(true)` for
that reason.

## Reproduce

From the repository root:

```bash
dotnet build experiments/DetachedOperations/RuntimeV1/DetachedRuntime.V1.csproj -c Release
dotnet build experiments/DetachedOperations/RuntimeV2/DetachedRuntime.V2.csproj -c Release
dotnet build experiments/DetachedOperations/Host/Host.csproj -c Release
dotnet run --project experiments/DetachedOperations/Host/Host.csproj -c Release --no-build -- \
  run artifacts/detached/10.0.0.0 artifacts/detached/10.1.0.0
```

Exit code 1 means a case failed; JSON on stdout carries the raw observations. The runner uses a unique
temporary directory and writes nothing outside it.

## macOS observations, 2026-09-21

macOS 27.0.0 (arm64), .NET 10.0.12. **17/17 passed, exit 0.**

- The operation started on `10.0.0.0` kept answering `Running` and stayed owned by `10.0.0.0` after
  `10.1.0.0` was activated mid-flight.
- One external effect line, written by V1, after the update: `v1-executed-by-10.0.0.0`.
- Failure and cancellation both produced terminal states and no effect line.
- The killed child's operation came back `Unknown` / `history-unavailable` with `Target` intact; a
  never-issued identifier still came back `NotFound`; the same identifier without evidence came back
  `NotFound`.

## Interpretation and limits

Evidence is not replay. Two lines are written per operation and nothing about the work itself, so recovery
can say an outcome is unavailable but can never resume or retry. Persisting a record does not make a retry
safe and this probe does not claim it does.

Retention in C2 comes from both the ledger's owner reference and the detached work's own closure; the
probe shows the release outlives the update, not which of the two references achieves it.

Not covered: multi-threaded contention on the swap window itself (A5 exercises the sequence, not a
concurrent storm of callers racing for the same scope), native libraries or resources, real
Creatio/DI dependencies, GC timing guarantees, multi-process coordination, server-side reconciliation
(asking Creatio what is actually running), and any integration with `UpdatingComposition` — this probe
loads releases through its own collectible context rather than through Core, deliberately, so it cannot
conflict with the retirement branch.

## Windows observations, 2026-09-21

Windows 10.0.26200, .NET 10.0.11 (a different patch from the macOS run, which used 10.0.12).
**17/17 passed, exit 0**, from a clean clone of this branch at `e3138962cec2` — nothing modified.
Raw output in `windows-results.json`.

Every case behaves identically to macOS, including the two controls. This matters most for A4 and R1,
which are the ones with platform-sensitive mechanics: the external kill still leaves `Unknown` rather
than `NotFound`, and the release still becomes collectible only after its operation ends.

An independent reproduction by @kirillkrylov is still welcome — this one is mine, on my own probe.

## Smallest next question

`IsQuiescent(target)` is answered from this process's own ledger. After a process loss it reports
`Unknown` operations but cannot say whether their work is still running somewhere — for Creatio-side work
the authoritative answer lives on the server. So: which operation classes can be reconciled against
Creatio, and which can only ever be reported as uncertain? That boundary decides whether a staged host
update can ever be triggered automatically after a crash, or only after a clean shutdown.
