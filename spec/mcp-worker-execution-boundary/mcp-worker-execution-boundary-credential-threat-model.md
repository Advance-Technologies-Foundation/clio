# Inventory 3 — threat model for the parent→child credential channel

**Feature:** mcp-worker-execution-boundary · **Jira:** ENG-95262 · **Stage:** 0 (design artifact)
**Measured against:** `origin/master` @ `3fc50bf99`, 2026-08-17
**Governs:** Stage 5 (HTTP credential channel + per-client sticky isolation); binding on Stages 2, 3 and 7.

Today credentials never leave the process that received them. Moving execution into a child process creates
a channel that does not exist yet: the parent holds the authentication material and the child needs it to
talk to Creatio. **That channel is new attack surface, and it is the reason Stage 5 is a separate stage
rather than a detail of Stage 4.**

Scope: how material reaches a worker, what a worker may do with it, and which worker a caller may reach.
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
**Requirement:** **secret material must never appear in a child's command line — no exceptions, no
"temporarily for debugging".** The channel is an inherited handle: an environment variable set on the child
only (not the parent's environment), or a pipe written after spawn. The design preference is the pipe:
environment blocks are inherited by grandchildren and appear in some crash dumps, while a pipe is read once
and closed.
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
`BuildPassthroughCacheKey` (`ToolCommandResolver.cs:316`), which already uses the **full** SHA-256 rather
than a truncation precisely because "same url, different token" is the norm on this feature and a truncation
collision would be a credential crossover.
**Requirement:** worker lookup **fails closed** — an unmatched key spawns a new worker; it never falls back
to "closest match" or "any worker for this environment".
**Verification:** two concurrent callers, same environment, different principals → two distinct workers,
each observing its own identity at the Creatio end.

### T-5 — Target normalisation collision

**Attack:** the normalisation that makes a registered *name* and an explicit *URI* one key (rule 10) is the
same normalisation that decides whether two requests may share a worker. Normalising too aggressively (case,
trailing slash, default port, host aliases, IP-vs-hostname) merges targets that are not the same, and the
merged worker carries one caller's credentials to another caller's target.
**Requirement:** normalisation is **conservative and explicit** — a documented, tested list of equivalences.
Anything not on the list is a different target. When in doubt, spawn another worker; the cost is 0.7 s and
the alternative is a credential crossover.
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
**Verification:** a redaction test over the relay's error path with a known secret marker, asserting the
marker appears nowhere in the parent's output.

### T-7 — Orphaned worker holding a live session

**Attack:** the parent dies (crash, SIGKILL, host restart) while a worker holds an authenticated session.
The worker survives, keeps the session alive, and is no longer supervised by anything.
**Observed:** the prototype **leaked one orphan** when the parent was killed mid-operation. This is measured
behaviour, not a hypothetical.
**Requirement (rule 6):** Windows Job Object with kill-on-close; Unix process-group containment plus
parent-death signalling; identity-checked stale-worker cleanup at parent startup — *identity-checked*
because PIDs are reused, and killing a stranger's process is its own defect.
**Verification (E2E):** SIGKILL the parent while a worker has a descendant of its own; both must disappear.
Windows containment is unmeasured (OQ-1) and belongs to Stage 2.

### T-8 — Worker outliving its credential's validity

**Attack:** a sticky worker holds a session established with a token that has since expired or been revoked.
Work continues under revoked authority.
**Requirement:** a sticky worker's lifetime is bounded by the shorter of the operation's completion and the
credential's validity; revocation upstream must not be silently outlived. Where validity is unknown
(passthrough cookie), an explicit maximum sticky lifetime applies.
**Note:** this threat is *created* by stickiness. Per-call workers do not have it, which is one more reason
stickiness is confined to the four long-running families rather than used as a general performance
optimisation.

## 4. Requirements summary

| # | Requirement | Stage | Verified by |
|---|---|---|---|
| R-1 | No secret material in a child's command line; inherited handle or pipe only | 3 | command-line/environment inspection test |
| R-2 | Routing key derived from resolved tenant identity, never from raw tool arguments | 1, 4 | smuggling-rejection tests still pass with routing on |
| R-3 | Worker builds its client through the same `ApplicationClientFactory` path as the parent | 3 | fail-first identity assertion |
| R-4 | Worker fails closed on unusable material — never falls back to a default identity | 3 | negative auth test asserting refusal, not success |
| R-5 | Sticky scope key = principal + normalised target + credential fingerprint; lookup fails closed | 5, 7 | two-caller isolation test |
| R-6 | Conservative, documented, tested target normalisation | 5 | equivalence-table test |
| R-7 | No secrets in logs, errors, notifications, dumps or snapshots; worker stderr redacted | 2–5 | redaction test with secret marker |
| R-8 | Cross-platform containment; identity-checked stale-worker cleanup | 2 | parent-SIGKILL E2E |
| R-9 | Sticky lifetime bounded by credential validity, with an explicit maximum | 7 | lifetime test |

## 5. Residual risk accepted

- **A local user with the same OS account as the parent can read a worker's memory.** Out of scope: that
  user can already read the parent's memory and its `appsettings.json`. The channel design does not make
  this worse; it must simply not make it *easier* (which is what T-1 is about).
- **Stdio workers read `appsettings.json` directly.** Deliberate — it avoids a channel entirely for the
  Stage 6 cohort, and the file is already readable by anything running as that user.
- **Windows containment behaviour is unverified** (OQ-1). Stage 2 cannot close without measuring it; until
  then no cohort ships on Windows.
