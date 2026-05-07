# idp-set-secret

Set the client secret for an identity provider.

## Usage

```bash
clio idp-set-secret (--id <VALUE> | --name <VALUE>) --client-secret <VALUE> [options]
```

## Options

```bash
--id <VALUE>              Identity provider ID.
--name <VALUE>            Identity provider name.
--client-secret <VALUE>   OAuth client secret. Required.
--timeout <NUMBER>        Request timeout in milliseconds. Default: 100000.
-e, --Environment         Environment name.
```

## Examples

```bash
clio idp-set-secret --name MainIdP --client-secret "$IDP_SECRET" -e dev
clio idp-set-secret --id 00000000-0000-0000-0000-000000000001 --client-secret "$IDP_SECRET" -e dev
```

- [Clio Command Reference](../../Commands.md#idp-set-secret)
