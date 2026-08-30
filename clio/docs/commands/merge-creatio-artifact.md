# merge-creatio-artifact

## Name

merge-creatio-artifact - Semantically merge three versions of a supported Creatio artifact

## Description

Reads explicit Git stage files and prints a semantic three-way merge result as JSON. The command does not inspect a repository or write the merged artifact.

The CLI command and resident MCP tool are available by default.

Supported artifact families are EntitySchema, ClientUnit, ServiceSchema, supported Addon metadata, non-process descriptor, properties, resource XML, data binding, and supported ClientUnit JavaScript. ProcessSchema metadata, descriptors, and resources, plus C# and SQL artifacts, are recognized and return `not-implemented`.

Combined inline content and serialized output are limited to 4 MiB. Flat metadata is limited to 2,500 operations per stage; larger inputs return `invalid-input` before semantic transpilation.

## Synopsis

```bash
clio merge-creatio-artifact --artifact-path <PATH> --base-file <FILE> \
  --ours-file <FILE> --theirs-file <FILE> [--descriptor-file <FILE>]
```

## Options

- `--artifact-path <PATH>`: repository-relative artifact path used to classify the artifact.
- `--base-file <FILE>`: file containing the common-base content (Git stage 1).
- `--ours-file <FILE>`: file containing the current-branch content (Git stage 2).
- `--theirs-file <FILE>`: file containing the incoming-branch content (Git stage 3).
- `--descriptor-file <FILE>`: resolved sibling descriptor file. Required for metadata and data-binding artifacts.

## Result and exit codes

The JSON result contains `status`, `artifact-kind`, `resolver-version`, `content`, `report`, and `diagnostics`. For a recognized EntitySchema column type conflict, `diagnostics` includes a human-readable question such as `Which type should UsrColumn keep: Number or Date/Time?`. Ask the user before selecting either marker side. Only a `resolved` result returns exit code `0`. `conflicts-remain`, `not-implemented`, `unsupported`, `invalid-input`, and transient `busy` return exit code `1` so automation fails closed. Retry `busy` with the same input.

## Example

```bash
clio merge-creatio-artifact \
  --artifact-path packages/MyApp/Schemas/UsrOrder/metadata.json \
  --base-file .merge/base.json \
  --ours-file .merge/ours.json \
  --theirs-file .merge/theirs.json \
  --descriptor-file .merge/descriptor.json
```

The caller may obtain these files with Git and inspect the JSON result. Apply `content` directly only when `status` is `resolved`. When `status` is `conflicts-remain`, ask any question returned in `diagnostics`, retain only the user-selected marker side, verify the result is marker-free, and then stage it. Clio deliberately does not run Git or modify the working tree.

## Reporting Bugs

https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#merge-creatio-artifact)
