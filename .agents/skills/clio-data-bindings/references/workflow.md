# Data Binding Workflow

Use `get-guidance {"name":"data-bindings"}` as the canonical workflow summary.
This reference restates only the generic binding recipes.

## 1. Inline Lookup Seeding

- Prefer `sync-schemas` when lookup creation or update and seed rows belong to one schema batch.
- Refresh app or schema context after the batch.
- Do not create a separate binding artifact unless the workflow explicitly needs one.

## 2. Standalone DB-First Binding Work

- Use `create-data-binding-db` when the workflow needs a remote binding outside a `sync-schemas` batch.
- Use `upsert-data-binding-row-db` only after the binding exists.
- Prefer remote read-back after mutation.

## 3. Local Binding Artifact Work

- Use `create-data-binding` to materialize the local binding artifact.
- Use `add-data-binding-row` and `remove-data-binding-row` for row-level local edits.
- Verify generated files or normalized command output instead of assuming success.
