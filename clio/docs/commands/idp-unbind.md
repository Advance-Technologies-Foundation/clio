# idp-unbind

Unbind an identity provider from an external service or feature code.

## Usage

```bash
clio idp-unbind --service-code <VALUE> [options]
```

## Options

```bash
--service-code <VALUE>   External service or feature code. Required.
--timeout <NUMBER>       Request timeout in milliseconds. Default: 100000.
-e, --Environment        Environment name.
```

## Examples

```bash
clio idp-unbind --service-code GlobalSearch -e dev
```

- [Clio Command Reference](../../Commands.md#idp-unbind)
