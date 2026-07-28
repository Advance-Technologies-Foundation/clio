# Test Plan: Clio MCP HTTP Standard Authorization (OAuth 2.1 RS)

**Jira**: ENG-93386 · **PRD**: [prd-mcp-http-standard-authorization.md](../prd/prd-mcp-http-standard-authorization.md) · **ADR**: [adr-mcp-http-standard-authorization.md](../adr/adr-mcp-http-standard-authorization.md)
**Created**: 2026-07-10

## Risk assessment
- **Highest risk: lockout / partial-open.** A misconfigured issuer/audience bricks the public edge, OR the "enabled" flag mis-evaluates and leaves it open. Both must be covered by explicit enabled/disabled matrices.
- **Confused-deputy / token passthrough.** The MCP JWT must never reach Creatio; the credential header must never be honored unauthenticated. These are security invariants — non-negotiable coverage.
- **Regression.** stdio and unconfigured-auth loopback must be byte-for-byte today's behavior.

## Regression scope
- ENG-93208 passthrough suite (isolation, no-regression, multi-tenant) must stay green with auth **disabled** (default) and be re-validated with auth **enabled**.
- `Module=McpServer` + `Module=Common` unit suites; both HTTP and stdio host graphs pass `ValidateOnBuild`.

## Unit tests (`Category=Unit`)
| ID | Covers | Assertion |
|----|--------|-----------|
| TC-U-01 | FR-04 | Auth options parse (kebab-case); flag+env unioned; "enabled iff issuer set". |
| TC-U-02 | FR-01 | JWT accept/reject matrix (test signing key): valid ⇒ accept; wrong-issuer / wrong-audience / expired / bad-signature ⇒ reject. |
| TC-U-03 | FR-01/FR-11 | Insufficient scope ⇒ `403`; missing/invalid token ⇒ `401`. |
| TC-U-04 | FR-05 | No issuer configured ⇒ auth off; pipeline identical to pre-auth (no JWT middleware in graph). |
| TC-U-05 | FR-02 | PRM document shape matches config (`resource`, `authorization_servers`, `scopes_supported`); canonical URI derivation incl. ingress form. |
| TC-U-06 | FR-07 | Credential header ignored when `User.Identity` is unauthenticated; honored only when authenticated. |
| TC-U-07 | FR-08 | Principal not permitted for asserted tenant ⇒ `403`; permitted ⇒ proceeds. |
| TC-U-08 | FR-09 | **Invariant:** inbound MCP JWT never attached to any outbound Creatio request (spy on the client factory / outbound headers). |
| TC-U-09 | FR-05/OQ-A | Wildcard bind + no issuer ⇒ warning emitted (or startup refused per OQ-A verdict). |
| TC-U-10 | FR-13/AC-ERR | No secret (JWT, client assertion, Creatio credential) in logs/responses/PRM. |
| TC-U-11 | FR-12 | `--platform-api-key` disposition: default path no longer uses it (or dev-fallback is off-by-default and cannot weaken OAuth). |
| TC-U-12 | Transport pins | HTTP + stdio graphs pass `ValidateOnBuild`/`ValidateScopes`; transport defaults unchanged. |

## Integration tests (`Category=Integration`)
| ID | Covers | Assertion |
|----|--------|-----------|
| TC-I-01 | FR-02/AC-02 | `GET /.well-known/oauth-protected-resource` (anonymous) returns the metadata document. |
| TC-I-02 | FR-01/FR-03/AC-01 | Unauthenticated `/mcp` ⇒ `401` + `WWW-Authenticate: Bearer resource_metadata="…"`; valid token ⇒ tool call succeeds. |
| TC-I-03 | FR-11/AC-05 | Auth failures resolve at the auth layer (401/403), never as a tool-body `Environment name is required`. |

## E2E tests (`Category=E2E`, manual / skip-gated) — `clio.mcp.e2e/`
| ID | Covers | Assertion |
|----|--------|-----------|
| TC-E-01 | Goal 1 | Obtain a `client_credentials` token (aud = clio-mcp) from a dev identity-platform; call a passthrough tool successfully. |
| TC-E-02 | AC-01 | Wrong-audience token ⇒ `401`; insufficient scope ⇒ `403`. |
| TC-E-03 | AC-03/FR-09 | With a valid token + tenant creds header, the tool reaches Creatio, and a capture proves the MCP JWT was not forwarded upstream. |
| TC-E-04 | AC-04 | Auth-disabled `mcp-http` + stdio behave as pre-auth (regression). |

## Gating notes
- E2E requires a reachable identity-platform AS + a registered confidential client → env-var skip-gated (like the ENG-93208 fixtures); NOT in CI until an AS is available to CI.
- Story 1 (spike) findings freeze the concrete issuer/audience/scope/claims used by TC-U-02, TC-U-07, TC-E-01/02.
