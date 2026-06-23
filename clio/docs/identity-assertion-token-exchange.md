# Identity assertion & token exchange via AI chat (Identity Service V3)

This guide explains how to use clio to set up and exercise the Creatio identity-assertion
flow that lets an embedded AI chat identify a Creatio user to the Agentic Platform through an
**Identity Service V3 token exchange** — without a separate login.

It is written so a human operator **or** an AI agent can perform onboarding and verify the flow
end to end. It covers ENG-89189 acceptance criteria 1–8.

## Background — how identity flows (conceptual)

The chat does not authenticate the user itself. It relies on Creatio asserting the user's
identity in a form Identity Service V3 can verify.

1. **Keys.** Each Creatio instance owns an asymmetric signing key pair. The **private key never
   leaves the instance**; the **public key** is registered once with Identity Service V3 at
   onboarding.
2. **Assertion.** When a user opens the embedded chat, Creatio mints a short-lived identity
   assertion (a signed JWT) for the currently authenticated user.
3. **Exchange.** The chat passes the assertion to its backend, which presents it to Identity
   Service V3. V3 verifies the signature using the registered public key.
4. **Credentials.** On success, V3 issues the chat backend credentials scoped to that specific
   user; the chat calls the Agentic Platform on the user's behalf.

The asymmetric model is what makes this safe: only Creatio can mint valid assertions, and V3
verifies them independently without calling back to Creatio at request time. The same model is
reused for 3rd-party hosts — they register their own public keys.

## Creatio endpoints (server side)

The behavior lives in the Creatio core OAuth controllers. clio wraps these endpoints; you do not
call them directly:

| Endpoint | Method | Purpose |
|---|---|---|
| `identityAssertion/currentUser` | POST | Issue a short-lived signed assertion for the current user |
| `identityAssertion/publicJwk` | GET | Return the instance public key as a JWK |
| `identityAssertion/regenerateSigningKey` | POST | Regenerate the signing key pair (private key stays server-side) |
| `identityServiceInfo/canUseAuthorizationCodeFlow` | GET | Whether the OAuth authorization code flow is available |

On .NET Framework instances the routes are served under the `0/` prefix; clio adds that prefix
automatically based on the registered environment runtime.

### Prerequisites on the environment

- The Creatio **feature `EnableIdentityAssertionIssuer` must be enabled** for the three
  `identityAssertion/*` endpoints. While it is off they return `403 Forbidden`.
- `publicJwk` and `regenerateSigningKey` additionally require the current user to have the
  **`CanManageIdentityAssertionIssuer`** operation permission.
- `canUseAuthorizationCodeFlow` does not depend on the feature.

## clio commands

| Command (alias) | Wraps | Output (`--format text`, default) | `--format json` |
|---|---|---|---|
| `get-identity-assertion` (`identity-assertion`) | `currentUser` | the assertion token (JWT) | full payload `{assertion, assertionType, expiresIn, expiresAt, issuer, audience}` |
| `get-identity-public-jwk` (`identity-public-jwk`) | `publicJwk` | compact single-line JWK | indented JWK |
| `regenerate-identity-signing-key` (`identity-regenerate-key`) | `regenerateSigningKey` | `OK` | `{"status":"regenerated"}` |
| `check-auth-code-flow` (`auth-code-flow`) | `canUseAuthorizationCodeFlow` | `true` / `false` | `{"canUseAuthorizationCodeFlow":<bool>}` |

All four accept the standard environment selector (`-e <env>` or `-u/-l/-p`, or OAuth
`--clientId/--clientSecret/--authAppUri`). They authenticate as the current environment user, so
the assertion is always issued for an **authorized** user.

The same four operations are exposed as MCP tools with identical names, each taking an
`environmentName` argument and an optional `format` (`text` | `json`).

## Onboarding walkthrough

```bash
# 1. (Optional) Establish a fresh signing key pair on the instance.
#    Destructive: invalidates any assertions signed with the previous key.
clio regenerate-identity-signing-key -e my-creatio

# 2. Export the public key (JWK) and register it with Identity Service V3.
clio get-identity-public-jwk -e my-creatio --format json > instance-public-jwk.json
#    -> hand instance-public-jwk.json to the Identity Service V3 onboarding step.

# 3. (Diagnostic) Confirm the environment can use the OAuth authorization code flow.
clio check-auth-code-flow -e my-creatio
```

## Verifying the token-exchange flow

```bash
# 4. Issue an assertion for the current user (what the frontend would obtain).
clio get-identity-assertion -e my-creatio
#    -> prints the JWT. Use --format json to inspect expiry/issuer/audience.

# 5. The chat backend presents this assertion to Identity Service V3, which verifies it with the
#    public key registered in step 2 and returns user-scoped credentials. (Performed by the chat
#    backend / Identity Service V3, outside clio.)
```

A successful end-to-end check looks like:

- `check-auth-code-flow` prints `true`.
- `get-identity-public-jwk` returns a JWK with a stable `kid`/key material.
- `get-identity-assertion` returns a JWT whose signature validates against that JWK, with a short
  `expiresIn` (seconds) and the expected `issuer`/`audience`.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `403` / "endpoint is disabled" | `EnableIdentityAssertionIssuer` is off | Enable the feature on the environment |
| `403` / "AccessDenied" on jwk/regenerate | Missing `CanManageIdentityAssertionIssuer` | Grant the operation permission to the user |
| `401` on `get-identity-assertion` | User not authenticated / session expired | Re-check environment credentials (`clio reg-web-app --check-login`) |
| `400` / "UserEmailMissing" | Current user has no email | Set the user's email in Creatio |
| Assertion fails to validate in V3 | Public key not (re-)registered after a key regeneration | Re-run step 2 with the current public JWK |

## Related

- [`get-identity-assertion`](commands/get-identity-assertion.md)
- [`get-identity-public-jwk`](commands/get-identity-public-jwk.md)
- [`regenerate-identity-signing-key`](commands/regenerate-identity-signing-key.md)
- [`check-auth-code-flow`](commands/check-auth-code-flow.md)
