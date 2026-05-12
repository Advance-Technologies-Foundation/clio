# idp-services

List external services and their identity provider bindings.

## Usage

```bash
clio idp-services [options]
```

## Options

```bash
--json                 Output as indented JSON instead of a table.
--timeout <NUMBER>     Request timeout in milliseconds. Default: 100000.
-e, --Environment      Environment name.
```

## Examples

```bash
clio idp-services -e dev
clio idp-services --json -e dev
```

- [Clio Command Reference](../../Commands.md#idp-services)
