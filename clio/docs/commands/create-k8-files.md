# create-k8-files

Prepare K8 files for deployment.

## Usage

```bash
clio create-k8-files [options]
```

## Description

Prepare K8 files for deployment.

## Aliases

`ck8f`

## Examples

```bash
clio create-k8-files [options]
```

## Options

```bash
-p, --path <VALUE>
    Path to infrastructure files (default: auto-detected from clio settings)
--pg-limit-memory <VALUE>
    PostgreSQL memory limit (default: 4Gi). Default: 4Gi.
--pg-limit-cpu <VALUE>
    PostgreSQL CPU limit (default: 2). Default: 2.
--pg-request-memory <VALUE>
    PostgreSQL memory request (default: 2Gi). Default: 2Gi.
--pg-request-cpu <VALUE>
    PostgreSQL CPU request (default: 1). Default: 1.
--mssql-limit-memory <VALUE>
    MSSQL memory limit (default: 4Gi). Default: 4Gi.
--mssql-limit-cpu <VALUE>
    MSSQL CPU limit (default: 2). Default: 2.
--mssql-request-memory <VALUE>
    MSSQL memory request (default: 2Gi). Default: 2Gi.
--mssql-request-cpu <VALUE>
    MSSQL CPU request (default: 1). Default: 1.
```

- [Clio Command Reference](../../Commands.md#create-k8-files)
