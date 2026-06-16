# modify-business-process

## Name

modify-business-process - Edit an existing business process on a Creatio environment by applying a list of operations

## Description

Edits an existing business process via the `ProcessDesignService` package: loads the process as an
editable schema, applies an ordered list of operations, and saves it. Operations apply in order; any
failure aborts the whole edit (nothing is saved). Identify the process by `--name` or `--uid`, and
provide the operations as a JSON array file (`--operations`) or inline (`--operations-json`). Requires
the `clioprocessbuilder` package on the target environment.

Phase 1 operations: `addElement`, `removeElement`, `addFlow`, `removeFlow`.

## Synopsis

```bash
clio modify-business-process --name <CODE> --operations <FILE> -e <ENVIRONMENT_NAME>
clio modify-bp --uid <GUID> --operations-json <JSON> -e <ENVIRONMENT_NAME>
```

## Options

```bash
--name <CODE>
Process code (schema Name) to edit. Provide this or --uid.

--uid <GUID>
Process schema UId to edit. Provide this or --name.

--operations <FILE>
Path to a JSON file with the operations array. Provide this or --operations-json.

--operations-json <JSON>
Inline JSON operations array (alternative to --operations).

-e, --Environment <ENVIRONMENT_NAME>
Target environment name (registered via reg-web-app)
```

## Operations

A JSON array; each item is an object with an `op`:

| op | Fields | Effect |
|---|---|---|
| `addElement` | `element` (id, type, caption, userTaskName?, signal?) | Adds an element (same descriptor as a build). |
| `removeElement` | `elementId` (local id or UId) | Removes the element plus its sequence flows. |
| `addFlow` | `source`, `target` (element ids) | Adds a sequence flow. |
| `removeFlow` | `source`, `target` (element ids) | Removes the matching sequence flow. |

Example — switch a process to start on record save (the proper alternative to a client save handler):

```json
[
  { "op": "removeElement", "elementId": "StartEvent1" },
  { "op": "addElement", "element": { "id": "SignalStart1", "type": "signalStart", "signal": { "entity": "UsrTestRunButton", "on": "save" } } },
  { "op": "addFlow", "source": "SignalStart1", "target": "task1" }
]
```

## Examples

```bash
clio modify-business-process --name UsrSampleProcess --operations ops.json -e production
Apply the operations in ops.json to the process
```

## See Also

create-business-process - Build a business process from a declarative descriptor
describe-process - Read an existing process and return its structured graph (inspect element ids first)

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#modify-business-process)
