# modify-business-process

## Name

modify-business-process - Edit an existing business process on a Creatio environment by applying a list of operations

## Description

Edits an existing business process via the `ProcessDesignService` package: loads the process as an
editable schema, applies an ordered list of operations, and saves it. Operations apply in order; any
failure aborts the whole edit (nothing is saved). Identify the process by exactly one of `--name` or
`--uid`, and provide the operations as a JSON array file (`--operations`) or inline (`--operations-json`). Requires
the `clioprocessbuilder` package on the target environment.

Operations: `addElement`, `removeElement`, `addFlow`, `removeFlow`, `addParameter`, `addMapping`, `setFilter`, `clearFilter`.

## Synopsis

```bash
clio modify-business-process --name <CODE> --operations <FILE> -e <ENVIRONMENT_NAME>
clio modify-bp --uid <GUID> --operations-json <JSON> -e <ENVIRONMENT_NAME>
```

## Options

```bash
--name <CODE>
Process code (schema Name) to edit. Provide exactly one of --name or --uid.

--uid <GUID>
Process schema UId to edit. Provide exactly one of --name or --uid.

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
| `addElement` | `element` (id, type, caption, userTaskName?, signal?, filter?) | Adds an element (same descriptor as a build). |
| `removeElement` | `elementId` (local id or UId) | Removes the element plus its sequence flows. |
| `addFlow` | `source`, `target` (element ids) | Adds a sequence flow. |
| `removeFlow` | `source`, `target` (element ids) | Removes the matching sequence flow. |
| `addParameter` | `parameter` (name, type, direction?, caption?, referenceSchema?) | Adds a process-level parameter (same shape as a build `parameters[]` entry). `referenceSchema` (an object name) makes it a Lookup to that object. |
| `addMapping` | `mapping` (elementId, elementParameter, + one of processParameter/value/expression) | Binds an element input parameter to a value (same shape as a build `mappings[]` entry). |
| `setFilter` | `elementId`, `filter` (object, logicalOperation?, conditions[], groups?) | Sets a data source filter (same shape as a build element's `filter`). On a `signalStart` restricts the record trigger to matching records. |
| `clearFilter` | `elementId` | Removes the element's data source filter. |

Example — switch a process to start on record save (the proper alternative to a client save handler):

```json
[
  { "op": "removeElement", "elementId": "StartEvent1" },
  { "op": "addElement", "element": { "id": "SignalStart1", "type": "signalStart", "signal": { "entity": "UsrTestRunButton", "on": "save" } } },
  { "op": "addFlow", "source": "SignalStart1", "target": "task1" }
]
```

Example — add a process parameter and bind it to a task's input parameter:

```json
[
  { "op": "addParameter", "parameter": { "name": "AccountId", "type": "Guid", "direction": "In", "caption": "Account" } },
  { "op": "addMapping", "mapping": { "elementId": "task1", "elementParameter": "RecordId", "processParameter": "AccountId" } }
]
```

Example — add a lookup parameter referencing an object (a Lookup to `City`):

```json
[
  { "op": "addParameter", "parameter": { "name": "City", "referenceSchema": "City", "direction": "In" } }
]
```

Example — restrict a signal-start trigger to records where `Name` (column `UsrName`) equals `Start`
(the server serializes the platform filter; never hand-write filter JSON):

```json
[
  {
    "op": "setFilter",
    "elementId": "SignalStart1",
    "filter": {
      "object": "UsrTestRunButton",
      "conditions": [ { "column": "UsrName", "comparison": "equal", "value": "Start" } ]
    }
  }
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
