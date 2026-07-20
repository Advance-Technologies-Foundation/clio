# install-knowledge

## Command Type

    Integrations & tools

## Name

install-knowledge - Install verified knowledge from one source or all enabled sources

## Synopsis

```bash
clio install-knowledge [--source <alias>] [--json]
```

## Description

Retrieves, verifies, and atomically installs the current generation for every enabled source. Pass
`--source` to select exactly one configured alias instead. A source failure cannot replace that
source's last-known-good generation and does not withdraw other installed libraries.

NuGet sources download a bounded stable package and verify its signed version 1 bundle. Git sources
clone or fast-forward their configured public repository, resolve the configured commit, tag, or
branch, and validate the catalog contract directly from that checkout without executing repository
code. Installed content is kept under `knowledge.root-path` in Clio's visible `appsettings.json`.

For NuGet, provenance is the verified package version, bundle signature, and digest. For Git,
provenance is the configured trusted repository URL and the exact resolved commit after catalog
validation; Git sources do not use NuGet bundle-signing keys. Public HTTPS Git repositories and
NuGet feeds are the supported transports in this proof of concept; authenticated private sources
are not supported. If a Git source has no configured reference, only a successful install persists
the discovered remote default branch and its exact resolved commit.

The command is idempotent for valid active generations. It does not implicitly enable disabled
sources; use `enable-knowledge-source` first.

## Options

```bash
--source <alias>   Install only this configured source; omit for all enabled sources
--json             Emit the per-source result as indented JSON
```

## Examples

```bash
clio install-knowledge
clio install-knowledge --source creatio
clio install-knowledge --source partner --json
```

## Exit Codes

    0   Every selected source is installed or already current
    1   Selection, retrieval, verification, locking, or filesystem failure

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#install-knowledge)
