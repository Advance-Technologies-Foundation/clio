# idp-list

List identity providers configured in Creatio.

## Usage

```bash
clio idp-list [options]
```

## Options

```bash
--json                 Output as indented JSON instead of a table.
--timeout <NUMBER>     Request timeout in milliseconds. Default: 100000.
-e, --Environment      Environment name.
```

## Examples

```bash
clio idp-list -e dev
clio idp-list --json -e dev
```

- [Clio Command Reference](../../Commands.md#idp-list)
