# install-knowledge

## Command Type

    Integrations & tools

## Name

install-knowledge - Install the latest verified Clio knowledge package on disk

## Synopsis

```bash
clio install-knowledge
```

## Description

Downloads the highest stable compatible package from the configured NuGet v3 source, verifies the signed
inner knowledge bundle, and atomically publishes it under `knowledge-root-path` from Clio's
`appsettings.json`.

When the setting is absent, Clio creates `<clio-home>/knowledge` and writes that absolute path to
`appsettings.json`. The installed archive, extracted guidance, manifests, and future reference
examples are therefore visible to users and coding agents on disk.

Clio claims only an empty directory by writing `.clio-knowledge-root`. A non-empty directory
without that ownership marker is rejected, so a misconfigured path cannot authorize cleanup of
unrelated `versions`, `staging`, or `examples` directories. Symbolic links and junctions in managed
paths are also rejected.

The command is idempotent for a valid installation. If the active disk materialization is damaged,
the command repairs it from a freshly downloaded verified package. Use `update-knowledge` to check
for a newer compatible version.

## Configuration

- `CLIO_KNOWLEDGE_NUGET_SOURCE` - absolute NuGet v3 service-index URL
- `CLIO_KNOWLEDGE_NUGET_PACKAGE_ID` - package ID
- `CLIO_KNOWLEDGE_TRUSTED_KEY_ID` - expected signing-key ID
- `CLIO_KNOWLEDGE_TRUSTED_PUBLIC_KEY_PATH` - absolute ECDSA P-256 public-key PEM path

## Examples

```bash
clio install-knowledge
clio info-knowledge --offline
```

## Exit Codes

    0   Knowledge is installed, or was already installed
    1   Configuration, download, verification, locking, or filesystem failure

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#install-knowledge)
