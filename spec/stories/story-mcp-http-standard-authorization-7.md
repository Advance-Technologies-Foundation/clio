# Story 7 — Retire/gate `--platform-api-key` + unified auth error taxonomy

**Feature**: mcp-http-standard-authorization · **Jira**: ENG-93386 · **Size**: M · **Status**: ready-for-dev
**Depends on**: 5 · **Blocks**: 8 · **Status: DONE (2026-07-11)**

> **OQ-D resolved per ADR D-6: retire as the default front door, keep as an explicit dev/offline fallback.** `--platform-api-key` is no longer combined with OAuth — it is fully bypassed once `--auth-authority` is configured. `EnforcePlatformApiKeyGate` now branches on `authorizationEnabled`: when true, it skips the legacy key check entirely and derives `PassthroughEnabledItemKey` solely from `context.User.Identity.IsAuthenticated` (which `RequireAuthorization` on `/mcp`, Story 5, already guaranteed); when false, it behaves exactly as before ENG-93386. This isn't a design preference — `Authorization: Bearer …` can carry only one scheme at a time, and once OAuth is enabled the JWT bearer handler already claims that header, so an AND/OR combination with the old key isn't even meaningful. A matching legacy key can never re-enable passthrough for an unauthenticated OAuth request (verified by test), closing the "two conflated Bearer meanings" ambiguity named in the ADR context.
>
> **Silent-misconfiguration guard:** a new pure predicate `ShouldWarnPlatformApiKeyIgnored(authorizationEnabled, platformApiKeyCount)` triggers a loud startup warning when both `--auth-authority` and `--platform-api-key` are configured together — not a security problem (the key is simply inert), but worth surfacing so an operator never assumes the old key is still enforced.
>
> **Unified taxonomy (FR-11):** already substantially closed by Stories 5/6 (`RequireAuthorization` + the SDK's PRM/401-challenge handler resolve every OAuth-mode auth outcome before the tool layer); this story removes the remaining "two Bearer schemes on one header" ambiguity so the two modes (OAuth vs legacy dev/offline) never overlap on the same request. Help text + docs updated to state the retired/fallback disposition explicitly (CLI option HelpText, `help/en/mcp-http.txt`, `docs/commands/mcp-http.md`).
>
> 7 new tests in `PlatformApiKeyDispositionTests.cs` (4 for the warning predicate + 3 for the bypass/legacy-preservation behavior).

## As a / I want / So that

## As a / I want / So that
As a maintainer, I want one clear authorization stage with predictable errors, so the two-Bearer confusion and the mixed error surfaces are gone.

## Scope
- **`--platform-api-key` fate (FR-12, OQ-D):** per the ADR, retire it as the default front door. If kept, only as an explicit non-OAuth dev/offline fallback behind a separate opt-in flag that (a) is off by default, (b) cannot be combined with OAuth to weaken it, (c) is clearly labelled insecure for public use. Remove/deprecate `PlatformApiKeyGate` from the default path accordingly.
- **Unified taxonomy (FR-11):** all auth outcomes resolve at the auth layer before the tool layer — `401` (missing/invalid/expired/wrong-audience) with spec `WWW-Authenticate`; `403` (insufficient scope / gateway not authorized for tenant); `400` (malformed). Eliminate the current path where an auth-ish failure surfaces as a tool-body `Environment name is required` exit-code-1.
- Secret hygiene sweep (FR-13): tokens/assertions/creds never logged/echoed.

## Acceptance Criteria
- Chosen `--platform-api-key` disposition implemented + documented; if removed, ENG-93208 tests referencing it are updated/retired coherently.
- Auth failures never appear as a tool-body error (AC-05); consistent JSON + status.
- Secret-hygiene tests green.

## Definition of Done
- [x] api-key disposition per OQ-D — kept as opt-in dev/offline fallback (existing `--platform-api-key` flag *is* the opt-in; not on by default), bypassed entirely when OAuth is configured, loud warning when both are set.
- [x] Unified error taxonomy + tests — closed the two-Bearer-schemes ambiguity; OAuth-mode taxonomy already covered by Stories 5/6 tests.
- [x] No secret leakage (tests) — warning message carries no key/token material; existing FR-13 redaction tests unaffected.

## Notes
This story reconciles the ENG-93208 `EnforcePlatformApiKeyGate`/`CaptureCredentialContext` middleware with the new OAuth front door — likely the api-key gate is removed and credential-capture is gated on `User.Identity.IsAuthenticated` instead.
