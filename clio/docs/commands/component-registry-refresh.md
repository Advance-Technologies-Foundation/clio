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

Every targeted version is refreshed for the web (`ComponentRegistry.json`, cache
root), mobile (`MobileComponentRegistry.json`, `mobile/` subdirectory), requests
(`RequestRegistry.json`, `requests/` subdirectory, the Freedom UI request catalog
consumed by `get-request-info`), and mobile-requests (`MobileRequestRegistry.json`,
`mobile-requests/` subdirectory, the mobile Freedom UI request catalog consumed by
`get-request-info schema-type=mobile`) flavors.

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

Two payload shapes are accepted interchangeably:

- legacy CDN form — a top-level array `[ { "componentType": ..., ... }, ... ]`;
- wrapped form — an object `{ "components": [ { ... }, ... ] }`.

Either way, the inner items must be `ComponentRegistryEntry` objects (the
`{version}.meta.json` sidecar format used inside `~/.clio/cache/` is **not**
supported). The override is **fail-fast**: a non-empty
`CLIO_COMPONENT_REGISTRY_LOCAL_FILE` that points at a missing or unreadable
path raises `FileNotFoundException` rather than silently falling through to
the CDN — otherwise a typo in the env var would let stale CDN data masquerade
as your in-progress payload. Unset the variable to use the normal CDN/cache
chain. `component-registry-refresh` itself ignores the override and always
pulls fresh bytes from the CDN.

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
