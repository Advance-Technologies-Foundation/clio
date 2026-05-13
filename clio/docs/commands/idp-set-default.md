# idp-set-default

Set the default identity provider in Creatio.

## Usage

```bash
clio idp-set-default (--id <VALUE> | --name <VALUE>) [options]
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
clio idp-set-default --name MainIdP -e dev
clio idp-set-default --id 00000000-0000-0000-0000-000000000001 -e dev
```

- [Clio Command Reference](../../Commands.md#idp-set-default)
