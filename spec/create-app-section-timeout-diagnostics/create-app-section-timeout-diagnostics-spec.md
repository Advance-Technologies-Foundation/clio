# create-app-section timeout diagnostics — SPEC

> Jira: [ENG-90679](https://creatio.atlassian.net/browse/ENG-90679) — `[Clio MCP] create-app-section times out and never completes`

## Problem

`create-app-section` issues the `ApplicationSection` InsertQuery with
`requestTimeout = Timeout.Infinite` (the `IApplicationClient.ExecutePostRequest`
default). On a busy/broken Creatio instance the call hangs indefinitely; the MCP
client gives up first and surfaces `MCP error -32001: Request timed out`. The
calling agent then:

1. cannot tell whether the request ever reached Creatio,
2. cannot tell whether the section was (or still will be) created server-side,
3. retries blindly — observed 6 retries across 45 minutes with zero progress
   (ENG-90679 stdout.log) while clio kept 6 infinite-timeout inserts in flight.

Session-transcript evidence shows the opposite hazard too: the insert **did**
complete server-side minutes later, so "timed out" ≠ "no side effect". A retry
can therefore double-create or fail with a confusing "already bound" error.

Related: PR #688 (ENG-91274) adds an MCP progress heartbeat that keeps the MCP
client's inactivity timer alive while clio works. That fixes premature client
timeouts but NOT the unbounded server-side wait and NOT the
indistinguishable-failure problem. This feature is complementary and
deliberately overlaps none of #688's mechanics.

## Requirements

R1. The `ApplicationSection` InsertQuery must run under a finite request budget.
    Default 90 s (lowered from 300 s — see ENG-91540 follow-up below),
    overridable via env var `CLIO_CREATE_SECTION_TIMEOUT_SECONDS`
    (values ≤ 0 or non-numeric → default).

R2. On failure, the operation must classify the outcome into exactly one of:
    - `transport` — the request never reached Creatio (DNS/connect/TLS failure).
      Retry is safe.
    - `creatio-timeout` — the request was sent but no response arrived within
      the budget. Side effects unknown; retry is NOT safe until verified.
    - `server-error` — Creatio replied with an error (HTTP error status,
      non-JSON/HTML body, or `success:false` InsertQuery response). Retrying
      the same arguments will likely fail again.

R3. After a `creatio-timeout`, clio must itself verify the side effect with a
    bounded (20 s) `ApplicationSection` readback:
    - section found → continue the normal readback flow and return **success**
      (timeout recovered);
    - section absent → report `section-created: false` plus guidance that the
      insert may still be processing and how to verify before retrying;
    - verification failed → report `section-created: unknown`.

R4. The MCP error envelope of `create-app-section` must carry the
    classification as structured fields (`error-class`, `section-created`,
    `retry-guidance`) in addition to the human-readable `error` message. The
    CLI (`clio create-app-section`) must print the same self-contained message.

R5. Existing success-path behavior, tool contract, and argument surface must
    not change.

## Acceptance criteria

AC1. Insert call observes the finite budget (default and env-var override).
AC2. Connect-class `WebException`/`HttpRequestException` → `error-class=transport`,
     `section-created=false`, retry-safe guidance.
AC3. Timeout-class failure with section visible on verification → tool returns
     `success:true` with full section readback.
AC4. Timeout-class failure with section absent → `error-class=creatio-timeout`,
     `section-created=false`, wait-verify-then-retry guidance.
AC5. Timeout-class failure with verification failure → `section-created=unknown`.
AC6. HTTP protocol error / HTML body / `success:false` → `error-class=server-error`.
AC7. All structured fields appear in the MCP JSON envelope; absent (null) on
     success responses.
AC8. Unit + MCP e2e coverage for the above; docs updated
     (help txt, command md, Commands.md, MCP guidance resources).

## ENG-91540 follow-up — the budget must beat the client's request ceiling

> Jira: [ENG-91540](https://creatio.atlassian.net/browse/ENG-91540) — `MCP error -32001 is still reproducing` (sub-bug of ENG-90679)

### What was still wrong

The ENG-90679 classification logic is correct, but the 300 s default budget made
it unreachable through real MCP clients. The `create-task-section` regression
(GitHub Copilot CLI) kept surfacing the opaque `MCP error -32001: Request timed
out` every ~180 s:

- The MCP **client** enforces a fixed per-request ceiling (~180 s for the Copilot
  CLI) and abandons the call at that point.
- The progress heartbeat (ENG-91274) does **not** rescue this client: it either
  sends no `progressToken` (so clio emits no beats) or keeps a hard ceiling that
  progress notifications do not reset.
- Because the clio insert budget (300 s) was **longer** than that ceiling, clio
  was still blocked inside `ExecutePostRequest` when the client gave up. The
  structured `creatio-timeout` envelope (`error-class` / `section-created` /
  `retry-guidance`) was never produced in time — the agent only ever saw the
  raw transport `-32001` and blind-retried.

### Fix

- Lower the default insert budget to **90 s** and the verification readback to
  **20 s**, so clio's full response (insert budget + readback ≈ 110 s, plus a few
  seconds of preparation reads) returns comfortably **before** the ~180 s client
  ceiling. The agent now receives the actionable `creatio-timeout` envelope
  instead of `-32001`, and the section — which may still materialize
  server-side minutes later — is found by the recommended `list-app-sections`
  check.
- `CLIO_CREATE_SECTION_TIMEOUT_SECONDS` still raises the budget for patient
  clients (those that honor progress and have a generous ceiling) or large
  environments where the insert legitimately needs longer.

### Acceptance criteria (ENG-91540)

AC9.  Default insert budget is 90 s; verification readback is 20 s.
AC10. When the insert exceeds the budget and the section is not yet visible,
      `create-app-section` returns a structured `creatio-timeout` envelope
      (`error-class=creatio-timeout`, `section-created` ∈ {`false`,`unknown`},
      `retry-guidance`) — never a bare transport timeout — within the budget.
AC11. `CLIO_CREATE_SECTION_TIMEOUT_SECONDS` still overrides the 90 s default
      (and AC1's invalid/non-positive fallback rules are unchanged).

### Residual (out of scope here)

The pre-insert preparation reads (`get-app-info`, icon resolution, schema
existence) still run unbounded. On a pathologically slow stand they could
themselves exceed the client ceiling before the insert begins (the separate
`get-app-info` latency concern is tracked under ENG-91316). The ENG-91540 repro
showed fast preparation and a slow insert, so bounding the insert is the
targeted fix.
