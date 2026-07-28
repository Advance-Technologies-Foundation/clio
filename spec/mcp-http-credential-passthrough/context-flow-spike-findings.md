# Spike findings — Per-request context flow (Story 1, RISK #1 / OQ-04)

**Feature**: mcp-http-credential-passthrough · **Jira**: ENG-93208 · **Story**: `story-mcp-http-credential-passthrough-1.md`
**Scope**: prove the singleton `ICredentialContextAccessor`-over-`IHttpContextAccessor` seam and the FR-06 async-flow prerequisite. INVESTIGATION spike — no production credential code shipped.

**SDK under test**: `ModelContextProtocol.AspNetCore` **1.4.0** + `ModelContextProtocol.Core` **1.4.0** (net10.0), decompiled with `ilspycmd 10.1.0.8386`.
**Host grounding**: `clio/Command/McpServer/McpHttpServerCommand.cs` — `.WithHttpTransport()` with **no options** (line 68); `ValidateOnBuild=true`+`ValidateScopes=true` (lines 57-60); `app.MapMcp(options.Path)` (line 81).

---

## Verdict (headline)

- **Seam GO / NO-GO: ✅ GO.** A singleton `ICredentialContextAccessor` backed by the singleton, `AsyncLocal`-backed `IHttpContextAccessor` reads back a per-request value set by middleware at MCP tool-handler time, under `.WithHttpTransport()`'s defaults and the host's `ValidateOnBuild`+`ValidateScopes`. Stories 4/7/10 build the real accessor on this seam. No fallback needed.
- **FR-06 async-flow verdict: ✅ AsyncLocal-on-singleton is OK** (no per-invocation capture object required) — **with the ADR's existing placement constraint**: the capture `AsyncLocal` must be set *inside* the tool-execution boundary (`BaseTool.InternalExecute`'s `try/finally`), NOT in middleware. Each concurrent tool call runs under its own `ExecutionContext.Run`, so AsyncLocal writes are copy-on-write isolated per invocation.
- **Evidence strength**: Q1 and Q3 are **source-decisive** (official XML docs + full decompiled dispatch chain + a .NET language guarantee). Q2/Q1/Q3 additionally **runtime-verified** by a throwaway probe booting the real SDK host on loopback (see "Runtime probe").

---

## Q1 — Does the SDK invoke tool handlers on the originating HTTP request's `ExecutionContext` under clio's `.WithHttpTransport()` (no options)?

**Answer: YES. Runtime-verified + source-decisive.**

Transport-default facts (decompiled `HttpServerTransportOptions`, `ModelContextProtocol.AspNetCore.decompiled.cs`):

| Option | Default | Evidence |
|---|---|---|
| `EnableLegacySse` | **false** | line 842 — initialized from an `AppContext` switch that is unset ⇒ false; XML `<value>` "The default is `false`" (line 816). |
| `PerSessionExecutionContext` | **false** | line 893 — auto-property, no initializer ⇒ default false; XML `<value>` "The default is `false`" (line 883). |
| `Stateless` | **false** | line 809 — auto-property, no initializer; XML "The default is `false`" (line 800). |

`.WithHttpTransport()` with no `configureOptions` (host line 68) leaves all three at default, so clio runs **stateful Streamable HTTP with per-request ExecutionContext flow ON**.

The authoritative doc names the seam directly — `PerSessionExecutionContext` XML remarks (lines 885-891):
> If `false`, handlers like tools get called with the `ExecutionContext` belonging to the corresponding HTTP request, which can change throughout the MCP session. If `true`, handlers will get called with the same `ExecutionContext` used to call `ConfigureSessionOptions`… Enabling a per-session `ExecutionContext`… **prevents you from using IHttpContextAccessor in handlers.**

⇒ At clio's default (`false`), `IHttpContextAccessor` **is** usable in handlers, by explicit design contract.

**Mechanism chain (decompiled, end-to-end):**
1. `ModelContextProtocol.AspNetCore.decompiled.cs:1896-1898` / `1945-1947` — the AspNetCore layer constructs the per-request transport and sets
   `val.set_FlowExecutionContextFromRequests(!HttpServerTransportOptions.PerSessionExecutionContext)`. With the default `false`, `FlowExecutionContextFromRequests = true`.
2. `ModelContextProtocol.Core.decompiled.cs:33707-33709` — inside the per-request POST handler, when `FlowExecutionContextFromRequests` is true the transport captures the request's context on the request thread:
   ```csharp
   if (parentTransport.FlowExecutionContextFromRequests)
       message.Context.ExecutionContext = ExecutionContext.Capture();
   ```
   `ExecutionContext.Capture()` runs synchronously on the POST request's thread, where `IHttpContextAccessor`'s `AsyncLocal<HttpContext>` is populated — so the captured snapshot carries the live `HttpContext`.
3. `ModelContextProtocol.Core.decompiled.cs:22586-22594` — the server message loop runs each message's handler on that captured snapshot:
   ```csharp
   if (message.Context?.ExecutionContext == null) { ProcessMessageAsync(); continue; }
   ExecutionContext.Run(message.Context.ExecutionContext, delegate { _ = ProcessMessageAsync(); }, null);
   ```
   The tool handler therefore executes with the originating request's `HttpContext` restored ⇒ `IHttpContextAccessor.HttpContext` is non-null at handler time.

Streamable HTTP also holds the POST open until the handler responds (`HandlePostRequestAsync`, AspNetCore lines 1692-1700: `session.Transport.HandlePostRequestAsync(message, context.Response.Body, …)`), so request and response share one `HttpContext` — no POST/SSE channel split that would strand the context.

---

## Q2 — Does a singleton `ICredentialContextAccessor` over `IHttpContextAccessor` return the middleware-set value at tool-invocation time, under `ValidateOnBuild`+`ValidateScopes`, without throwing at build?

**Answer: YES. Runtime-verified.**

- `AddHttpContextAccessor()` registers `IHttpContextAccessor` → `HttpContextAccessor` as **singleton**, backed by an `AsyncLocal<HttpContextHolder>` (`_httpContextCurrent`). A **singleton** wrapper reading a singleton, `AsyncLocal`-backed dependency is a singleton→singleton edge, which **cannot** trip `ValidateScopes` (that rule only fires when a singleton captures a *scoped* service). So `ValidateOnBuild=true`+`ValidateScopes=true` build the graph cleanly.
- The middleware runs on the same `HttpContext` the SDK endpoint later captures (Q1), so a value stamped into `HttpContext.Items` (or an `AsyncLocal` set in middleware) survives into the captured `ExecutionContext` and is readable through the singleton accessor at handler time.
- Runtime probe confirms the read-back value is **non-null and correct** at tool-handler time (see below). No `ValidateOnBuild` exception at startup.

**ADR alignment**: matches the corrected OQ-04 rationale — register `ICredentialContextAccessor` **singleton** to match the singleton/`AsyncLocal`-backed `IHttpContextAccessor`; transient would also work but singleton is the least-surprising choice. The resolver (`IToolCommandResolver`) is transient (assembly-scan), which is irrelevant to the accessor's lifetime.

---

## Q3 — Do two concurrent tool calls run on independent async flows (an `AsyncLocal<T>` set in invocation A is not seen by B)?

**Answer: YES, independent flows. Runtime-verified + source-decisive.**

Source basis:
- Each POST captures **its own** `ExecutionContext` snapshot (Core line 33709) and the server loop dispatches each message under **its own** `ExecutionContext.Run` (Core line 22591). Note the loop fires handlers *without awaiting* — `_ = ProcessMessageAsync()` (line 22593) — so multiple in-flight calls genuinely overlap.
- `ExecutionContext.Run` establishes a copy-on-write async-flow scope: an `AsyncLocal<T>` **written inside** the delegate mutates only that flow's copy and is invisible to the captured snapshot and to sibling flows. This is a .NET runtime guarantee, independent of the MCP SDK.

⇒ FR-06 capture isolation via `AsyncLocal` on the singleton `ConsoleLogger` / `DbOperationLogContextAccessor` is **sound** — provided (per ADR FR-05/06) the `AsyncLocal` scope is opened **inside** the tool-execution boundary (`BaseTool.InternalExecute` `try/finally`), not in middleware. A value merely *inherited* from middleware is shared-by-copy (fine for reading credentials); a value *written* per invocation is isolated (fine for per-call log/db capture).

Runtime probe confirms: two overlapping `tools/call` invocations (forced to be simultaneously in-handler via a shared barrier) each observe **only their own** invocation-scoped `AsyncLocal` value; neither sees the other's. No per-invocation capture object from a child container is required.

> **Scope note (probe vs source).** The runtime probe used **two separate MCP sessions** (one `McpClient` per marker). The **same-session** concurrent-call path — two `tools/call` interleaved on one session's message loop (`_ = ProcessMessageAsync()`, Core line 22593, fires without awaiting) — was **not** exercised by the probe; its isolation rests on the same per-message `ExecutionContext.Capture()` at Core line 33709 (each message captures independently regardless of session), i.e. **source-reasoned** for the same-session case. The verdict holds for both, but the same-session case is not runtime-verified here — Story 15's concurrency-isolation e2e (AC-05/06) should exercise it explicitly.

---

## Q4 — Fallback seam (only if Q1/Q2 had failed)

**Not triggered** — Q1/Q2 passed. Recorded for completeness in case a future SDK bump flips a default:

If the request `ExecutionContext` ever stops flowing to handlers (e.g. `PerSessionExecutionContext` defaults change, or clio enables it), the fallback is the SDK's **per-request DI scope**. In stateful Streamable HTTP the request `HttpContext` is available via `HttpContext.RequestServices` (a request-scoped `IServiceProvider`) inside the endpoint; the credential context would be attached to `HttpContext.Items` / a request-scoped service and read through `IHttpContextAccessor` at the endpoint boundary rather than deep in the handler. Downstream steps that would re-target it: **step 4** (header→context middleware attach point), **step 7** (`IToolCommandResolver` consumption of `ICredentialContextAccessor.Current`), **step 10** (FR-19 mode-gated arg rejection reads the mode flag from the same seam), and **FR-19 enforcement** generally. The step-15e assertion (below) is the tripwire that would catch such a drift before it reached production.

---

## step-15e transport-default assertion target

Assert against the resolved options, and (recommended) also pin them explicitly.

- **Assertion (minimum, unit test)**: resolve `IOptions<HttpServerTransportOptions>` from the built host and assert:
  - `EnableLegacySse == false`
  - `PerSessionExecutionContext == false`
  - `Stateless == false`
- **Recommended stronger pin**: change clio's call to
  ```csharp
  .WithHttpTransport(o => {
      o.EnableLegacySse = false;
      o.PerSessionExecutionContext = false;
      // o.Stateless left default false — stateful required for the credential seam
  });
  ```
  so an SDK default change cannot silently flip the seam. The assertion then guards the explicit pin. **Recommendation: apply the explicit pin in the FR-04 implementation story and back it with the `IOptions` assertion** — belt and braces, since the whole feature hinges on these two defaults.

> **Delta vs ADR step-15e (flag for orchestrator).** The ADR's step-15e currently names only `EnableLegacySse` / `PerSessionExecutionContext`. This spike **adds `Stateless == false`** to the assertion set: stateful mode is a hard requirement for the credential seam (the SDK's per-session/Streamable-HTTP request context flow depends on it), so a silent flip to `Stateless=true` would also break the seam and must be guarded. Update step-15e's spec to include `Stateless` so the extra assertion does not later read as unexplained drift.

---

## Runtime probe

A throwaway standalone probe (net10.0 console, referencing the same `ModelContextProtocol.AspNetCore` 1.4.0 package) booted the real SDK host on loopback with `ValidateOnBuild=true`+`ValidateScopes=true`, `AddHttpContextAccessor()`, a singleton accessor over `IHttpContextAccessor`, a marker middleware stamping a per-request value from an `X-Marker` header into `HttpContext.Items`, and a single tool that (a) reads the marker back via the singleton accessor and (b) sets an invocation-scoped `AsyncLocal` then, after a shared barrier forcing overlap with the sibling call, reports whether it observes the other invocation's value. The SDK's own `HttpClientTransport` + `McpClient` drove the initialize→session→`tools/call` handshake (two calls fired without awaiting the first, with distinct `X-Marker` values).

**Probe location (NOT committed to clio)**: `<scratchpad>/context-flow-probe/` — throwaway, outside the repo tree.

**Probe result — all pass** (`dotnet run -c Release`, SDK host booted on `http://127.0.0.1:<ephemeral>/mcp`):
```json
{
  "Q2_buildValidated": "no ValidateOnBuild exception at host build/start",
  "Q1Q2_A_markerReadBack": "A",
  "Q1Q2_B_markerReadBack": "B",
  "Q1Q2_pass": true,
  "Q3_A_asyncLocalAfterBarrier": "A",
  "Q3_B_asyncLocalAfterBarrier": "B",
  "Q3_bothOverlapped": true,
  "Q3_independentFlows": true
}
```
Interpretation:
- `Q2_buildValidated` — the singleton-accessor graph built and started with `ValidateOnBuild=true`+`ValidateScopes=true`; no exception. (Q2 build-safety confirmed.)
- `Q1Q2_pass=true` — each tool handler read back **its own** request's middleware-set marker (`A`→`A`, `B`→`B`) via the singleton accessor over `IHttpContextAccessor`, and `HttpContext` was non-null at handler time. (Q1 handler-on-request-context + Q2 read-back confirmed.)
- `Q3_bothOverlapped=true` — a `Barrier(2)` released only because **both** invocations were inside the handler simultaneously (genuine concurrency, not serialized). `Q3_independentFlows=true` — after both wrote their invocation-scoped `AsyncLocal` and crossed the barrier, each still read **its own** value (`A`/`B`), never the sibling's. (Q3 independent-async-flow isolation confirmed.)

---

## DoD / kept-scaffold statement

- **No production credential-passthrough code was written or kept in the clio repo from this spike.** The only clio-tree artifacts are this findings note and the Story-1 / sprint-status updates. The runtime probe lives entirely under the scratchpad, outside the repo, and is throwaway — so there is **nothing to keep CLIO001/CLIO005-clean or register in `BindingsModule`**. The real singleton `ICredentialContextAccessor` + `AddHttpContextAccessor()` wiring is deferred to stories 4/7/10 (which will own the CLIO/DI compliance).
