# Repair multiple-column input

Issue: [#647](https://github.com/Advance-Technologies-Foundation/clio/issues/647)

## Intent and decision

Create every requested column in one `create-entity-schema` invocation. Normalize
complete repeated `--column` groups into one parser sequence without splitting
payload tokens. Expand a JSON array through the existing structured-column builder.
Keep the native save, DB structure, publish and readback path unchanged.

Missing repeated values remain parser errors. Empty arrays, null elements,
malformed JSON and invalid column definitions fail before saving a schema.

## Acceptance and validation

- Repeated legacy flags, repeated JSON objects, a JSON array, one sequence and a
  single object all create the requested columns without losing punctuation.
- Existing MCP structured column lists retain their contract.
- Independent Creatio reads verify column count, type, required flag and captions.
- The array case writes and reads both generated fields through OData, waiting for
  asynchronous rebuild readiness without retrying writes.
- Tests run only on an explicitly opted-in, exclusive local sandbox.

Unit coverage lives in `CreateEntitySchemaCommandTests` and
`RemoteEntitySchemaCreatorTests`. The manual runtime matrix lives in
`EntitySchemaMultipleColumnsE2ETests`. Execution evidence is recorded in the PR.

MCP reviewed, no update required: its existing structured list is serialized into
single-object column specifications. The `app-modeling` guidance and its tool
description trigger remain accurate. ClioRing compatibility reviewed, no
Ring-consumed contract changed: inspected `clio-ring/ClioRing.Ipc`,
`clio-ring/ClioRing`, and `clio-ring/ClioRing.Desktop/actions.json`.
