# ADR — external-access token exchange as a clio authentication path

- Status: proposed
- Date: 2026-09-17
- Supersedes (in part): `spec/archive/browser-session-handoff/adr/adr-browser-session-handoff.md`,
  option B ("OAuthTokenLogin (token→cookie)"), rejected 2026-06-10.

## Context

The Story-11 spike rejected `OAuthTokenLogin` because clio's **client-credentials** token is not
accepted there: `ExternalAccessValidator` requires the claims `prop:SysAdminUnitId` and
`prop:ResourceId` plus a matching `ExternalAccess` row. That finding was right, and it also named the
one token the endpoint does accept — the external-access token that `work.creatio.com` mints for a
support grant. Support engineers already use it daily through `Actions → Show access list`.

The rejection was therefore about clio's own token, not about the endpoint. With the right token the
path works, and it is the only way to reach a customer site for which no login and password exist.

## Decision

clio accepts an externally minted external-access token (`--external-access-token`), exchanges it
once for a Creatio session at `<base>/ServiceModel/AuthService.svc/OAuthTokenLogin`, imports the
resulting cookies into a credential-less `CreatioClient`, and runs every command on that session.

clio does **not** mint the token. Minting needs a `work.creatio.com` session, `work.creatio.com` is
reachable only to Creatio employees and only through SSO, and clio ships outside the company. The
mint half lives in an `ai-instructions` skill that drives the browser and writes the
`ExternalAccessRequestLog` audit row.

## Alternatives considered

| Alternative | Verdict |
|---|---|
| Reuse the existing `AccessToken` bearer branch | Rejected. It attaches `Authorization: Bearer` to every request, and `OAuthAuthorizationHelper` on the customer site requires an `OAuthClientApp` row for the token's `client_id`. The external-access token has none, so every API call would fail. |
| Cache the exchanged session per environment | Rejected for v1. The grant window (`ExternalAccess.DueDate`, day-granularity) and the cookie lifetime are independent, so a cached cookie that passes an expiry check can belong to a session the server already dropped. Additive later if a measured token lifetime justifies it. |
| Full cycle inside clio (list grants + mint + exchange) | Rejected. See the decision above: the mint half is employee-only and SSO-bound. |
| Register the route in `ServiceUrlBuilder.KnownRoutes` | Rejected. `Build` prepends `0/` on .NET Framework; this endpoint is served from the site root on both hosts, like `AuthService.svc/Login`. It is the prefix exception AGENTS.md documents. |

## Consequences

- An external-access session cannot be renewed. `NoReauthExecutor` is wired so an expired session
  reports that fact and asks for a fresh token, instead of attempting a forms login with credentials
  that do not exist.
- ATF-backed commands are unavailable under external access: `ATF.Repository.RemoteDataProvider` has
  no session-cookie constructor. They fail closed with that reason named.
- The token never reaches disk. It carries the same secret discipline as `AccessToken`.
- A restricted grant (`isDataIsolationEnabled` / `isSystemOperationsRestricted`) yields a session
  whose failures look like ordinary permission errors. clio cannot see those flags — the minting
  skill reports them.
