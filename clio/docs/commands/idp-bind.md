# idp-bind

Bind an identity provider to an external service or feature code.

## Usage

```bash
clio idp-bind (--provider-id <VALUE> | --provider-name <VALUE>) --service-code <VALUE> [options]
```

## Options

```bash
--provider-id <VALUE>     Identity provider ID.
--provider-name <VALUE>   Identity provider name.
--service-code <VALUE>    External service or feature code. Required.
--create-service          Create the external service when missing. Default: false.
--timeout <NUMBER>        Request timeout in milliseconds. Default: 100000.
-e, --Environment         Environment name.
```

## Examples

```bash
clio idp-bind --provider-name MainIdP --service-code GlobalSearch -e dev
clio idp-bind --provider-id 00000000-0000-0000-0000-000000000001 --service-code GlobalSearch --create-service -e dev
```

- [Clio Command Reference](../../Commands.md#idp-bind)
