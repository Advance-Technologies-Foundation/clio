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
2. `--environment <name>` or `--uri ...` — probe the environment for its
   core version to pick the matching catalog. The probe uses the standard
   `ApplicationInfoService` (no cliogate required — an authenticated session
   is enough) and falls back to the cliogate `GetSysInfo` endpoint only if
   that yields no version. This means version-accurate results work on
   environments without cliogate installed.
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

On a **detail** response, a collection/visual component type
(`crt.DataGrid`, `crt.List`, `crt.FileList`, `crt.MultiList`,
`crt.ImageInput`) also carries a `relatedComponents` array — a curated
"see also" pointing at an overlooked better fit such as `crt.Gallery` for
image/card collections. Every detail response additionally carries a
`discoveryTip` string nudging you to list the full catalog before settling
on a layout from memory. Under `--pretty` these render under
`relatedComponents:` and `tip:` sections.

A detail response also carries Solution A **selection metadata** (ENG-91571)
when the producer has populated it: `synonyms` (alternate names a user might
use), `useCases` (concrete scenarios the component fits), one-line `whenToUse`
/ `whenNotToUse` guidance, a `category` from the controlled taxonomy, and an
`appliesToCustomEntities` applicability flag with an `entityCouplingNote`
(e.g. `crt.CommunicationOptions` is bound to the Contact/Account model and
cannot be built on a custom entity). Use these to match a natural-language
request to the right component; list-mode `--search` also matches across them.

The web-catalog response carries `resolvedTargetVersion` and `resolvedFrom`
markers (`"environment"` | `"latest-fallback"`) so consumers can tell when
the catalog actually matched the requested target version and when it fell
back.

When `resolvedFrom` is `"latest-fallback"` the response also carries a
`versionWarning` string. `latest` is a superset of every GA version, so a
component listed under fallback (for example a freshly shipped `crt.Switch`)
may not exist in the target environment's actual platform version and a page
built against it can fail to render at runtime. Pass `--version` or
`--environment` (the MCP tool accepts `environment-name`) to scope the
catalog to a real version; the warning is omitted once `resolvedFrom` is
`"environment"`. Under `--pretty` the warning renders on a `WARNING:` line
beneath the header.

The MCP `get-component-info` tool mirrors this resolution 1:1 and accepts the
same per-call selectors — `environment-name` (preferred), `version`, or
`uri`/`login`/`password` as an emergency fallback. Prefer passing the same
`environment-name` you edit pages on so the catalog matches that
environment's real component set.

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
