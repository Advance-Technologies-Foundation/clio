# Section deletion safety

Intent: deleting one application section must never select unrelated artifacts by a name prefix.

The existing implementation sweeps GetWorkspaceItems with StartsWith(section.Code). Resolve the application section, read persisted SysModule identities, and select only its declared page UIds. Preserve the card when retaining the entity, all registered edit/mini/search-row pages, and pages referenced by other sections. Resolve an opted-in entity by its exact declared name and entity item type; refuse missing/ambiguous/shared entities before writes. Retain auxiliary artifacts without explicit ownership instead of guessing names. Use existing services and routes; add no coordination protocol or rollback abstraction.

Acceptance: prefix-sharing entities/listeners/pages/addons remain untouched; default entity and seeded records survive; only the unshared declared list page is removed by the default flow; shared artifacts survive; unsafe discovery refuses before writes; schema deletion failures surface as errors. Destructive calls remain nontransactional and require state inspection after failure.

Validation: Command/McpServer unit suites, explicit disposable MCP E2E, CLI/runtime database snapshots, independent cleanup verification. Guidance warning tracked by clio-knowledge#212.
