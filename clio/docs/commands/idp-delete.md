# idp-delete

Delete an identity provider. Creatio rejects deletion when the provider is default or still bound to services.

## Usage

```bash
clio idp-delete (--id <VALUE> | --name <VALUE>) [options]
```

## Options

```bash
--id <VALUE>          Identity provider ID.
--name <VALUE>        Identity provider name.
--timeout <NUMBER>    Request timeout in milliseconds. Default: 100000.
-e, --Environment     Environment name.
```

## Examples

```bash
clio idp-delete --name MainIdP -e dev
clio idp-delete --id 00000000-0000-0000-0000-000000000001 -e dev
```

- [Clio Command Reference](../../Commands.md#idp-delete)
