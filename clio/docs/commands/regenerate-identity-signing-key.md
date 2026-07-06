# regenerate-identity-signing-key

## Command Type

    Identity commands

## Name

regenerate-identity-signing-key - regenerate the instance identity-assertion signing key pair

## Aliases

identity-regenerate-key

## Description

The regenerate-identity-signing-key command triggers a server-side regeneration of the instance
identity-assertion signing key pair via the Creatio endpoint `identityAssertion/regenerateSigningKey`.
The private key is created and stored securely inside Creatio and never leaves the instance.

This operation is destructive: assertions signed with the previous key stop validating, and the
new public key must be re-registered with Identity Service V3. After regenerating, run
`get-identity-public-jwk` to export the new public key.

Requires the `EnableIdentityAssertionIssuer` feature to be enabled and the
`CanManageIdentityAssertionIssuer` operation permission for the current user.

## Synopsis

```bash
regenerate-identity-signing-key [options]
```

## Options

```bash
--format                            Output format: text (OK, default) or json (status object)
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
clio regenerate-identity-signing-key -e development
Regenerate the signing key pair on the development environment

clio identity-regenerate-key -e development --format json
Regenerate the key and print a JSON status object
```

## Output

    0       Signing key regenerated successfully
    1       Signing key could not be regenerated or an error occurred

## Prerequisites

- Valid Creatio environment with accessible web services
- The `EnableIdentityAssertionIssuer` feature enabled on the environment
- The `CanManageIdentityAssertionIssuer` operation permission

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#regenerate-identity-signing-key)
- [Identity assertion & token exchange guide](../identity-assertion-token-exchange.md)
