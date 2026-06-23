# get-identity-public-jwk

## Command Type

    Identity commands

## Name

get-identity-public-jwk - get the instance public key (JWK) for Identity Service V3

## Aliases

identity-public-jwk

## Description

The get-identity-public-jwk command reads the instance public key from the Creatio endpoint
`identityAssertion/publicJwk`. The public key (JWK) is registered once with Identity Service V3
at onboarding so V3 can verify the identity assertions signed by this instance. The matching
private key never leaves Creatio.

Requires the `EnableIdentityAssertionIssuer` feature to be enabled and the
`CanManageIdentityAssertionIssuer` operation permission for the current user.

## Synopsis

```bash
get-identity-public-jwk [options]
```

## Options

```bash
--format                            Output format: text (compact single-line JWK, default) or json (indented JWK)
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
clio get-identity-public-jwk -e development
Print the public JWK as a compact single line (ready to paste into Identity Service V3)

clio identity-public-jwk -e development --format json
Print the public JWK indented
```

## Output

    0       Public JWK retrieved successfully
    1       Public JWK could not be retrieved or an error occurred

## Prerequisites

- Valid Creatio environment with accessible web services
- The `EnableIdentityAssertionIssuer` feature enabled on the environment
- The `CanManageIdentityAssertionIssuer` operation permission

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#get-identity-public-jwk)
- [Identity assertion & token exchange guide](../identity-assertion-token-exchange.md)
