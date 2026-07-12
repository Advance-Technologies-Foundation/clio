# ADR: Ring ↔ clio integration transport — MCP-over-stdio baseline

- **Status:** Proposed (transport spike validated; pending Kirill's live click-through). Transport-spike acceptance is distinct from production readiness — see "Open production blockers".
- **Date:** 2026-07-11
- **Deciders:** Kirill (product), [nikonov] Codex (architecture synthesis), claude-clio (implementation + evidence)
- **Scope:** How the clio ring desktop app talks to clio, as the ring becomes clio's primary GUI.

## Context

The ring is intended to become clio's primary graphical interface (environments, deploys, commands, and future GitHub / live-Creatio features). Driving clio by shelling out `clio <verb> --args` per action and scraping stdout is **brittle** (fragile text parsing) and **slow** (process cold-start per call). We want a persistent, typed, request/response channel — Kirill's framing: "start clio once as a process, talk over a pipe, no HTTP, no network."

## Decision

**Use MCP-over-stdio as the baseline transport for command request/response**, reusing clio's existing `clio mcp-server`, with **one persistent clio child per ring session**. The ring is an MCP *client* (official `ModelContextProtocol` C# SDK).

Rationale: clio already ships this exact channel and keeps its command→tool surface in sync with the CLI under CI. We reuse a maintained contract instead of inventing one.

## Evidence (measured, spike `spike/ring-clio-ipc`, clio 8.1.0.77)

- Handshake: server `clio 8.1.0.77`, protocol `2025-11-25`, caps `tools/resources/prompts/logging`.
- Cold start (spawn→initialized) ~680 ms; warm protocol RTT 0.2–3.1 ms.
- **Full catalog: 150 tools (71 destructive, 26 resident)** via `get-tool-contract {"args":{}}`.
- `list-environments` ~8 ms → 23 environments; `describe-environment` via `clio-run` ~130 ms (read-only path proven).
- Child-death → respawn ~0.6 s.

## Discovery contract

`tools/list` is curated (~27) for **LLM** context budgets; the ring is not an LLM and must not treat it as the full catalog. The **complete catalog** is one call: `get-tool-contract {"args":{}}` → index of all tools (name, purpose, `resident`, `destructive`). Per-tool schema: `get-tool-contract {"args":{"tool-names":[name]}}`. Non-resident (long-tail) tools execute through the resident **`clio-run`** dispatcher. Discovery is **capability-probed, not version-inferred** — an empty/old-shape response shows an explicit incompatible-clio state (path + version).

## Tracked gaps (first-class work, not ring workarounds)

1. **Cancellation** — MCP cancel does not abort in-flight clio work today (backend runs sync or is detached to survive disconnect). Add operation IDs + cooperative cancellation on capable commands (clio-side); the ring UI must show honest `cancel-requested` / `not-cancellable` / `detached` states, never fake success.
2. **Result typing** — input is schema-driven (per-tool `inputSchema`), but output is **JSON-in-text**, not `structuredContent`+`outputSchema`. It is a *working read-only command path*, not fully-typed end-to-end. Add versioned result envelopes + output schemas, keeping the text path during migration.
3. **Live events** — no server-initiated subscriptions over MCP today. Live env/deploy/GitHub-PR/Creatio events need a documented MCP notification/subscription extension (preferred) before any new transport.

## Packaging — dual mode

Support both, chosen via handshake/capability policy: **managed sidecar** (a ring-tested clio published self-contained per RID and bundled) and **external clio** (use the installed one). Preserves independent release cadence + a safe fallback. Ring pins a minimum via `serverInfo.version` + capability negotiation and degrades/labels unavailable features.

## Security

Stdio has **no network surface** (no remote-auth concern). Still: validate the child clio's path/version/signature, constrain the inherited environment/secrets, and gate destructive execution (the proof disables it entirely). The HTTP transport (`clio mcp-http`, loopback) is an alternative but adds a network surface needing Host/Origin controls — not the baseline.

## Alternatives rejected

- **Bespoke framed local IPC** — re-expose every command + track every clio change; large permanent maintenance cost; buys little over the ~8 ms MCP warm path.
- **Named pipe / Unix domain socket** — not supported by clio today (only stdio + HTTP); would require a custom `ITransport`. Not free.
- **HTTP transport** — exists (loopback) but adds a network surface + Host/Origin controls for no benefit over stdio in a child-process model.

## Consequences

- **Version is load-bearing:** stale clio (8.1.0.64) lacks the compact `get-tool-contract` index → 0 catalog; current (8.1.0.77) returns 150. Hence version-pin + capability-probe.
- **AOT stays clean:** the reflection-heavy MCP SDK is isolated in a non-AOT `ClioLauncher.Ipc` project so the `PublishAot` app keeps 0 IL2026/IL3050.
- **Lifecycle:** one-server-per-client; keep one long-lived child (don't spawn per call); respawn on child death; bounded (~750 ms) non-blocking shutdown.

## Open production blockers (spike ≠ production)

- **Process ownership is not deterministic yet.** The SDK's `StdioClientTransport` spawns and owns the child and exposes **no process handle**; the spike captures the PID via a best-effort snapshot diff. Bounded shutdown "works" in the spike on that basis, but **PID-snapshot force-termination is NOT production-safe** (race / misidentification risk). Production needs deterministic ownership — spawn the child via our own `Process` and hand its stdio to the client, and/or a Windows **Job Object** / POSIX process group so the child reliably dies with the ring. Until then, do not claim safe force-termination.
- The three tracked gaps above (cancellation, result typing, live events) are also production work, not spike blockers.

## Transport-spike acceptance criteria (met on `b2c7eff`, experiment-gated, installed build untouched)

Persistent child ✓ · versioned handshake ✓ · full 150-tool catalog ✓ · read-only workflow (list-environments + describe-environment) ✓ · child-death respawn ✓ · bounded shutdown *demonstrated* (via PID-snapshot force-terminate — see ownership blocker) ✓ · destructive execution disabled ✓ · self-identifying connection (counts + path + version) ✓ · incompatible-clio panel ✓.

These prove the **transport decision**. They are explicitly **not** a production-readiness sign-off (see Open production blockers).

## Follow-ups / phasing

1. First typed end-to-end workflow: **ADAC toolkit** install/update/delete (`C:\Projects\ToolKit\adac-setup-wizard`) wrapped as clio commands → auto-surfaced as tools.
2. Then providers: GitHub PR/commit status on workspace repos; live Creatio event monitoring (on the events extension).
3. Address the three tracked gaps as clio-side + protocol work.
