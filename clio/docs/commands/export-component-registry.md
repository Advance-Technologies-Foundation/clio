# export-component-registry

## Command Type

    Development commands

## Name

export-component-registry - Write the full Freedom UI component registry for a resolved platform version to a file

**Aliases:** `export-registry`

## Description

The `export-component-registry` command writes the FULL Freedom UI component registry for a resolved
platform version to a file, **byte-faithful** to the source registry payload — no re-serialization through a
typed model. This matters: the registry's typed C# model (`ComponentPropertyDefinition`) does not map every
producer field (for example `deprecated`/`deprecationReason` on an `inputs` entry exist only as raw JSON), so
re-serializing through it would silently drop exactly the fields a consumer validating the catalog needs.

It exists to replace dozens of per-component [`get-component-info`](get-component-info.md) round-trips when a
caller needs to validate many `crt.*` componentTypes and `propMap` keys at once against a target platform
version — for example a classic→Freedom migration engine running in CI, where no live Creatio stand exists to
query directly. `IComponentRegistryClient` already downloads and caches the whole per-version registry
internally; this command is the way to hand that payload back as one file.

Version resolution mirrors `get-component-info` exactly:

1. an explicit `--version` wins — authoritative;
2. `--environment`/`--uri` probes cliogate `GetSysInfo` on that environment;
3. neither flag falls back to `latest`, and the response reports `resolvedFrom: "latest-fallback"` with
   `requiresVersionConfirmation: true` — the caller must not silently assume the exported component set matches
   any specific environment.

**Documentation bodies are never fetched.** A component entry's `references.docs[]` array carries *paths* to
markdown files; this command writes those paths as-is (they are already part of the registry payload) but never
calls the docs client to fetch their content — that would cost ~150-190 HTTP round-trips and 1.2-1.5 MB, none of
which is needed to validate `componentType`/`propMap` keys.

The registry content itself is written to the file, **never returned** in the command's own output — the
response carries only the absolute output path, the version-resolution fields, and small structural counters.

## Synopsis

```bash
clio export-component-registry [options]
```

## Options

```bash
--version                          Explicit catalog version to export (3-part semver,
                                   e.g. 8.3.4). Mutually exclusive with
                                   --environment/--uri. Default: latest

--schema-type                      Component registry to export: 'web' (default) or
                                   'mobile'

--output-file                      Destination file path. An explicit path must resolve
                                   inside the workspace or the OS temp directory (symlinks
                                   resolved); a path outside both, or one that already
                                   exists, is rejected. Omit for the default (re-runnable):
                                   <workspace-root>/.clio-migration/component-registry/<version>.json

--uri                    -u       Application uri

--Password               -p       User password

--Login                  -l       User login (administrator permission required)

--Environment            -e       Environment name
```

## Example

```bash
clio export-component-registry -e dev
# Probe dev's platform version, write its web registry to
# <workspace-root>/.clio-migration/component-registry/<version>.json

clio export-component-registry --version 8.3.4 --schema-type mobile --output-file ./mobile-registry.json
# Export the mobile registry for an explicit version to a chosen path
```

## Output format

The response JSON reports `success`, `outputFile` (absolute path), `resolvedTargetVersion`, `resolvedFrom`
(`environment` | `environment-superset` | `latest-fallback`), `resolvedFromReason` (present only on
`latest-fallback`), `requiresVersionConfirmation`, `schemaTypeWarning` (present only for an unrecognized
`schema-type` value), `componentCount`, `compositeCount`, `inputCount`, and `error`. None of these fields carry
registry content — the file at `outputFile` is the only place the registry data lives.

## Notes

- Read-only against the Creatio environment: the only write this command performs is the local output file. An
  `--environment`/`--uri` call only probes `GetSysInfo` (via cliogate) to resolve the platform version — it does
  not read or write any Creatio data.
- `--output-file` is confined to the workspace anchor or the OS temp directory, exactly like
  [`get-classic-page-sources`](get-classic-page-sources.md): the command is MCP-callable, so the output path can
  be supplied by an agent rather than typed at a shell. Symlinks are resolved before the check, an anchor that is
  a filesystem root or an ancestor of `$HOME` is not trusted, and an explicit `--output-file` that already exists
  is refused rather than overwritten. A path escaping both allowed locations fails before any write.
- The default path (`<workspace-root>/.clio-migration/component-registry/<version>.json`) is a **different**
  contract from an explicit `--output-file`: it is tool-owned and re-runnable, so a second run at the default
  path overwrites the first. An explicit `--output-file` never overwrites an existing target.
- `--schema-type mobile` sources from the separate mobile component registry (same transport/cache chain, a
  distinct CDN file and cache subdirectory) — see the "Mobile flavor" section in `get-component-info`'s
  implementation notes.
- Over MCP the error text is redacted the same way `get-component-info`/`get-classic-page-sources` redact
  theirs, so a backend host or URI carried in an underlying failure never reaches the caller's context.

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#export-component-registry)
