# ADR: Ship a local JSON Schema beside ClioRing app settings

Status: accepted

## Context

`actions.json` already has a colocated schema, but `app-settings.json` does not. Users therefore cannot discover supported properties or tell whether `Channel` controls the clio child process.

## Decision

Ship `app-settings.schema.json` beside `app-settings.json`, reference it with a relative `$schema`, and copy both files through the desktop project. Keep the schema descriptive rather than executable: Ring continues to deserialize through its source-generated `System.Text.Json` context and retains its tolerant startup behavior.

`Channel` remains an unrestricted non-empty display label. Development clio selection is expressed through `DevClioPath` or `ClioIpc`; `DevClioPath` has precedence when valid. The schema rejects unknown properties in editors to catch typos, while the runtime remains forward-tolerant.

## Consequences

- Editors provide validation and hover documentation without adding a runtime schema dependency.
- NativeAOT behavior is unchanged.
- The published directory gains one JSON file.
- Local refresh must preserve the user's existing settings and add `$schema` only as an intentional configuration migration.

