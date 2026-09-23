# Connecting through a support access grant (`--external-access-token`)

Some Creatio sites can only be reached through a **support access grant**: the customer grants
temporary access from their site, and a support engineer opens it from `Actions → Show access list`
on the Creatio support portal. No login and password are ever typed, and for most such sites none
exist for the engineer at all.

`--external-access-token` lets clio connect to exactly those sites.

## What clio does, and what it does not

clio performs **one** step of that mechanism: it takes a token that was already minted for a grant
and exchanges it for a Creatio session.

```
POST <site>/ServiceModel/AuthService.svc/OAuthTokenLogin
Authorization: Bearer <token>
```

clio does **not** list the grants and does **not** mint the token. Both require a session on the
support portal, which is reachable only to Creatio employees and only through SSO. Minting is the
job of the tooling that drives that portal; it hands clio the token.

## Usage

The option works on any command that takes an environment:

```bash
clio get-info -u https://customer.creatio.com --external-access-token <token>
```

```bash
clio push-pkg ./MyPackage.gz -u https://customer.creatio.com --external-access-token <token>
```

**The token is valid for 120 seconds**, measured rather than estimated — the grant can run for days,
but the token minted for it expires two minutes after it is issued. clio therefore caches the session
the token buys: the first command exchanges the token, and every later command reuses that session
for as long as the site keeps it. Keep passing the same token; it does not have to still be valid.

clio validates the cached session with a real request before reusing it, so a grant that was revoked,
or a session the site dropped, sends it back to the token — and only then, if the token has expired
too, does the command fail and ask for a fresh one.

The token is **never stored**. It is not written to `appsettings.json`, does not appear in
`clio show-web-app-list` output, and has to be supplied on every invocation. Register the
environment url with `reg-web-app` if you want a short `-e <name>`, and pass the token alongside it.

The cached session **is** stored, as an owner-only file under `<clio home>/sessions/`, one per
(environment, grant). It holds live session cookies, so treat it as a credential; deleting the file
simply costs one more exchange.

Every run names the grant it is working under, so a later permission failure is attributable:

```
[INF] - External access grant 62821f5d-… on https://customer.creatio.com - no restrictions, valid until 2026-09-22.
```

## Behavior and limits

| | |
|---|---|
| **The session cannot be renewed.** | clio has no way to mint a replacement token, so when the session ends the command fails and names external access as the reason, instead of silently trying a login with credentials that do not exist. Request a fresh token and run the command again. |
| **It cannot be combined with `--access-token` or an OAuth client (`--clientId`/`--clientSecret`).** | They are different authentication models against different identities. clio refuses the combination on every dispatch path rather than picking one. A registered environment that stores a `--clientId` counts here too: name the identity you mean, or register the environment without one. |
| **Commands that read through ATF.Repository are unavailable.** | That library accepts a login and password, an OAuth client, or an API token — but not session cookies. Such a command fails with that reason named. |
| **A restricted grant produces permission errors.** | A grant issued with data access or configuration access denied yields a session that the site restricts accordingly. clio reads both flags out of the token and prints them at the start of every run. |

## Common failures

| Message | What it means |
|---|---|
| `External access to '<url>' was refused: External access ... Access is inactive` | The grant was deactivated on the customer site. |
| `External access to '<url>' was refused: External access ... Access was expired on ...` | The grant window has passed. Ask for a new grant. |
| `External access to '<url>' was refused: HTTP 200 ... must have three segments` | The value passed is not a JWT — usually a truncated copy or the wrong field. |
| `... issued no authentication cookie` | The site accepted the token but started no session; the grant does not match this site. |

## Reporting bugs

https://github.com/Advance-Technologies-Foundation/clio
