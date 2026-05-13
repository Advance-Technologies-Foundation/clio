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
`https://academy.creatio.com/api/component-registry/` and can be overridden
with the `CLIO_COMPONENT_REGISTRY_CDN_BASE_URL` environment variable for
dev or staging.

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
                                   Omit to refresh latest.json.

--all                              Refresh every version currently present
                                   in the local cache directory.
```

## Examples

```bash
# Refresh the latest.json alias (the most common case).
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
