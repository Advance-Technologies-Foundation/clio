# Inventory 3 — threat model for the parent→child credential channel

**Feature:** mcp-worker-execution-boundary · **Jira:** ENG-95262 · **Stage:** 0 (design artifact)
**Measured against:** `origin/master` @ `3fc50bf99`, 2026-08-17 · **Re-read against this branch** 2026-08-18
for the reviewer findings folded into T-4, T-6, T-9, T-10, §5 and §6
**Governs:** Stage 5 (HTTP credential channel + per-client sticky isolation); binding on Stages 2, 3, 6
and 7. **Stage 5 was deferred on 2026-08-18** (ADR §5, OQ on `mcp-http`'s fate) — the requirements it owned
(R-5, R-6, and the HTTP half of R-1) are therefore *unreachable* rather than unmet, because the stdio-only
gate stops any HTTP call reaching a worker. §6 states requirement-by-stage applicability so this is a
recorded position rather than an inference.

Today credentials never leave the process that received them. Moving execution into a child process creates
a channel that does not exist yet: the parent holds the authentication material and the child needs it to
talk to Creatio. **That channel is new attack surface, and it is the reason Stage 5 is a separate stage
rather than a detail of Stage 4.**

Scope: how material reaches a worker, what a worker may do with it, and which worker a caller may reach.

**Scope widened on 2026-08-18, deliberately.** T-9 (spawn exhaustion) and T-10 (executable substitution) are
availability and integrity threats rather than confidentiality ones, so they sit outside the original
"credential channel" framing. They are here because they are threats *created by the same act* — moving
execution into a spawned process — and this is the only document that models that act. Splitting them into a
second file would mean two threat models for one boundary, which is how a threat stops being anybody's.

Out of scope: how the material reaches the parent in the first place — that is
`adr-mcp-http-standard-authorization.md` (OAuth 2.1 resource server) and
`adr-mcp-http-credential-passthrough.md` (`X-Integration-Credentials`), both unchanged by this work.

## 1. What the parent holds

| Transport | Material | Where it lives today |
|---|---|---|
| `mcp-http`, passthrough | `CredentialMaterial` — one effective `CredentialKind` after `AccessToken → LoginPassword → Cookie` precedence (`CredentialContext.cs:22-32`) | per-request, in the parent's `HttpContext` |
| `mcp-http`, OAuth 2.1 | validated bearer principal | per-request, in the parent's `HttpContext` |
| `stdio` | environment registration in `appsettings.json` | on disk, readable by the child directly |

The stdio case is materially easier: the child can read the same `appsettings.json` the parent reads, so no
secret needs to cross the channel at all — only the environment **name**. **This is why Stage 6's first
cohort is stdio-only.** The HTTP cases are the ones that require a channel, and they are deferred to Stage 5
precisely so the first worker cohort ships without inventing one.

## 2. Assets

| Asset | Compromise means |
|---|---|
| A-1 Bearer access token | full impersonation of the caller against the target Creatio |
| A-2 Login/password | durable credential theft — survives token expiry |
| A-3 Auth cookie | session hijack for its lifetime |
| A-4 Caller↔target binding | one caller's request executed against another caller's environment |
| A-5 Caller↔worker binding | one caller's *results* delivered to another caller (a sticky worker is a live authenticated session) |

## 3. Threats

### T-1 — Credential material on the command line

**Attack:** the parent passes a token or password as a child process argument.
**Why it matters:** process arguments are world-readable on Linux (`/proc/<pid>/cmdline`), visible in
`ps`/Task Manager to any local user, and routinely captured by crash handlers and monitoring agents. A local
unprivileged user reads A-1/A-2/A-3 without any exploit.
**Requirement:** **secret material must never appear in a child's command line or in its environment block —
no exceptions, no "temporarily for debugging".** The channel is a pipe or other inherited handle, written
after spawn and closed once read. The environment block is excluded for the same reason the command line is,
not as a less-preferred option: environment blocks are inherited by grandchildren and appear in some crash
dumps, while a pipe is read once and closed.
**Verification:** an E2E test that spawns a worker with a real credential and asserts the credential string
does not appear in the child's command line, and — where the platform allows reading it — its environment
block.

### T-2 — Credential smuggling through tool arguments

**Attack:** a caller on the `mcp-http` passthrough edge supplies `uri` / `login` / `password` /
`client-id` / `client-secret` / `environment` as *tool arguments*, redirecting execution to a target of
their choice under the ambient credentials.
**Status:** already rejected in the parent — `ToolCommandResolver.cs:104-116` refuses explicit
credential/environment arguments on that edge.
**What this work must not break:** the router runs **before** execution, so it must not become a path that
reads those arguments to decide a route and thereby resurrects the vector. **The routing key is derived from
the resolved tenant identity, never from raw tool arguments** (rule 3).
**Verification:** the existing rejection tests must still pass with routing enabled, plus one asserting the
router itself rejects rather than routes.

### T-3 — Credential downgrade in the child (fail-open)

**Attack:** none needed — this is a latent defect class, and it has already happened once in this codebase.
When the per-environment MCP child container was introduced, it built Creatio connections inline in
`RegisterActiveEnvironmentServices` instead of going through `ApplicationClientFactory`. An authentication
mode added only to the factory was therefore silently dropped, and a **bearer-authenticated caller was
executed as `Supervisor`** — a privilege escalation with no error, no log line, and a successful response.
**Why the worker model makes it likelier:** the worker is a second construction site for the same client.
Any auth mode added to one and not the other produces exactly this failure, and the symptom is *success*.
**Requirements:**
- The worker builds its client through the **same** `ApplicationClientFactory` path as the parent — one
  construction site, not two.
- **Fail closed:** a worker that receives material it cannot apply in the intended mode must refuse the
  call. It must never fall back to registry credentials, to an ambient session, or to a default identity.
- Bearer-first precedence is preserved end to end.
**Verification (the discriminator):** a **fail-first identity assertion** — execute a call as a
non-Supervisor bearer principal and assert the identity observed *at the Creatio end* is that principal.
A test that only asserts "the call succeeded" passes while authenticated as the wrong user, which is
precisely how the original defect survived. `get-identity-assertion` exists for this.

### T-4 — Sticky worker reachable by the wrong caller

**Attack:** caller B's tool call is routed to a sticky worker holding caller A's authenticated session, and
executes as A. Compromises A-4 and A-5 together.
**Why the naive key fails:** a sticky worker is currently the natural place to key by *environment*, and on
`mcp-http` two different authenticated callers routinely target the same environment. Environment-only
scoping is therefore a cross-client boundary violation, not a cache-efficiency trade-off. Status tools are
already credential-scoped today, so environment-only scoping would also be a *regression* against shipped
behaviour.
**Requirement:** a sticky worker's scope key is
**`authenticated session/principal` + `normalised target` + `credential fingerprint`** — all three. The
fingerprint is a hash of the effective material (never the material itself), following
`BuildPassthroughCacheKey` (`clio/Command/McpServer/Tools/ToolCommandResolver.cs:316-327`), which already
uses the **full** SHA-256 rather than a truncation precisely because "same url, different token" is the norm
on this feature and a truncation collision would be a credential crossover.

**The fingerprint is an unsalted digest, and that has two consequences worth naming.** `HashSecretMaterial`
is a bare `SHA256.HashData` over the concatenated material — no salt, no key
(`ToolCommandResolver.cs:333-336`). Anyone who can read a fingerprint and guess a candidate credential can
confirm the guess **offline**: hash the candidate in the same shape, compare, done — no request to Creatio,
no lockout, no log line, no rate limit. For a high-entropy bearer token that is not a practical attack; for
a login/password pair it is an ordinary offline dictionary attack, and the login half is usually known
already. So two requirements, one of which costs nothing today:

- **The fingerprint is classified under R-7, explicitly.** It is treated as the secrets it derives from:
  never logged, never written into an error envelope, never carried in a progress notification, never
  captured in a test snapshot. It is a key, not a diagnostic. Holding this is easy right now — the digest
  exists only inside in-memory cache keys, and the on-disk worker registry carries no credential-derived
  field at all (`clio/Common/McpWorker/StaleWorkerRegistry.cs:37-44`). Stage 7's sticky scope keys are what
  would spread it.
- **Before any fingerprint is persisted or crosses a process boundary, the digest must be keyed** — an HMAC
  under a per-parent-process random key, or an equivalent per-process random salt — so that a leaked
  fingerprint cannot be tested against a candidate credential. A per-process key is sufficient and costs
  nothing, because every consumer of the fingerprint is scoped to one parent's lifetime and nothing needs it
  stable across restarts. *Unproven:* whether any Stage 7 design will need cross-restart stability. If one
  does, that is the point at which this stops being a free change and becomes a design decision.

**Requirement:** worker lookup **fails closed** — an unmatched key spawns a new worker; it never falls back
to "closest match" or "any worker for this environment".
**Verification:** two concurrent callers, same environment, different principals → two distinct workers,
each observing its own identity at the Creatio end.

### T-5 — Target normalisation collision

**Attack:** the normalisation that makes a registered *name* and an explicit *URI* one key (rule 10) is the
same normalisation that decides whether two requests may share a worker. Normalising too aggressively (case,
trailing slash, default port, host aliases, IP-vs-hostname) merges targets that are not the same, and the
merged worker carries one caller's credentials to another caller's target.
**Requirement:** normalisation is **conservative and explicit** — the algorithm below, component by
component. Anything the algorithm does not explicitly fold is a different target. When in doubt, spawn
another worker; the cost is 0.7 s and the alternative is a credential crossover.

**The algorithm (binding; TC-U-503's equivalence table is generated from this list, not from ad-hoc cases).**
Applied to the resolved target URI, in order:

| Component | Rule | Direction |
|---|---|---|
| Scheme | lowercase | **folded** — `HTTP` ≡ `http` |
| Scheme value | `http` and `https` are **different targets** | not folded — a downgrade is a different security context |
| Host, ASCII | lowercase (DNS is case-insensitive) | **folded** |
| Host, non-ASCII | convert to Punycode / A-label (IDNA 2008, `UseStd3AsciiRules`), then lowercase | **folded** |
| Host, IPv6 literal | RFC 5952 canonical form (lowercase hex, `::` at the longest zero run, brackets kept) | **folded** |
| Host, IPv4 literal | dotted-quad only; non-canonical forms (octal, decimal-integer, `0x`) are **rejected**, not normalised | rejected |
| Host vs IP | a hostname and an IP address are **different targets** even when DNS resolves one to the other | not folded — resolution is neither stable nor authenticated |
| Port | elide the scheme default (`:80` for `http`, `:443` for `https`) | **folded** |
| Port, non-default | exact match | not folded |
| Userinfo (`user:pass@`) | **rejected** — credentials never travel in the target (T-1, T-2) | rejected |
| Path | strip exactly one trailing `/`; resolve `.` / `..` segments; keep percent-encoding case-normalised (uppercase hex) but decode only unreserved characters per RFC 3986 §6.2.2 | **folded** |
| Path, case | preserved — Creatio paths are case-sensitive | not folded |
| Query, fragment | **rejected** — a target is an origin plus base path, never a query | rejected |

Everything not named above is left byte-exact and therefore distinguishing. Two rules are load-bearing and
deliberately asymmetric: the IP/hostname split and the `http`/`https` split both cost an extra worker in the
rare case and prevent a credential crossover in the wrong one.
**Note:** rejection means the call fails with an explicit error, not a silent fallback to a looser key —
fail-closed, as in R-5.
**Current state:** `BuildCacheKey` (`ToolCommandResolver.cs:361-379`) already carries the uri in the
identity after ENG-94529, but the name branch and the URI branch still yield different keys for one target,
so the normalisation is work not yet done — see rule 10.
**Verification:** a table-driven test of the equivalence list, asserting both directions (equivalent pairs
share a key; near-miss pairs do not).

### T-6 — Secret leakage through diagnostics

**Attack:** the new components — supervisor, relay, worker — log what they route, and the material is right
there. Worker stdout/stderr is captured by the parent by construction. Crash dumps, progress notifications,
tool results, error envelopes and test snapshots are all outbound paths.
**Requirement:** no secret-bearing configuration, connection string, token, password or authorization header
is logged, persisted, put in an error message, or captured in a test snapshot. This is the standing rule
from the ClioRing contribution policy applied to the new surface, and `SensitiveErrorTextRedactor` is the
existing mechanism.
**Requirement:** worker stderr is treated as untrusted, potentially secret-bearing text — redacted before it
reaches a log or an error envelope, never echoed verbatim into a tool result.

**Redaction can be DEFEATED UPSTREAM of itself, and that is a failure class rather than one bug.**
(Recorded 2026-08-18, story 21. Fixed for the drain; the class stays here because the next bounded copy of
untrusted text will reproduce it.)

Every pattern in `SensitiveErrorTextRedactor` recognises a secret by CONTEXT that surrounds it — the key in
`password=…`, the `Bearer ` prefix, the `eyJ` header, the `scheme://` authority, the `:port` suffix. **Any
transformation applied to untrusted text between CAPTURE and REDACTION can remove that context, and the
redactor then cannot see what it was built to see.** The redactor is not at fault and cannot be patched into
safety: it is handed a string that no longer contains the evidence.

Two directions, both real, and naming only the first would leave the second to be rediscovered:

- **Tail truncation orphans a value from its key.** `WorkerStandardErrorDrain` (nested in
  `clio/Command/McpServer/Relay/McpWorkerCallDispatcher.cs`, bounded by `StandardErrorTailLimit`) keeps the
  LAST N characters and trims from the front at an arbitrary offset. A cut inside `password=` leaves
  `word=<secret>`, which matches no alternative of `CredentialPairRegex`, and the value was copied verbatim
  into `worker-stderr` on the failure envelope — the one that goes to the client, into the model transcript,
  and onward to whatever third party reads it. This is the ordinary `key=value` credential, the most common
  shape in a stack trace or a connection-string dump. The self-identifying shapes (`JwtRegex`,
  `BearerTokenRegex`, URI, host:port) survive a front cut because they carry their own context.
  **Resolution:** the drain drops the leading PARTIAL line of a trimmed tail before the redactor runs, and
  withholds the tail behind an explicit notice when no complete line survived the bound — so a redaction
  input can no longer begin part-way through a token. Verified end to end on the failure envelope, from a
  fixture that asserts its own cut really lands inside the key
  (`McpWorkerCallDispatcherTests.DispatchAsync_ShouldNotLeakACredential_WhenTheBoundCutItsKeyInHalf`).
- **Head truncation bisects a self-identifying shape.** The mirror case: `value[..N]` keeps the front, so a
  key can never lose its value — but a JWT cut mid-token leaves fewer than three base64url segments and
  `JwtRegex` misses it, and a URI cut short of its authority can slip past `UriRegex`. Head truncation is
  therefore *safer*, not safe. The head-truncating call sites known today (`ODataCreateTool.Truncate`,
  `ExecuteEsqTool.Truncate`, `ODataReadTool`'s parse-failure preview) all cut a JSON body at 500 characters,
  well past any authority or token that appears at its head, so none is a live leak — but the reasoning is
  positional, not structural, and it must be redone whenever a bound moves.

**Two adjacent gaps found by the same sweep, neither of them this class, both open.** They are recorded here
because the sweep is the only place they would otherwise be noticed: `CompileOperationRegistry.BuildMessageTail`
caps a compile log at 50 MESSAGES and `CompileStatusTool` surfaces that tail to an MCP caller **without
calling the redactor at all** — it cannot orphan anything (it cuts between whole messages, never inside a
string), so this is a missing-redaction gap, not a truncation one; and `ExecuteEsqTool.cs` returns
`Truncate(json)` of a DataService error body unredacted on two paths (`:129`, `:145`), while the sibling path
at `:164` redacts. Both belong to T-6's first requirement (nothing secret-bearing reaches a tool result) and
neither is closed by story 21.

**The rule this yields, and the one to apply to any new bounded copy: REDACT FIRST, then transform.**
`ServiceResponseJsonGuard.BuildResponsePreview` is the reference — it redacts the whole collapsed body and
truncates the *redacted* string, so no transformation can ever run between capture and redaction. Where the
order cannot be inverted — the drain must bound memory *before* it has a whole message to redact, because
bounding is liveness (ADR §3.4) — the transformation must at least cut on a **structural** boundary rather
than an arbitrary offset, so that the redactor is never handed a fragment of a token.

**Residual, stated because a line boundary is weaker than it looks and the next reader will assume
otherwise.** A line break is a boundary no pattern can be cut *inside*, but it is **not** a boundary the
patterns cannot *span*: `CredentialPairRegex` separates key from value with `\s*`, and `\s` includes `\n`
(confirmed against the pattern, 2026-08-18 — `password=\nSECRET` redacts today). So a key that ends the
dropped partial line with its value beginning the surviving one is still orphaned by the line-boundary cut.
This is **recorded, not fixed**: once the key is on the discarded side of the cut, nothing local to the drain
can recover it, and the alternatives (keep the partial line, or drop a second line on the chance the first
ended in a key) each cost more than they buy. The shape is also much rarer than the one story 21 closed — a
worker would have to break its line exactly between a credential key and its value. **The durable answer
remains REDACT FIRST wherever the ordering allows it**; the drain is the one place it does not.

**Redaction is only half of "untrusted". The other half is size and shape, and it is not covered today.**
T-6 as originally written assumed the danger in worker output was what it *says*; a worker's output is also
something the parent has to *hold*, and the parent is the process this whole feature exists to keep alive.

- **Bounded today:** the standard-error tail only — `StandardErrorTailLimit` characters (2000), continuously
  front-trimmed by the nested `WorkerStandardErrorDrain` in
  `clio/Command/McpServer/Relay/McpWorkerCallDispatcher.cs`. *Cited by member name rather than line: that
  file was under active edit on 2026-08-18 (the constant moved from `private` to `internal` and the drain
  class moved by ~75 lines within the hour), so a line anchor here would be wrong before it was read.*
- **NOT bounded today, and this is the finding:** the worker's standard *output*. The relay reads the
  child's messages through `StreamClientTransport`
  (`clio/Command/McpServer/Relay/WorkerChildTransportOwner.ConnectAsync`), whose session transport reads the pipe
  with `StreamReader.ReadLineAsync` and imposes **no maximum line length**
  (`ModelContextProtocol.Core` 2.2.0, `StreamClientSessionTransport.ReadMessagesAsync`; decompiled and read
  2026-08-18). A worker emitting one very long line grows a string **in the parent** until allocation fails.
  There is no frame-size bound in clio's code and none in the SDK's, so a defective — or hostile — worker's
  output is bounded by nothing.

**Requirement (payload size):** the parent bounds how much a single worker may make it hold, on **both**
streams, and a call that exceeds the bound fails with a named relay error rather than by exhausting the
host. No number is fixed here on purpose: a bound smaller than a legitimate `get-schema` or `get-page`
result would break the Stage 6 cohort, and the largest legitimate worker response has not been measured.
**Measuring it is the prerequisite, not the bound** — a guessed constant here would be a new failure mode
invented by the fix, exactly like a budget measured from admission (ADR §2.4).
**Requirement (defensive parsing):** worker output is parsed as untrusted input — malformed, truncated,
oversized or wrongly-typed payloads produce a named relay failure and never an unhandled exception, and
never a value that reads to the caller as a domain answer. A partial defence exists already:
`WorkerMcpRelay.Deserialize` converts a `JsonException` into a named `WorkerRelayException`
(`clio/Command/McpServer/Relay/WorkerMcpRelay`, private `Deserialize<TResult>`). It is not sufficient on its
own, because it runs *after* the bytes are already in the parent's memory.
**Verification:** a redaction test over the relay's error path with a known secret marker, asserting the
marker appears nowhere in the parent's output; plus an oversized-output test asserting the parent answers
with a bounded named error instead of growing without limit.

### T-7 — Orphaned worker holding a live session

**Attack:** the parent dies (crash, SIGKILL, host restart) while a worker holds an authenticated session.
The worker survives, keeps the session alive, and is no longer supervised by anything.
**Observed:** the prototype **leaked one orphan** when the parent was killed mid-operation. This is measured
behaviour, not a hypothetical.
**Requirement (rule 6), split by platform because the verification differs:**
- **R-8a (Unix):** process-group containment plus parent-death signalling, verified by E2E on Linux and
  macOS.
- **R-8b (Windows):** Job Object with kill-on-close, verified by E2E on Windows.

Both carry identity-checked stale-worker cleanup at parent startup — *identity-checked* because PIDs are
reused, and killing a stranger's process is its own defect.

**Why the split is not cosmetic:** Windows containment is unmeasured (OQ-1). A single cross-platform R-8
would be satisfiable by a Unix-only test and then read as green everywhere, which is the outcome the split
exists to prevent. **No cohort ships on Windows until R-8b is verified**; a delivery made before then is
explicitly scoped to R-8a only, and says so.
**Verification (E2E):** SIGKILL the parent while a worker has a descendant of its own; both must disappear
(TC-E-201, Unix). The Windows equivalent is TC-E-203, blocked on OQ-1; both belong to Stage 2.

### T-8 — Worker outliving its credential's validity

**Attack:** a sticky worker holds a session established with a token that has since expired or been revoked.
Work continues under revoked authority.
**Requirement:** a sticky worker's lifetime is bounded by the shorter of the operation's completion and the
credential's validity; revocation upstream must not be silently outlived. Where validity is unknown
(passthrough cookie), an explicit maximum sticky lifetime applies.
**Note:** this threat is *created* by stickiness. Per-call workers do not have it, which is one more reason
stickiness is confined to the four long-running families rather than used as a general performance
optimisation.

### T-9 — Worker spawn exhaustion (denial of service)

**Attack:** a caller — or an agent in a retry loop, which is the likelier source — issues MCP calls faster
than workers finish them. Every environment-touching call spawns a clio process, so an unbounded call rate
is an unbounded process count. Nothing in R-1…R-9 says otherwise; they all govern *what a worker may do*,
not *how many there may be*.
**Measured ceiling** (ADR §2.4, Windows Server 2022, 4 cores / 16 GB): peak working set 334 MB at width 4,
649 MB at width 8, 1073 MB at width 16. Wall time grows linearly past the core count, so nothing is gained
above ~4 on that box — and at width 16 one call waited **16.9 s** to reach `initialize`, purely queued
behind CPU, against a perfectly healthy backend. CPU, not memory, is the binding constraint.
**Status — capped, and the cap is measured rather than guessed.** `WorkerProcessSupervisor` admits at most
`ConcurrencyCap` workers, defaulting to `Environment.ProcessorCount`. *Members in this block are cited by
name rather than by line: `clio/Common/McpWorker/WorkerProcessSupervisor.cs` was under active edit on
2026-08-18 and its line numbers moved by several hundred while this section was being written.*

**Behaviour at the cap, as actually implemented: queue with a bound, then refuse with a named error.** A
caller waits on the slot pool for at most `QueueWaitBound` — default `DefaultQueueWaitBound` = **60 s**,
operator-overridable through `CLIO_MCP_WORKER_QUEUE_WAIT_SECONDS` (accepted range 0 < n ≤ 3600) — and on
expiry the call is refused with `WorkerQueueWaitExpiredException`, carrying the wait endured, the bound, the
cap, and the queue depth *including the refused call*. Nothing is spawned and no request reaches Creatio. The
60 s default is derived from ADR §2.4 rather than chosen: the worst *healthy* queue wait ever measured was
16.9 s at width 16 on the four-core Windows stand, and 60 s of queueing plus the 120 s response budget is
about the hard ceiling an MCP client gives one call. The exception is deliberately neither a
`TimeoutException` nor an `OperationCanceledException`, because both would be misread — the first blames a
backend clio never spoke to, the second blames a caller that was still waiting.

**Why bounding the wait is the point, not a nicety:** an *unbounded* queue wait reproduces this feature's own
defect signature exactly — a call that returns nothing and issues zero requests to Creatio, for an
arbitrarily long time — differing only in that it eventually clears. "Eventually" is not a bound.

**One gap remains, named rather than papered over:**

- **G-1 — CLOSED 2026-08-18 (Stage 7).** `CLIO_MCP_WORKER_CONCURRENCY` now configures the total cap,
  following the queue-wait precedent exactly: pure static resolver, invariant parse, accepted range
  `0 < n <= 64`, fallback `Math.Max(1, ProcessorCount)` for null / empty / non-numeric / out-of-range.
  Sticky capacity stays **derived** (`total / 2`) rather than separately configurable — two independent
  knobs would let an operator set sticky >= total and reintroduce the exhaustion the split removes, and
  would mean clamping a relationship rather than a range. The variable is excluded from the child-inherit
  allowlist for the same reason as the other supervisor variables: a worker spawns no workers.
  **The structural note below now has an answer rather than only a warning** — sticky capacity is a
  CEILING on the shared pool, not a partition of it, so per-call work keeps the whole cap while no sticky
  worker exists and still retains `total - sticky` once one does. Covered by
  `WorkerAdmissionCapacityTests` and the amended TC-U-201. *Original text retained below for history.*
- **G-1 (original) — the concurrency cap itself is not operator-configurable.** The queue-*wait* bound is; the *cap* is
  not. The only constructor taking an explicit cap is `internal` and documented as test-only; the public
  constructor always takes `ProcessorCount`. No `CLIO_MCP_WORKER_CONCURRENCY`-style override exists —
  grepped 2026-08-18, the supervisor defines exactly one environment variable and it is the queue-wait one.
  So an operator on an over-subscribed host cannot lower the cap, and one on a large host cannot raise it;
  they can only change how long callers wait before being refused. tetiana-moshon's finding asked for a
  *configurable cap*, and that half is still outstanding.
- **A structural note that makes G-1 sharper, not a second gap.** A slot is held from spawn to lease
  dispose, so a worker whose lifetime outlives the answer it produced occupies capacity for its whole life.
  On a four-core host, four such holders fill the cap and everything else queues. That is tolerable while
  every worker is per-call; **Stage 7's sticky workers make it the ordinary case**, and that is the point at
  which a fixed, underivable cap becomes the thing an operator needs to change.

**Requirement:** R-10.
**Verification:** TC-U-201 covers admit-N / queue-N+1 / never-drop. The queue-wait bound, its override
parsing and the named refusal carry their own Stage 2 unit coverage. **G-1 now has behaviour and tests** — see the closure note above; the sentence below described the
state before Stage 7. *G-1 had no test because it had no behaviour* — that was the gap, not an oversight
in the plan.

**How to read this section's history.** When this threat was first written on 2026-08-18 the queue wait was
genuinely unbounded, and it named two gaps. The bound landed the same day, so only G-1 survives. The finding
was still worth raising — it is what produced the bound.

### T-10 — Worker executable substitution — **checked against the source, and already constrained**

Raised by m-dymytrova with her own low-confidence flag ("worth a look rather than a confirmed gap"). It was
looked at. The answer is recorded here rather than only in a review reply, so the next reader does not have
to re-derive it.

**Attack the question implies:** the parent is made to spawn something other than clio — a planted binary, a
directory-traversing path, an attacker-chosen `dotnet` — and that process then inherits the worker's
position inside the boundary.
**Status: constrained at three independent points, all read on this branch 2026-08-18.**

- **The candidate never comes from request data.** Resolution derives only from *this* process's own
  identity: `Environment.ProcessPath` when clio runs as an apphost, otherwise the running assembly's
  `Assembly.Location` passed to the muxer that is already hosting it
  (`clio/Common/McpWorker/IClioExecutablePathProvider.cs:69-97`). No tool argument, no `appsettings.json`
  entry and no MCP parameter reaches it.
- **The final string is validated, not trusted.** `ProcessExecutor.ResolveExecutablePath` accepts **only** a
  bare name or a fully-qualified path and throws on anything containing a directory separator
  (`clio/Common/ProcessExecutor.cs:372-381`); a bare name is searched **only** in fully-qualified `PATH`
  entries, relative entries being skipped precisely so a caller-controlled working directory cannot decide
  which executable runs (`:398-403`).
- **The resolved file is checked before use** — it must exist, must not be a directory, symlinks are
  resolved to their final target, and on Unix at least one execute bit must be set (`:421-445`).

**Residuals, both inside a boundary §5 already accepts:**

- `WorkerSpawnRequest.LaunchOverride` bypasses the provider entirely
  (`clio/Common/McpWorker/WorkerProcessSupervisor`, private `BuildLaunchRequest`). It is a documented test
  seam — "used by tests, which contain and kill a purpose-built fixture rather than a real worker"
  (`IWorkerProcessSupervisor.cs`, `WorkerSpawnRequest.LaunchOverride`) — set in code, never from a request.
  Setting it from anything request-derived would be the defect; nothing does today.
- `PATH`, `DOTNET_ROOT*` and `DOTNET_HOST_PATH` are inherited from the parent's own environment
  (`WorkerProcessSupervisor.DefaultInheritedEnvironmentVariableAllowlist`), so whoever can set the parent's
  environment picks the muxer. That is
  the same OS account that can already read the parent's memory and its `appsettings.json` — §5's first
  accepted residual, not a new one.

**Requirement:** R-12 — recorded as *satisfied by construction today*, so that a future change which loosens
any of the three points is visibly a regression rather than a new opinion.
**Verification — and the honest state of it.** The behaviour is in the code and was read; the **coverage is
thin**. Exactly two tests touch this path, and both assert the happy case: `ProcessExecutorTests`
"executable resolution pins a bare program name to an absolute executable path"
(`clio.tests/Common/ProcessExecutorTests.cs:179-185`) and
`WorkerProcessSupervisorTests.ClioExecutablePathProvider_ShouldPassTheAssemblyToTheMuxer_WhenClioRunsThroughIt`.
The three properties that actually carry the security argument — a separator in the name is **rejected**, a
relative `PATH` entry is **skipped**, a non-executable or directory candidate is **refused** — have **no
negative test**. So R-12 is *satisfied by construction, not by assertion*, and a refactor could quietly
remove any of the three without a red test. Adding those three negative cases is the cheapest way to turn
this from a read into a guarantee.

## 4. Requirements summary

| # | Requirement | Stage | Verified by |
|---|---|---|---|
| R-1 | No secret material in a child's command line **or environment block**; pipe or other inherited handle only, written after spawn and closed | 3 | command-line/environment inspection test |
| R-2 | Routing key derived from resolved tenant identity, never from raw tool arguments | 1, 4 | smuggling-rejection tests still pass with routing on |
| R-3 | Worker builds its client through the same `ApplicationClientFactory` path as the parent | 3 | fail-first identity assertion |
| R-4 | Worker fails closed on unusable material — never falls back to a default identity | 3 | negative auth test asserting refusal, not success |
| R-5 | Sticky scope key = principal + normalised target + credential fingerprint; lookup fails closed | 5, 7 | two-caller isolation test |
| R-6 | Target normalisation follows the component-by-component algorithm in T-5; nothing is folded that the algorithm does not name | 5 | equivalence-table test generated from the T-5 table |
| R-7 | No secrets in logs, errors, notifications, dumps or snapshots; worker stderr redacted. **The credential fingerprint is classified here too** — never logged, persisted, notified or snapshotted (T-4). **No transformation may run between capture and redaction that removes the context a pattern needs** — redact first and transform the redacted text, or cut only on a boundary the patterns cannot straddle (T-6, story 21) | 2–5 | redaction test with secret marker; mid-key-cut test on the failure envelope (story 21) |
| R-8a | Unix process-group containment plus parent-death signalling; identity-checked stale-worker cleanup | 2 | parent-SIGKILL E2E on Linux and macOS (TC-E-201) |
| R-8b | Windows Job Object containment with kill-on-close; identity-checked stale-worker cleanup | 2 | parent-kill E2E on Windows (TC-E-203) — unmeasured today, **OQ-1** |
| R-9 | Sticky lifetime bounded by credential validity, with an explicit maximum | 7 | lifetime test |
| R-10 | Total live worker count capped by a processor-count-derived value; at the cap calls **queue under a bounded wait** (60 s default, `CLIO_MCP_WORKER_QUEUE_WAIT_SECONDS`) and are refused with a *named* saturation error carrying cap and queue depth — never an unbounded process count, never an unbounded silent wait, never an error that reads as a backend timeout. **Closed 2026-08-18 (Stage 7):** the cap is operator-configurable via `CLIO_MCP_WORKER_CONCURRENCY` (0 < n <= 64), and sticky capacity is a derived CEILING on the shared pool rather than a partition, so per-call work keeps the whole cap until sticky work exists and retains a guaranteed floor after (T-9, G-1) | 2 | TC-U-201 (admit/queue/never-drop) + Stage 2 coverage of the wait bound, its override parsing and the named refusal; `WorkerAdmissionCapacityTests` plus the amended TC-U-201 |
| R-11 | Worker output is bounded on **both** streams and parsed defensively: an oversized, malformed or wrongly-typed payload becomes a named relay failure, never an unhandled exception, never memory growth without limit, never a value the caller reads as a domain answer. The bound is set from a **measured** largest legitimate response, not guessed (T-6) | 4, 6 | stderr redaction test (TC-U-505) today; stdout size bound **not yet built** |
| R-12 | Worker executable resolution derives only from this process's own identity, accepts only a bare name or a fully-qualified path, searches only fully-qualified `PATH` entries, and validates the resolved file. **Satisfied by construction** (T-10); recorded so that loosening it is visibly a regression | 2 | two happy-path tests only; the three load-bearing **negative** cases (separator rejected, relative `PATH` entry skipped, non-executable refused) have **no test** — see T-10 |

## 5. Residual risk accepted

- **A local user with the same OS account as the parent can read a worker's memory.** Out of scope: that
  user can already read the parent's memory and its `appsettings.json`. The channel design does not make
  this worse; it must simply not make it *easier* (which is what T-1 is about).
- **Stdio workers read `appsettings.json` directly.** Deliberate — it avoids a channel entirely for the
  Stage 6 cohort, and the file is already readable by anything running as that user.
- **A local user with the same OS account as the parent can signal a worker's process group.** On Linux the
  worker arms `prctl(PR_SET_PDEATHSIG, SIGTERM)` together with a `PosixSignalRegistration` for SIGTERM whose
  handler kills the worker's own process group with SIGKILL
  (`clio/Common/McpWorker/UnixParentDeathWatch.cs:187-206`, handler at `:157-164`). One SIGTERM from a
  same-UID process therefore destroys that worker and every descendant it spawned, mid-operation — the same
  effect as the parent's own budget kill, triggered by somebody else. On macOS that SIGTERM handler is not
  installed (the parent-death watch there is a `kqueue` `EVFILT_PROC` watch), so an injected SIGTERM
  terminates the worker under the default disposition and its descendants are left to the parent's own
  containment kill.
  **What this is:** a denial of service, and a way to interrupt an in-flight operation without touching the
  parent. **What it is not:** a confidentiality change — no material is read, written or redirected — and
  not a widening of the boundary, since the same user can already `kill` the parent, read its memory and
  read `appsettings.json`, which the first bullet accepts. The outcome of a killed worker is exactly the
  *indeterminate* path the design already specifies (ADR §3.3): a run that dies without a terminal event is
  reported indeterminate and is never retried automatically. Group promotion in fact **narrows** what a
  stray signal can reach — an unpromoted child would have inherited the launching shell's group
  (`clio/Common/McpWorker/UnixProcessGroupContainment.cs:14-22`).
  **Accepted**, on one condition that the terminal-stage protocol already meets: a signal-induced death must
  never be reported to the caller as a domain answer.
- **Windows containment: the mechanism is measured, the in-suite proof is platform-conditional.** OQ-1 is
  **CLOSED** (ADR §8, measured 2026-08-17 on ts1-core-dev04, §2.4): a standalone probe showed
  `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` kills the whole subtree when — and only when — the child is assigned
  to the job before it executes a single instruction. That is the R-8b *mechanism*. The R-8b **test** exists
  in the suite (`clio.mcp.e2e/McpWorkerContainmentE2ETests.cs:141`, TC-E-203) but is skipped with an
  explicit reason on any non-Windows host, so it closes R-8b only on the Windows agents of
  `Team_Atf_ClioMcpE2eTests`. *Unproven from this checkout:* whether TC-E-203 has yet run green on those
  agents. Until that result is stated, no cohort ships on Windows and any interim delivery is scoped to
  R-8a only — the rule is unchanged, only its reason is now "the in-suite run is unstated" rather than "the
  behaviour is unmeasured".

## 6. Requirement applicability by stage

Which requirements a stage boundary actually settles — derived from what has landed on this branch, not
from the plan's intent. Read 2026-08-18. "Fully satisfied" means the requirement is implemented **and** has
in-suite coverage on this branch; "partial / open" names what is missing, in words.

| Stage | State | Fully satisfied at this boundary | Partial / open | Not yet applicable |
|---|---|---|---|---|
| 0 | done (design only) | none — no code exists | every requirement is *stated*, none is *held* | R-1…R-12 |
| 1 | done | R-2 (routing key from resolved tenant identity, resolved after unwrapping `clio-run`; TC-U-101…109) | — | R-1, R-3…R-12 |
| 2 | done | R-8a (TC-E-201/202, Unix) | **R-8b** — mechanism measured (§2.4), in-suite TC-E-203 skipped off Windows; **R-10** — cap exists, the queue wait is bounded and the refusal is named, but the **cap** is not operator-configurable (G-1); **R-12** — satisfied by construction, but its three load-bearing negative cases have no test | R-5, R-6, R-9, R-11 |
| 3 | done | R-1 (TC-E-301 — and on stdio no material crosses the channel at all), R-3 (TC-E-302, fail-first identity), R-4 (TC-E-303, refusal not fallback) | — | R-5, R-6, R-9 |
| 4 | done | — | **R-7** — worker stderr is drained and redacted onto the failure envelope (TC-U-505), and the upstream-transformation defeat found in story 21 is closed at the drain (the trimmed tail can no longer begin part-way through a line); the fingerprint classification added in T-4 still has no test and R-11's stdout bound does not exist | R-5, R-6, R-9 |
| ~~5~~ | **deferred** (OQ, mcp-http's fate) | none | **R-5** and **R-6** are unbuilt, and the HTTP half of R-1 with them. They are *not violated*: the stdio-only gate means no HTTP call reaches a worker at all (`clio/Command/McpServer/IMcpWorkerPathGate.cs`), so the requirements are unreachable rather than unmet. **R-6's normalisation work does not wait for this stage** — story 7 carries it in stdio scope (see that story's prerequisite) | R-5/R-6 stay inapplicable while the gate holds |
| 6 | done | R-1…R-4 and R-8a hold for the shipped cohort; the stdio-only gate is the enforcement (TC-E-601…604, TC-U-601) | **R-10** (G-1 carries forward — this is the first stage where call rate is real), **R-11** (no stdout bound), **R-12** (holds, untested negatives) | R-5, R-6, R-9 |
| 7 | not started | — | **R-5** (sticky scope key), **R-6** (normalisation, re-homed here in stdio scope), **R-9** (sticky lifetime), the T-4 keyed-digest condition (sticky keys are what spread the fingerprint), and **R-10's G-1** — a slot is held for a worker's whole lifetime, so a fixed cap binds hardest exactly here | — |
| 8 | not started | — | **R-7** and **R-11** get their hardest case: a deploy child streams for minutes and its output is the terminal-stage signal | R-5/R-6 while the gate holds |
| 9 | done (`clio/Command/McpServer/Tools/InterprocessFileGate.cs`) | none of R-1…R-12 — the file gates answer ADR rule 8, not a credential requirement | — | not a credential-model stage |
| 10 | not started | — | nothing new; deletion must not remove a bound that R-10 or R-11 turns out to rely on | — |
