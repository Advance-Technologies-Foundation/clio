# ENG-95262 — reproduction lab (spike, not shipped)

Throwaway harnesses that produced the measurements in
[ENG-95262](https://creatio.atlassian.net/browse/ENG-95262). They live on this branch only: nothing here is
built by the solution, referenced by clio, or intended to merge. The shipping artifact is a C# regression
test in `clio.mcp.e2e`; this is the lab that proves what that test has to assert.

Everything was measured on macOS. Windows spawn cost and Job Object containment remain unverified.

## What is here

| file | what it is |
|---|---|
| `stub_creatio.py` | deterministic Creatio stub: login/SelectQuery with request counters and `stall-headers` / `stall-body` / `delay` modes, plus `/control`, `/counters`, `/reset` |
| `mcp_wedge_harness.py` | minimal MCP stdio client that reproduces the wedge and asserts on **backend request counters**, not timings |
| `mcp_proxy.py` | the proxy execution model: parent routes, short-lived child `clio mcp-server` per call, sticky child per `(environment, family)` for tracked long operations |
| `session-lock-probe.py` | raw-HTTP probe that refuted the platform session-lock hypothesis (shared session vs separate logins, `ForceUseSession` matrix) |
| `relay/` | the relay spike on MCP C# SDK 1.4.1: `parent/` (C#), `spike_child.py`, `spike_client.py` |

## Reproduce the wedge (no Creatio stand needed)

```bash
python3 stub_creatio.py 8099 &
clio reg-web-app stubwedge -u http://127.0.0.1:8099 -l Supervisor -p stub --checkLogin false
curl -sX POST "http://127.0.0.1:8099/control?stall=true"
python3 mcp_wedge_harness.py --env stubwedge --deadline 12 --tool list-pages
```

On master, with a **locked** tool (`list-pages`): A 12 s / 1 backend request, B 12 s / **0**, C 12 s / **0**,
and D — with the backend already healthy again — 12 s / **0**. One backend request for four calls, and the
environment never recovers. Run it with `--tool list-packages` and it does *not* reproduce: that tool takes no
tenant monitor, which is how the 44-of-123 lock split was found.

Through the proxy (`--server "python3 mcp_proxy.py --budget 12"`): A, B and C each issue their own request and
are killed at the budget, D succeeds in 0.8 s.

## Long operations through the proxy

```bash
curl -sX POST "http://127.0.0.1:8099/control?delay=25"
CLIO_MCP_RESPONSE_DEADLINE_SECONDS=8 python3 mcp_proxy.py --budget 12   # driven by the harness
```

`clio-run compile-creatio` returns the in-progress envelope at 8 s; `compile-status` polls are routed to the
same sticky child and answer `running` in ~0.01 s from its in-process registry; a fresh child answers
`not-found`. On a terminal status the parent reaps the child.

## Relay spike (MCP C# SDK 1.4.1)

```bash
cd relay/parent && dotnet build -c Release && cd ..
python3 spike_client.py .
```

Result: sampling relay **PASS** (the child's `sampling/createMessage` reaches the real client and the answer
comes back), `_meta.clioStageEvent` + `progressToken` fidelity **PASS**, notification ordering **FAIL**
(`[5, 4, 2, 3, 0, 1]` for an emitted `0..5`). A single-consumer FIFO in the parent does not fix the ordering —
the reordering happens at or before the SDK's notification-handler dispatch, which is why the implementation
must own the child's transport read loop.

The C# project deliberately opts out of the repository's central package management and analyzers so it
builds standalone and cannot affect a repo-wide build.
