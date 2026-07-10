# Story 1 — Spike: confirm identity-platform M2M capability (OQ-B) [BLOCKING]

**Feature**: mcp-http-standard-authorization · **Jira**: ENG-93386 · **Size**: S · **Status**: ready-for-dev
**Depends on**: — · **Blocks**: 2, 3, 6

## As a / I want / So that
As the implementer, I want to confirm what the AI-Platform `identity-platform` Authorization Server actually supports, so the issuer/audience/grant contract is frozen on facts, not assumptions.

## Scope
Confirm with the platform team / by probing a dev identity-platform:
1. `grant_type=client_credentials` is supported for a pre-registered confidential client.
2. The RFC 8707 `resource` parameter is honored and reflected in the token's audience.
3. `private_key_jwt` (RFC 7523) client authentication is available (else client_secret fallback).
4. OIDC discovery document (`/.well-known/openid-configuration`) + JWKS endpoint are reachable; token signing alg.
5. The `issuer` value and the token claims available for scope / tenant-authorization (FR-08).

## Acceptance Criteria
- A findings doc (`spec/mcp-http-standard-authorization/identity-platform-spike-findings.md`) records: supported grant(s), resource/audience behavior, client-auth method, issuer, JWKS URL, alg, and available claims — each with evidence (probe output or platform-team confirmation).
- A GO/NO-GO verdict per assumption; if any is NO-GO, the fallback and its impact on FR-04/FR-06 is stated.

## Definition of Done
- [ ] Findings doc committed with evidence.
- [ ] GO/NO-GO verdict + fallbacks.
- [ ] ADR OQ-B updated with the verdict.

## Notes
No production code. This is a hard prerequisite: a wrong audience/issuer assumption bricks the RS validation in Story 3.
