# idp-upsert

Create or update an identity provider. The command never prints the client secret.

## Usage

```bash
clio idp-upsert --name <VALUE> --server-url <VALUE> --client-id <VALUE> [options]
```

## Options

```bash
--id <VALUE>              Identity provider ID to update.
--name <VALUE>            Identity provider name. Required.
--description <VALUE>     Identity provider description.
--server-url <VALUE>      Identity provider server URL. Required.
--client-id <VALUE>       OAuth client ID. Required.
--client-secret <VALUE>   OAuth client secret.
--timeout <NUMBER>        Request timeout in milliseconds. Default: 100000.
-e, --Environment         Environment name.
```

## Examples

```bash
clio idp-upsert --name MainIdP --server-url https://idp.example.com --client-id clio -e dev
clio idp-upsert --id 00000000-0000-0000-0000-000000000001 --name MainIdP --server-url https://idp.example.com --client-id clio --description "Main IdP" -e dev
```

- [Clio Command Reference](../../Commands.md#idp-upsert)
