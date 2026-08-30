# show-package-file-content

## Name

show-package-file-content - List or read compiled package files

## Description

Lists files materialized in a compiled package's `Files` directory, or reads one file by its
package-relative path. In non-FSM development this includes the generated `<PACKAGE_NAME>.csproj`
file created when the package is compiled. Individual text reads are limited to 10 MiB and listings
traverse at most 10,000 filesystem entries so the privileged endpoint remains bounded.

## Requirements

This command requires cliogate `2.0.0.47` or newer on the target Creatio environment. Install or
update it with:

```bash
clio install-gate -e <ENVIRONMENT_NAME>
```

## Synopsis

```bash
clio show-package-file-content --package <PACKAGE_NAME> [--file <RELATIVE_PATH>] -e <ENVIRONMENT_NAME>
```

## Options

| Option | Required | Description |
|---|---:|---|
| `--package <PACKAGE_NAME>` | Yes | Creatio package name. |
| `--file <RELATIVE_PATH>` | No | Package-relative file path. Omit to list files. |
| `-e, --environment <ENVIRONMENT_NAME>` | Yes for a registered target | Registered clio environment. |
| `--timeout <MILLISECONDS>` | No | Request timeout in milliseconds. |

## Examples

List the files materialized for a package:

```bash
clio show-package-file-content --package UsrCode -e dev
```

Read the generated non-FSM project:

```bash
clio show-package-file-content --package UsrCode --file UsrCode.csproj -e dev
```

Read one package source file:

```bash
clio show-package-file-content --package UsrCode --file src/cs/Probe.cs -e dev
```

## MCP tools

- `list-package-files` returns normalized package-relative paths.
- `get-package-file` returns exact requested file content together with the generated
  `<PACKAGE_NAME>.csproj` content. If the requested file exists but the generated project is not
  materialized, the primary read still succeeds and `project-error` explains why project content is
  absent.

Both tools are read-only, non-destructive, idempotent, and environment-sensitive. Prefer
`environment-name`; direct `uri`/`login`/`password` arguments are only for bootstrap or emergency
fallback flows.

## See Also

- `pull-pkg` - Download package files locally.
- `install-gate` - Install or update the required cliogate package.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#show-package-file-content)
