# Evidence package — execution lifetime, discussion #1643

One document instead of a thread. Everything here is measured; where something is asserted rather than
measured it says so. Claims that were published and later narrowed are listed with their corrections,
because a reader who only sees the surviving version cannot tell which parts were tested by someone
disagreeing.

**Status at `79cb0026f85f`: 63/63 on macOS, 63/63 on Windows from a clean clone.** Raw output in
`macos-results.json` and `windows-results.json`. Reproduce with the commands in `README.md`; the run
needs no environment, no network and no Creatio.

---

## 1. The problem, restated from measurement

A long-lived MCP session cannot pick up a new clio. Three facts replaced three assumptions:

- **The lock is Windows-only, and it is not on clio.** `dotnet tool update` fails because Windows will
  not let the versioned store directory be *deleted* while an image in it is mapped. Side-by-side
  installation into a separate directory works on every platform, under the lock. Measured on real
  isolated installs, `8.1.0.129 → 8.1.0.131`, macOS 15 arm64 and Windows 11 x64.
- **The deeper obstacle is an architectural assumption, not a file lock.** clio's MCP server is written
  as "one process = one session", stated in `Tools/CompileOperationRegistry.cs`. Operation registries are
  `AddSingleton`, so replacing the process loses them.
- **Replacing the process silently lies about work in progress.** Reproduced against a live stand: start
  `compile-creatio`, get `status: running` and an id, replace the backend, and the same session answers
  `success: true`, `status: "not-found"`, "No compile-creatio operation has been recorded … in the
  current MCP server session" — while the compile is still running on the server. An agent reads that as
  "nothing was started" and starts a second one, which the platform rejects rather than queues.

## 2. What is settled

The eight clauses live in `shared-contract.md`; this is the index, not a replacement.

| # | Clause | Cases |
|---|---|---|
| 1 | A release is identified by a string; compatibility is decided before activation, never mid-operation; an incompatible release is inspected, discarded and never consulted | A1a, F1, F2 |
| 2 | An operation id is opaque, is the unit of truth for *state*, and never entitles anyone to resume or retry | — |
| 3 | Only host-owned portable data crosses back. A runtime-defined value, exception **or** delegate retains its release; the string form does not | O1, G1–G4 |
| 4 | Response completion, outcome publication and ownership release are three distinct events; quiescence follows ownership. **When the owner is a process, liveness replaces disposal** | P5, H1–H5 |
| 5 | Use `TryReserveAdmission`, not `TryEnterSwapWindow`: polling for quiescence under load starves forever. The drain must be bounded and the reservation released when the budget expires | D1, D2, E1, E2 |
| 5b | A degraded scope is repaired or explicitly abandoned, never cleared by the fault going away | E3–E6 |
| 6 | A configuration snapshot needs one thing from this side: a portable identity. Everything else is the settings lane's | J1–J5 |
| 7 | The assembly crossing the boundary references `System.Runtime` and `System.Collections` and nothing else | F3, F4 |
| 8 | A case title is a claim; a case naming a temporal or contention property carries a mutation control, and the mutation must be **observed to fail** | — |

### The three results worth reading even if nothing else is

**Polling for a quiet moment never gets one.** `TryEnterSwapWindow` requires quiescence *before* it
grants anything, so an updater polling it against a busy target is starved for the whole observation on
both platforms (D1). `TryReserveAdmission` closes the scope first and drains second, so the in-flight set
is finite and empties — ~130 ms under the load that starved D1 (D2).

**Without owner liveness, a drain after a lost owner never finishes.** The orphan retains forever, the
scope never reaches quiescence, and the reservation can only expire. Mutation control with liveness
disabled: H3's `afterLiveOwnerFinished` is `false`, permanently. This is a property of the drain-first
design, which is the design being recommended.

**Adding an optional parameter to a contract method is binary incompatible.** Source compatible,
compiler silent, and every release already built against the previous contract dies at its first
operation with `MissingMethodException`. The seam is an overload instead (J5). The same trap applies to
the MCP tool contract and the settings contract.

## 3. What is NOT established

- **A real MCP client surviving a handover with work genuinely in flight.** @vladimir-nikonov's
  `McpClientProbe` is a real client over a real connection and it works — but its swap is gated on a
  completed drain, so the published case never had work in flight at swap time (mutation A on
  [`Alexandr-Kravchuk/mcp-client-mutation-control`](https://github.com/Advance-Technologies-Foundation/clio/tree/Alexandr-Kravchuk/mcp-client-mutation-control),
  `49e622e027dd`). Forcing it produced a client told `Running` forever, which is what the H cases then
  fixed on the ledger side.
- **Package authenticity and trusted acquisition.** Untouched, by anyone, and it is a release gate.
  Nothing in this package bears on it.
- **Which architecture wins.** Not decided, and this package deliberately does not recommend one.
- **Anything in clio itself.** No product code has changed. These are probes.

## 4. Defects in clio 8, found along the way

Independent of the architecture choice, and actionable separately:

1. `McpServerCommand.DrainHostBackgroundWork` waits for the component-registry refresh and the telemetry
   flush, 10 s each. **Heartbeat-detached operations are not drained at all** — the host tidies its own
   housekeeping and abandons work that is changing someone's Creatio.
2. Operation registries are `AddSingleton`, so any process replacement — including `/mcp` Reconnect, not
   just a supervisor — loses them. Already documented at smaller scale in `Relay/StickyWorkerPoll.cs`.
3. Work detached after the response deadline starts with `CancellationToken.None` in the expectation of a
   long-lived process (`McpProgressHeartbeat.cs`). Measured A/B against a live stand: the control created
   the section and entity; the run with a backend replacement 1 s later created nothing in 4.5 minutes,
   while the caller had been told to wait and explicitly not to retry.

`McpHostPresence` already records running MCP hosts with PID and clio version, so detecting a stale
running host and comparing versions does not need building. There is no single-instance guard.

## 5. Corrections, on the record

Eight published claims of mine were narrowed or withdrawn: six caught by @kirillkrylov's review, two by
me.

| Claim as published | Correction |
|---|---|
| Path-loading makes cleanup impossible | Narrowed: it makes *in-place replacement* impossible on Windows |
| Tier 2 converts for free | Withdrawn |
| No single-instance guard → coexistence unless IIS | Narrowed to what was actually measured |
| Retiring the release destroys evidence | Withdrawn: evidence is on disk and outlives the release |
| Tier 1 is attributable | Withdrawn |
| "No need to wait" | Withdrawn; became clause 5's bounded drain |
| F1 shows two releases executing concurrently | Self-caught: a sequential run satisfied every assertion. Fixed by observing `Running` |
| "Deferral puts us back where we started" | Withdrawn (@kirillkrylov): a month-long session is not a month-long operation; draining finite work delivers the update |

Two method failures are also on the record, because both produced confident wrong results:

- **D1's first form** polled before the load existed and won on an empty scope.
- **The first liveness mutation** used `if (true) return`, tripped CS0162, failed the build, and
  `--no-build` reran the previous binary reporting a full pass. A mutation that silently does not run
  looks exactly like one the code survived. Hence the clause 8 amendment.

## 6. Ownership going forward

- **@vladimir-nikonov** — the single MCP client harness, and the settings fixture. No second client.
- **Alexandr** — the lifetime/evidence contract and the seam it exposes. Consumes the harness.
- **@kirillkrylov** — review, and the in-process composition lane.

The joint proof still outstanding: a partner workflow pinned to one release while another arrives with
changed settings, with an outcome-storage failure injected. Every piece it needs now exists except the
settings fixture.
