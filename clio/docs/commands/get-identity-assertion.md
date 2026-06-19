# get-identity-assertion

## Command Type

    Identity commands

## Name

get-identity-assertion - issue a short-lived signed identity assertion (JWT) for the current user

## Aliases

identity-assertion

## Description

The get-identity-assertion command requests a short-lived, signed identity assertion (JWT)
for the currently authorized user from the Creatio endpoint `identityAssertion/currentUser`.
This assertion is the token the Creatio frontend passes to the AI chat to start the Identity
Service V3 token-exchange flow on the user's behalf. The assertion is signed with the instance
private key, which never leaves Creatio.

Requires the `EnableIdentityAssertionIssuer` feature to be enabled on the environment and an
authenticated user with a configured email.

## Synopsis

```bash
get-identity-assertion [options]
```

## Options

```bash
--format                            Output format: text (plain assertion token, default) or json (full payload)
Default: text

--timeout                           Request timeout in milliseconds
Default: 100000

--uri                   -u          Application uri

--Password              -p          User password

--Login                 -l          User login (administrator permission required)

--Environment           -e          Environment name

--Maintainer            -m          Maintainer name

--clientId                          OAuth client id

--clientSecret                      OAuth client secret

--authAppUri                        OAuth app URI
```

## Examples

```bash
clio get-identity-assertion -e development
Print the assertion token (plain text) for the development environment

clio identity-assertion -e development --format json
Print the full assertion payload (assertion, expiresIn, expiresAt, issuer, audience)
```

## Output

    0       Assertion issued successfully
    1       Assertion could not be issued or an error occurred

## Prerequisites

- Valid Creatio environment with accessible web services
- The `EnableIdentityAssertionIssuer` feature enabled on the environment
- An authenticated user with a configured email

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#get-identity-assertion)
- [Identity assertion & token exchange guide](../identity-assertion-token-exchange.md)
