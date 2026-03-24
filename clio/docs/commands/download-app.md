# download-app

Alias for downloading an application package from an environment.

## Synopsis

```bash
clio download-app <APP_NAME> [OPTIONS]
```

## Description

`download-app` is an alias of `download-application`. It exports an application package
from the selected environment to a local file.

## Common options

- `<APP_NAME>` - Application name to download
- `-e`, `--Environment` - Source environment
- `-f`, `--FilePath` - Output file path

## Examples

```bash
clio download-app MyApplication -e dev
clio download-app MyApplication -e dev -f ./MyApplication.gz
```

## See also

- [Commands.md](../../Commands.md#download-app)
