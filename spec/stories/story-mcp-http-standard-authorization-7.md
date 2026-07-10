# Story 7 — Retire/gate `--platform-api-key` + unified auth error taxonomy

**Feature**: mcp-http-standard-authorization · **Jira**: ENG-93386 · **Size**: M · **Status**: ready-for-dev
**Depends on**: 5 · **Blocks**: 8

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
- [ ] api-key disposition per OQ-D.
- [ ] Unified error taxonomy + tests.
- [ ] No secret leakage (tests).

## Notes
This story reconciles the ENG-93208 `EnforcePlatformApiKeyGate`/`CaptureCredentialContext` middleware with the new OAuth front door — likely the api-key gate is removed and credential-capture is gated on `User.Identity.IsAuthenticated` instead.
