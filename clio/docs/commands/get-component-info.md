# get-component-info

## Command Type

    General

## Name

get-component-info - Get curated Freedom UI component metadata by component
type or list the catalog.

## Description

The get-component-info command exposes the same curated Freedom UI component
catalog that the MCP `get-component-info` tool serves to AI clients, but as
a regular CLI verb. It supports two modes:

- **List mode** (default): grouped catalog summary by category, optionally
  filtered with `--search`.
- **Detail mode**: full metadata for a specific component type passed as the
  first positional argument.

The catalog is loaded through the CDN → file cache → embedded snapshot
fallback chain (see `component-registry-refresh` for cache control). For
local payload iteration, point `CLIO_COMPONENT_REGISTRY_LOCAL_FILE` at a
`ComponentRegistry.json` on disk — it short-circuits every other tier
(`source=local`) and is re-read on every call. The version to load is
chosen by:

1. `--version <semver>` — explicit override (highest priority).
2. `--environment <name>` or `--uri ...` — probe cliogate `GetSysInfo` on
   that environment to pick the matching catalog.
3. Neither — default to `latest`.

`--version` and `--environment` (or `--uri`) are mutually exclusive.

Pass `--schema-type mobile` to query the mobile component registry instead
of the default web one. The mobile catalog ships as static data inside
clio.dll, has no CDN tier, and ignores `--version` / `--environment`; its
responses omit `resolvedTargetVersion` and `resolvedFrom` accordingly.

Output defaults to JSON (identical to the MCP tool's response shape,
including the long-form `documentation` field built from any
`references.docs[]` markdown files) so the result pipes cleanly into `jq`
and other scripting tools. Pass `--pretty` for a human-readable text
rendering on stdout — the docs block surfaces under a `documentation:`
section.

The web-catalog response carries `resolvedTargetVersion` and `resolvedFrom`
markers (`"environment"` | `"latest-fallback"`) so consumers can tell when
the catalog actually matched the requested target version and when it fell
back.

## Synopsis

```bash
get-component-info [<component-type>] [--search <keyword>] [--version <semver>] [--environment <name>] [--schema-type <web|mobile>] [--pretty]
```

## Aliases

component-info

## Options

```bash
<component-type>                   First positional. Freedom UI component
                                   type (e.g. crt.TabContainer). Omit or
                                   pass 'list' to return the grouped catalog.

--search                           Keyword filter applied in list mode and
                                   in not-found suggestions.

--version                          Explicit catalog version to load
                                   (3-part semver, e.g. 8.3.4). Mutually
                                   exclusive with --environment. Default: latest.

-e, --environment                  Registered environment name to probe via
                                   cliogate GetSysInfo for the platform
                                   version.

-u, --uri                          Application URI (alternative to
                                   --environment for ad-hoc probes).

-l, --login                        User login (when probing by --uri).

-p, --password                     User password (when probing by --uri).

--pretty                           Emit a human-readable text block on
                                   stdout instead of JSON.

--schema-type                      Component registry to query: 'web'
                                   (default) or 'mobile'. Mobile ignores
                                   --version/--environment.
```

## Examples

```bash
# Grouped catalog (JSON)
clio get-component-info

# Detail for one component
clio get-component-info crt.TabContainer

# Filtered list
clio get-component-info --search menu

# Explicit version
clio get-component-info crt.Button --version 8.3.4

# Probe the active environment for its platform version
clio get-component-info crt.Input --environment dev

# Pretty text output for humans
clio get-component-info crt.Button --pretty

# Mobile catalog
clio get-component-info --schema-type mobile
clio get-component-info crt.Toggle --schema-type mobile

# Pipe into jq
clio get-component-info | jq '.groups[].items[].componentType'
```

## Exit codes

| Code | Meaning |
|------|---------|
| 0    | List returned successfully, or detail found. |
| 1    | Unknown component type, `--version`/`--environment` conflict, or catalog load failure. |
