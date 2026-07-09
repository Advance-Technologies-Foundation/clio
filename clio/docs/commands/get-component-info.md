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
  filtered with `--search`. The list response also carries a `composites`
  array (see below) and flags composite-only components with `compositeOnly`.
- **Detail mode**: full metadata for a specific component type passed as the
  first positional argument.
- **Composite mode** (`--composite <caption>`): assembly docs for a composite
  Designer element. Mutually exclusive with the positional component-type.

### Composites and `compositeOnly`

Some Designer elements are not standalone components but pre-built combinations
of several components, so they have no `componentType` of their own. They are
surfaced in a top-level `composites` array (`{ caption, description, docs }`) in
list mode — read that array to discover which composites exist for the resolved
version — and fetched by caption with `--composite "<caption>"`, which returns a `mode:"composite"`
response carrying the composite's assembly docs. A `--composite` lookup whose
docs all fail to load sets `documentationUnavailable: true` so a transient docs
fetch failure is distinguishable from a composite that genuinely ships no docs.

A component with no standalone Designer toolbar presence carries
`compositeOnly: true` (with `compositeOnlyHint` on detail). Composites carry no
machine-readable list of their member components, so the hint encodes a decision
rule rather than naming the owning composite: discover composites in list mode,
confirm membership by reading each candidate's recipe (`--composite "<caption>"`),
build the composite when one assembles this component, and otherwise build the
component directly as a fallback — only when the component's own applicability
(`appliesToCustomEntities` / `entityCouplingNote`) allows it. This CLI verb and the
MCP tool stay in lockstep on composites; both share the same catalog and response
builders.

When a positional `component-type` is not a known component it is resolved by
name and description rather than dead-ending: it first matches components (by
type, description, synonyms, use-cases); if none match it matches composites by
caption/description; only when neither matches does it fall back to the
closest-type shortlist. So passing a composite's caption as the component-type
(e.g. `clio get-component-info "Expanded list"`) returns a not-found response
that routes you to `--composite "Expanded list"` for the assembly recipe — an
agent reaching for the human label still finds the composite instead of
hand-building it. The closest-type shortlist is capped at 8 entries.

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

Detail responses additionally carry the producer's **selection-metadata**
when it is published for the component:

- `whenToUse` / `whenNotToUse` — one-line "pick this when…" / "do NOT pick
  this when…" guidance. Use them to choose between visually similar
  components that look alike but differ in behavior.
- `synonyms` / `useCases` — alternate names and concrete scenarios. These are
  also folded into `--search` matching, so an informal term like `table`
  surfaces `crt.DataGrid`.
- `appliesToCustomEntities` — `false` marks an entity-coupled component that
  cannot be built on a custom entity; `entityCouplingNote` explains why.

Each field is omitted when the producer published none; under `--pretty` they
render directly beneath the `description:` line.

The web-catalog response carries `resolvedTargetVersion` and `resolvedFrom`
markers (`"environment"` | `"environment-superset"` | `"latest-fallback"`) so
consumers can tell when the catalog actually matched the requested target
version (`environment`), when the version was known but its exact catalog was
unavailable so `latest` stood in (`environment-superset`, a soft caveat), and
when the version could not be determined at all (`latest-fallback`, the hard
stop).

When `resolvedFrom` is `"latest-fallback"` the response also carries a
`versionWarning` string. `latest` is a superset of every GA version, so a
component listed under fallback (for example a freshly shipped `crt.Switch`)
may not exist in the target environment's actual platform version and a page
built against it can fail to render at runtime. Pass `--version` or
`--environment` (the MCP tool accepts `environment-name`) to scope the
catalog to a real version; the warning is omitted once `resolvedFrom` is
`"environment"`. Under `--pretty` the warning renders on a `WARNING:` line
beneath the header; on `latest-fallback` the machine-readable markers below are
appended to that same line
(`[requiresVersionConfirmation=true; resolvedFromReason=...]`) so the human view
reaches parity with the JSON consumers.

Alongside the prose `versionWarning`, a `latest-fallback` response also sets
two machine-readable markers so a consumer can branch programmatically instead
of parsing text:

- `requiresVersionConfirmation: true` — the hard stop. The version is unknown,
  so the consumer must tell the user and request explicit confirmation before
  proceeding against `latest`; it is emitted only on `latest-fallback` and
  omitted on `"environment"` / `"environment-superset"` (both have a known
  version).
- `resolvedFromReason` — a kebab-case classification of why the version could
  not be determined: `probe-error` (transient — a retry or a reachable
  environment may resolve it), or the stable `no-active-environment` /
  `core-version-missing` / `core-version-unparseable`. Use it to decide whether
  a retry is worthwhile or a clearer input (an explicit `--version`) is needed.

The MCP `get-component-info` tool mirrors this resolution 1:1 and accepts the
same per-call selectors — `environment-name` (preferred), `version`, or
`uri`/`login`/`password` as an emergency fallback. Prefer passing the same
`environment-name` you edit pages on so the catalog matches that
environment's real component set.

## Synopsis

```bash
get-component-info [<component-type>] [--composite <caption>] [--search <keyword>] [--version <semver>] [--environment <name>] [--schema-type <web|mobile>] [--pretty]
```

## Aliases

component-info

## Options

```bash
<component-type>                   First positional. Freedom UI component
                                   type (e.g. crt.TabContainer). Omit or
                                   pass 'list' to return the grouped catalog.

--composite                        Composite Designer-element caption (e.g.
                                   'Expanded list'). Returns the composite's
                                   assembly docs. Mutually exclusive with the
                                   positional component-type.

--search                           Keyword filter applied in list mode (filters
                                   components AND composites) and in not-found
                                   suggestions.

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

# Composite Designer element (assembly docs)
clio get-component-info --composite "Expanded list"

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
