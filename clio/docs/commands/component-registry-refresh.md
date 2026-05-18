# component-registry-refresh

## Command Type

    General

## Name

component-registry-refresh - Refresh the local Freedom UI component
registry cache from the academy.creatio.com CDN.

## Description

The component-registry-refresh command force-pulls one or more Freedom UI
component-registry payloads from the academy.creatio.com CDN regardless of
the 24-hour cache TTL applied by the MCP get-component-info tool. This is
useful when a user needs to pick up a newly published platform GA without
waiting for the natural refresh window.

The cache lives at `~/.clio/cache/component-registry/`. Each refresh writes
a fresh `{version}.json` payload and the matching `{version}.meta.json`
sidecar (ETag, Last-Modified, SHA-256). Failed downloads do not poison the
cache — the previous entry stays as-is.

The CDN base URL defaults to
`https://academy.creatio.com/api/mcp/` and can be overridden with the
`CLIO_COMPONENT_REGISTRY_CDN_BASE_URL` environment variable for dev or
staging. Per-version payloads live at
`{base}{version}/ComponentRegistry.json`, with `latest/ComponentRegistry.json`
as the alias for the most recently published GA.

### Local file override (developer workflow)

While iterating on the `ComponentRegistry.json` payload itself, point
`CLIO_COMPONENT_REGISTRY_LOCAL_FILE` at the file you are editing:

```bash
export CLIO_COMPONENT_REGISTRY_LOCAL_FILE=/path/to/my/ComponentRegistry.json
```

When set, every `get-component-info` call reads the file directly and reports
`source=local` — the CDN, the on-disk cache, and the embedded snapshot are all
bypassed. The env variable is read on every call, so edits are visible to a
long-running `clio mcp serve` without restarting it.

The file must follow the same JSON shape as the CDN payload — a top-level
array of `ComponentRegistryEntry` objects, **not** the
`{version}.meta.json` sidecar format used inside `~/.clio/cache/`. A missing
file or invalid path is logged and the normal fallback chain takes over.
`component-registry-refresh` itself ignores the override and always pulls
fresh bytes from the CDN.

## Synopsis

```bash
component-registry-refresh [--version <semver>] [--all]
```

## Aliases

component-registry

## Options

```bash
--version                          Refresh a specific platform version
                                   (3-part SemVer, for example 8.2.1).
                                   Omit to refresh latest/ComponentRegistry.json.

--all                              Refresh every version currently present
                                   in the local cache directory.
```

## Examples

```bash
# Refresh the latest/ComponentRegistry.json alias (the most common case).
clio component-registry-refresh

# Refresh a specific GA file.
clio component-registry-refresh --version 8.2.1

# Refresh every version already present in the local cache.
clio component-registry-refresh --all
```

## Exit codes

| Code | Meaning |
|------|---------|
| 0    | Every requested refresh returned a 2xx response from the CDN. |
| 1    | At least one CDN call failed (5xx, network error, exception, or unavailable endpoint). |
